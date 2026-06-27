namespace MedicalAIPlatform.Services.TrainingCenter;

public sealed class CheXNetPathResolver
{
    private readonly IWebHostEnvironment _env;
    private readonly Microsoft.Extensions.Options.IOptions<MedicalAIPlatform.Options.TrainingCenterOptions> _options;
    private string? _cachedRoot;

    public CheXNetPathResolver(
        IWebHostEnvironment env,
        Microsoft.Extensions.Options.IOptions<MedicalAIPlatform.Options.TrainingCenterOptions> options)
    {
        _env = env;
        _options = options;
    }

    public string ResolveCheXNetMasterDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_cachedRoot))
            return _cachedRoot;

        var configured = _options.Value.CheXNetMasterPath?.Trim();
        if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
        {
            _cachedRoot = Path.GetFullPath(configured);
            return _cachedRoot;
        }

        var projectDir = FindProjectRoot(_env.ContentRootPath) ?? _env.ContentRootPath;
        var repoRoot = Directory.GetParent(Directory.GetParent(projectDir)?.FullName ?? projectDir)?.FullName ?? projectDir;
        var candidates = new[]
        {
            Path.Combine(repoRoot, "CheXNet", "CheXNet-master"),
            Path.Combine(Directory.GetParent(repoRoot)?.FullName ?? repoRoot, "CheXNet", "CheXNet-master"),
            Path.Combine(projectDir, "..", "..", "CheXNet", "CheXNet-master"),
        };

        foreach (var c in candidates)
        {
            var full = Path.GetFullPath(c);
            if (Directory.Exists(full))
            {
                _cachedRoot = full;
                return _cachedRoot;
            }
        }

        _cachedRoot = Path.GetFullPath(candidates[0]);
        return _cachedRoot;
    }

    public string ResolvePythonExecutable()
    {
        var venvPython = Path.Combine(ResolveCheXNetMasterDirectory(), ".venv", "Scripts", "python.exe");
        if (File.Exists(venvPython))
            return venvPython;

        var venvPythonUnix = Path.Combine(ResolveCheXNetMasterDirectory(), ".venv", "bin", "python");
        if (File.Exists(venvPythonUnix))
            return venvPythonUnix;

        return OperatingSystem.IsWindows() ? "python" : "python3";
    }

    public string ResolveBraxRoot() => Path.Combine(ResolveCheXNetMasterDirectory(), "datasets", "BRAX");

    public string ResolveModelVersionsDirectory() =>
        Path.Combine(ResolveCheXNetMasterDirectory(), "model_versions");

    public string ResolveJobStateDirectory()
    {
        var root = Path.Combine(_env.ContentRootPath, _options.Value.JobStateRoot);
        Directory.CreateDirectory(root);
        return root;
    }

    public string ResolveDatasetUploadDirectory()
    {
        var root = Path.Combine(_env.ContentRootPath, _options.Value.DatasetUploadRoot);
        Directory.CreateDirectory(root);
        return root;
    }

    private static string? FindProjectRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "MedicalAIPlatform.csproj")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }
}
