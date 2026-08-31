using Microsoft.JSInterop;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// snet.outsideClick（区域外点击收起菜单）所用的 DotNetObjectReference 释放流程。
/// 必须先让 JS 移除 document 上的全局外部点击监听，再释放引用。
/// 若组件 Dispose 时立即 _jsRef.Dispose()，而 JS 监听因异步尚未移除，残留监听在下一次点击
/// （例如头部的中/EN 语言切换按钮，它永远位于 .tree-actions 之外）就会对已释放对象调用
/// invokeMethodAsync('CloseAllActions')，Blazor Server 端随即报：
/// System.NullReferenceException ... Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime.EndInvokeDotNet
/// </summary>
public static class OutsideClickHelper
{
    /// <summary>
    /// 先异步让 JS 调用 snet.outsideClick.unregisterAll 移除 document 监听，待其完成（无论成败）后再释放引用，
    /// 确保引用存活到监听确实不再被触发，杜绝 EndInvokeDotNet 的 NullReferenceException。
    /// </summary>
    public static void DisposeAfterUnregister<T>(IJSRuntime js, DotNetObjectReference<T>? jsRef) where T : class
    {
        if (jsRef is null) return;
        if (js is null)
        {
            jsRef.Dispose();
            return;
        }

        try
        {
            // 触发的监听一旦被移除，之后就不会再有 JS → .NET 回调指向该引用。
            // ValueTask.AsTask() 后可用 ContinueWith：任务结束（成功/失败/取消）同步执行释放并吞掉未观察异常。
            var unregister = js.InvokeVoidAsync("snet.outsideClick.unregisterAll").AsTask();
            _ = unregister.ContinueWith(
                t => { _ = t.Exception; jsRef.Dispose(); },
                TaskContinuationOptions.ExecuteSynchronously);
        }
        catch
        {
            // 无法发出 JS 调用（例如电路已断开）时立即释放，避免引用悬挂
            jsRef.Dispose();
        }
    }
}
