using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using BlackCore.Engine.Abstractions;
using BlackCore.Engine.Models;

namespace BlackCore.Infrastructure.Processes;

/// <summary>
/// Harici süreç çalıştırıcı (şartname 6.4, 15):
/// argümanlar ArgumentList ile ayrı geçirilir (shell birleştirmesi yok), timeout ve iptal desteklenir,
/// süreç ağacı sonlandırılır, stdout/stderr ayrı okunur ve çıktı boyutu sınırlandırılır.
/// </summary>
public sealed class ProcessRunner : IExternalToolRunner
{
    public const int DefaultMaxCapturedCharacters = 1_000_000;

    private static readonly TimeSpan PipeDrainTimeout = TimeSpan.FromSeconds(5);

    private readonly int _maxCapturedCharacters;

    public ProcessRunner(int maxCapturedCharacters = DefaultMaxCapturedCharacters)
    {
        if (maxCapturedCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCapturedCharacters));
        }

        _maxCapturedCharacters = maxCapturedCharacters;
    }

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.ExecutablePath))
        {
            throw new ArgumentException("Yürütülebilir yol boş.", nameof(request));
        }

        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("Timeout pozitif olmalı.", nameof(request));
        }

        var started = DateTimeOffset.UtcNow;

        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        if (request.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (request.Environment is not null)
        {
            foreach (var (key, value) in request.Environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            return new ProcessResult(false, -1, string.Empty, string.Empty, false, false, false, started, DateTimeOffset.UtcNow, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return new ProcessResult(false, -1, string.Empty, string.Empty, false, false, false, started, DateTimeOffset.UtcNow, ex.Message);
        }

        var stdout = new BoundedCapture(_maxCapturedCharacters);
        var stderr = new BoundedCapture(_maxCapturedCharacters);

        // Çıktılar, süreç çalışırken de boşaltılmalı; aksi halde pipe dolup süreç kilitlenir.
        var pumps = Task.WhenAll(
            PumpAsync(process.StandardOutput, stdout),
            PumpAsync(process.StandardError, stderr));

        using var timeoutCts = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var timedOut = false;
        var cancelled = false;

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);

            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
            }
            else
            {
                timedOut = true;
            }
        }

        try
        {
            await pumps.WaitAsync(PipeDrainTimeout);
        }
        catch (TimeoutException)
        {
            // Alt süreç pipe'ı açık tutuyorsa bekleme sınırlandırılır; yakalanan çıktı kullanılır.
        }

        var finished = DateTimeOffset.UtcNow;
        var exitCode = timedOut || cancelled ? -1 : process.ExitCode;

        return new ProcessResult(
            Started: true,
            ExitCode: exitCode,
            StandardOutput: stdout.ToString(),
            StandardError: stderr.ToString(),
            TimedOut: timedOut,
            Cancelled: cancelled,
            OutputTruncated: stdout.Truncated || stderr.Truncated,
            StartedUtc: started,
            FinishedUtc: finished);
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit((int)PipeDrainTimeout.TotalMilliseconds);
            }
        }
        catch (InvalidOperationException)
        {
            // Süreç bu arada bitmiş olabilir.
        }
        catch (Win32Exception)
        {
            // Erişim/durum hatası: sonuç zaten timeout/iptal olarak raporlanır.
        }
    }

    private static async Task PumpAsync(StreamReader reader, BoundedCapture capture)
    {
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory())) > 0)
        {
            capture.Append(buffer.AsSpan(0, read));
        }
    }

    /// <summary>Üst sınırı olan çıktı tamponu; sınır aşılsa da okuma sürer, fazlası atılır.</summary>
    private sealed class BoundedCapture
    {
        private readonly StringBuilder _builder = new();
        private readonly int _limit;
        private readonly object _gate = new();
        private bool _truncated;

        public BoundedCapture(int limit)
        {
            _limit = limit;
        }

        public bool Truncated
        {
            get
            {
                lock (_gate)
                {
                    return _truncated;
                }
            }
        }

        public void Append(ReadOnlySpan<char> text)
        {
            lock (_gate)
            {
                var remaining = _limit - _builder.Length;
                if (remaining <= 0)
                {
                    if (text.Length > 0)
                    {
                        _truncated = true;
                    }

                    return;
                }

                if (text.Length > remaining)
                {
                    _builder.Append(text[..remaining]);
                    _truncated = true;
                }
                else
                {
                    _builder.Append(text);
                }
            }
        }

        public override string ToString()
        {
            lock (_gate)
            {
                return _builder.ToString();
            }
        }
    }
}
