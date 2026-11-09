using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Models;

namespace BlackCore.Infrastructure.Toolchains;

/// <summary>Harici araç çağrılarında ortak yardımcılar.</summary>
public static class ToolInvocation
{
    public static Task<ProcessResult> RunAsync(
        IExternalToolRunner runner,
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        return runner.RunAsync(
            new ProcessRequest(executable, arguments, timeout, workingDirectory, environment),
            cancellationToken);
    }

    /// <summary>Başarısız bir çağrıyı hata koduna ve insan okunur mesaja çevirir. Başarılıysa null.</summary>
    public static StepFailure? Classify(ProcessResult result, string toolName, ErrorCode failureCode)
    {
        if (!result.Started)
        {
            return new StepFailure(ErrorCode.ToolNotFound, $"{toolName} başlatılamadı: {result.StartFailureMessage}");
        }

        if (result.TimedOut)
        {
            return new StepFailure(ErrorCode.ToolTimeout, $"{toolName} zaman aşımına uğradı.");
        }

        if (result.Cancelled)
        {
            return new StepFailure(ErrorCode.Cancelled, $"{toolName} iptal edildi.");
        }

        if (result.ExitCode != 0)
        {
            var detail = FirstErrorLine(result.StandardError) ?? FirstErrorLine(result.StandardOutput);
            var text = detail is null
                ? $"{toolName} çıkış kodu {result.ExitCode}."
                : $"{toolName} başarısız: {detail}";
            return new StepFailure(failureCode, text);
        }

        return null;
    }

    public static string? FirstErrorLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var line = lines.FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)) ?? lines.LastOrDefault();
        if (line is null)
        {
            return null;
        }

        return line.Length > 500 ? line[..500] + "…" : line;
    }
}

public sealed record StepFailure(ErrorCode ErrorCode, string Message);
