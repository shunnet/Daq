namespace Snet.Iot.Daq.Web.Components.Shared;

/// <summary>表示一条可由界面稳定跟踪的 Toast 消息。</summary>
/// <param name="Message">显示文本。</param>
/// <param name="Type">用于样式选择的消息类型。</param>
/// <param name="Id">本次消息的唯一标识。</param>
public sealed record ToastItem(string Message, string Type, Guid Id);

/// <summary>管理当前 Blazor 会话的 Toast 消息，并在作用域释放时终止延时任务。</summary>
public sealed class ToastService : IAsyncDisposable
{
    private readonly object _itemsLock = new();
    private readonly List<ToastItem> _items = [];
    private readonly List<Task> _removalTasks = [];
    private readonly CancellationTokenSource _disposeCts = new();
    private bool _disposed;
    /// <summary>所有释放调用共同等待的清理任务。</summary>
    private Task? _disposalTask;

    /// <summary>在消息集合发生变化后触发。</summary>
    public event Action? OnChanged;

    /// <summary>获取当前消息的只读快照，调用方无法修改服务内部集合。</summary>
    public IReadOnlyList<ToastItem> Items
    {
        get
        {
            lock (_itemsLock)
                return _items.ToArray();
        }
    }

    /// <summary>添加消息，并在指定时长后自动移除。</summary>
    /// <param name="message">显示文本。</param>
    /// <param name="type">用于样式选择的消息类型。</param>
    /// <param name="durationMs">显示毫秒数；小于零的值按零处理。</param>
    public void Show(string message, string type = "info", int durationMs = 4000)
    {
        var item = new ToastItem(message, type, Guid.NewGuid());
        lock (_itemsLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _items.Add(item);
            _removalTasks.RemoveAll(static task => task.IsCompleted);
            _removalTasks.Add(RemoveAfterDelayAsync(item, Math.Max(1, durationMs), _disposeCts.Token));
        }
        NotifyChanged();
    }

    /// <summary>显示普通信息。</summary>
    public void Info(string message) => Show(message, "info");

    /// <summary>显示成功信息。</summary>
    public void Success(string message) => Show(message, "success");

    /// <summary>显示错误信息。</summary>
    public void Error(string message) => Show(message, "error");

    /// <summary>显示警告信息。</summary>
    public void Warning(string message) => Show(message, "warning");

    /// <summary>等待显示时长后移除消息；会话释放导致的取消不作为错误传播。</summary>
    private async Task RemoveAfterDelayAsync(ToastItem item, int durationMs, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(durationMs, cancellationToken).ConfigureAwait(false);
            lock (_itemsLock)
                _items.Remove(item);
            NotifyChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>在集合锁外逐个通知订阅者；页面销毁或订阅者异常不影响后续订阅者与延时任务。</summary>
    private void NotifyChanged()
    {
        var handlers = OnChanged;
        if (handlers is null) return;
        foreach (Action handler in handlers.GetInvocationList())
        {
            try { handler(); }
            catch (Exception ex) { Console.Error.WriteLine($"[Toast] 通知失败: {ex.Message}"); }
        }
    }

    /// <summary>进入终态，取消并等待全部延时任务；并发或重复释放共同等待同一任务。</summary>
    public ValueTask DisposeAsync()
    {
        lock (_itemsLock)
        {
            if (_disposalTask is not null) return new ValueTask(_disposalTask);
            _disposed = true;
            var tasks = _removalTasks.ToArray();
            _items.Clear();
            return new ValueTask(_disposalTask = DisposeCoreAsync(tasks));
        }
    }

    /// <summary>在锁外取消延时任务，等待退出后释放令牌源，即使任务失败也完成资源清理。</summary>
    /// <param name="tasks">进入终态前捕获的本服务任务。</param>
    private async Task DisposeCoreAsync(Task[] tasks)
    {
        await Task.Yield();
        try
        {
            _disposeCts.Cancel();
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        finally
        {
            _disposeCts.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
