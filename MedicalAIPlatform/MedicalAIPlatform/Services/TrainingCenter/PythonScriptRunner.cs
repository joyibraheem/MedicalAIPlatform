using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MedicalAIPlatform.Models;
using MedicalAIPlatform.Models.TrainingCenter;

namespace MedicalAIPlatform.Services.TrainingCenter;

public sealed class PythonScriptRunner
{
    private readonly CheXNetPathResolver _paths;
    private readonly ILogger<PythonScriptRunner> _log;

    public PythonScriptRunner(CheXNetPathResolver paths, ILogger<PythonScriptRunner> log)
    {
        _paths = paths;
        _log = log;
    }

    public async Task<(int ExitCode, string Output)> RunAsync(
        string scriptName,
        IReadOnlyList<string> args,
        CancellationToken ct = default)
    {
        var python = _paths.ResolvePythonExecutable();
        var workDir = _paths.ResolveCheXNetMasterDirectory();
        var scriptPath = Path.Combine(workDir, scriptName);
        if (!File.Exists(scriptPath))
            throw new FileNotFoundException($"Python script not found: {scriptPath}");

        var argText = string.Join(" ", args.Select(a => QuoteArg(a)));
        var psi = new ProcessStartInfo
        {
            FileName = python,
            Arguments = $"\"{scriptPath}\" {argText}",
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var output = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) output.AppendLine(e.Data); };

        _log.LogInformation("Running {Script} {Args}", scriptName, argText);
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(ct).ConfigureAwait(false);
        return (proc.ExitCode, output.ToString());
    }

    public async Task<T?> RunJsonReportAsync<T>(
        string scriptName,
        IReadOnlyList<string> args,
        string reportPath,
        CancellationToken ct = default)
    {
        var allArgs = new List<string>(args) { "--save-report", reportPath };
        var (code, output) = await RunAsync(scriptName, allArgs, ct).ConfigureAwait(false);
        if (!File.Exists(reportPath))
            throw new InvalidOperationException(
                $"Script {scriptName} did not produce report at {reportPath}. Exit={code}\n{output}");

        await using var fs = File.OpenRead(reportPath);
        using var doc = await JsonDocument.ParseAsync(fs, cancellationToken: ct).ConfigureAwait(false);
        return doc.RootElement.Deserialize<T>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static string QuoteArg(string arg)
    {
        if (string.IsNullOrEmpty(arg)) return "\"\"";
        return arg.Contains(' ') || arg.Contains('"') ? $"\"{arg.Replace("\"", "\\\"")}\"" : arg;
    }
}

public sealed class BraxTrainingJobRuntime
{
    public Guid JobId { get; init; }
    public string ModelId { get; init; } = "";
    public string Status { get; set; } = "Running";
    public string? OutputDir { get; set; }
    public string? LogPath { get; set; }
    public string? DatasetPath { get; set; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public string? Error { get; set; }
    public Process? Process { get; set; }
    public TrainingStartRequestDto Request { get; init; } = new();
    public string StartedByUserId { get; init; } = "";
}

public sealed class TrainingJobRuntimeStore
{
    private readonly ConcurrentDictionary<Guid, BraxTrainingJobRuntime> _jobs = new();

    public void Register(BraxTrainingJobRuntime job) => _jobs[job.JobId] = job;

    public BraxTrainingJobRuntime? Get(Guid jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job : null;

    public BraxTrainingJobRuntime? GetActive() =>
        _jobs.Values.FirstOrDefault(j =>
            j.Status is TrainingJobStatuses.Running or TrainingJobStatuses.Preparing or "Training");

    public IEnumerable<BraxTrainingJobRuntime> All => _jobs.Values.OrderByDescending(j => j.StartedAt);
}
