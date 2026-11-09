using BlackCore.Engine.Artifacts;
using BlackCore.Engine.Models;
using BlackCore.Infrastructure.Processes;

namespace BlackCore.Engine.Tests;

public sealed class ArtifactValidatorTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void AcceptsAmd64PortableExecutable()
    {
        var path = Path.Combine(_temp.FullPath, "ok.exe");
        TestPe.Write(path);

        var result = ArtifactValidator.ValidatePortableExecutable(path, TargetArchitecture.X64);

        Assert.True(result.Valid, result.Message);
    }

    [Fact]
    public void RejectsMissingFile()
    {
        var result = ArtifactValidator.ValidatePortableExecutable(Path.Combine(_temp.FullPath, "yok.exe"), TargetArchitecture.X64);

        Assert.False(result.Valid);
    }

    [Fact]
    public void RejectsEmptyFile()
    {
        var path = Path.Combine(_temp.FullPath, "empty.exe");
        File.WriteAllBytes(path, Array.Empty<byte>());

        var result = ArtifactValidator.ValidatePortableExecutable(path, TargetArchitecture.X64);

        Assert.False(result.Valid);
        Assert.Contains("sıfır bayt", result.Message);
    }

    [Fact]
    public void RejectsNonPeFile()
    {
        var path = Path.Combine(_temp.FullPath, "text.exe");
        File.WriteAllText(path, new string('x', 200));

        var result = ArtifactValidator.ValidatePortableExecutable(path, TargetArchitecture.X64);

        Assert.False(result.Valid);
        Assert.Contains("MZ", result.Message);
    }

    [Fact]
    public void RejectsWrongMachineType()
    {
        var path = Path.Combine(_temp.FullPath, "x86.exe");
        TestPe.Write(path, TestPe.I386);

        var result = ArtifactValidator.ValidatePortableExecutable(path, TargetArchitecture.X64);

        Assert.False(result.Valid);
        Assert.Contains("Mimari uyumsuz", result.Message);
    }
}

public sealed class ProcessRunnerTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task CapturesStdoutOfSuccessfulProcess()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            new ProcessRequest("cmd.exe", new[] { "/c", "echo", "merhaba" }, Generous),
            default);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("merhaba", result.StandardOutput);
    }

    [Fact]
    public async Task MissingExecutableReportsStartFailure()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            new ProcessRequest(@"C:\bu\yol\yok\tool.exe", Array.Empty<string>(), Generous),
            default);

        Assert.False(result.Started);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.StartFailureMessage);
    }

    [Fact]
    public async Task TimeoutKillsLongRunningProcess()
    {
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            new ProcessRequest("cmd.exe", new[] { "/c", "ping", "-n", "60", "127.0.0.1" }, TimeSpan.FromMilliseconds(500)),
            default);

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.True(result.Duration < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task CancellationStopsProcess()
    {
        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var result = await runner.RunAsync(
            new ProcessRequest("cmd.exe", new[] { "/c", "ping", "-n", "60", "127.0.0.1" }, Generous),
            cts.Token);

        Assert.True(result.Cancelled);
        Assert.False(result.TimedOut);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task OutputIsBoundedAndFlagged()
    {
        var runner = new ProcessRunner(maxCapturedCharacters: 50);

        var result = await runner.RunAsync(
            new ProcessRequest("cmd.exe", new[] { "/c", "for /L %i in (1,1,200) do @echo satir%i" }, Generous),
            default);

        Assert.True(result.OutputTruncated);
        Assert.True(result.StandardOutput.Length <= 50);
        Assert.True(result.Succeeded);
    }
}
