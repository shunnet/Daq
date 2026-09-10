namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 主题服务：服务端仅维护状态供组件/图表订阅，实际 CSS 应用由前端 JS（localStorage + data-theme）执行
/// </summary>
public class ThemeService
{
    #region 主题状态
    /// <summary>获取当前是否使用深色主题。</summary>
    public bool IsDark { get; private set; } = true;

    /// <summary>主题状态变化时触发。</summary>
    public event Action? ThemeChanged;

    /// <summary>在深色与浅色主题之间切换。</summary>
    public void Toggle() => SetDark(!IsDark);

    /// <summary>设置主题状态，并仅在值变化时通知订阅者。</summary>
    /// <param name="dark">使用深色主题时为 <see langword="true"/>。</param>
    public void SetDark(bool dark)
    {
        if (IsDark == dark) return;
        IsDark = dark;
        ThemeChanged?.Invoke();
    }
    #endregion
}
