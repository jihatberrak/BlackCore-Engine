using System.Security.Cryptography;
using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Configuration;
using BlackCore.Engine.Diagnostics;
using BlackCore.Engine.Models;
using BlackCore.Engine.Pipeline.Steps;

namespace BlackCore.Engine.Pipeline;

public sealed record BuildOutcome(
    bool Succeeded,
    ErrorCode ErrorCode,
    string Message,
    string? BuildId,
    string? OutputPath,
    string? ReportPath);

/// <summary>
/// Derleme giriş noktası. Dil → frontend ve dil → backend eşlemesi burada yapılır;
/// adımlar sırayla <see cref="BuildPipeline"/> içinde çalışır.
/// </summary>
public sealed class BuildEngine
{
    private static readonly IReadOnlyDictionary<SourceLanguage, string> FrontendIds =
        new Dictionary<SourceLanguage, string>
        {
            [SourceLanguage.Cpp] = "cpp",
            [SourceLanguage.CSharp] = "csharp",
        };

    private static readonly IReadOnlyDictionary<SourceLanguage, string> BackendIds =
        new Dictionary<SourceLanguage, string>
        {
            [SourceLanguage.Cpp] = "llvm",
            [SourceLanguage.CSharp] = "nativeaot",
        };

    private readonly IReadOnlyList<ILanguageFrontend> _frontends;
    private readonly IReadOnlyList<ICompilerBackend> _backends;
    private readonly IReadOnlyList<IProtectionProvider> _providers;
    private readonly IExternalToolRunner _tools;
    private readonly TimeProvider _time;

    public BuildEngine(
        IEnumerable<ILanguageFrontend> frontends,
        IEnumerable<ICompilerBackend> backends,
        IEnumerable<IProtectionProvider> providers,
        IExternalToolRunner tools,
        TimeProvider? time = null)
    {
        _frontends = frontends.ToList();
        _backends = backends.ToList();
        _providers = providers.ToList();
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _time = time ?? TimeProvider.System;
    }

    public async Task<BuildOutcome> BuildAsync(
        BuildRequest request,
        BlackCoreConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(config);

        var context = new BuildContext(request, config, _time, _tools);

        var frontend = _frontends.FirstOrDefault(f => f.LanguageId == FrontendIds[request.Language]);
        var backend = _backends.FirstOrDefault(b => b.BackendId == BackendIds[request.Language]);

        var pipeline = new BuildPipeline(new IPipelineStep[]
        {
            new ValidateRequestStep(),
            new CheckSourceStep(),
            new CreateWorkspaceStep(),
            new AnalyzeStep(frontend),
            new CompileStep(backend),
            new ValidateCompilationStep(),
            new SmokeTestStep(),
            new ProtectionStep(_providers),
            new PublishStep(),
        });

        var outcome = await pipeline.RunAsync(context, cancellationToken);

        // Çalışma klasörü hiç oluşmadıysa yazılacak rapor da yoktur.
        if (context.Workspace is null)
        {
            context.Log.Dispose();
            return new BuildOutcome(outcome.Succeeded, outcome.ErrorCode, outcome.Message, null, null, null);
        }

        var finished = _time.GetUtcNow();
        var report = CreateReport(context, outcome, finished);

        var reportPath = Path.Combine(context.Workspace.ReportsDirectory, "build-report.json");
        report.WriteTo(reportPath);

        // Analyzer raporu: tüm analiz bulguları (hata, uyarı, NativeAOT riski) ayrı dosyada.
        if (context.Analysis is not null)
        {
            var analysisReportPath = Path.Combine(context.Workspace.ReportsDirectory, "analysis-report.json");
            var findings = context.Analysis.Diagnostics.Select(d => new
            {
                severity = d.Severity.ToString(),
                code = d.Code,
                message = d.Message,
                file = d.FilePath,
                line = d.Line,
            });
            File.WriteAllText(
                analysisReportPath,
                System.Text.Json.JsonSerializer.Serialize(
                    new { language = context.Request.Language.ToString(), count = context.Analysis.Diagnostics.Count, findings },
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }

        context.Log.Info("Report", outcome.Succeeded ? "Derleme başarılı." : $"Derleme başarısız: {outcome.Message}");
        context.Log.Dispose();

        return new BuildOutcome(
            outcome.Succeeded,
            outcome.ErrorCode,
            outcome.Message,
            context.Workspace.BuildId,
            context.PublishedOutputPath,
            reportPath);
    }

    private static BuildReport CreateReport(BuildContext context, StepOutcome outcome, DateTimeOffset finished)
    {
        var r = context.Request;
        var report = new BuildReport
        {
            BuildId = context.Workspace!.BuildId,
            StartedUtc = context.StartedUtc,
            FinishedUtc = finished,
            Succeeded = outcome.Succeeded,
            ErrorCode = outcome.ErrorCode.ToString(),
            Message = outcome.Message,
            SourcePath = r.SourcePath,
            Language = r.Language.ToString(),
            TargetOperatingSystem = r.TargetOs.ToString(),
            TargetArchitecture = r.TargetArch.ToString(),
            Configuration = r.Configuration.ToString(),
            Optimization = r.Optimization.ToString(),
            ProtectionEnabled = r.ProtectionEnabled,
            ProtectionProviderId = r.ProtectionProviderId,
        };

        foreach (var (key, value) in context.ToolVersions)
        {
            report.ToolVersions[key] = value;
        }

        if (context.Analysis is not null)
        {
            report.AnalysisErrorCount = context.Analysis.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
            report.AnalysisWarningCount = context.Analysis.Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
            report.AnalysisReportPath = Path.Combine(context.Workspace!.ReportsDirectory, "analysis-report.json");
        }

        report.Stages.AddRange(context.Stages);
        report.Warnings.AddRange(context.Warnings);

        if (!outcome.Succeeded)
        {
            report.Errors.Add(outcome.Message);
        }

        if (outcome.Succeeded && context.PublishedOutputPath is { } output)
        {
            report.OutputPath = output;
            report.OutputSizeBytes = new FileInfo(output).Length;
            using var stream = File.OpenRead(output);
            report.OutputSha256 = Convert.ToHexString(SHA256.HashData(stream));
        }

        return report;
    }
}
