using System.Text.Json;

namespace BlackCore.Engine.Diagnostics;

/// <summary>Tekil build raporu (şartname 14). Her derleme için yazılır, başarılı da olsa başarısız da olsa.</summary>
public sealed class BuildReport
{
    public string BuildId { get; init; } = string.Empty;

    public DateTimeOffset StartedUtc { get; init; }

    public DateTimeOffset FinishedUtc { get; init; }

    public bool Succeeded { get; init; }

    public string ErrorCode { get; init; } = "None";

    public string Message { get; init; } = string.Empty;

    public string SourcePath { get; init; } = string.Empty;

    public string Language { get; init; } = string.Empty;

    public string TargetOperatingSystem { get; init; } = string.Empty;

    public string TargetArchitecture { get; init; } = string.Empty;

    public string Configuration { get; init; } = string.Empty;

    public string Optimization { get; init; } = string.Empty;

    public bool ProtectionEnabled { get; init; }

    public string? ProtectionProviderId { get; init; }

    public Dictionary<string, string> ToolVersions { get; } = new();

    /// <summary>Analiz bulgularının özeti; ayrıntı <c>reports/analysis-report.json</c> içindedir.</summary>
    public int? AnalysisErrorCount { get; set; }

    public int? AnalysisWarningCount { get; set; }

    public string? AnalysisReportPath { get; set; }

    public List<StageReport> Stages { get; } = new();

    public List<string> Warnings { get; } = new();

    public List<string> Errors { get; } = new();

    public string? OutputPath { get; set; }

    public long? OutputSizeBytes { get; set; }

    public string? OutputSha256 { get; set; }

    public void WriteTo(string path)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}

public sealed class StageReport
{
    public string Name { get; init; } = string.Empty;

    public bool Succeeded { get; init; }

    public bool Skipped { get; init; }

    public string ErrorCode { get; init; } = "None";

    public string Message { get; init; } = string.Empty;

    public DateTimeOffset StartedUtc { get; init; }

    public DateTimeOffset FinishedUtc { get; init; }

    public double DurationMs { get; init; }
}
