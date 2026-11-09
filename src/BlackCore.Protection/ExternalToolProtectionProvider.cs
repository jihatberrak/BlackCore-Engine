using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Models;
using BlackCore.Infrastructure.Toolchains;

namespace BlackCore.Protection;

/// <summary>
/// Harici bir koruma aracını çalıştıran sağlayıcı iskeleti (şartname 11.2).
/// Program yolu yapılandırmadan verilir; argümanlar sağlayıcıya özel adapter tarafından üretilir;
/// araç yoksa veya zaman aşımına uğrarsa koruma başarısız sayılır. Çıkış kodu tek başına yeterli değildir;
/// çıktı dosyası pipeline'da ayrıca doğrulanır.
/// </summary>
public abstract class ExternalToolProtectionProvider : IProtectionProvider
{
    private readonly IExternalToolRunner _runner;

    protected ExternalToolProtectionProvider(string executablePath, IExternalToolRunner runner)
    {
        ExecutablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public abstract string ProviderId { get; }

    public string ExecutablePath { get; }

    /// <summary>Adapter: istek için komut satırı argümanlarını üretir. Araç argümanları burada, ayrı listede oluşturulur.</summary>
    protected abstract IReadOnlyList<string> BuildArguments(ProtectionRequest request);

    public async Task<ProtectionResult> ProtectAsync(ProtectionRequest request, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;

        if (!File.Exists(ExecutablePath))
        {
            return Failed(started, ErrorCode.ProtectionProviderNotFound,
                $"Koruma aracı bulunamadı: {ExecutablePath}");
        }

        var run = await ToolInvocation.RunAsync(
            _runner,
            ExecutablePath,
            BuildArguments(request),
            request.Timeout,
            Path.GetDirectoryName(request.InputPath),
            environment: null,
            cancellationToken);

        if (ToolInvocation.Classify(run, Path.GetFileName(ExecutablePath), ErrorCode.ProtectionFailed) is { } failure)
        {
            return Failed(started, failure.ErrorCode, failure.Message);
        }

        return new ProtectionResult
        {
            Succeeded = true,
            Message = $"Harici koruma aracı tamamladı ({ProviderId}).",
            ProviderId = ProviderId,
            OutputPath = request.OutputPath,
            ProducedFiles = new[] { request.OutputPath },
            StandardOutput = run.StandardOutput,
            StandardError = run.StandardError,
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
        };
    }

    private ProtectionResult Failed(DateTimeOffset started, ErrorCode code, string message) => new()
    {
        Succeeded = false,
        ErrorCode = code,
        Message = message,
        ProviderId = ProviderId,
        StartedUtc = started,
        FinishedUtc = DateTimeOffset.UtcNow,
    };
}

/// <summary>
/// TEST ADAPTÖRÜ — gerçek koruma yapmaz. Harici süreç yolunu (argüman listesi, timeout, çıkış kodu)
/// uçtan uca test etmek için Windows'un <c>cmd.exe /c copy</c> komutunu kullanır.
/// </summary>
public sealed class ExternalCopyTestProvider : ExternalToolProtectionProvider
{
    public const string Id = "external-copy-test";

    public ExternalCopyTestProvider(string cmdExecutablePath, IExternalToolRunner runner)
        : base(cmdExecutablePath, runner)
    {
    }

    public override string ProviderId => Id;

    protected override IReadOnlyList<string> BuildArguments(ProtectionRequest request) =>
        new[] { "/d", "/c", "copy", "/Y", request.InputPath, request.OutputPath };
}
