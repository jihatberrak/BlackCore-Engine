namespace BlackCore.Engine.Models;

public enum SourceLanguage
{
    Cpp,
    CSharp,
}

public enum BuildConfiguration
{
    Debug,
    Release,
}

public enum OptimizationProfile
{
    None,
    O1,
    O2,
    O3,
    Os,
    Oz,
}

public enum TargetOperatingSystem
{
    Windows,
}

public enum TargetArchitecture
{
    X64,
}

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error,
}

public enum ErrorCode
{
    None,
    ConfigInvalid,
    ConfigVersionUnsupported,
    RequestInvalid,
    SourceNotFound,
    WorkspaceFailed,
    ComponentNotFound,
    AnalysisFailed,
    AnalysisPolicyViolation,
    CompilationFailed,
    ToolNotFound,
    ToolchainMismatch,
    ToolFailed,
    ToolTimeout,
    OutputInvalid,
    SmokeTestFailed,
    ProtectionProviderNotFound,
    ProtectionFailed,
    PublishFailed,
    Cancelled,
    Internal,
}
