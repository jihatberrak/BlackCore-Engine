using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Artifacts;
using BlackCore.Engine.Models;
using BlackCore.Engine.Workspace;

namespace BlackCore.Engine.Pipeline.Steps;

internal sealed class ValidateRequestStep : IPipelineStep
{
    public string Name => "ValidateRequest";

    public Task<StepOutcome> ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var r = context.Request;

        if (string.IsNullOrWhiteSpace(r.SourcePath))
        {
            return Fail(ErrorCode.RequestInvalid, "Kaynak yolu boş.");
        }

        if (string.IsNullOrWhiteSpace(r.ArtifactsRoot))
        {
            return Fail(ErrorCode.RequestInvalid, "Artefakt kök klasörü boş.");
        }

        if (string.IsNullOrWhiteSpace(r.OutputFileName)
            || r.OutputFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !r.OutputFileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return Fail(ErrorCode.RequestInvalid, "Çıktı dosya adı geçersiz (.exe ile bitmeli).");
        }

        if (r.TargetOs != TargetOperatingSystem.Windows)
        {
            return Fail(ErrorCode.RequestInvalid, "MVP yalnızca Windows hedefini destekler.");
        }

        if (r.TargetArch != TargetArchitecture.X64)
        {
            return Fail(ErrorCode.RequestInvalid, "MVP yalnızca x64 mimarisini destekler.");
        }

        if (r.Timeout <= TimeSpan.Zero)
        {
            return Fail(ErrorCode.RequestInvalid, "Timeout pozitif olmalı.");
        }

        if (r.ProtectionEnabled && string.IsNullOrWhiteSpace(r.ProtectionProviderId))
        {
            return Fail(ErrorCode.RequestInvalid, "Koruma etkinken sağlayıcı kimliği zorunludur.");
        }

        if (!r.ProtectionEnabled && !string.IsNullOrWhiteSpace(r.ProtectionProviderId))
        {
            context.Warnings.Add("Koruma kapalı; belirtilen sağlayıcı kullanılmayacak.");
        }

        return Ok("İstek geçerli.");
    }

    private static Task<StepOutcome> Ok(string message) => Task.FromResult(StepOutcome.Ok(message));

    private static Task<StepOutcome> Fail(ErrorCode code, string message) =>
        Task.FromResult(StepOutcome.Fail(code, message));
}

internal sealed class CheckSourceStep : IPipelineStep
{
    public string Name => "CheckSource";

    public Task<StepOutcome> ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var path = context.Request.SourcePath;
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return Task.FromResult(StepOutcome.Fail(ErrorCode.SourceNotFound, $"Kaynak bulunamadı: {path}"));
        }

        return Task.FromResult(StepOutcome.Ok("Kaynak erişilebilir."));
    }
}

internal sealed class CreateWorkspaceStep : IPipelineStep
{
    public string Name => "CreateWorkspace";

    public Task<StepOutcome> ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var workspace = BuildWorkspace.Create(
            Path.GetFullPath(context.Request.ArtifactsRoot),
            context.Time.GetUtcNow());

        context.Workspace = workspace;
        context.Log.AttachFile(Path.Combine(workspace.LogsDirectory, "build.log"));
        context.Log.Info(Name, $"Çalışma klasörü oluşturuldu: {workspace.Root}");

        return Task.FromResult(StepOutcome.Ok($"Çalışma klasörü: {workspace.BuildId}"));
    }
}

internal sealed class AnalyzeStep : IPipelineStep
{
    private readonly ILanguageFrontend? _frontend;

    public AnalyzeStep(ILanguageFrontend? frontend)
    {
        _frontend = frontend;
    }

    public string Name => "Analyze";

    public async Task<StepOutcome> ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        if (_frontend is null)
        {
            return StepOutcome.Fail(ErrorCode.ComponentNotFound, "Bu dil için analiz frontend'i kayıtlı değil.");
        }

        var analysis = await _frontend.AnalyzeAsync(context.Request, cancellationToken);
        context.Analysis = analysis;

        if (analysis.ToolVersion is not null)
        {
            context.ToolVersions[_frontend.LanguageId] = analysis.ToolVersion;
        }

        if (!analysis.Succeeded)
        {
            return StepOutcome.Fail(
                analysis.ErrorCode == ErrorCode.None ? ErrorCode.AnalysisFailed : analysis.ErrorCode,
                analysis.Message);
        }

        var errors = analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            return StepOutcome.Fail(ErrorCode.AnalysisFailed, $"Analiz hatası ({errors.Count}): {errors[0].Message}");
        }

        var warningCount = analysis.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
        foreach (var warning in analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Warning))
        {
            context.Warnings.Add($"[{warning.Code}] {warning.Message}");
        }

        if (warningCount > 0 && context.Config.Analysis.FailOnWarnings)
        {
            return StepOutcome.Fail(
                ErrorCode.AnalysisPolicyViolation,
                $"Analiz uyarısı var ve failOnWarnings etkin ({warningCount}).");
        }

        return StepOutcome.Ok($"Analiz tamamlandı ({warningCount} uyarı).");
    }
}

internal sealed class CompileStep : IPipelineStep
{
    private readonly ICompilerBackend? _backend;

    public CompileStep(ICompilerBackend? backend)
    {
        _backend = backend;
    }

    public string Name => "Compile";

    public async Task<StepOutcome> ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        if (_backend is null)
        {
            return StepOutcome.Fail(ErrorCode.ComponentNotFound, "Bu dil için derleme backend'i kayıtlı değil.");
        }

        var result = await _backend.CompileAsync(
            context.Request,
            context.Analysis!,
            context.Workspace!,
            cancellationToken);

        context.Compilation = result;
        if (result.ToolVersion is not null)
        {
            context.ToolVersions[_backend.BackendId] = result.ToolVersion;
        }

        if (!result.Succeeded)
        {
            return StepOutcome.Fail(
                result.ErrorCode == ErrorCode.None ? ErrorCode.CompilationFailed : result.ErrorCode,
                result.Message);
        }

        return StepOutcome.Ok("Derleme tamamlandı.");
    }
}

internal sealed class ValidateCompilationStep : IPipelineStep
{
    public string Name => "ValidateCompilation";

    public Task<StepOutcome> ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var path = context.Compilation?.OutputPath;
        if (string.IsNullOrEmpty(path))
        {
            return Task.FromResult(StepOutcome.Fail(ErrorCode.OutputInvalid, "Derleyici çıktı yolu bildirmedi."));
        }

        var validation = ArtifactValidator.ValidatePortableExecutable(path, context.Request.TargetArch);
        if (!validation.Valid)
        {
            return Task.FromResult(StepOutcome.Fail(ErrorCode.OutputInvalid, validation.Message));
        }

        context.PublishCandidate = path;
        return Task.FromResult(StepOutcome.Ok(validation.Message));
    }
}

/// <summary>
/// Üretilen EXE'yi kontrollü çalıştırır (şartname 10.7). Yalnızca <see cref="BuildRequest.RunSmokeTest"/> ise çalışır.
/// Beklenen çıktı verilmişse stdout ile karşılaştırılır.
/// </summary>
internal sealed class SmokeTestStep : IPipelineStep
{
    public string Name => "SmokeTest";

    public async Task<StepOutcome> ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        if (!request.RunSmokeTest)
        {
            return StepOutcome.Skip("Smoke test istenmedi.");
        }

        var candidate = context.PublishCandidate!;
        var timeout = TimeSpan.FromSeconds(context.Config.Execution.SmokeTestTimeoutSeconds);

        var run = await context.Tools.RunAsync(
            new ProcessRequest(candidate, Array.Empty<string>(), timeout, Path.GetDirectoryName(candidate)),
            cancellationToken);

        if (!run.Started)
        {
            return StepOutcome.Fail(ErrorCode.SmokeTestFailed, $"Smoke test başlatılamadı: {run.StartFailureMessage}");
        }

        if (run.TimedOut)
        {
            return StepOutcome.Fail(ErrorCode.SmokeTestFailed, $"Smoke test zaman aşımına uğradı ({timeout.TotalSeconds:0} sn).");
        }

        if (run.Cancelled)
        {
            return StepOutcome.Fail(ErrorCode.Cancelled, "Smoke test iptal edildi.");
        }

        if (run.ExitCode != 0)
        {
            return StepOutcome.Fail(ErrorCode.SmokeTestFailed, $"Smoke test çıkış kodu {run.ExitCode}.");
        }

        if (request.ExpectedSmokeOutput is { } expected)
        {
            var actual = Normalize(run.StandardOutput);
            if (!string.Equals(actual, Normalize(expected), StringComparison.Ordinal))
            {
                return StepOutcome.Fail(
                    ErrorCode.SmokeTestFailed,
                    $"Beklenen çıktı alınamadı. Beklenen: '{Normalize(expected)}', alınan: '{actual}'.");
            }
        }

        return StepOutcome.Ok("Smoke test başarılı.");
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
}

/// <summary>Koruma kapalıysa sağlayıcı hiç çağrılmaz (şartname 6.3, 16.4).</summary>
internal sealed class ProtectionStep : IPipelineStep
{
    private readonly IReadOnlyList<IProtectionProvider> _providers;

    public ProtectionStep(IEnumerable<IProtectionProvider> providers)
    {
        _providers = providers.ToList();
    }

    public string Name => "Protection";

    public async Task<StepOutcome> ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;
        if (!request.ProtectionEnabled)
        {
            return StepOutcome.Skip("Koruma kapalı; sağlayıcı çağrılmadı.");
        }

        var providerId = request.ProtectionProviderId!;
        var provider = _providers.FirstOrDefault(p => p.ProviderId == providerId);
        if (provider is null)
        {
            return StepOutcome.Fail(
                ErrorCode.ProtectionProviderNotFound,
                $"Koruma sağlayıcısı bulunamadı: {providerId}. Koruma adımı başarısız sayıldı.");
        }

        var input = context.PublishCandidate!;
        var output = Path.Combine(context.Workspace!.OutputDirectory, "protected-" + Path.GetFileName(input));

        ProtectionResult result;
        try
        {
            result = await provider.ProtectAsync(
                new ProtectionRequest(input, output, providerId, request.Timeout),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Sağlayıcı istisnası koruma hatasıdır; ayrıntı loga yazılır, build açıkça başarısız olur.
            context.Log.Error(Name, $"Koruma sağlayıcısı istisna fırlattı ({providerId}): {ex}");
            return StepOutcome.Fail(ErrorCode.ProtectionFailed, $"Koruma sağlayıcısı beklenmeyen hata verdi: {providerId}.");
        }

        context.Protection = result;

        if (!result.Succeeded)
        {
            return StepOutcome.Fail(
                result.ErrorCode == ErrorCode.None ? ErrorCode.ProtectionFailed : result.ErrorCode,
                result.Message);
        }

        // Başarılı çıkış kodu tek başına yeterli değil (şartname 11.2): çıktı yeniden doğrulanır.
        var protectedPath = result.OutputPath ?? output;
        var validation = ArtifactValidator.ValidatePortableExecutable(protectedPath, request.TargetArch);
        if (!validation.Valid)
        {
            return StepOutcome.Fail(
                ErrorCode.OutputInvalid,
                $"Koruma sonrası doğrulama başarısız: {validation.Message}");
        }

        context.PublishCandidate = protectedPath;
        return StepOutcome.Ok($"Koruma tamamlandı ve doğrulandı ({providerId}). {result.Message}");
    }
}

/// <summary>
/// Son artefaktı çıktı klasörüne atomik olarak yerleştirir: önce geçici dosya yazılır,
/// doğrulanır, sonra nihai ada taşınır. Başarısız derleme nihai dosyaya dokunmaz.
/// </summary>
internal sealed class PublishStep : IPipelineStep
{
    public string Name => "Publish";

    public Task<StepOutcome> ExecuteAsync(BuildContext context, CancellationToken cancellationToken)
    {
        var candidate = context.PublishCandidate;
        if (string.IsNullOrEmpty(candidate))
        {
            return Task.FromResult(StepOutcome.Fail(ErrorCode.PublishFailed, "Yayımlanacak aday dosya yok."));
        }

        var workspace = context.Workspace!;
        var finalPath = Path.Combine(workspace.OutputDirectory, context.Request.OutputFileName);
        var tempPath = finalPath + ".tmp";

        File.Copy(candidate, tempPath, overwrite: true);

        var validation = ArtifactValidator.ValidatePortableExecutable(tempPath, context.Request.TargetArch);
        if (!validation.Valid)
        {
            File.Delete(tempPath);
            return Task.FromResult(StepOutcome.Fail(ErrorCode.OutputInvalid, validation.Message));
        }

        File.Move(tempPath, finalPath, overwrite: true);
        context.PublishedOutputPath = finalPath;

        return Task.FromResult(StepOutcome.Ok($"Artefakt yayımlandı: {finalPath}"));
    }
}
