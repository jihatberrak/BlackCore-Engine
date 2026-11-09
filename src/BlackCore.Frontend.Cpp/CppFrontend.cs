using System.Text.RegularExpressions;
using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Models;
using BlackCore.Infrastructure.Toolchains;

namespace BlackCore.Frontend.Cpp;

/// <summary>
/// C++ frontend'i: Clang ile sözdizimi/semantik kontrolü (-fsyntax-only) ve diagnostic üretimi.
/// Derleme hataları (parse/compile) analiz aşamasında yakalanır ve build'i durdurur.
/// </summary>
public sealed partial class CppFrontend : ILanguageFrontend
{
    public const string Id = "cpp";
    public const string TargetTriple = "x86_64-pc-windows-msvc";

    private static readonly TimeSpan PerFileTimeout = TimeSpan.FromMinutes(2);

    private readonly CppToolchainProvider _toolchains;
    private readonly IExternalToolRunner _runner;

    public CppFrontend(CppToolchainProvider toolchains, IExternalToolRunner runner)
    {
        _toolchains = toolchains ?? throw new ArgumentNullException(nameof(toolchains));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public string LanguageId => Id;

    public async Task<AnalysisResult> AnalyzeAsync(BuildRequest request, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;

        CppToolchain toolchain;
        try
        {
            toolchain = await _toolchains.GetAsync(cancellationToken);
        }
        catch (BlackCoreException ex)
        {
            return Failed(started, ex.ErrorCode, ex.Message);
        }

        var sources = CppSources.Enumerate(request.SourcePath);
        if (sources.Count == 0)
        {
            return Failed(started, ErrorCode.SourceNotFound, $"C++ kaynak dosyası (.cpp) bulunamadı: {request.SourcePath}");
        }

        var diagnostics = new List<Diagnostic>();
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();

        foreach (var source in sources)
        {
            var result = await ToolInvocation.RunAsync(
                _runner,
                toolchain.Llvm.Clang,
                new[] { "-std=c++20", $"--target={TargetTriple}", "-fsyntax-only", source },
                PerFileTimeout,
                Path.GetDirectoryName(source),
                toolchain.Environment,
                cancellationToken);

            stdout.Append(result.StandardOutput);
            stderr.Append(result.StandardError);

            var parsed = ClangDiagnosticParser.Parse(result.StandardError);
            diagnostics.AddRange(parsed);

            if (!result.Started || result.TimedOut || result.Cancelled)
            {
                var failure = ToolInvocation.Classify(result, "clang++", ErrorCode.ToolFailed)!;
                return Failed(started, failure.ErrorCode, failure.Message, diagnostics, stdout.ToString(), stderr.ToString());
            }

            // Çıkış kodu sıfır değilse ama ayrıştırılabilir hata yoksa genel bir hata ekle.
            if (result.ExitCode != 0 && parsed.All(d => d.Severity != DiagnosticSeverity.Error))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticSeverity.Error,
                    "CLANG_FAILED",
                    $"clang++ sözdizimi kontrolü başarısız (çıkış {result.ExitCode}).",
                    source));
            }
        }

        var firstError = diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        var hasErrors = firstError is not null;
        var message = hasErrors
            ? $"C++ analizi hata buldu: {firstError!.FilePath}:{firstError.Line}: {firstError.Message}"
            : $"C++ analizi tamamlandı ({sources.Count} dosya).";

        return new AnalysisResult
        {
            Succeeded = !hasErrors,
            ErrorCode = hasErrors ? ErrorCode.AnalysisFailed : ErrorCode.None,
            Message = message,
            Diagnostics = diagnostics,
            StandardOutput = stdout.ToString(),
            StandardError = stderr.ToString(),
            ToolVersion = toolchain.Llvm.Version,
            StartedUtc = started,
            FinishedUtc = DateTimeOffset.UtcNow,
            ProducedFiles = Array.Empty<string>(),
        };
    }

    private static AnalysisResult Failed(
        DateTimeOffset started,
        ErrorCode code,
        string message,
        List<Diagnostic>? diagnostics = null,
        string stdout = "",
        string stderr = "") => new()
    {
        Succeeded = false,
        ErrorCode = code,
        Message = message,
        Diagnostics = diagnostics ?? new List<Diagnostic>(),
        StandardOutput = stdout,
        StandardError = stderr,
        StartedUtc = started,
        FinishedUtc = DateTimeOffset.UtcNow,
    };
}
