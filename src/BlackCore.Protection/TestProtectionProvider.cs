using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Models;

namespace BlackCore.Protection;

/// <summary>
/// TEST SAĞLAYICISI — gerçek koruma YAPMAZ. Girdi PE'yi kopyalar ve sonuna işaretleyici bayt ekler.
/// Yalnızca koruma akışını (çağrı, başarısızlık, koruma sonrası doğrulama, yayımlama) test etmek için vardır
/// (şartname 16.4). Üretilen dosya gerçek bir korumaya sahip değildir.
/// </summary>
public sealed class TestProtectionProvider : IProtectionProvider
{
    public const string Id = "blackcore-test-provider";
    public const string Marker = "BLACKCORE-TEST-PROTECTION";
    public const string Notice = "[TEST] Gerçek koruma uygulanmadı; yalnızca koruma akışı doğrulandı.";

    public string ProviderId => Id;

    public Task<ProtectionResult> ProtectAsync(ProtectionRequest request, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;

        if (!File.Exists(request.InputPath))
        {
            return Task.FromResult(Failed(started, ErrorCode.ProtectionFailed, "Girdi dosyası yok."));
        }

        File.Copy(request.InputPath, request.OutputPath, overwrite: true);
        using (var stream = new FileStream(request.OutputPath, FileMode.Append, FileAccess.Write))
        {
            var marker = System.Text.Encoding.ASCII.GetBytes(Marker);
            stream.Write(marker, 0, marker.Length);
        }

        return Task.FromResult(new ProtectionResult
        {
            Succeeded = true,
            ErrorCode = ErrorCode.None,
            Message = Notice,
            ProviderId = Id,
            OutputPath = request.OutputPath,
            ProducedFiles = new[] { request.OutputPath },
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
        });
    }

    private static ProtectionResult Failed(DateTimeOffset started, ErrorCode code, string message) => new()
    {
        Succeeded = false,
        ErrorCode = code,
        Message = message,
        ProviderId = Id,
        StartedUtc = started,
        FinishedUtc = DateTimeOffset.UtcNow,
    };
}
