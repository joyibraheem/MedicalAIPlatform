using System.Diagnostics;
using System.Net.Http;

namespace MedicalAIPlatform.Services;

/// <summary>
/// Starts the local CheXNet FastAPI server (uvicorn) automatically when the app starts.
/// If something is already on port 8000 but doesn't have /predict/ct, starts the full API on port 8002 and switches the app to use it.
/// </summary>
public sealed class CheXNetApiHostedService : IHostedService
{
    private readonly ILogger<CheXNetApiHostedService> _logger;
    private readonly CheXNetApiEndpointOptions _options;
    private Process? _proc;

    public CheXNetApiHostedService(ILogger<CheXNetApiHostedService> logger, CheXNetApiEndpointOptions options)
    {
        _logger = logger;
        _options = options;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var auto = Environment.GetEnvironmentVariable("CHEXNET_API_AUTOSTART");
        if (string.Equals(auto, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(auto, "0", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("CheXNet API autostart disabled (CHEXNET_API_AUTOSTART={Value}).", auto);
            return;
        }

        var host = Environment.GetEnvironmentVariable("CHEXNET_API_HOST") ?? "127.0.0.1";
        var port8000 = Environment.GetEnvironmentVariable("CHEXNET_API_PORT") ?? "8000";
        var base8000 = $"http://{host}:{port8000}";

        // Nothing on 8000 -> start our API on 8000
        if (!await IsHealthyAsync(base8000, cancellationToken))
        {
            if (await TryStartApiAsync(host, port8000, base8000, cancellationToken))
            {
                _options.BaseUrl = base8000 + "/";
            }
            return;
        }

        // Something on 8000: require full route set (stale uvicorn from before BRAX integration lacks /predict/raddino).
        if (await HasPredictCtAsync(base8000, cancellationToken)
            && await HasRouteAsync(base8000, "/predict/raddino", cancellationToken))
        {
            _logger.LogInformation("CheXNet API at {BaseUrl} already has /predict/ct and /predict/raddino.", base8000);
            return;
        }

        if (await HasPredictCtAsync(base8000, cancellationToken))
        {
            _logger.LogWarning(
                "CheXNet API at {BaseUrl} is missing /predict/raddino (likely a stale server). Starting updated API on port 8002.",
                base8000);
        }

        // 8000 has /health but not the full route set -> start our API on 8002 and point app to it
        const string port8002 = "8002";
        var base8002 = $"http://{host}:{port8002}";
        if (await TryStartApiAsync(host, port8002, base8002, cancellationToken))
        {
            _options.BaseUrl = base8002 + "/";
            _logger.LogInformation("CheXNet API (with /predict/ct) started on port {Port}. App now using {BaseUrl}.", port8002, _options.BaseUrl);
        }
        else
        {
            _logger.LogWarning("Port 8000 has no /predict/ct and starting API on 8002 failed. Set LungAI:BaseUrl to a server that has /predict/ct.");
        }
    }

    /// <summary>GET /predict/ct: 405 = route exists, 404 = missing.</summary>
    private static async Task<bool> HasPredictCtAsync(string baseUrl, CancellationToken ct)
    {
        return await HasRouteAsync(baseUrl, "/predict/ct", ct).ConfigureAwait(false);
    }

    /// <summary>Any non-404 means the route is registered (405/422/400 are OK for GET on POST-only endpoints).</summary>
    private static async Task<bool> HasRouteAsync(string baseUrl, string path, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var resp = await http.GetAsync($"{baseUrl.TrimEnd('/')}{path}", ct).ConfigureAwait(false);
            return resp.StatusCode != System.Net.HttpStatusCode.NotFound;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryStartApiAsync(string host, string port, string baseUrl, CancellationToken cancellationToken)
    {
        var contentRoot = AppContext.BaseDirectory;
        var projectDir = FindProjectRoot(contentRoot) ?? Directory.GetCurrentDirectory();
        // Go up from MedicalAIPlatform/MedicalAIPlatform/ to MedicalAIPlatform final/
        var repoRoot = Directory.GetParent(Directory.GetParent(projectDir)?.FullName ?? projectDir)?.FullName ?? projectDir;
        var apiDir = Path.Combine(repoRoot, "CheXNet", "CheXNet-master");
        
        // If that doesn't exist, try alternative path (in case structure is different)
        if (!Directory.Exists(apiDir))
        {
            // Try going up one more level if needed
            var altRepoRoot = Directory.GetParent(repoRoot)?.FullName ?? repoRoot;
            var altApiDir = Path.Combine(altRepoRoot, "CheXNet", "CheXNet-master");
            if (Directory.Exists(altApiDir))
            {
                apiDir = altApiDir;
            }
        }
        
        var uvicornPath = Path.Combine(apiDir, ".venv", "Scripts", "uvicorn.exe");

        if (!Directory.Exists(apiDir))
        {
            _logger.LogWarning("CheXNet API folder not found at {ApiDir}.", apiDir);
            return false;
        }
        if (!File.Exists(uvicornPath))
        {
            _logger.LogWarning("uvicorn.exe not found at {UvicornPath}.", uvicornPath);
            return false;
        }

        var uvicornArgs = $"fastapi_app:app --host {host} --port {port}";
        _logger.LogInformation("Starting CheXNet API: {Exe} {Args}", uvicornPath, uvicornArgs);

        var psi = new ProcessStartInfo
        {
            FileName = uvicornPath,
            Arguments = uvicornArgs,
            WorkingDirectory = apiDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        _proc = Process.Start(psi);
        if (_proc is null)
        {
            _logger.LogWarning("Failed to start uvicorn process.");
            return false;
        }

        _proc.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger.LogInformation("[CheXNet API] {Line}", e.Data);
        };
        _proc.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _logger.LogWarning("[CheXNet API] {Line}", e.Data);
        };
        _proc.BeginOutputReadLine();
        _proc.BeginErrorReadLine();

        for (var i = 0; i < 40 && !cancellationToken.IsCancellationRequested; i++)
        {
            await Task.Delay(250, cancellationToken);
            if (await IsHealthyAsync(baseUrl, cancellationToken))
            {
                _logger.LogInformation("CheXNet API is up at {BaseUrl}.", baseUrl);
                return true;
            }
        }
        _logger.LogWarning("CheXNet API did not become healthy at {BaseUrl} within startup window.", baseUrl);
        try { _proc.Kill(entireProcessTree: true); } catch { }
        _proc = null;
        return false;
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
