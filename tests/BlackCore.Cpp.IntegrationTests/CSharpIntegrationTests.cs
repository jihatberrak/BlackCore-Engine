using BlackCore.Backend.NativeAot;
using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Configuration;
using BlackCore.Engine.Models;
using BlackCore.Engine.Pipeline;
using BlackCore.Frontend.CSharp;
using BlackCore.Infrastructure.Processes;
using BlackCore.Infrastructure.Toolchains;
using Xunit;

namespace BlackCore.Cpp.IntegrationTests;

/// <summary>Gerçek dotnet publish (NativeAOT) ve MSVC bağlayıcısı kullanan uçtan uca C# testleri.</summary>
public sealed class CSharpIntegrationTests : IDisposable
{
    private readonly string _artifacts = Path.Combine(Path.GetTempPath(), "bc-cs-" + Guid.NewGuid().ToString("N"));

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
    public async Task HelloBlackCoreSamplePublishesNativeAotAndMatchesExpectedOutput()
    {
        await RequireNativeAotToolsAsync();
        var sample = Path.Combine(RepoRoot(), "samples", "csharp", "HelloBlackCore");
        var expected = File.ReadAllText(Path.Combine(sample, "expected-output.txt"));

        var outcome = await BuildAsync(sample, expected, "HelloBlackCore.exe");

        Assert.True(outcome.Succeeded, $"[{outcome.ErrorCode}] {outcome.Message}");
        Assert.NotNull(outcome.OutputPath);
        Assert.True(File.Exists(outcome.OutputPath));
    }

    [SkippableFact]
    public async Task CSharpSyntaxErrorFailsAnalysisAndPublishesNothing()
    {
        await RequireNativeAotToolsAsync();
        var source = Path.Combine(Path.GetTempPath(), "bc-cs-bad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(source);
        try
        {
            File.WriteAllText(Path.Combine(source, "Bad.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(source, "Program.cs"), "System.Console.WriteLine(\"x\"");

            var outcome = await BuildAsync(source, expectedOutput: null, "Bad.exe");

            Assert.False(outcome.Succeeded);
            Assert.Equal(ErrorCode.AnalysisFailed, outcome.ErrorCode);
            Assert.Null(outcome.OutputPath);
        }
        finally
        {
            Directory.Delete(source, recursive: true);
        }
    }

    private async Task<BuildOutcome> BuildAsync(string source, string? expectedOutput, string outputName)
    {
        var runner = new ProcessRunner();
        var engine = new BuildEngine(
            new ILanguageFrontend[] { new CSharpFrontend() },
            new ICompilerBackend[] { new NativeAotBackend(runner, "dotnet") },
            Array.Empty<IProtectionProvider>(),
            runner);

        var request = new BuildRequest(
            SourcePath: source,
            Language: SourceLanguage.CSharp,
            TargetOs: TargetOperatingSystem.Windows,
            TargetArch: TargetArchitecture.X64,
            Configuration: BuildConfiguration.Release,
            Optimization: OptimizationProfile.O2,
            ArtifactsRoot: _artifacts,
            OutputFileName: outputName,
            ProtectionEnabled: false,
            ProtectionProviderId: null,
            Timeout: TimeSpan.FromMinutes(10),
            RunSmokeTest: true,
            ExpectedSmokeOutput: expectedOutput);

        return await engine.BuildAsync(request, new BlackCoreConfig(), CancellationToken.None);
    }

    private static async Task RequireNativeAotToolsAsync()
    {
        try
        {
            await MsvcEnvironment.DiscoverAsync(new ProcessRunner(), CancellationToken.None);
        }
        catch (BlackCoreException ex)
        {
            Skip.If(true, $"NativeAOT araç seti bulunamadı, entegrasyon testi atlandı: {ex.Message}");
        }
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
