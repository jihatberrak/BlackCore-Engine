using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Models;
using BlackCore.Engine.Workspace;

namespace BlackCore.Engine.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        FullPath = Path.Combine(Path.GetTempPath(), "bc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(FullPath, recursive: true);
        }
        catch (IOException)
        {
            // Geçici dosya kilitliyse test sonucunu etkilemesin.
        }
    }
}

internal static class TestPe
{
    public const ushort Amd64 = 0x8664;
    public const ushort I386 = 0x014C;

    /// <summary>Makine alanı verilen minimal bir PE dosyası yazar.</summary>
    public static void Write(string path, ushort machine = Amd64, byte[]? trailer = null)
    {
        var bytes = new byte[256];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3C);
        bytes[0x80] = (byte)'P';
        bytes[0x81] = (byte)'E';
        BitConverter.GetBytes(machine).CopyTo(bytes, 0x84);

        if (trailer is not null)
        {
            bytes = bytes.Concat(trailer).ToArray();
        }

        File.WriteAllBytes(path, bytes);
    }
}

internal sealed class FakeFrontend : ILanguageFrontend
{
    private int _calls;

    public string LanguageId => "cpp";

    public int Calls => _calls;

    public Func<BuildRequest, AnalysisResult> Behavior { get; set; } =
        _ => new AnalysisResult { Succeeded = true, Message = "Analiz tamam." };

    public Task<AnalysisResult> AnalyzeAsync(BuildRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(Behavior(request));
    }
}

internal sealed class FakeBackend : ICompilerBackend
{
    private int _calls;

    public string BackendId => "llvm";

    public int Calls => _calls;

    /// <summary>Varsayılan: çalışma klasörüne geçerli bir AMD64 PE yazar.</summary>
    public Func<BuildRequest, BuildWorkspace, CompilationResult> Behavior { get; set; } = (_, ws) =>
    {
        var path = Path.Combine(ws.OutputDirectory, "candidate.exe");
        TestPe.Write(path);
        return new CompilationResult { Succeeded = true, OutputPath = path, Message = "Derlendi.", ToolVersion = "22.1.8" };
    };

    public Task<CompilationResult> CompileAsync(
        BuildRequest request,
        AnalysisResult analysis,
        BuildWorkspace workspace,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(Behavior(request, workspace));
    }
}

internal sealed class FakeProvider : IProtectionProvider
{
    private int _calls;

    public string ProviderId => "test-provider";

    public int Calls => _calls;

    public Func<ProtectionRequest, ProtectionResult> Behavior { get; set; } = request =>
    {
        // Test sağlayıcısı: girdiyi kopyalar ve bir işaret ekler. Gerçek koruma yapmaz.
        File.Copy(request.InputPath, request.OutputPath, overwrite: true);
        using var stream = File.Open(request.OutputPath, FileMode.Append);
        stream.Write("PROTECTED"u8);
        return new ProtectionResult { Succeeded = true, Message = "Test sağlayıcısı tamamlandı.", ProviderId = request.ProviderId, OutputPath = request.OutputPath };
    };

    public Task<ProtectionResult> ProtectAsync(ProtectionRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(Behavior(request));
    }
}

internal sealed class CountingToolRunner : IExternalToolRunner
{
    private int _calls;

    public int Calls => _calls;

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);
        var now = DateTimeOffset.UtcNow;
        return Task.FromResult(new ProcessResult(true, 0, string.Empty, string.Empty, false, false, false, now, now));
    }
}
