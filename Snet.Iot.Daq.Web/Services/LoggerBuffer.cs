namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 日志缓冲：环形缓冲（快照）+ 事件推送（供控制台实时显示）
/// </summary>
public class LoggerBuffer
{
    private const int MaxLines = 2000;
    private readonly List<string> _lines = new();
    private readonly object _lock = new();

    /// <summary>缓冲内容发生变化时触发。</summary>
    public event Action? Changed;

    #region 缓冲操作
    /// <summary>追加一行日志，并在超过容量时丢弃最早的内容。</summary>
    /// <param name="line">要追加的完整日志行。</param>
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

    /// <summary>获取按写入顺序排列的日志行快照。</summary>
    /// <returns>与内部缓冲区独立的列表。</returns>
    public List<string> Snapshot()
    {
        lock (_lock)
            return _lines.ToList();
    }

    /// <summary>清空全部缓冲日志并通知订阅者。</summary>
    public void Clear()
    {
        lock (_lock)
            _lines.Clear();
        Changed?.Invoke();
    }
    #endregion
}
