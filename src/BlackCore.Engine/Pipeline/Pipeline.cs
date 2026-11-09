using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Configuration;
using BlackCore.Engine.Diagnostics;
using BlackCore.Engine.Models;
using BlackCore.Engine.Workspace;

namespace BlackCore.Engine.Pipeline;

/// <summary>Tek bir pipeline adımı (şartname 7). Her adım ayrı test edilebilir ve loglanabilir.</summary>
public interface IPipelineStep
{
    string Name { get; }

    Task<StepOutcome> ExecuteAsync(BuildContext context, CancellationToken cancellationToken);
}

public sealed record StepOutcome(bool Succeeded, ErrorCode ErrorCode, string Message, bool Skipped = false)
{
    public static StepOutcome Ok(string message) => new(true, ErrorCode.None, message);

    public static StepOutcome Skip(string message) => new(true, ErrorCode.None, message, Skipped: true);

    public static StepOutcome Fail(ErrorCode errorCode, string message) => new(false, errorCode, message);
}

/// <summary>Adımları sırayla çalıştırır; bir adım başarısız olursa sonraki adımları durdurur.</summary>
public sealed class BuildPipeline
{
    private readonly IReadOnlyList<IPipelineStep> _steps;

    public BuildPipeline(IEnumerable<IPipelineStep> steps)
    {
        _steps = steps.ToList();
    }

    /// <summary>Başarılıysa Succeeded=true olan son sonucu, başarısızsa ilk hatayı döndürür.</summary>
    public async Task<StepOutcome> RunAsync(BuildContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var step in _steps)
        {
            var started = context.Time.GetUtcNow();
            StepOutcome outcome;

            try
            {
                outcome = await step.ExecuteAsync(context, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                outcome = StepOutcome.Fail(ErrorCode.Cancelled, "Derleme iptal edildi.");
            }
            catch (BlackCoreException ex)
            {
                outcome = StepOutcome.Fail(ex.ErrorCode, ex.Message);
            }
            catch (Exception ex)
            {
                // Ayrıntı yalnızca loga yazılır; kullanıcıya iç hata ayrıntısı sızdırılmaz.
                context.Log.Error(step.Name, $"Beklenmeyen hata: {ex}");
                outcome = StepOutcome.Fail(ErrorCode.Internal, "Beklenmeyen iç hata (ayrıntılar logda).");
            }

            var finished = context.Time.GetUtcNow();
            context.Stages.Add(new StageReport
            {
                Name = step.Name,
                Succeeded = outcome.Succeeded,
                Skipped = outcome.Skipped,
                ErrorCode = outcome.ErrorCode.ToString(),
                Message = outcome.Message,
                StartedUtc = started,
                FinishedUtc = finished,
                DurationMs = (finished - started).TotalMilliseconds,
            });

            if (outcome.Skipped)
            {
                context.Log.Info(step.Name, $"Atlandı: {outcome.Message}");
            }
            else if (outcome.Succeeded)
            {
                context.Log.Info(step.Name, outcome.Message);
            }
            else
            {
                context.Log.Error(step.Name, outcome.Message);
                return outcome;
            }
        }

        return StepOutcome.Ok("Tüm adımlar tamamlandı.");
    }
}

/// <summary>Bir derlemenin adımlar arası paylaşılan durumu.</summary>
public sealed class BuildContext
{
    public BuildContext(BuildRequest request, BlackCoreConfig config, TimeProvider time, IExternalToolRunner tools)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Time = time ?? throw new ArgumentNullException(nameof(time));
        Tools = tools ?? throw new ArgumentNullException(nameof(tools));
        Log = new BuildLog(time);
        StartedUtc = time.GetUtcNow();
    }

    public BuildRequest Request { get; }

    public BlackCoreConfig Config { get; }

    public TimeProvider Time { get; }

    public IExternalToolRunner Tools { get; }

    public BuildLog Log { get; }

    public DateTimeOffset StartedUtc { get; }

    public BuildWorkspace? Workspace { get; set; }

    public AnalysisResult? Analysis { get; set; }

    public CompilationResult? Compilation { get; set; }

    public ProtectionResult? Protection { get; set; }

    /// <summary>Yayımlama adımına girecek, doğrulanmış aday dosya.</summary>
    public string? PublishCandidate { get; set; }

    public string? PublishedOutputPath { get; set; }

    public List<StageReport> Stages { get; } = new();

    public Dictionary<string, string> ToolVersions { get; } = new();

    public List<string> Warnings { get; } = new();
}
