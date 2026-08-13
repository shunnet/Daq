using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 认证状态提供者：SSR 首渲染阶段从 HttpContext 读取登录用户，
/// 经 PersistentComponentState 持久化传递到交互电路（布局按角色渲染导航）。
/// </summary>
public class ServerAuthStateProvider : RevalidatingServerAuthenticationStateProvider
{
    private static readonly Task<AuthenticationState> DefaultUnauthenticated =
        Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

    private readonly Task<AuthenticationState> _authStateTask;

    public ServerAuthStateProvider(ILoggerFactory loggerFactory, PersistentComponentState persistentState, IHttpContextAccessor httpContextAccessor)
        : base(loggerFactory)
    {
        // 1) SSR 首渲染：从 HttpContext 读取登录用户（AuthorizeRouteView 依赖它判定授权，否则重定向登录页）
        if (httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } httpUser)
        {
            _authStateTask = Task.FromResult(new AuthenticationState(httpUser));
            return;
        }
        // 2) 交互电路建立：从 SSR 阶段持久化的认证状态恢复
        var restored = persistentState.TryTakeFromJson<PersistedAuthState>(nameof(PersistedAuthState), out var saved);
        if (restored && saved is not null)
        {
            var identity = new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, saved.Name),
                new Claim(ClaimTypes.Role, saved.Role)
            }, CookieAuthenticationDefaults.AuthenticationScheme);
            _authStateTask = Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
            return;
        }
        _authStateTask = DefaultUnauthenticated;
    }

    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(30);

    protected override Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken) =>
        Task.FromResult(authenticationState.User.Identity?.IsAuthenticated == true);

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _authStateTask;

    /// <summary>SSR → circuit 传递的认证状态（Name + Role，无需完整 Claim 集）</summary>
    public sealed class PersistedAuthState
    {
        public required string Name { get; set; }
        public required string Role { get; set; }
    }

    /// <summary>SSR 首渲染时由 App 根组件调用：把 HttpContext 用户持久化给交互电路（PersistAsJson 只能在 OnPersisting 回调中执行）</summary>
    public static void PersistFromHttpContext(PersistentComponentState state, ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated == true)
        {
            state.RegisterOnPersisting(() =>
            {
                state.PersistAsJson(nameof(PersistedAuthState), new PersistedAuthState
                {
                    Name = user.Identity.Name ?? "",
                    Role = user.FindFirstValue(ClaimTypes.Role) ?? AuthService.RoleUser
                });
                return Task.CompletedTask;
            });
        }
    }
}
