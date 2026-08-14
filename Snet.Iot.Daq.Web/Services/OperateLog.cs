using Microsoft.AspNetCore.Components.Authorization;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 操作日志：写入 logs/{日期}/operate/{用户名}/，格式 "用户名 - 角色 - 操作内容"（对齐 DAQ LogHelper 规范）。
/// 按日志级别按需选用 Info / Warning / Error。
/// </summary>
public static class OperateLog
{
    #region 日志写入
    public static Task Info(string username, string role, string action)
            => Snet.Log.LogHelper.InfoAsync($"{username} - {role} - {action}", foldername: Path.Combine("operate", username));

    public static Task Warning(string username, string role, string action)
        => Snet.Log.LogHelper.WarningAsync($"{username} - {role} - {action}", foldername: Path.Combine("operate", username));

    public static Task Error(string username, string role, string action, Exception? exception = null)
        => Snet.Log.LogHelper.ErrorAsync($"{username} - {role} - {action}", foldername: Path.Combine("operate", username), exception: exception);

    /// <summary>从认证状态提取 用户名/角色（Role claim 缺失时按空字符串处理）</summary>
    #endregion

    #region 认证状态提取
    public static (string User, string Role) From(AuthenticationState state)
    {
        var user = state.User;
        return (user.Identity?.Name ?? "", user.IsInRole(AuthService.RoleAdmin) ? "管理员" : "普通用户");
    }
    #endregion
}
