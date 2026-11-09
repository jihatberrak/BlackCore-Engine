using BlackCore.Backend.NativeAot;
using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Models;
using BlackCore.Engine.Workspace;
using BlackCore.Frontend.CSharp;

namespace BlackCore.Engine.Tests;

public sealed class CSharpFrontendTests : IDisposable
{
    private const string Csproj = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>
        """;

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Project(string programSource)
    {
        File.WriteAllText(Path.Combine(_temp.FullPath, "App.csproj"), Csproj);
        File.WriteAllText(Path.Combine(_temp.FullPath, "Program.cs"), programSource);
        return _temp.FullPath;
    }

    private static BuildRequest Request(string source) => new(
        SourcePath: source,
        Language: SourceLanguage.CSharp,
        TargetOs: TargetOperatingSystem.Windows,
        TargetArch: TargetArchitecture.X64,
        Configuration: BuildConfiguration.Release,
        Optimization: OptimizationProfile.O2,
        ArtifactsRoot: "unused",
        OutputFileName: "App.exe",
        ProtectionEnabled: false,
        ProtectionProviderId: null,
        Timeout: TimeSpan.FromMinutes(1));

    [Fact]
    public async Task ValidProjectAnalyzesWithoutErrors()
    {
        var source = Project("Console.WriteLine(\"hi\");");

        var result = await new CSharpFrontend().AnalyzeAsync(Request(source), default);

        Assert.True(result.Succeeded, result.Message);
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public async Task SyntaxErrorFailsAnalysisWithLocation()
    {
        var source = Project("Console.WriteLine(\"hi\"")
            ;

        var result = await new CSharpFrontend().AnalyzeAsync(Request(source), default);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCode.AnalysisFailed, result.ErrorCode);
        // Bir eksik ")" birden çok sözdizimi hatası üretebilir; ilk hatanın konumu doğrulanır.
        var error = result.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
        Assert.EndsWith("Program.cs", error.FilePath);
        Assert.Equal(1, error.Line);
    }

    [Fact]
    public async Task SemanticErrorIsReported()
    {
        var source = Project("int x = \"not an int\";");

        var result = await new CSharpFrontend().AnalyzeAsync(Request(source), default);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task ReflectionCallWarnsForNativeAotButDoesNotFailBuild()
    {
        var source = Project("var o = Activator.CreateInstance(typeof(string));");

        var result = await new CSharpFrontend().AnalyzeAsync(Request(source), default);

        Assert.True(result.Succeeded, result.Message);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == CSharpFrontend.NativeAotRuleCode && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task MissingProjectFileFails()
    {
        File.WriteAllText(Path.Combine(_temp.FullPath, "Program.cs"), "class A {}");

        var result = await new CSharpFrontend().AnalyzeAsync(Request(_temp.FullPath), default);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCode.SourceNotFound, result.ErrorCode);
    }
}

public sealed class NativeAotBackendTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>Gerçek dotnet yerine, publish argümanlarını kaydeden ve sahte bir EXE üreten çalıştırıcı.</summary>
    private sealed class RecordingRunner : IExternalToolRunner
    {
        public List<ProcessRequest> Calls { get; } = new();

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            Calls.Add(request);
            var now = DateTimeOffset.UtcNow;

            if (request.Arguments.Count > 0 && request.Arguments[0] == "publish")
            {
                var outputIndex = request.Arguments.ToList().IndexOf("-o");
                var outputDir = request.Arguments[outputIndex + 1];
                Directory.CreateDirectory(outputDir);
                TestPe.Write(Path.Combine(outputDir, "App.exe"));
            }

            return Task.FromResult(new ProcessResult(true, 0, "10.0.100\n", string.Empty, false, false, false, now, now));
        }
    }

    [Fact]
    public async Task PublishUsesNativeAotForWin64AndIsolatesTheProject()
    {
        var project = Path.Combine(_temp.FullPath, "src");
        Directory.CreateDirectory(Path.Combine(project, "bin", "Release"));
        Directory.CreateDirectory(Path.Combine(project, "obj"));
        File.WriteAllText(Path.Combine(project, "App.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(project, "Program.cs"), "class P {}");
        File.WriteAllText(Path.Combine(project, "bin", "Release", "stale.dll"), "x");

        var workspace = BuildWorkspace.Create(Path.Combine(_temp.FullPath, "artifacts"), DateTimeOffset.UtcNow);
        var runner = new RecordingRunner();
        var backend = new NativeAotBackend(runner, "dotnet");
        var request = new BuildRequest(
            project, SourceLanguage.CSharp, TargetOperatingSystem.Windows, TargetArchitecture.X64,
            BuildConfiguration.Release, OptimizationProfile.O2, "unused", "App.exe", false, null, TimeSpan.FromMinutes(1));

        var result = await backend.CompileAsync(request, new AnalysisResult { Succeeded = true }, workspace, default);

        Assert.True(result.Succeeded, result.Message);
        Assert.EndsWith("App.exe", result.OutputPath);
        Assert.Equal("10.0.100", result.ToolVersion);

        var publish = runner.Calls.Single(c => c.Arguments[0] == "publish");
        Assert.Equal("dotnet", publish.ExecutablePath);
        Assert.Contains("-p:PublishAot=true", publish.Arguments);
        Assert.Equal("win-x64", publish.Arguments[publish.Arguments.ToList().IndexOf("-r") + 1]);

        // Kaynak ağacındaki bin/obj çıktıları izole kopyaya alınmaz.
        var isolated = Path.Combine(workspace.WorkDirectory, "project");
        Assert.True(File.Exists(Path.Combine(isolated, "Program.cs")));
        Assert.False(Directory.Exists(Path.Combine(isolated, "bin")));
        Assert.False(Directory.Exists(Path.Combine(isolated, "obj")));
    }

    [Fact]
    public async Task MissingProjectFailsWithoutRunningDotnet()
    {
        var workspace = BuildWorkspace.Create(Path.Combine(_temp.FullPath, "artifacts"), DateTimeOffset.UtcNow);
        var runner = new RecordingRunner();
        var request = new BuildRequest(
            _temp.FullPath, SourceLanguage.CSharp, TargetOperatingSystem.Windows, TargetArchitecture.X64,
            BuildConfiguration.Release, OptimizationProfile.O2, "unused", "App.exe", false, null, TimeSpan.FromMinutes(1));

        var result = await new NativeAotBackend(runner).CompileAsync(request, new AnalysisResult { Succeeded = true }, workspace, default);

        Assert.False(result.Succeeded);
        Assert.Empty(runner.Calls);
    }
}
