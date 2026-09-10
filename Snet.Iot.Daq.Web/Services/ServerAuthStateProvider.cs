using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>使用框架维护电路认证状态，账号和权限变更后撤销会话。</summary>
public sealed class ServerAuthStateProvider(ILoggerFactory loggerFactory, AuthService auth)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    /// <inheritdoc />
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    protected override Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState, CancellationToken cancellationToken) =>
        auth.IsSessionValidAsync(authenticationState.User);
}
