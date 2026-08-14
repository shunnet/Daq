using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Snet.Iot.Daq.Web.Services;

namespace Snet.Iot.Daq.Web.Components;

/// <summary>
/// 本地化感知组件基类：语言切换时自动整组件重渲染
/// </summary>
public abstract class LocalizedComponentBase : ComponentBase, IDisposable
{
    [Inject]
    protected LocalizationService Localization { get; set; } = null!;

    /// <summary>基类独立级联字段（按类型注入，避开子类同名 AuthState 遮蔽）</summary>
    [CascadingParameter]
    private Task<AuthenticationState>? _authState { get; set; }

    protected string T(string key) => Localization.T(key);

    /// <summary>记录当前用户操作日志（logs/operate/{用户名}/，用户名 - [角色] 操作内容）</summary>
    protected async Task OperateInfoAsync(string action)
    {
        try
        {
            if (_authState is not null)
            {
                var (user, role) = OperateLog.From(await _authState);
                await OperateLog.Info(user, role, action);
            }
        }
        catch { /* 日志失败不影响操作 */ }
    }

    /// <summary>记录当前用户操作异常日志</summary>
    protected async Task OperateErrorAsync(string action, Exception? ex = null)
    {
        try
        {
            if (_authState is not null)
            {
                var (user, role) = OperateLog.From(await _authState);
                await OperateLog.Error(user, role, action, ex);
            }
        }
        catch { /* 日志失败不影响操作 */ }
    }

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
