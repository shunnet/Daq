using Microsoft.AspNetCore.Components.Authorization;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 操作日志：写入 logs/{日期}/operate/{用户名}/，格式 "用户名 - 角色 - 操作内容"（对齐 DAQ LogHelper 规范）。
/// 按日志级别按需选用 Info / Warning / Error。
/// </summary>
public static class OperateLog
{
    // 登录失败的用户名来自匿名输入，不允许作为任意路径或新增无限日志目录。
    /// <summary>将用户名转换为受约束的操作日志相对目录。</summary>
    /// <param name="username">认证用户名或匿名登录输入。</param>
    /// <returns>安全的操作日志相对目录。</returns>
    public static string UserFolder(string username)
    {
        if (string.IsNullOrWhiteSpace(username) || username.Length > 64
            || username is "." or ".." || username.Contains('/') || username.Contains('\\')
            || username.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || username.Any(char.IsControl))
            return Path.Combine("operate", "invalid-user");
        return Path.Combine("operate", username);
    }

    #region 日志写入
    /// <summary>写入一条普通操作日志。</summary>
    /// <param name="username">操作者用户名。</param>
    /// <param name="role">操作者角色。</param>
    /// <param name="action">操作内容。</param>
    public static Task Info(string username, string role, string action)
            => Snet.Log.LogHelper.InfoAsync($"{username} - {role} - {action}", foldername: UserFolder(username));

    /// <summary>写入一条警告操作日志。</summary>
    /// <param name="username">操作者用户名。</param>
    /// <param name="role">操作者角色。</param>
    /// <param name="action">操作内容。</param>
    public static Task Warning(string username, string role, string action)
        => Snet.Log.LogHelper.WarningAsync($"{username} - {role} - {action}", foldername: UserFolder(username));

    /// <summary>写入一条错误操作日志。</summary>
    /// <param name="username">操作者用户名。</param>
    /// <param name="role">操作者角色。</param>
    /// <param name="action">操作内容。</param>
    /// <param name="exception">与操作关联的异常。</param>
    public static Task Error(string username, string role, string action, Exception? exception = null)
        => Snet.Log.LogHelper.ErrorAsync($"{username} - {role} - {action}", foldername: UserFolder(username), exception: exception);

    #endregion

    #region 认证状态提取
    /// <summary>从认证状态提取 用户名/角色（Role claim 缺失时按空字符串处理）</summary>
    public static (string User, string Role) From(AuthenticationState state)
    {
        var user = state.User;
        return (user.Identity?.Name ?? "", user.IsInRole(AuthService.RoleAdmin) ? "管理员" : "普通用户");
    }
    #endregion
}
