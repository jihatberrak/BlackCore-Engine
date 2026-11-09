using System.Security.Cryptography;
using BlackCore.Engine.Models;

namespace BlackCore.Engine.Workspace;

/// <summary>
/// Her derleme için benzersiz klasör: artifacts/&lt;yyyy-MM-dd_HHmmss_XXXXXX&gt;/ (şartname 14).
/// </summary>
public sealed class BuildWorkspace
{
    private const string IdAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int MaxAttempts = 10;

    private BuildWorkspace(string buildId, string root)
    {
        BuildId = buildId;
        Root = root;
    }

    public string BuildId { get; }

    public string Root { get; }

    public string WorkDirectory => Path.Combine(Root, "workspace");

    public string IntermediateDirectory => Path.Combine(Root, "intermediate");

    public string LogsDirectory => Path.Combine(Root, "logs");

    public string ReportsDirectory => Path.Combine(Root, "reports");

    public string OutputDirectory => Path.Combine(Root, "output");

    public static BuildWorkspace Create(string artifactsRoot, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactsRoot);
        Directory.CreateDirectory(artifactsRoot);

        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var buildId = $"{now.UtcDateTime:yyyy-MM-dd_HHmmss}_{RandomNumberGenerator.GetString(IdAlphabet, 6)}";
            var root = Path.GetFullPath(Path.Combine(artifactsRoot, buildId));
            if (Directory.Exists(root))
            {
                continue;
            }

            var workspace = new BuildWorkspace(buildId, root);
            foreach (var dir in new[]
                     {
                         workspace.WorkDirectory,
                         workspace.IntermediateDirectory,
                         workspace.LogsDirectory,
                         workspace.ReportsDirectory,
                         workspace.OutputDirectory,
                     })
            {
                Directory.CreateDirectory(dir);
            }

            return workspace;
        }

        throw new BlackCoreException(ErrorCode.WorkspaceFailed, "Benzersiz çalışma klasörü oluşturulamadı.");
    }
}
