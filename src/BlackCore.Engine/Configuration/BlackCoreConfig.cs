namespace BlackCore.Engine.Configuration;

/// <summary>blackcore.json şeması (şartname 12). Bilinmeyen alanlar yükleyicide hata verir.</summary>
public sealed class BlackCoreConfig
{
    public int Version { get; set; } = 1;

    public TargetSection Target { get; set; } = new();

    public BuildSection Build { get; set; } = new();

    public ToolchainSection Toolchains { get; set; } = new();

    public AnalysisSection Analysis { get; set; } = new();

    public ProtectionSection Protection { get; set; } = new();

    public ExecutionSection Execution { get; set; } = new();
}

public sealed class TargetSection
{
    public string OperatingSystem { get; set; } = "windows";

    public string Architecture { get; set; } = "x64";
}

public sealed class BuildSection
{
    public string Configuration { get; set; } = "Release";

    public string Optimization { get; set; } = "O2";

    public string OutputDirectory { get; set; } = "artifacts";
}

public sealed class ToolchainSection
{
    public string? LlvmDirectory { get; set; }

    public string DotnetExecutable { get; set; } = "dotnet";
}

public sealed class AnalysisSection
{
    public bool FailOnWarnings { get; set; }

    public bool RunStaticAnalysis { get; set; } = true;
}

public sealed class ProtectionSection
{
    public bool Enabled { get; set; }

    public string? Provider { get; set; }
}

public sealed class ExecutionSection
{
    public int ToolTimeoutSeconds { get; set; } = 300;

    public int SmokeTestTimeoutSeconds { get; set; } = 15;
}
