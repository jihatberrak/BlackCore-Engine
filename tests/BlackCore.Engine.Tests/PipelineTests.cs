using System.Text.Json;
using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Configuration;
using BlackCore.Engine.Models;
using BlackCore.Engine.Pipeline;

namespace BlackCore.Engine.Tests;

public sealed class BuildEngineTests : IDisposable
{
    private static readonly string[] ExpectedStages =
    {
        "ValidateRequest", "CheckSource", "CreateWorkspace", "Analyze",
        "Compile", "ValidateCompilation", "SmokeTest", "Protection", "Publish",
    };

    private readonly TempDirectory _temp = new();
    private readonly string _source;
    private readonly string _artifactsRoot;

    public BuildEngineTests()
    {
        _source = Path.Combine(_temp.FullPath, "main.cpp");
        File.WriteAllText(_source, "int main() { return 0; }");
        _artifactsRoot = Path.Combine(_temp.FullPath, "artifacts");
    }

    public void Dispose() => _temp.Dispose();

    private BuildRequest Request(bool protection = false, string? providerId = null, string? source = null) => new(
        SourcePath: source ?? _source,
        Language: SourceLanguage.Cpp,
        TargetOs: TargetOperatingSystem.Windows,
        TargetArch: TargetArchitecture.X64,
        Configuration: BuildConfiguration.Release,
        Optimization: OptimizationProfile.O2,
        ArtifactsRoot: _artifactsRoot,
        OutputFileName: "BlackCoreDemo.exe",
        ProtectionEnabled: protection,
        ProtectionProviderId: providerId,
        Timeout: TimeSpan.FromMinutes(1));

    private static (BuildEngine Engine, FakeFrontend Frontend, FakeBackend Backend, FakeProvider Provider, CountingToolRunner Tools)
        Create()
    {
        var frontend = new FakeFrontend();
        var backend = new FakeBackend();
        var provider = new FakeProvider();
        var tools = new CountingToolRunner();
        var engine = new BuildEngine(new[] { frontend }, new[] { backend }, new[] { provider }, tools, TimeProvider.System);
        return (engine, frontend, backend, provider, tools);
    }

    private static string? PublishedFile(BuildOutcome outcome) => outcome.OutputPath;

    private string ExpectedFinalPath(BuildOutcome outcome, string name = "BlackCoreDemo.exe") =>
        Path.Combine(_artifactsRoot, outcome.BuildId!, "output", name);

    [Fact]
    public async Task SuccessfulBuildPublishesExecutableAndWritesReport()
    {
        var (engine, _, backend, provider, tools) = Create();

        var outcome = await engine.BuildAsync(Request(), new BlackCoreConfig(), default);

        Assert.True(outcome.Succeeded, outcome.Message);
        Assert.Equal(ErrorCode.None, outcome.ErrorCode);
        Assert.Equal(ExpectedFinalPath(outcome), PublishedFile(outcome));
        Assert.True(File.Exists(outcome.OutputPath));
        Assert.Equal(1, backend.Calls);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(0, tools.Calls);

        using var doc = JsonDocument.Parse(File.ReadAllText(outcome.ReportPath!));
        var root = doc.RootElement;
        Assert.True(root.GetProperty("Succeeded").GetBoolean());
        Assert.Equal(outcome.BuildId, root.GetProperty("BuildId").GetString());
        Assert.Equal("22.1.8", root.GetProperty("ToolVersions").GetProperty("llvm").GetString());
        var stageNames = root.GetProperty("Stages").EnumerateArray().Select(s => s.GetProperty("Name").GetString()).ToArray();
        Assert.Equal(ExpectedStages, stageNames);
    }

    [Fact]
    public async Task AnalysisErrorStopsPipelineBeforeBackend()
    {
        var (engine, frontend, backend, _, _) = Create();
        frontend.Behavior = _ => new AnalysisResult
        {
            Succeeded = true,
            Diagnostics = new[] { new Diagnostic(DiagnosticSeverity.Error, "CS0001", "Sözdizimi hatası") },
        };

        var outcome = await engine.BuildAsync(Request(), new BlackCoreConfig(), default);

        Assert.False(outcome.Succeeded);
        Assert.Equal(ErrorCode.AnalysisFailed, outcome.ErrorCode);
        Assert.Equal(0, backend.Calls);
        Assert.Null(outcome.OutputPath);
    }

    [Fact]
    public async Task WarningsFailBuildWhenPolicyRequiresIt()
    {
        var (engine, frontend, backend, _, _) = Create();
        frontend.Behavior = _ => new AnalysisResult
        {
            Succeeded = true,
            Diagnostics = new[] { new Diagnostic(DiagnosticSeverity.Warning, "W1", "Uyarı") },
        };
        var config = new BlackCoreConfig();
        config.Analysis.FailOnWarnings = true;

        var outcome = await engine.BuildAsync(Request(), config, default);

        Assert.Equal(ErrorCode.AnalysisPolicyViolation, outcome.ErrorCode);
        Assert.Equal(0, backend.Calls);
    }

    [Fact]
    public async Task BackendFailureDoesNotPublishAnything()
    {
        var (engine, _, backend, _, _) = Create();
        backend.Behavior = (_, _) => new CompilationResult
        {
            Succeeded = false,
            ErrorCode = ErrorCode.CompilationFailed,
            Message = "LLVM verifier hatası",
        };

        var outcome = await engine.BuildAsync(Request(), new BlackCoreConfig(), default);

        Assert.False(outcome.Succeeded);
        Assert.Equal(ErrorCode.CompilationFailed, outcome.ErrorCode);
        Assert.Null(outcome.OutputPath);
        Assert.False(File.Exists(Path.Combine(_artifactsRoot, outcome.BuildId!, "output", "BlackCoreDemo.exe")));
    }

    [Fact]
    public async Task NonPeOutputFailsValidationAndIsNotPublished()
    {
        var (engine, _, backend, _, _) = Create();
        backend.Behavior = (_, ws) =>
        {
            var path = Path.Combine(ws.OutputDirectory, "candidate.exe");
            File.WriteAllText(path, "bu bir PE değil");
            return new CompilationResult { Succeeded = true, OutputPath = path };
        };

        var outcome = await engine.BuildAsync(Request(), new BlackCoreConfig(), default);

        Assert.Equal(ErrorCode.OutputInvalid, outcome.ErrorCode);
        Assert.Null(outcome.OutputPath);
    }

    [Fact]
    public async Task WrongArchitectureOutputIsRejected()
    {
        var (engine, _, backend, _, _) = Create();
        backend.Behavior = (_, ws) =>
        {
            var path = Path.Combine(ws.OutputDirectory, "candidate.exe");
            TestPe.Write(path, TestPe.I386);
            return new CompilationResult { Succeeded = true, OutputPath = path };
        };

        var outcome = await engine.BuildAsync(Request(), new BlackCoreConfig(), default);

        Assert.Equal(ErrorCode.OutputInvalid, outcome.ErrorCode);
        Assert.Contains("Mimari uyumsuz", outcome.Message);
    }

    [Fact]
    public async Task ProtectionDisabledNeverCallsProvider()
    {
        var (engine, _, _, provider, _) = Create();

        var outcome = await engine.BuildAsync(Request(protection: false), new BlackCoreConfig(), default);

        Assert.True(outcome.Succeeded, outcome.Message);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task MissingProtectionProviderFailsExplicitly()
    {
        var (engine, _, backend, provider, _) = Create();

        var outcome = await engine.BuildAsync(Request(protection: true, providerId: "yok"), new BlackCoreConfig(), default);

        Assert.False(outcome.Succeeded);
        Assert.Equal(ErrorCode.ProtectionProviderNotFound, outcome.ErrorCode);
        Assert.Equal(0, provider.Calls);
        Assert.Null(outcome.OutputPath);
        Assert.Equal(1, backend.Calls);
    }

    [Fact]
    public async Task ValidProtectedOutputIsPublished()
    {
        var (engine, _, _, provider, _) = Create();

        var outcome = await engine.BuildAsync(
            Request(protection: true, providerId: "test-provider"),
            new BlackCoreConfig(),
            default);

        Assert.True(outcome.Succeeded, outcome.Message);
        Assert.Equal(1, provider.Calls);
        // Yayımlanan dosya, sağlayıcının ürettiği (işaretli) dosya olmalı.
        var bytes = File.ReadAllBytes(outcome.OutputPath!);
        Assert.EndsWith("PROTECTED", System.Text.Encoding.ASCII.GetString(bytes));
    }

    [Fact]
    public async Task ProviderSuccessWithoutOutputFails()
    {
        var (engine, _, _, provider, _) = Create();
        provider.Behavior = request => new ProtectionResult
        {
            Succeeded = true,
            Message = "Başarılı dedi ama dosya yok.",
            ProviderId = request.ProviderId,
        };

        var outcome = await engine.BuildAsync(
            Request(protection: true, providerId: "test-provider"),
            new BlackCoreConfig(),
            default);

        Assert.False(outcome.Succeeded);
        Assert.Equal(ErrorCode.OutputInvalid, outcome.ErrorCode);
        Assert.Null(outcome.OutputPath);
    }

    [Fact]
    public async Task MissingSourceFailsBeforeWorkspaceIsCreated()
    {
        var (engine, _, backend, _, _) = Create();

        var outcome = await engine.BuildAsync(
            Request(source: Path.Combine(_temp.FullPath, "yok.cpp")),
            new BlackCoreConfig(),
            default);

        Assert.False(outcome.Succeeded);
        Assert.Equal(ErrorCode.SourceNotFound, outcome.ErrorCode);
        Assert.Null(outcome.BuildId);
        Assert.Equal(0, backend.Calls);
    }

    [Fact]
    public async Task EachBuildUsesItsOwnWorkspace()
    {
        var (engine, _, _, _, _) = Create();

        var first = await engine.BuildAsync(Request(), new BlackCoreConfig(), default);
        var second = await engine.BuildAsync(Request(), new BlackCoreConfig(), default);

        Assert.True(first.Succeeded && second.Succeeded);
        Assert.NotEqual(first.BuildId, second.BuildId);
        Assert.NotEqual(first.OutputPath, second.OutputPath);
    }
}
