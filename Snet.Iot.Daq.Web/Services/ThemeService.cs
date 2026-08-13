namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 主题服务：服务端仅维护状态供组件/图表订阅，实际 CSS 应用由前端 JS（localStorage + data-theme）执行
/// </summary>
public class ThemeService
{
    public bool IsDark { get; private set; } = true;

    public event Action? ThemeChanged;

    public void Toggle() => SetDark(!IsDark);

    public void SetDark(bool dark)
    {
        if (IsDark == dark) return;
        IsDark = dark;
        ThemeChanged?.Invoke();
    }
}
