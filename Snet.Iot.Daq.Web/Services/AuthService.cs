using System.Security.Cryptography;
using System.Text.Json;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// 认证服务：多用户（管理员/普通用户两级）+ PBKDF2 哈希 + 连续失败锁定。账号数据存 config/ui/User.json。
/// </summary>
public class AuthService
{
    public const string RoleAdmin = "Admin";
    public const string RoleUser = "User";
    /// <summary>管理员授权策略名（[Authorize(Policy = ...)] 使用）</summary>
    public const string AdminPolicy = "AdminOnly";

    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly string _path = WebPaths.UserConfigPath;

    public AuthService()
    {
        EnsureDefaultUser();
    }

    internal sealed record UserRecord(
        string Username,
        string PasswordHash,
        string Salt,
        bool MustChangePassword,
        int FailedAttempts,
        DateTime? LockoutUntil,
        string Role = RoleAdmin);

    private sealed record UsersFile(List<UserRecord> Users);

    /// <summary>用户概览（供用户管理界面展示，不含敏感字段）</summary>
    public sealed record UserInfo(string Username, string Role, bool MustChangePassword);

    private List<UserRecord>? _cache;

    private List<UserRecord> Load()
    {
        if (_cache is not null) return _cache;
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
                        _cache = new List<UserRecord> { legacy with { Role = RoleAdmin } };
                        Save();
                        return _cache;
                    }
                }
                var file = JsonSerializer.Deserialize<UsersFile>(json);
                if (file?.Users is { Count: > 0 })
                {
                    _cache = file.Users;
                    return _cache;
                }
            }
            catch (Exception ex)
            {
                // 凭据文件损坏：自愈重建为默认账号（admin/admin + 强制改密），避免登录接口持续 500
                Console.Error.WriteLine($"[AuthService] User.json 损坏，已重建默认账号: {ex.Message}");
            }
        }
        _cache = new List<UserRecord> { CreateDefault() };
        Save();
        return _cache;
    }

    private UserRecord CreateDefault()
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return new UserRecord(
            Username: "admin",
            PasswordHash: HashPassword("admin", salt),
            Salt: Convert.ToBase64String(salt),
            MustChangePassword: true,
            FailedAttempts: 0,
            LockoutUntil: null,
            Role: RoleAdmin);
    }

    private void Save()
    {
        if (_cache is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(new UsersFile(_cache), new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        // 原子写：临时文件 + 替换，崩溃不损坏凭据文件
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }

    private static string HashPassword(string password, byte[] salt) =>
        Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32));

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

    /// <summary>按用户名查用户（供登录后取角色用）</summary>
    internal UserRecord? FindUser(string username) =>
        Load().FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 校验登录。返回 (成功, 错误消息)
    /// </summary>
    public async Task<(bool Ok, string? Error)> ValidateAsync(string username, string password)
    {
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
            if (!TryParseSalt(user.Salt, out var salt))
                return (false, "InvalidUsernameOrPassword");
            if (user.PasswordHash != HashPassword(password, salt))
            {
                RegisterFailure(user, users);
                return (false, "InvalidUsernameOrPassword");
            }
            users[index] = user with { FailedAttempts = 0, LockoutUntil = null };
            Save();
            return (true, null);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private void RegisterFailure(UserRecord user, List<UserRecord> users)
    {
        var failed = user.FailedAttempts + 1;
        var index = users.FindIndex(u => u.Username == user.Username);
        users[index] = failed >= MaxFailedAttempts
            ? user with { FailedAttempts = failed, LockoutUntil = DateTime.UtcNow + LockoutDuration }
            : user with { FailedAttempts = failed };
        Save();
    }

    public bool MustChangePassword(string username) => FindUser(username)?.MustChangePassword ?? false;

    public async Task<(bool Ok, string? Error)> ChangePasswordAsync(string username, string oldPassword, string newPassword)
    {
        await _fileLock.WaitAsync();
        try
        {
            var user = FindUser(username);
            if (user is null
                || !TryParseSalt(user.Salt, out var oldSalt)
                || user.PasswordHash != HashPassword(oldPassword, oldSalt))
                return (false, "InvalidOldPassword");
            if (newPassword.Length < 6)
                return (false, "PasswordTooShort");
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var users = Load();
            var index = users.FindIndex(u => u.Username == user.Username);
            users[index] = user with
            {
                PasswordHash = HashPassword(newPassword, salt),
                Salt = Convert.ToBase64String(salt),
                MustChangePassword = false,
                FailedAttempts = 0,
                LockoutUntil = null
            };
            Save();
            return (true, null);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    // ================= 用户管理（仅管理员） =================

    public List<UserInfo> ListUsers() =>
        Load().Select(u => new UserInfo(u.Username, u.Role, u.MustChangePassword)).ToList();

    /// <summary>新增用户。返回 (成功, 错误消息)</summary>
    public async Task<(bool Ok, string? Error)> AddUserAsync(string username, string password, string role)
    {
        await _fileLock.WaitAsync();
        try
        {
            if (string.IsNullOrWhiteSpace(username) || username.Length < 2)
                return (false, "InvalidUsername");
            if (password.Length < 6)
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
            Save();
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
            if (target.Role == RoleAdmin && users.Count(u => u.Role == RoleAdmin) <= 1)
                return (false, "LastAdmin");
            if (string.Equals(username, currentUser, StringComparison.OrdinalIgnoreCase))
                return (false, "CannotRemoveSelf");
            users.Remove(target);
            Save();
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
            if (newPassword.Length < 6)
                return (false, "PasswordTooShort");
            var users = Load();
            var index = users.FindIndex(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return (false, "UserNotFound");
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            users[index] = users[index] with
            {
                PasswordHash = HashPassword(newPassword, salt),
                Salt = Convert.ToBase64String(salt),
                MustChangePassword = false, // 管理员已输入新密码，用户直接可用，无需再强制首登改密
                FailedAttempts = 0,
                LockoutUntil = null
            };
            Save();
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
            if (target.Role == RoleAdmin && role != RoleAdmin && users.Count(u => u.Role == RoleAdmin) <= 1)
                return (false, "LastAdmin");
            users[index] = target with { Role = role == RoleAdmin ? RoleAdmin : RoleUser };
            Save();
            return (true, null);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private void EnsureDefaultUser()
    {
        if (!File.Exists(_path)) Load();
    }
}
