using System.Security.Cryptography;
using System.Text.Json;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 认证服务：多用户（管理员/普通用户两级）+ PBKDF2 哈希 + 连续失败锁定。账号数据存 config/ui/User.json。
/// </summary>
public class AuthService
{
    #region 常量与字段
    /// <summary>管理员角色名称。</summary>
    public const string RoleAdmin = "Admin";
    /// <summary>普通用户角色名称。</summary>
    public const string RoleUser = "User";
    /// <summary>管理员授权策略名（[Authorize(Policy = ...)] 使用）</summary>
    public const string AdminPolicy = "AdminOnly";

    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int MaxFailedAttempts = 5;
    private const int MaxUsernameLength = 64;
    private const int MaxPasswordLength = 1024;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly string _path = WebPaths.UserConfigPath;

    /// <summary>创建认证服务并确保默认管理员账号存在。</summary>
    public AuthService()
    {
        EnsureDefaultUser();
    }

    #endregion

    #region 数据模型
    internal sealed record UserRecord(
            string Username,
            string PasswordHash,
            string Salt,
            bool MustChangePassword,
            int FailedAttempts,
            DateTime? LockoutUntil,
            string Role = RoleAdmin,
            bool IsDisabled = false,
            string? SecurityStamp = null);

    private sealed record UsersFile(List<UserRecord> Users);

    /// <summary>用户概览（供用户管理界面展示，不含敏感字段）</summary>
    public sealed record UserInfo(string Username, string Role, bool MustChangePassword, bool IsDisabled = false);

    #endregion

    #region 加载与持久化
    private volatile UserRecord[]? _cache;

    private List<UserRecord> Load()
    {
        if (_cache is not null) return _cache.ToList();
        if (File.Exists(_path))
        {
            try
            {
                var json = File.ReadAllText(_path);
                // 旧格式（单用户对象）自动迁移为多用户文件
                if (!json.Contains("\"Users\""))
                {
                    var legacy = JsonSerializer.Deserialize<UserRecord>(json);
                    if (legacy is not null && !string.IsNullOrEmpty(legacy.Username) && !string.IsNullOrEmpty(legacy.PasswordHash))
                    {
                        var migrated = new List<UserRecord> { legacy with { Role = RoleAdmin } };
                        Save(migrated);
                        return migrated;
                    }
                }
                var file = JsonSerializer.Deserialize<UsersFile>(json);
                if (file?.Users is { Count: > 0 })
                {
                    _cache = file.Users.ToArray();
                    return file.Users;
                }
            }
            catch (Exception ex)
            {
                // 已存在的凭据损坏必须保留并拒绝加载，不能恢复公开默认密码。
                throw new InvalidDataException("无法读取 User.json，已保留原文件；请恢复凭据备份。", ex);
            }
            throw new InvalidDataException("User.json 中没有有效用户，已保留原文件。请恢复凭据备份。");
        }
        var initial = new List<UserRecord> { CreateDefault() };
        Save(initial);
        return initial;
    }

    private UserRecord CreateDefault()
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return new UserRecord(
            Username: "snet",
            PasswordHash: HashPassword("123456", salt),
            Salt: Convert.ToBase64String(salt),
            MustChangePassword: true,
            FailedAttempts: 0,
            LockoutUntil: null,
            Role: RoleAdmin);
    }

    private void Save(List<UserRecord> users)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(new UsersFile(users), new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        // 原子写：临时文件 + 替换，崩溃不损坏凭据文件
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
        _cache = users.ToArray();
    }

    #endregion

    #region 密码学工具
    private static string HashPassword(string password, byte[] salt) =>
            Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32));

    /// <summary>
    /// 使用固定时间比较校验 PBKDF2 密码；损坏的 Base64 凭据按校验失败处理。
    /// </summary>
    private static bool VerifyPassword(string password, UserRecord user)
    {
        if (!TryParseSalt(user.Salt, out var salt))
            return false;

        try
        {
            var expected = Convert.FromBase64String(user.PasswordHash);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>检查用户名是否可用于账号标识和操作日志目录名。</summary>
    private static bool IsValidUsername(string username) =>
        !string.IsNullOrWhiteSpace(username)
        && username.Length is >= 2 and <= MaxUsernameLength
        && username is not ("." or "..")
        && !username.Contains('/')
        && !username.Contains('\\')
        && username.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && !username.Any(char.IsControl);

    private static bool TryParseSalt(string? salt, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrEmpty(salt)) return false;
        try
        {
            bytes = Convert.FromBase64String(salt);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    #endregion

    #region 登录校验与失败锁定
    /// <summary>按用户名查用户（供登录后取角色用）</summary>
    internal UserRecord? FindUser(string username) =>
            Load().FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 校验登录。返回 (成功, 错误消息)
    /// </summary>
    public async Task<(bool Ok, string? Error)> ValidateAsync(string username, string password)
    {
        if (!IsValidUsername(username) || password.Length > MaxPasswordLength)
            return (false, "InvalidUsernameOrPassword");

        await _fileLock.WaitAsync();
        try
        {
            var users = Load();
            var index = users.FindIndex(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
            // 用户名不匹配不计失败次数（防任意人刷 5 次锁死 admin）
            if (index < 0) return (false, "InvalidUsernameOrPassword");
            var user = users[index];
            // 锁定过期时清零失败计数，避免"过期后 1 次失败即再锁"的永久 DoS
            if (user.LockoutUntil is { } until && until <= DateTime.UtcNow)
            {
                user = user with { FailedAttempts = 0, LockoutUntil = null };
                users[index] = user;
            }
            if (user.LockoutUntil is { } lockUntil && lockUntil > DateTime.UtcNow)
                return (false, "AccountLocked");
            if (!VerifyPassword(password, user))
            {
                RegisterFailure(user, users, index);
                return (false, "InvalidUsernameOrPassword");
            }
            // 密码正确但账号已停用：拒绝登录（不计失败次数）
            if (user.IsDisabled)
                return (false, "UserDisabled");
            // 登录成功：清零失败计数
            users[index] = user with { FailedAttempts = 0, LockoutUntil = null };
            Save(users);
            return (true, null);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private void RegisterFailure(UserRecord user, List<UserRecord> users, int index)
    {
        var failed = user.FailedAttempts + 1;
        // BUG 修复：原实现用 u.Username == user.Username（区分大小写）重新查找，
        // 登录时大小写不同（如输入 Snet、存储 snet）会 FindIndex 返回 -1 → users[-1] 越界 500。
        // 改为直接使用调用方按 OrdinalIgnoreCase 定位好的 index。
        users[index] = failed >= MaxFailedAttempts
            ? user with { FailedAttempts = failed, LockoutUntil = DateTime.UtcNow + LockoutDuration }
            : user with { FailedAttempts = failed };
        Save(users);
    }

    /// <summary>确定指定用户下次登录后是否必须修改密码。</summary>
    /// <param name="username">用户名。</param>
    /// <returns>需要修改密码时为 <see langword="true"/>。</returns>
    public bool MustChangePassword(string username) => FindUser(username)?.MustChangePassword ?? false;

    internal async Task<bool> IsSessionValidAsync(System.Security.Claims.ClaimsPrincipal principal)
    {
        await _fileLock.WaitAsync();
        try
        {
            var user = FindUser(principal.Identity?.Name ?? "");
            return user is not null && !user.IsDisabled
                && principal.IsInRole(user.Role)
                && principal.FindFirst("securityStamp")?.Value == (user.SecurityStamp ?? user.Salt)
                && principal.HasClaim("mustChangePassword", "true") == user.MustChangePassword;
        }
        finally { _fileLock.Release(); }
    }

    #endregion

    #region 修改密码
    /// <summary>验证旧密码并更新指定用户的密码。</summary>
    /// <param name="username">用户名。</param>
    /// <param name="oldPassword">当前密码。</param>
    /// <param name="newPassword">符合密码策略的新密码。</param>
    /// <returns>操作结果及可供界面显示的错误消息。</returns>
    public async Task<(bool Ok, string? Error)> ChangePasswordAsync(string username, string oldPassword, string newPassword)
    {
        if (!IsValidUsername(username) || oldPassword.Length > MaxPasswordLength)
            return (false, "InvalidOldPassword");
        if (newPassword.Length is < 6 or > MaxPasswordLength)
            return (false, "PasswordTooShort");

        await _fileLock.WaitAsync();
        try
        {
            var user = FindUser(username);
            if (user?.IsDisabled == true) return (false, "UserDisabled");
            if (user?.LockoutUntil > DateTime.UtcNow) return (false, "AccountLocked");
            if (user is null || !VerifyPassword(oldPassword, user))
            {
                if (user is not null)
                {
                    var records = Load();
                    var failureIndex = records.FindIndex(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
                    if (user.LockoutUntil <= DateTime.UtcNow)
                        user = user with { FailedAttempts = 0, LockoutUntil = null };
                    RegisterFailure(user, records, failureIndex);
                }
                return (false, "InvalidOldPassword");
            }
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var users = Load();
            // BUG 修复：原用 u.Username == user.Username（区分大小写），大小写混合登录改密时
            // FindIndex 返回 -1 → users[-1] 越界。与 FindUser 一致改为 OrdinalIgnoreCase。
            var index = users.FindIndex(u => string.Equals(u.Username, user.Username, StringComparison.OrdinalIgnoreCase));
            users[index] = user with
            {
                PasswordHash = HashPassword(newPassword, salt),
                Salt = Convert.ToBase64String(salt),
                SecurityStamp = Guid.NewGuid().ToString("N"),
                MustChangePassword = false,
                FailedAttempts = 0,
                LockoutUntil = null
            };
            Save(users);
            return (true, null);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    // ================= 用户管理（仅管理员） =================

    #endregion

    #region 用户管理
    /// <summary>获取不含密码哈希、盐和安全戳的用户列表快照。</summary>
    /// <returns>用户概览列表。</returns>
    public List<UserInfo> ListUsers() =>
            Load().Select(u => new UserInfo(u.Username, u.Role, u.MustChangePassword, u.IsDisabled)).ToList();

    /// <summary>新增用户。返回 (成功, 错误消息)</summary>
    public async Task<(bool Ok, string? Error)> AddUserAsync(string username, string password, string role)
    {
        await _fileLock.WaitAsync();
        try
        {
            if (!IsValidUsername(username))
                return (false, "InvalidUsername");
            if (password.Length is < 6 or > MaxPasswordLength)
                return (false, "PasswordTooShort");
            var users = Load();
            if (users.Any(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase)))
                return (false, "UserExists");
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            users.Add(new UserRecord(
                Username: username,
                PasswordHash: HashPassword(password, salt),
                Salt: Convert.ToBase64String(salt),
                MustChangePassword: false,
                FailedAttempts: 0,
                LockoutUntil: null,
                Role: role == RoleAdmin ? RoleAdmin : RoleUser));
            Save(users);
            return (true, null);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>删除用户。返回 (成功, 错误消息)。不允许删除最后一个管理员、不允许删除自己以外的限制由界面把控</summary>
    public async Task<(bool Ok, string? Error)> RemoveUserAsync(string username, string currentUser)
    {
        await _fileLock.WaitAsync();
        try
        {
            var users = Load();
            var target = users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
            if (target is null) return (false, "UserNotFound");
            if (target.Role == RoleAdmin && !target.IsDisabled && users.Count(u => u.Role == RoleAdmin && !u.IsDisabled) <= 1)
                return (false, "LastAdmin");
            if (string.Equals(username, currentUser, StringComparison.OrdinalIgnoreCase))
                return (false, "CannotRemoveSelf");
            users.Remove(target);
            Save(users);
            return (true, null);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>重置用户密码（下次登录无需强制改密）</summary>
    public async Task<(bool Ok, string? Error)> ResetPasswordAsync(string username, string newPassword)
    {
        await _fileLock.WaitAsync();
        try
        {
            if (newPassword.Length is < 6 or > MaxPasswordLength)
                return (false, "PasswordTooShort");
            var users = Load();
            var index = users.FindIndex(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return (false, "UserNotFound");
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            users[index] = users[index] with
            {
                PasswordHash = HashPassword(newPassword, salt),
                Salt = Convert.ToBase64String(salt),
                SecurityStamp = Guid.NewGuid().ToString("N"),
                MustChangePassword = false,
                FailedAttempts = 0,
                LockoutUntil = null
            };
            Save(users);
            return (true, null);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>启用/停用用户。返回 (成功, 错误消息)。不允许停用最后一个管理员、不允许停用自己</summary>
    public async Task<(bool Ok, string? Error)> SetDisabledAsync(string username, bool disabled, string currentUser)
    {
        await _fileLock.WaitAsync();
        try
        {
            var users = Load();
            var index = users.FindIndex(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return (false, "UserNotFound");
            var target = users[index];
            if (disabled && target.Role == RoleAdmin && !target.IsDisabled && users.Count(u => u.Role == RoleAdmin && !u.IsDisabled) <= 1)
                return (false, "LastAdmin");
            if (disabled && string.Equals(username, currentUser, StringComparison.OrdinalIgnoreCase))
                return (false, "CannotDisableSelf");
            users[index] = target with { IsDisabled = disabled, SecurityStamp = Guid.NewGuid().ToString("N") };
            Save(users);
            return (true, null);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>切换用户角色。返回 (成功, 错误消息)。不允许移除最后一个管理员</summary>
    public async Task<(bool Ok, string? Error)> SetRoleAsync(string username, string role)
    {
        await _fileLock.WaitAsync();
        try
        {
            var users = Load();
            var index = users.FindIndex(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return (false, "UserNotFound");
            var target = users[index];
            if (target.Role == RoleAdmin && !target.IsDisabled && role != RoleAdmin && users.Count(u => u.Role == RoleAdmin && !u.IsDisabled) <= 1)
                return (false, "LastAdmin");
            users[index] = target with { Role = role == RoleAdmin ? RoleAdmin : RoleUser, SecurityStamp = Guid.NewGuid().ToString("N") };
            Save(users);
            return (true, null);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    #endregion

    #region 默认账号初始化
    private void EnsureDefaultUser()
    {
        Load();
    }
    #endregion
}
