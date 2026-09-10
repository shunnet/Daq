using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Snet.Iot.Daq.Web.Services;

namespace Snet.Iot.Daq.Web.Components;

/// <summary>
/// 本地化感知组件基类：语言切换时自动整组件重渲染
/// </summary>
public abstract class LocalizedComponentBase : ComponentBase, IDisposable
{
    /// <summary>获取当前用户电路使用的本地化服务。</summary>
    [Inject]
    protected LocalizationService Localization { get; set; } = null!;

    /// <summary>基类独立级联字段（按类型注入，避开子类同名 AuthState 遮蔽）</summary>
    [CascadingParameter]
    private Task<AuthenticationState>? _authState { get; set; }

    /// <summary>获取指定资源键在当前语言下的文本。</summary>
    /// <param name="key">Core 语言资源中的资源键。</param>
    /// <returns>本地化文本；资源不存在时返回资源键。</returns>
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

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        Localization.LanguageChanged += OnLanguageChanged;
    }

    private async void OnLanguageChanged()
    {
        try
        {
            await InvokeAsync(async () =>
            {
                await OnLanguageChangedCoreAsync();
                StateHasChanged();
            });
        }
        catch (ObjectDisposedException)
        {
            // 语言切换与页面销毁同时发生时，组件已经不再需要刷新。
        }
        catch (InvalidOperationException)
        {
            // 电路断开后调度器不可用，忽略这次界面刷新。
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Localization] 语言切换后的组件刷新失败: {ex.Message}");
        }
    }

    /// <summary>语言切换钩子：子类可在界面刷新前异步重建语言相关数据。</summary>
    /// <returns>重建操作。</returns>
    protected virtual Task OnLanguageChangedCoreAsync() => Task.CompletedTask;

    /// <summary>取消语言事件订阅，释放组件持有的托管资源。</summary>
    public virtual void Dispose()
    {
        Localization.LanguageChanged -= OnLanguageChanged;
        GC.SuppressFinalize(this);
    }
}
