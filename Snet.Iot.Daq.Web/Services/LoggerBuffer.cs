using System.Threading.Channels;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 日志缓冲：环形缓冲（快照）+ 事件推送（供控制台实时显示）
/// </summary>
public class LoggerBuffer
{
    private const int MaxLines = 2000;
    private readonly List<string> _lines = new();
    private readonly object _lock = new();

    public event Action? Changed;

    public void Push(string line)
    {
        lock (_lock)
        {
            _lines.Add(line);
            if (_lines.Count > MaxLines)
                _lines.RemoveRange(0, _lines.Count - MaxLines);
        }
        Changed?.Invoke();
    }

    public List<string> Snapshot()
    {
        lock (_lock)
            return _lines.ToList();
    }

    public void Clear()
    {
        lock (_lock)
            _lines.Clear();
        Changed?.Invoke();
    }
}
