using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MedicalAIPlatform.Models.TrainingCenter;

namespace MedicalAIPlatform.Services.TrainingCenter;

public sealed class TrainingMonitorReader
{
    private readonly CheXNetPathResolver _paths;

    public TrainingMonitorReader(CheXNetPathResolver paths) => _paths = paths;

    private static readonly Regex EpochLine = new(
        @"Epoch\s+(\d+)/(\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public LiveTrainingMonitorDto? ReadLive(BraxTrainingJobRuntime job)
    {
        if (job is null) return null;

        var dto = new LiveTrainingMonitorDto
        {
            JobId = job.JobId,
            ModelId = job.ModelId,
            Status = job.Status,
            OutputDir = job.OutputDir,
            ElapsedSeconds = (DateTimeOffset.UtcNow - job.StartedAt).TotalSeconds,
            CanCancel = job.Status == "Running" && job.Process is { HasExited: false },
            CanPause = false,
            CanResume = false,
        };

        if (!string.IsNullOrEmpty(job.OutputDir))
        {
            var lossCsv = Path.Combine(job.OutputDir, "loss.csv");
            if (File.Exists(lossCsv))
                ApplyLossCsv(dto, lossCsv);
            else if (!string.IsNullOrEmpty(job.LogPath) && File.Exists(job.LogPath))
                ApplyLogTail(dto, job.LogPath);
        }
        else if (!string.IsNullOrEmpty(job.LogPath) && File.Exists(job.LogPath))
        {
            ApplyLogTail(dto, job.LogPath);
        }

        if (dto.CurrentEpoch is null && job.Status is "Training" or "Running")
        {
            var liveDir = FindLatestRunDirectory(job.StartedAt);
            if (liveDir is not null)
            {
                dto.OutputDir = liveDir;
                var lossCsv = Path.Combine(liveDir, "loss.csv");
                if (File.Exists(lossCsv))
                    ApplyLossCsv(dto, lossCsv);
            }
        }

        if (dto.TotalEpochs is > 0 && dto.CurrentEpoch is > 0)
        {
            dto.ProgressPercent = Math.Clamp(100.0 * dto.CurrentEpoch.Value / dto.TotalEpochs.Value, 0, 100);
            if (dto.ElapsedSeconds > 0 && dto.CurrentEpoch > 0)
            {
                var perEpoch = dto.ElapsedSeconds / dto.CurrentEpoch.Value;
                var remaining = (dto.TotalEpochs.Value - dto.CurrentEpoch.Value) * perEpoch;
                dto.EtaSeconds = remaining;
            }
        }

        return dto;
    }

    public TrainingChartsDto ReadCharts(string outputDir)
    {
        var charts = new TrainingChartsDto();
        var lossCsv = Path.Combine(outputDir, "loss.csv");
        if (!File.Exists(lossCsv))
            return charts;

        var lines = File.ReadAllLines(lossCsv);
        if (lines.Length <= 1) return charts;

        for (var i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            if (parts.Length < 2) continue;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
                continue;

            if (TryDouble(parts, 1, out var trainLoss))
                charts.TrainLoss.Add(new ChartPointDto { Epoch = epoch, Value = trainLoss });
            if (TryDouble(parts, 2, out var valLoss))
                charts.ValLoss.Add(new ChartPointDto { Epoch = epoch, Value = valLoss });
            if (TryDouble(parts, 3, out var acc))
                charts.F1.Add(new ChartPointDto { Epoch = epoch, Value = acc });
            if (parts.Length > 7 && TryDouble(parts, 7, out var roc))
                charts.RocAuc.Add(new ChartPointDto { Epoch = epoch, Value = roc });
        }

        var metricsPath = Path.Combine(outputDir, "metrics.json");
        if (File.Exists(metricsPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(metricsPath));
            if (doc.RootElement.TryGetProperty("history", out var history) && history.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in history.EnumerateArray())
                {
                    var epoch = row.TryGetProperty("epoch", out var ep) ? ep.GetInt32() : 0;
                    if (row.TryGetProperty("f1_micro", out var f1))
                        charts.F1.Add(new ChartPointDto { Epoch = epoch, Value = f1.GetDouble() });
                    if (row.TryGetProperty("roc_auc_macro", out var roc))
                        charts.RocAuc.Add(new ChartPointDto { Epoch = epoch, Value = roc.GetDouble() });
                }
            }
        }

        return charts;
    }

    private static void ApplyLossCsv(LiveTrainingMonitorDto dto, string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length <= 1) return;
        var last = lines[^1].Split(',');
        if (!int.TryParse(last[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
            return;

        dto.CurrentEpoch = epoch;
        if (TryDouble(last, 1, out var trainLoss)) dto.TrainLoss = trainLoss;
        if (TryDouble(last, 2, out var valLoss)) dto.ValLoss = valLoss;
        if (TryDouble(last, 3, out var acc)) dto.Accuracy = acc;
        if (TryDouble(last, 6, out var f1)) dto.F1 = f1;
        if (last.Length > 7 && TryDouble(last, 7, out var roc)) dto.RocAuc = roc;
        dto.TotalEpochs = dto.TotalEpochs ?? epoch;
    }

    private static void ApplyLogTail(LiveTrainingMonitorDto dto, string logPath)
    {
        var tail = ReadTail(logPath, 8000);
        dto.LogTail = tail;
        var matches = EpochLine.Matches(tail);
        if (matches.Count == 0) return;
        var m = matches[^1];
        if (int.TryParse(m.Groups[1].Value, out var cur)) dto.CurrentEpoch = cur;
        if (int.TryParse(m.Groups[2].Value, out var tot)) dto.TotalEpochs = tot;

        foreach (var line in tail.Split('\n').Reverse().Take(30))
        {
            if (line.Contains("Train loss:", StringComparison.OrdinalIgnoreCase) &&
                TryParseAfterColon(line, out var v)) dto.TrainLoss = v;
            if (line.Contains("Val loss:", StringComparison.OrdinalIgnoreCase) &&
                TryParseAfterColon(line, out var vl)) dto.ValLoss = vl;
            if (line.Contains("F1 (micro):", StringComparison.OrdinalIgnoreCase) &&
                TryParseAfterColon(line, out var f1)) dto.F1 = f1;
            if (line.Contains("ROC-AUC (macro):", StringComparison.OrdinalIgnoreCase) &&
                TryParseAfterColon(line, out var roc)) dto.RocAuc = roc;
        }
    }

    private static bool TryDouble(string[] parts, int index, out double value)
    {
        value = 0;
        return index < parts.Length &&
               double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseAfterColon(string line, out double value)
    {
        value = 0;
        var idx = line.IndexOf(':');
        if (idx < 0) return false;
        return double.TryParse(line[(idx + 1)..].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private string? FindLatestRunDirectory(DateTimeOffset startedAfter)
    {
        var versionsDir = _paths.ResolveModelVersionsDirectory();
        if (!Directory.Exists(versionsDir)) return null;

        return Directory.GetDirectories(versionsDir)
            .Where(d => Path.GetFileName(d).StartsWith("BRAX_", StringComparison.OrdinalIgnoreCase))
            .Where(d => Directory.GetLastWriteTimeUtc(d) >= startedAfter.UtcDateTime.AddMinutes(-1))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string ReadTail(string path, int maxChars)
    {
        var text = File.ReadAllText(path);
        return text.Length <= maxChars ? text : text[^maxChars..];
    }
}
