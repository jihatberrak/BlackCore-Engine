using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Configuration;
using BlackCore.Engine.Models;
using BlackCore.Engine.Pipeline;
using BlackCore.Infrastructure.Processes;
using BlackCore.Protection;

namespace BlackCore.Engine.Tests;

public sealed class ProtectionFlowTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _source;
    private readonly string _artifactsRoot;

    public ProtectionFlowTests()
    {
        _source = Path.Combine(_temp.FullPath, "main.cpp");
        File.WriteAllText(_source, "int main() { return 0; }");
        _artifactsRoot = Path.Combine(_temp.FullPath, "artifacts");
    }

    public void Dispose() => _temp.Dispose();

    private BuildRequest Request(bool protection, string? providerId = "test-provider") => new(
        SourcePath: _source,
        Language: SourceLanguage.Cpp,
        TargetOs: TargetOperatingSystem.Windows,
        TargetArch: TargetArchitecture.X64,
        Configuration: BuildConfiguration.Release,
        Optimization: OptimizationProfile.O2,
        ArtifactsRoot: _artifactsRoot,
        OutputFileName: "App.exe",
        ProtectionEnabled: protection,
        ProtectionProviderId: providerId,
        Timeout: TimeSpan.FromMinutes(1));

    private BuildEngine MakeEngine(FakeBackend backend, params IProtectionProvider[] providers) =>
        new(new[] { new FakeFrontend() }, new[] { backend }, providers, new CountingToolRunner(), TimeProvider.System);

    private string FinalPath(BuildOutcome outcome) =>
        Path.Combine(_artifactsRoot, outcome.BuildId!, "output", "App.exe");

    [Fact]
    public async Task EnabledProtectionWithTestProviderPublishesMarkedOutput()
    {
        var engine = MakeEngine(new FakeBackend(), new TestProtectionProvider());

        var outcome = await engine.BuildAsync(
            Request(protection: true, providerId: TestProtectionProvider.Id), new BlackCoreConfig(), default);

        Assert.True(outcome.Succeeded, outcome.Message);
        Assert.Equal(FinalPath(outcome), outcome.OutputPath);
        var bytes = File.ReadAllBytes(outcome.OutputPath!);
        Assert.EndsWith(TestProtectionProvider.Marker, System.Text.Encoding.ASCII.GetString(bytes));
        Assert.Equal('M', (char)bytes[0]);
    }

    [Fact]
    public async Task DisabledProtectionPublishesUnprotectedOutput()
    {
        var provider = new FakeProvider();
        var engine = MakeEngine(new FakeBackend(), provider);

        var outcome = await engine.BuildAsync(Request(protection: false), new BlackCoreConfig(), default);

        Assert.True(outcome.Succeeded, outcome.Message);
        Assert.Equal(0, provider.Calls);
        Assert.DoesNotContain(
            TestProtectionProvider.Marker,
            System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(outcome.OutputPath!)));
    }

    [Fact]
    public async Task ProviderReportedFailureIsNotPublished()
    {
        var provider = new FakeProvider
        {
            Behavior = r => new ProtectionResult { Succeeded = false, ErrorCode = ErrorCode.ProtectionFailed, Message = "Araç hata verdi." },
        };
        var engine = MakeEngine(new FakeBackend(), provider);

        var outcome = await engine.BuildAsync(Request(protection: true), new BlackCoreConfig(), default);

        Assert.False(outcome.Succeeded);
        Assert.Equal(ErrorCode.ProtectionFailed, outcome.ErrorCode);
        Assert.Null(outcome.OutputPath);
        Assert.False(File.Exists(Path.Combine(_artifactsRoot, outcome.BuildId!, "output", "App.exe")));
    }

    [Fact]
    public async Task ProviderExceptionBecomesProtectionFailure()
    {
        var provider = new FakeProvider
        {
            Behavior = _ => throw new InvalidOperationException("sağlayıcı çöktü"),
        };
        var engine = MakeEngine(new FakeBackend(), provider);

        var outcome = await engine.BuildAsync(Request(protection: true), new BlackCoreConfig(), default);

        Assert.False(outcome.Succeeded);
        Assert.Equal(ErrorCode.ProtectionFailed, outcome.ErrorCode);
        Assert.DoesNotContain("sağlayıcı çöktü", outcome.Message);
    }

    [Fact]
    public async Task CorruptedProtectedOutputFailsPostValidation()
    {
        var provider = new FakeProvider
        {
            Behavior = r =>
            {
                File.WriteAllText(r.OutputPath, "korumalı değil");
                return new ProtectionResult { Succeeded = true, OutputPath = r.OutputPath, ProviderId = r.ProviderId };
            },
        };
        var engine = MakeEngine(new FakeBackend(), provider);

        var outcome = await engine.BuildAsync(Request(protection: true), new BlackCoreConfig(), default);

        Assert.False(outcome.Succeeded);
        Assert.Equal(ErrorCode.OutputInvalid, outcome.ErrorCode);
        Assert.Contains("Koruma sonrası doğrulama", outcome.Message);
        Assert.Null(outcome.OutputPath);
    }

    [Fact]
    public async Task MissingProviderFailsExplicitlyEvenWhenProtectionIsRequested()
    {
        var engine = MakeEngine(new FakeBackend());

        var outcome = await engine.BuildAsync(
            Request(protection: true, providerId: "olmayan"), new BlackCoreConfig(), default);

        Assert.Equal(ErrorCode.ProtectionProviderNotFound, outcome.ErrorCode);
        Assert.Null(outcome.OutputPath);
    }

    [Fact]
    public async Task ExternalCopyProviderRunsProcessAndProducesValidatedOutput()
    {
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var input = Path.Combine(_temp.FullPath, "in.exe");
        var output = Path.Combine(_temp.FullPath, "out.exe");
        TestPe.Write(input);

        var provider = new ExternalCopyTestProvider(cmd, new ProcessRunner());
        var result = await provider.ProtectAsync(
            new ProtectionRequest(input, output, provider.ProviderId, TimeSpan.FromSeconds(30)), default);

        Assert.True(result.Succeeded, result.Message);
        Assert.True(File.Exists(output));
        Assert.Equal(File.ReadAllBytes(input), File.ReadAllBytes(output));
    }

    [Fact]
    public async Task ExternalProviderWithMissingToolFailsWithProviderNotFound()
    {
        var provider = new ExternalCopyTestProvider(Path.Combine(_temp.FullPath, "yok.exe"), new ProcessRunner());

        var result = await provider.ProtectAsync(
            new ProtectionRequest("a", "b", provider.ProviderId, TimeSpan.FromSeconds(10)), default);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCode.ProtectionProviderNotFound, result.ErrorCode);
    }
}

