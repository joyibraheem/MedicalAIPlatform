using MedicalAIPlatform.Models.Dicom;
using MedicalAIPlatform.Options;

namespace MedicalAIPlatform.Services.Dicom;

/// <summary>Fuses per-slice scores into study-level probabilities / vote fractions.</summary>
public sealed class PredictionAggregationEngine
{
    private readonly DicomPipelineOptions _pipeline;

    public PredictionAggregationEngine(Microsoft.Extensions.Options.IOptions<DicomPipelineOptions> opts)
        => _pipeline = opts.Value;

    public AggregationResult Fuse(
        IReadOnlyList<Dictionary<string, double>> sliceScores,
        DicomAggregationMethod method,
        CancellationToken _)
    {
        var list = sliceScores.Select(d => new Dictionary<string, double>(d, StringComparer.OrdinalIgnoreCase)).ToList();
        return method switch
        {
            DicomAggregationMethod.MaxPooling => MaxPooling(list),
            DicomAggregationMethod.AveragePooling => AveragePooling(list),
            DicomAggregationMethod.ThresholdVoting => Voting(list, _pipeline.VotingThreshold),
            _ => MaxPooling(list),
        };
    }

    private static AggregationResult MaxPooling(List<Dictionary<string, double>> slices)
    {
        if (slices.Count == 0)
            return new AggregationResult(new Dictionary<string, double>(), [], "MaxPooling");

        var unionKeys = slices.SelectMany(s => s.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var final = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in unionKeys)
            final[k] = slices.Max(s => s.TryGetValue(k, out double v) ? v : 0);

        return new AggregationResult(final, slices, "MaxPooling");
    }

    private static AggregationResult AveragePooling(List<Dictionary<string, double>> slices)
    {
        if (slices.Count == 0)
            return new AggregationResult(new Dictionary<string, double>(), [], "AveragePooling");

        var sums = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in slices)
        {
            foreach (var (k, v) in s)
            {
                sums[k] = sums.GetValueOrDefault(k) + v;
                counts[k] = counts.GetValueOrDefault(k) + 1;
            }
        }

        var avg = sums.ToDictionary(kv => kv.Key, kv => kv.Value / counts[kv.Key], StringComparer.OrdinalIgnoreCase);
        return new AggregationResult(avg, slices, "AveragePooling");
    }

    /// <summary>Vote score = proportion of slices with label probability ≥ threshold.</summary>
    private static AggregationResult Voting(List<Dictionary<string, double>> slices, double threshold)
    {
        if (slices.Count == 0)
            return new AggregationResult(new Dictionary<string, double>(), [], "ThresholdVoting");

        double t = Math.Clamp(threshold, 1e-6, 1 - 1e-6);
        var unionKeys = slices.SelectMany(s => s.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var votes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var k in unionKeys)
        {
            int count = slices.Count(s => s.TryGetValue(k, out var v) && v >= t);
            votes[k] = (double)count / slices.Count;
        }

        return new AggregationResult(votes, slices, "ThresholdVoting");
    }
}

public sealed record AggregationResult(
    IReadOnlyDictionary<string, double> FinalPrediction,
    IReadOnlyList<Dictionary<string, double>> SliceScoreSnapshots,
    string MethodName);
