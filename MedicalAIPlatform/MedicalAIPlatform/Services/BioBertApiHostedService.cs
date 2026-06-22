using System.Diagnostics;
using System.Net.Http;

namespace MedicalAIPlatform.Services;

/// <summary>
/// Starts the local BioBERT FastAPI server automatically when the app starts.
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
        if (string.Equals(auto, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(auto, "0", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("BioBERT API autostart disabled (BIOBERT_API_AUTOSTART={Value}).", auto);
            return;
        }

        var port = "8001";
        var baseUrl = $"http://127.0.0.1:{port}";

        if (await IsHealthyAsync(baseUrl, cancellationToken))
        {
            _logger.LogInformation("BioBERT API already running at {BaseUrl}.", baseUrl);
            return;
        }

        // Try to find BioBERT service directory
        var contentRoot = AppContext.BaseDirectory;
        var projectDir = FindProjectRoot(contentRoot) ?? Directory.GetCurrentDirectory();
        
        // Try multiple possible locations
        var possiblePaths = new List<string>();
        
        // Option 1: In the same parent directory as MedicalAIPlatform
        var repoRoot = Directory.GetParent(Directory.GetParent(projectDir)?.FullName ?? projectDir)?.FullName ?? projectDir;
        possiblePaths.Add(Path.Combine(repoRoot, "BIOBERT", "BIOBERT", "biobert_service"));
        
        // Option 2: Check if there's an AIProjects parent directory
        var parentDir = Directory.GetParent(repoRoot)?.FullName;
        if (parentDir != null)
        {
            possiblePaths.Add(Path.Combine(parentDir, "BIOBERT", "BIOBERT", "biobert_service"));
        }
        
        // Option 3: Check current directory structure
        possiblePaths.Add(Path.Combine(repoRoot, "..", "BIOBERT", "BIOBERT", "biobert_service"));
        
        string? bioBertServiceDir = null;
        foreach (var path in possiblePaths)
        {
            var normalizedPath = Path.GetFullPath(path);
            if (Directory.Exists(normalizedPath))
            {
                bioBertServiceDir = normalizedPath;
                _logger.LogInformation("Found BioBERT service at {Dir}.", bioBertServiceDir);
                break;
            }
        }
        
        if (bioBertServiceDir == null)
        {
            _logger.LogWarning("BioBERT service directory not found. Tried paths: {Paths}. BioBERT API will not start.", 
                string.Join(", ", possiblePaths));
            return;
        }

        // Try to find python executable
        string pythonExe = "python";
        
        // Check for venv in BIOBERT/BIOBERT/.venv
        var venvPath = Path.Combine(Directory.GetParent(Directory.GetParent(bioBertServiceDir)?.FullName ?? bioBertServiceDir)?.FullName ?? bioBertServiceDir, ".venv", "Scripts", "python.exe");
        if (File.Exists(venvPath))
        {
            pythonExe = venvPath;
        }
        else
        {
            // Check common Python locations
            var pythonPaths = new[]
            {
                @"C:\Python39\python.exe",
                @"C:\Python310\python.exe",
                @"C:\Python311\python.exe",
                @"C:\Python312\python.exe",
                @"C:\Program Files\Python39\python.exe",
                @"C:\Program Files\Python310\python.exe",
                @"C:\Program Files\Python311\python.exe",
                @"C:\Program Files\Python312\python.exe",
            };
            
            foreach (var pp in pythonPaths)
            {
                if (File.Exists(pp))
                {
                    pythonExe = pp;
                    break;
                }
            }
        }
        
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

        _proc.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger.LogInformation("[BioBERT] {Line}", e.Data);
        };
        _proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger.LogWarning("[BioBERT] {Line}", e.Data);
        };
        _proc.BeginOutputReadLine();
        _proc.BeginErrorReadLine();

        // Wait for API to become healthy
        for (var i = 0; i < 40 && !cancellationToken.IsCancellationRequested; i++)
        {
            await Task.Delay(250, cancellationToken);
            if (await IsHealthyAsync(baseUrl, cancellationToken))
            {
                _logger.LogInformation("BioBERT API is up at {BaseUrl}.", baseUrl);
                return;
            }
        }
        _logger.LogWarning("BioBERT API did not become healthy at {BaseUrl} within startup window.", baseUrl);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_proc is { HasExited: false })
            {
                _proc.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignore
        }
        finally
        {
            _proc?.Dispose();
            _proc = null;
        }

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
        catch
        {
            return false;
        }
    }

    private static string? FindProjectRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MedicalAIPlatform.csproj")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
