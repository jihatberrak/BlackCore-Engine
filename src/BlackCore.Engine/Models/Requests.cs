namespace BlackCore.Engine.Models;

/// <summary>Derleme isteği (şartname 6.5).</summary>
public sealed record BuildRequest(
    string SourcePath,
    SourceLanguage Language,
    TargetOperatingSystem TargetOs,
    TargetArchitecture TargetArch,
    BuildConfiguration Configuration,
    OptimizationProfile Optimization,
    string ArtifactsRoot,
    string OutputFileName,
    bool ProtectionEnabled,
    string? ProtectionProviderId,
    TimeSpan Timeout,
    bool RunSmokeTest = false,
    string? ExpectedSmokeOutput = null);

/// <summary>Harici araç çağrısı. Yürütülebilir yol ve argümanlar ayrı alanlardadır (şartname 6.4).</summary>
public sealed record ProcessRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null);

public sealed record ProtectionRequest(
    string InputPath,
    string OutputPath,
    string ProviderId,
    TimeSpan Timeout);
