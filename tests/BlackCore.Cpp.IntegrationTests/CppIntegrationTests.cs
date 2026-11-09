using BlackCore.Backend.LLVM;
using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Configuration;
using BlackCore.Engine.Models;
using BlackCore.Engine.Pipeline;
using BlackCore.Frontend.Cpp;
using BlackCore.Infrastructure.Processes;
using BlackCore.Infrastructure.Toolchains;
using Xunit;

namespace BlackCore.Cpp.IntegrationTests;

/// <summary>
/// Gerçek LLVM ve MSVC/SDK kullanan uçtan uca testler. Araç seti yoksa test SKIP olur
/// (başarılı gösterilmez); bkz. şartname 18.7.
/// </summary>
public sealed class CppIntegrationTests : IDisposable
{
    private readonly string _artifacts;

    public CppIntegrationTests()
    {
        _artifacts = Path.Combine(Path.GetTempPath(), "bc-int-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_artifacts, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [SkippableFact]
    public async Task HelloBlackCoreSampleCompilesRunsAndMatchesExpectedOutput()
    {
        var provider = await RequireToolchainAsync();
        var repo = RepoRoot();
        var sample = Path.Combine(repo, "samples", "cpp", "HelloBlackCore");
        var expected = File.ReadAllText(Path.Combine(sample, "expected-output.txt"));

        var outcome = await BuildAsync(provider, sample, expected, "HelloBlackCore.exe");

        Assert.True(outcome.Succeeded, $"[{outcome.ErrorCode}] {outcome.Message}");
        Assert.NotNull(outcome.OutputPath);
        Assert.True(File.Exists(outcome.OutputPath));
        Assert.True(new FileInfo(outcome.OutputPath!).Length > 0);
    }

    [SkippableFact]
    public async Task InvalidCppFailsAnalysisAndPublishesNothing()
    {
        var provider = await RequireToolchainAsync();
        var source = Path.Combine(Path.GetTempPath(), "bc-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        try
        {
            File.WriteAllText(Path.Combine(source, "bad.cpp"), "int main( { return 0 }\n");

            var outcome = await BuildAsync(provider, source, expectedOutput: null, "Bad.exe");

            Assert.False(outcome.Succeeded);
            Assert.Equal(ErrorCode.AnalysisFailed, outcome.ErrorCode);
            Assert.Null(outcome.OutputPath);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    [SkippableFact]
    public async Task SmokeTestMismatchFailsBuildAndDoesNotPublish()
    {
        var provider = await RequireToolchainAsync();
        var sample = Path.Combine(RepoRoot(), "samples", "cpp", "HelloBlackCore");

        var outcome = await BuildAsync(provider, sample, expectedOutput: "bu metin hiçbir zaman çıkmaz\n", "Mismatch.exe");

        Assert.False(outcome.Succeeded);
        Assert.Equal(ErrorCode.SmokeTestFailed, outcome.ErrorCode);
        Assert.Null(outcome.OutputPath);
    }

    [SkippableFact]
    public async Task ToolchainReportsSingleConsistentLlvmVersion()
    {
        var provider = await RequireToolchainAsync();

        var toolchain = await provider.GetAsync(default);

        Assert.Matches(@"^\d+\.\d+\.\d+$", toolchain.Llvm.Version);
        Assert.Contains("VCToolsInstallDir", toolchain.Environment.Keys);
        Assert.Contains("WindowsSDKVersion", toolchain.Environment.Keys);
    }

    private async Task<BuildOutcome> BuildAsync(
        CppToolchainProvider provider,
        string source,
        string? expectedOutput,
        string outputName)
    {
        var runner = new ProcessRunner();
        var engine = new BuildEngine(
            new ILanguageFrontend[] { new CppFrontend(provider, runner) },
            new ICompilerBackend[] { new LlvmBackend(provider, runner) },
            Array.Empty<IProtectionProvider>(),
            runner);

        var config = new BlackCoreConfig();
        var request = new BuildRequest(
            SourcePath: source,
            Language: SourceLanguage.Cpp,
            TargetOs: TargetOperatingSystem.Windows,
            TargetArch: TargetArchitecture.X64,
            Configuration: BuildConfiguration.Release,
            Optimization: OptimizationProfile.O2,
            ArtifactsRoot: _artifacts,
            OutputFileName: outputName,
            ProtectionEnabled: false,
            ProtectionProviderId: null,
            Timeout: TimeSpan.FromMinutes(5),
            RunSmokeTest: true,
            ExpectedSmokeOutput: expectedOutput);

        return await engine.BuildAsync(request, config, CancellationToken.None);
    }

    private static async Task<CppToolchainProvider> RequireToolchainAsync()
    {
        var provider = new CppToolchainProvider(LlvmRootFromRepoConfig(), new ProcessRunner());
        try
        {
            await provider.GetAsync(CancellationToken.None);
        }
        catch (BlackCoreException ex)
        {
            Skip.If(true, $"Araç seti bulunamadı, entegrasyon testi atlandı: {ex.Message}");
        }

        return provider;
    }

    private static string? LlvmRootFromRepoConfig()
    {
        var configPath = Path.Combine(RepoRoot(), "blackcore.json");
        return ConfigLoader.Load(configPath).Toolchains.LlvmDirectory;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BlackCore.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("BlackCore.sln bulunamadı.");
    }
}

