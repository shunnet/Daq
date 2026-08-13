using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Snet.Iot.Daq.Web.Components;
using Snet.Iot.Daq.Web.Services;
using System.Security.Claims;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "Snet.Daq.Web.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthService.AdminPolicy, policy => policy.RequireRole(AuthService.RoleAdmin));
});
// Blazor 认证状态：SSR 阶段从 HttpContext 读取并持久化到 circuit（布局按角色渲染导航）
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider, Snet.Iot.Daq.Web.Services.ServerAuthStateProvider>();

builder.Services.AddSingleton<LocalizationService>();
builder.Services.AddSingleton<ThemeService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddSingleton<Snet.Iot.Daq.Web.Components.Shared.ToastService>();
builder.Services.AddSingleton<DbGate>();
builder.Services.AddSingleton<AppStateService>();
builder.Services.AddSingleton<LoggerBuffer>();
builder.Services.AddSingleton<DeviceRuntimeManager>();
builder.Services.AddSingleton<MonitorSampler>();
builder.Services.AddSingleton<DownloadTaskManager>();
// 单实例双注册：既作为托管服务启动，又可按类型注入（控制台服务端管理按钮需要调用其方法）
builder.Services.AddSingleton<DaqHostedService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DaqHostedService>());

// 登录端点限流（按 IP 固定窗口）：防 PBKDF2 计算 DoS 与暴力锁定
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        return ValueTask.CompletedTask;
    };
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

WebPaths.Init(app.Configuration);
// 对齐 WPF（程序目录即工作目录）：Snet.Opc UA 节点持久化（Opc.Ua.Service.Nodes.Json）与 Core 日志（logs/）
// 均按相对路径写入当前目录——把 CWD 切到数据目录，避免在启动目录（如代码目录）产生这些文件
Directory.SetCurrentDirectory(WebPaths.DataDir);

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
// 强制改密：MustChangePassword 会话仅允许访问登录/改密页与 Blazor 框架资源，其余一律重定向
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path;
    // 白名单：登录/改密页 + 框架资源 + 全部静态资源（改密页需要样式/脚本正常加载）
    var allowed = path.StartsWithSegments("/login")
        || path.StartsWithSegments("/logout")
        || path.StartsWithSegments("/_framework")
        || path.StartsWithSegments("/_blazor")
        || path.StartsWithSegments("/css")
        || path.StartsWithSegments("/js")
        || path.StartsWithSegments("/icons")
        || path.StartsWithSegments("/images")
        || path.StartsWithSegments("/_content")
        // MapStaticAssets 指纹化：静态资源实际 URL 为 /app.{hash}.css 等形式，
        // StartsWithSegments 按段匹配无法覆盖，改按扩展名放行全部静态资源
        || path.Value?.EndsWith(".css", StringComparison.OrdinalIgnoreCase) == true
        || path.Value?.EndsWith(".js", StringComparison.OrdinalIgnoreCase) == true
        || path.Value?.EndsWith(".png", StringComparison.OrdinalIgnoreCase) == true
        || path.Value?.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) == true
        || path.Value?.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) == true;
    if (ctx.User.Identity?.IsAuthenticated == true
        && ctx.User.HasClaim("mustChangePassword", "true")
        && !allowed)
    {
        ctx.Response.Redirect("/login?mode=change");
        return;
    }
    await next();
});
app.UseAuthorization();

app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();

// 登录/退出走服务端 endpoint：Blazor circuit 内无 HttpContext，Cookie 操作必须在请求管道中完成。
// DisableAntiforgery：匿名凭证提交端点，CSRF 危害仅限"替受害者登录"（无状态变更面），可接受。
app.MapPost("/login", async (HttpContext ctx, IFormCollection form, AuthService auth) =>
{
    var mode = form["mode"].ToString();
    var username = form["username"].ToString();
    var password = form["password"].ToString();

    if (mode == "change")
    {
        var newPassword = form["newPassword"].ToString();
        var confirm = form["confirmPassword"].ToString();
        if (newPassword != confirm)
            return Results.Redirect("/login?mode=change&error=PasswordMismatch");
        if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
            return Results.Redirect("/login?mode=change&error=PasswordTooShort");
        // 改密表单无 username 字段时取当前登录用户（强制改密会话已带 Cookie），不能默认 admin（普通用户会永远改密失败）
        if (string.IsNullOrEmpty(username))
            username = ctx.User.Identity?.Name ?? "";
        var (ok, err) = await auth.ChangePasswordAsync(username, password, newPassword);
        if (!ok) return Results.Redirect($"/login?mode=change&error={err}");
        var changedUser = auth.FindUser(username);
        await SignInAsync(ctx, username, changedUser?.Role ?? AuthService.RoleUser);
        return Results.Redirect("/console");
    }

    // 登录分支兜底：无用户名时按 admin 处理（登录表单始终带用户名，兜底仅防异常提交）
    if (string.IsNullOrEmpty(username)) username = "admin";
    var (valid, err2) = await auth.ValidateAsync(username, password);
    if (!valid) return Results.Redirect($"/login?error={err2}");
    var user = auth.FindUser(username);
    var role = user?.Role ?? AuthService.RoleUser;
    if (auth.MustChangePassword(username))
    {
        // 带"必须改密"声明的受限会话：只能访问登录/改密页（见下方强制改密中间件）
        await SignInAsync(ctx, username, role, mustChangePassword: true);
        return Results.Redirect("/login?mode=change");
    }
    await SignInAsync(ctx, username, role);
    return Results.Redirect("/console");
}).DisableAntiforgery().RequireRateLimiting("login");

app.MapGet("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).DisableAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static async Task SignInAsync(HttpContext ctx, string username, string role, bool mustChangePassword = false)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, username),
        new("displayName", username),
        new(ClaimTypes.Role, role)
    };
    if (mustChangePassword)
        claims.Add(new Claim("mustChangePassword", "true"));
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
        new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme)),
        new AuthenticationProperties { IsPersistent = true });
}
