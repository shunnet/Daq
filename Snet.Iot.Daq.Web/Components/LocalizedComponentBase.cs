using Microsoft.AspNetCore.Components;
using Snet.Iot.Daq.Web.Services;

namespace Snet.Iot.Daq.Web.Components;

/// <summary>
/// 本地化感知组件基类：语言切换时自动整组件重渲染
/// </summary>
public abstract class LocalizedComponentBase : ComponentBase, IDisposable
{
    [Inject]
    protected LocalizationService Localization { get; set; } = null!;

    protected string T(string key) => Localization.T(key);

    protected override void OnInitialized()
    {
        Localization.LanguageChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        OnLanguageChangedCore();
        InvokeAsync(StateHasChanged);
    }

    /// <summary>语言切换钩子：子类可在此重建语言相关的数据（在 StateHasChanged 之前调用）</summary>
    protected virtual void OnLanguageChangedCore() { }

    public virtual void Dispose()
    {
        Localization.LanguageChanged -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }
}
