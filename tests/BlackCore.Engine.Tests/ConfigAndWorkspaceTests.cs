using System.Text.RegularExpressions;
using BlackCore.Engine.Configuration;
using BlackCore.Engine.Models;
using BlackCore.Engine.Workspace;

namespace BlackCore.Engine.Tests;

public sealed class ConfigLoaderTests
{
    // Şartname 12. bölümdeki örnek yapılandırma.
    private const string SpecExample = """
    {
      "version": 1,
      "target": { "operatingSystem": "windows", "architecture": "x64" },
      "build": { "configuration": "Release", "optimization": "O2", "outputDirectory": "artifacts" },
      "toolchains": { "llvmDirectory": "C:/Program Files/LLVM", "dotnetExecutable": "dotnet" },
      "analysis": { "failOnWarnings": false, "runStaticAnalysis": true },
      "protection": { "enabled": false, "provider": null },
      "execution": { "toolTimeoutSeconds": 300, "smokeTestTimeoutSeconds": 15 }
    }
    """;

    [Fact]
    public void ParsesSpecExample()
    {
        var config = ConfigLoader.Parse(SpecExample);

        Assert.Equal("O2", config.Build.Optimization);
        Assert.Equal(300, config.Execution.ToolTimeoutSeconds);
        Assert.False(config.Protection.Enabled);
    }

    [Fact]
    public void RejectsUnknownKey()
    {
        var json = SpecExample.Replace("\"optimization\": \"O2\"", "\"optimizaton\": \"O2\"");

        var ex = Assert.Throws<BlackCoreException>(() => ConfigLoader.Parse(json));

        Assert.Equal(ErrorCode.ConfigInvalid, ex.ErrorCode);
    }

    [Fact]
    public void RejectsUnsupportedVersion()
    {
        var json = SpecExample.Replace("\"version\": 1", "\"version\": 2");

        var ex = Assert.Throws<BlackCoreException>(() => ConfigLoader.Parse(json));

        Assert.Equal(ErrorCode.ConfigVersionUnsupported, ex.ErrorCode);
    }

    [Fact]
    public void RejectsProtectionWithoutProvider()
    {
        var json = SpecExample.Replace("\"enabled\": false", "\"enabled\": true");

        var ex = Assert.Throws<BlackCoreException>(() => ConfigLoader.Parse(json));

        Assert.Equal(ErrorCode.ConfigInvalid, ex.ErrorCode);
    }

    [Fact]
    public void RejectsUnknownOptimizationProfile()
    {
        var json = SpecExample.Replace("\"O2\"", "\"O9\"");

        var ex = Assert.Throws<BlackCoreException>(() => ConfigLoader.Parse(json));

        Assert.Equal(ErrorCode.ConfigInvalid, ex.ErrorCode);
    }
}

public sealed class WorkspaceTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void CreatesStandardFoldersAndTimestampedId()
    {
        var workspace = BuildWorkspace.Create(_temp.FullPath, new DateTimeOffset(2026, 11, 7, 15, 0, 0, TimeSpan.Zero));

        Assert.Matches(new Regex(@"^2026-11-07_150000_[A-Z2-9]{6}$"), workspace.BuildId);
        Assert.True(Directory.Exists(workspace.WorkDirectory));
        Assert.True(Directory.Exists(workspace.IntermediateDirectory));
        Assert.True(Directory.Exists(workspace.LogsDirectory));
        Assert.True(Directory.Exists(workspace.ReportsDirectory));
        Assert.True(Directory.Exists(workspace.OutputDirectory));
    }

    [Fact]
    public void EachBuildGetsIsolatedFolder()
    {
        var now = DateTimeOffset.UtcNow;

        var first = BuildWorkspace.Create(_temp.FullPath, now);
        var second = BuildWorkspace.Create(_temp.FullPath, now);

        Assert.NotEqual(first.BuildId, second.BuildId);
        Assert.NotEqual(first.Root, second.Root);
    }
}
