namespace BlackCore.Engine.Models;

public sealed record Diagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string Message,
    string? FilePath = null,
    int? Line = null);

public sealed record ProcessResult(
    bool Started,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool Cancelled,
    bool OutputTruncated,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    string? StartFailureMessage = null)
{
    public TimeSpan Duration => FinishedUtc - StartedUtc;

    public bool Succeeded => Started && !TimedOut && !Cancelled && ExitCode == 0;
}

/// <summary>Tüm aşama sonuçlarının ortak alanları (şartname 6.6).</summary>
public abstract class StageResult
{
    public bool Succeeded { get; init; }

    public ErrorCode ErrorCode { get; init; } = ErrorCode.None;

    public string Message { get; init; } = string.Empty;

    public DateTimeOffset StartedUtc { get; init; }

    public DateTimeOffset FinishedUtc { get; init; }

    public TimeSpan Duration => FinishedUtc - StartedUtc;

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;

    public IReadOnlyList<string> ProducedFiles { get; init; } = Array.Empty<string>();

    public string? ToolVersion { get; init; }
}

public sealed class AnalysisResult : StageResult
{
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } = Array.Empty<Diagnostic>();
}

public sealed class CompilationResult : StageResult
{
    public string? OutputPath { get; init; }
}

public sealed class ProtectionResult : StageResult
{
    public string? ProviderId { get; init; }

    public string? OutputPath { get; init; }
}
