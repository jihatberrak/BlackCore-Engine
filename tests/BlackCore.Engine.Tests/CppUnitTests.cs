using BlackCore.Backend.LLVM;
using BlackCore.Engine.Models;
using BlackCore.Frontend.Cpp;

namespace BlackCore.Engine.Tests;

public sealed class ClangDiagnosticParserTests
{
    [Fact]
    public void ParsesErrorWithWindowsDrivePath()
    {
        const string stderr = @"C:\src\main.cpp:3:5: error: expected ';' after expression";

        var diagnostics = ClangDiagnosticParser.Parse(stderr);

        var d = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Equal(@"C:\src\main.cpp", d.FilePath);
        Assert.Equal(3, d.Line);
        Assert.Equal("expected ';' after expression", d.Message);
    }

    [Fact]
    public void ParsesWarningsAndFatalErrorsButSkipsNotes()
    {
        const string stderr =
            "a.cpp:1:1: warning: unused variable 'x'\n" +
            "a.cpp:1:1: note: declared here\n" +
            "b.cpp:2:2: fatal error: 'missing.h' file not found\n";

        var diagnostics = ClangDiagnosticParser.Parse(stderr);

        Assert.Equal(2, diagnostics.Count);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostics[0].Severity);
        Assert.Equal(DiagnosticSeverity.Error, diagnostics[1].Severity);
    }

    [Fact]
    public void EmptyStderrYieldsNoDiagnostics()
    {
        Assert.Empty(ClangDiagnosticParser.Parse(string.Empty));
    }
}

public sealed class LlvmPassPipelineTests
{
    [Theory]
    [InlineData(OptimizationProfile.None, "verify")]
    [InlineData(OptimizationProfile.O1, "verify,default<O1>,verify")]
    [InlineData(OptimizationProfile.O2, "verify,default<O2>,verify")]
    [InlineData(OptimizationProfile.O3, "verify,default<O3>,verify")]
    [InlineData(OptimizationProfile.Os, "verify,default<Os>,verify")]
    [InlineData(OptimizationProfile.Oz, "verify,default<Oz>,verify")]
    public void EveryProfileRunsVerifyBeforeAndAfter(OptimizationProfile profile, string expected)
    {
        Assert.Equal(expected, LlvmBackend.PassPipeline(profile));
    }
}
