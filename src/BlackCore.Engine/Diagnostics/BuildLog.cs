using System.Text;
using System.Text.Json;

namespace BlackCore.Engine.Diagnostics;

/// <summary>
/// Yapılandırılmış (JSON satırları) log. Çalışma klasörü oluşturulana kadar kayıtlar bellekte tutulur,
/// dosya bağlanınca yazılır.
/// </summary>
public sealed class BuildLog : IDisposable
{
    private readonly TimeProvider _time;
    private readonly List<string> _buffer = new();
    private readonly object _gate = new();
    private StreamWriter? _writer;

    public BuildLog(TimeProvider time)
    {
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    public void Info(string stage, string message) => Write("info", stage, message);

    public void Warning(string stage, string message) => Write("warning", stage, message);

    public void Error(string stage, string message) => Write("error", stage, message);

    public void AttachFile(string path)
    {
        lock (_gate)
        {
            _writer = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };
            foreach (var line in _buffer)
            {
                _writer.WriteLine(line);
            }

            _buffer.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void Write(string level, string stage, string message)
    {
        var line = JsonSerializer.Serialize(new
        {
            ts = _time.GetUtcNow(),
            level,
            stage,
            message,
        });

        lock (_gate)
        {
            if (_writer is null)
            {
                _buffer.Add(line);
            }
            else
            {
                _writer.WriteLine(line);
            }
        }
    }
}
