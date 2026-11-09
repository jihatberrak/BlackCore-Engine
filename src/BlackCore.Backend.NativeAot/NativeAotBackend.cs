using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Models;
using BlackCore.Engine.Workspace;
using BlackCore.Infrastructure.Projects;
using BlackCore.Infrastructure.Toolchains;

namespace BlackCore.Backend.NativeAot;

/// <summary>
/// C# → .NET NativeAOT → native Windows x64 EXE. Proje, çalışma klasörüne izole bir kopya olarak alınır;
/// böylece bin/obj çıktıları kaynak ağacına yazılmaz. Komut argümanları ayrı listede geçirilir.
/// </summary>
public sealed class NativeAotBackend : ICompilerBackend
{
    public const string Id = "nativeaot";
    public const string RuntimeIdentifier = "win-x64";

    private readonly IExternalToolRunner _runner;
    private readonly string _dotnetExecutable;

    public NativeAotBackend(IExternalToolRunner runner, string dotnetExecutable = "dotnet")
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _dotnetExecutable = string.IsNullOrWhiteSpace(dotnetExecutable) ? "dotnet" : dotnetExecutable;
    }

    public string BackendId => Id;

    public async Task<CompilationResult> CompileAsync(
        BuildRequest request,
        AnalysisResult analysis,
        BuildWorkspace workspace,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;

        var project = CSharpProjects.Find(request.SourcePath);
        if (project is null)
        {
            return Failed(started, ErrorCode.SourceNotFound, "NativeAOT için C# projesi (.csproj) bulunamadı.");
        }

        var isolated = Path.Combine(workspace.WorkDirectory, "project");
        CSharpProjects.CopyProject(project.Directory, isolated);
        var isolatedProject = Path.Combine(isolated, Path.GetFileName(project.ProjectFile));

        var publishDirectory = Path.Combine(workspace.IntermediateDirectory, "publish");
        Directory.CreateDirectory(publishDirectory);

        var configuration = request.Configuration == BuildConfiguration.Debug ? "Debug" : "Release";
        var publish = await ToolInvocation.RunAsync(
            _runner,
            _dotnetExecutable,
            new[]
            {
                "publish", isolatedProject,
                "-c", configuration,
                "-r", RuntimeIdentifier,
                "-p:PublishAot=true",
                "-o", publishDirectory,
                "--nologo",
            },
            request.Timeout,
            isolated,
            MsvcEnvironment.ToolPathOverride(),
            cancellationToken);

        if (ToolInvocation.Classify(publish, "dotnet publish (NativeAOT)", ErrorCode.CompilationFailed) is { } failure)
        {
            return Failed(started, failure.ErrorCode, failure.Message);
        }

        var assemblyName = Path.GetFileNameWithoutExtension(project.ProjectFile);
        var candidate = Path.Combine(publishDirectory, assemblyName + ".exe");
        if (!File.Exists(candidate))
        {
            return Failed(started, ErrorCode.CompilationFailed, $"NativeAOT yayımı EXE üretmedi: {candidate}");
        }

        var version = await ToolInvocation.RunAsync(
            _runner, _dotnetExecutable, new[] { "--version" }, TimeSpan.FromSeconds(60), null, null, cancellationToken);

        return new CompilationResult
        {
            Succeeded = true,
            Message = $"NativeAOT yayımı tamamlandı ({RuntimeIdentifier}, {configuration}).",
            OutputPath = candidate,
            ProducedFiles = new[] { candidate },
            ToolVersion = version.Succeeded ? version.StandardOutput.Trim() : null,
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
        };
    }

    private static CompilationResult Failed(DateTimeOffset started, ErrorCode code, string message) => new()
    {
        Succeeded = false,
        ErrorCode = code,
        Message = message,
        StartedUtc = started,
        FinishedUtc = DateTimeOffset.UtcNow,
    };
}
