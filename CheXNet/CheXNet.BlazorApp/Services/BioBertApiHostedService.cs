using System.Diagnostics;

namespace CheXNet.BlazorApp.Services;

/// <summary>
/// Starts the local BioBERT FastAPI server automatically.
/// </summary>
public sealed class BioBertApiHostedService : IHostedService
{
    private readonly ILogger<BioBertApiHostedService> _logger;
    private Process? _proc;

    public BioBertApiHostedService(ILogger<BioBertApiHostedService> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var auto = Environment.GetEnvironmentVariable("BIOBERT_API_AUTOSTART");
        if (string.Equals(auto, "false", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var port = "8001";
        var baseUrl = $"http://127.0.0.1:{port}";

        if (await IsHealthyAsync(baseUrl, cancellationToken))
        {
            _logger.LogInformation("BioBERT API already running at {BaseUrl}.", baseUrl);
            return;
        }

        // Logic to find D:\AIProjects\BIOBERT\BIOBERT\biobert_service
        // Current: D:\AIProjects\CheXNet\CheXNet.BlazorApp\
        
        var contentRoot = AppContext.BaseDirectory;
        var blazorDir = FindProjectRoot(contentRoot) ?? Directory.GetCurrentDirectory();
        // Up to AIProjects
        var repoRoot = Directory.GetParent(blazorDir)?.FullName; // CheXNet
        if (repoRoot != null) repoRoot = Directory.GetParent(repoRoot)?.FullName; // AIProjects

        if (repoRoot == null)
        {
             _logger.LogWarning("Could not find AIProjects root.");
             return;
        }

        var bioBertServiceDir = Path.Combine(repoRoot, "BIOBERT", "BIOBERT", "biobert_service");
        
        if (!Directory.Exists(bioBertServiceDir))
        {
            _logger.LogWarning("BioBERT service directory not found at {Dir}.", bioBertServiceDir);
            return;
        }

        // Try to find python. Prefer typical venv locations if we can guess, else 'python'
        string pythonExe = "python";
        
        // Check for venv in BIOBERT/BIOBERT/.venv
        var venvPath = Path.Combine(repoRoot, "BIOBERT", "BIOBERT", ".venv", "Scripts", "python.exe");
        if (File.Exists(venvPath)) pythonExe = venvPath;
        
        var args = "main.py"; // main.py runs uvicorn

        _logger.LogInformation("Starting BioBERT API: {Exe} {Args} in {Dir}", pythonExe, args, bioBertServiceDir);

        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = args,
            WorkingDirectory = bioBertServiceDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        _proc = Process.Start(psi);
        if (_proc is null)
        {
            _logger.LogWarning("Failed to start BioBERT process.");
            return;
        }

        _proc.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _logger.LogInformation("[BioBERT] {Line}", e.Data); };
        _proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) _logger.LogWarning("[BioBERT] {Line}", e.Data); };
        _proc.BeginOutputReadLine();
        _proc.BeginErrorReadLine();
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try { _proc?.Kill(true); } catch { }
        _proc?.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<bool> IsHealthyAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var resp = await http.GetAsync($"{baseUrl}/health", ct);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static string? FindProjectRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CheXNet.BlazorApp.csproj"))) return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }
}
