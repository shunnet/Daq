namespace Snet.Iot.Daq.Web.Components.Shared;

public record ToastItem(string Message, string Type, Guid Id);

/// <summary>Toast 通知服务（Snackbar）。Show 在电路线程、延时移除在线程池执行，Items 必须加锁访问</summary>
public class ToastService
{
    public event Action? OnChanged;

    private readonly object _itemsLock = new();
    private readonly List<ToastItem> _items = new();

    public IReadOnlyList<ToastItem> Items
    {
        get
        {
            lock (_itemsLock) return _items.ToList();
        }
    }

    public void Show(string message, string type = "info", int durationMs = 4000)
    {
        var item = new ToastItem(message, type, Guid.NewGuid());
        lock (_itemsLock) _items.Add(item);
        OnChanged?.Invoke();
        _ = Task.Run(async () =>
        {
            await Task.Delay(durationMs);
            lock (_itemsLock) _items.Remove(item);
            OnChanged?.Invoke();
        });
    }

    public void Info(string message) => Show(message, "info");
    public void Success(string message) => Show(message, "success");
    public void Error(string message) => Show(message, "error");
    public void Warning(string message) => Show(message, "warning");
}
