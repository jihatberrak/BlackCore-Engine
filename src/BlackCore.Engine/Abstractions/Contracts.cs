using BlackCore.Engine.Models;
using BlackCore.Engine.Workspace;

namespace BlackCore.Engine.Abstractions;

public interface ILanguageFrontend
{
    string LanguageId { get; }

    Task<AnalysisResult> AnalyzeAsync(BuildRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Şartname 6.2'den bir genişleme: backend çıktısını çalışma klasörüne yazabilmesi için
/// <see cref="BuildWorkspace"/> parametresi eklendi.
/// </summary>
public interface ICompilerBackend
{
    string BackendId { get; }

    Task<CompilationResult> CompileAsync(
        BuildRequest request,
        AnalysisResult analysis,
        BuildWorkspace workspace,
        CancellationToken cancellationToken);
}

public interface IProtectionProvider
{
    string ProviderId { get; }

    Task<ProtectionResult> ProtectAsync(ProtectionRequest request, CancellationToken cancellationToken);
}

public interface IExternalToolRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}
