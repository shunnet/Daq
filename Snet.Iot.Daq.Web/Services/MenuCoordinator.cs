namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 全局 ⋯ 菜单互斥协调器：所有操作菜单（项目树/详情树/工具栏/插件卡/设备卡）共用。
/// 组件在 OnInitialized 注册关闭回调，Toggle 打开前调 CloseAllOthers()——点 A 再点 B 时 A 必然收起，
/// 跨组件（树 vs 详情工具栏 vs 插件卡）同样互斥。
/// </summary>
public sealed class MenuCoordinator
{
    private readonly List<Action> _callbacks = new();
    private readonly object _lock = new();

    #region 注册与互斥
    /// <summary>注册一个菜单关闭回调。</summary>
    /// <param name="close">关闭所属组件菜单的回调。</param>
    public void Register(Action close)
    {
        lock (_lock)
        {
            if (!_callbacks.Contains(close)) _callbacks.Add(close);
        }
    }

    /// <summary>注销组件不再使用的菜单关闭回调。</summary>
    /// <param name="close">注册时使用的同一个回调实例。</param>
    public void Unregister(Action close)
    {
        lock (_lock) _callbacks.Remove(close);
    }

    /// <summary>关闭所有已注册组件的菜单（调用者随后自行设置新状态）</summary>
    public void CloseAllOthers()
    {
        List<Action> snapshot;
        lock (_lock) snapshot = _callbacks.ToList();
        foreach (var cb in snapshot)
        {
            try { cb(); }
            catch { /* 单个组件异常不影响其余菜单关闭 */ }
        }
    }
    #endregion
}
