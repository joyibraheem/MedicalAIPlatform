using System.Diagnostics;
using System.Net.Http;

static string RepoRoot()
{
    // D:\AIProjects\CheXNet\CheXNet.PythonApiHost\bin\Debug\net8.0\ -> up 4 to workspace root
    var dir = AppContext.BaseDirectory;
    for (var i = 0; i < 5; i++)
    {
        dir = Directory.GetParent(dir)?.FullName ?? dir;
        if (File.Exists(Path.Combine(dir, "CheXNet.sln")))
        {
            return dir;
        }
    }
    return Directory.GetCurrentDirectory();
}

var root = RepoRoot();
var apiDir = Path.Combine(root, "CheXNet-master");
var venvUvicorn = Path.Combine(apiDir, ".venv", "Scripts", "uvicorn.exe");

if (!Directory.Exists(apiDir))
{
    Console.Error.WriteLine($"CheXNet-master folder not found at: {apiDir}");
    Environment.Exit(1);
}

if (!File.Exists(venvUvicorn))
{
    Console.Error.WriteLine($"uvicorn.exe not found at: {venvUvicorn}");
    Console.Error.WriteLine("Make sure the Python venv exists at CheXNet-master/.venv and has uvicorn installed.");
    Environment.Exit(1);
}

var host = Environment.GetEnvironmentVariable("CHEXNET_API_HOST") ?? "127.0.0.1";
var port = Environment.GetEnvironmentVariable("CHEXNET_API_PORT") ?? "8000";

var uvicornArgs = $"fastapi_app:app --host {host} --port {port}";

var psi = new ProcessStartInfo
{
    FileName = venvUvicorn,
    Arguments = uvicornArgs,
    WorkingDirectory = apiDir,
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = false,
};

Console.WriteLine("Starting CheXNet FastAPI server...");
Console.WriteLine($"  cwd: {apiDir}");
Console.WriteLine($"  cmd: {venvUvicorn} {uvicornArgs}");

using var proc = Process.Start(psi);
if (proc is null)
{
    Console.Error.WriteLine("Failed to start uvicorn process.");
    Environment.Exit(1);
}

proc.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(e.Data); };
proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.Error.WriteLine(e.Data); };
proc.BeginOutputReadLine();
proc.BeginErrorReadLine();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// Wait until the API responds (best-effort) so VS starts feel reliable.
var baseUrl = $"http://{host}:{port}";
Console.WriteLine($"Waiting for API: {baseUrl}/health");
try
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
    for (var i = 0; i < 25 && !cts.IsCancellationRequested; i++)
    {
        await Task.Delay(250, cts.Token);
        try
        {
            var resp = await http.GetAsync($"{baseUrl}/health", cts.Token);
            if (resp.IsSuccessStatusCode)
            {
                Console.WriteLine("API is up.");
                break;
            }
        }
        catch
        {
            // ignore until it comes up
        }
    }
}
catch (OperationCanceledException)
{
    // ignore
}

Console.WriteLine("CheXNet API host running. Press Ctrl+C to stop.");

try
{
    while (!cts.IsCancellationRequested && !proc.HasExited)
    {
        await Task.Delay(500, cts.Token);
    }
}
catch (OperationCanceledException)
{
    // ignore
}

try
{
    if (!proc.HasExited)
    {
        proc.Kill(entireProcessTree: true);
    }
}
catch
{
    // ignore
}

Console.WriteLine("CheXNet API host stopped.");
