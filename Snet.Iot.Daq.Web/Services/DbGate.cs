using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Web.Data;
using SQLite;

namespace Snet.Iot.Daq.Web.Services;

/// <summary>
/// SQLite 门面：单连接 + 单锁（维持 WPF 端 GlobalConfigModel.sqliteOperate + DbLock 的既定并发模式）。
/// 使用与 WPF 同名的 AddressModel 表，保证 address.db 可互拷共用。
/// </summary>
public class DbGate
{
    public SQLiteConnection Db { get; }
    public object DbLock { get; } = new();

    #region 构造与迁移
    public DbGate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(WebPaths.DbPath)!);
        Db = new SQLiteConnection(WebPaths.DbPath);
        lock (DbLock)
        {
            Db.CreateTable<AddressModel>();
            MigrateLegacyAddressTable();
        }
    }

    /// <summary>
    /// 一次性迁移：旧版 Web 用 AddressModelCore 表（WPF 用 AddressModel）。
    /// 目标表无数据且旧表存在时，整表搬移并删除旧表（保留原 Index 主键值）。
    /// </summary>
    private void MigrateLegacyAddressTable()
    {
        try
        {
            var legacy = Db.QueryScalars<string>("SELECT name FROM sqlite_master WHERE type='table' AND name='AddressModelCore'").FirstOrDefault();
            if (legacy is null) return;
            if (Db.ExecuteScalar<int>("SELECT COUNT(*) FROM AddressModel") > 0) return; // 目标表已有数据（如 WPF 库），跳过
            Db.RunInTransaction(() =>
            {
                Db.Execute("""
                    INSERT INTO AddressModel ([Index], Address, [Type], Length, EncodingType, Guid, AnotherName, Describe, Topic, SimplifyValue, ExpandParam, [Time])
                    SELECT [Index], Address, [Type], Length, EncodingType, Guid, AnotherName, Describe, Topic, SimplifyValue, ExpandParam, [Time] FROM AddressModelCore
                    """);
                Db.Execute("DROP TABLE AddressModelCore");
            });
        }
        catch (Exception ex)
        {
            // 迁移失败不阻断启动：两表共存无害，待数据修复后可手动处理
            Console.Error.WriteLine($"[DbGate] 旧地址表迁移失败（不影响启动）: {ex.Message}");
        }
    }

    /// <summary>分页查询地址（模糊匹配 别名/地址/描述），keyword 为空返回全部</summary>
    #endregion

    #region 查询
    public List<AddressModel> QueryAddresses(string? keyword, int pageIndex, int pageSize, out int total)
    {
        lock (DbLock)
        {
            var query = Db.Table<AddressModel>();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var kw = keyword.Trim();
                query = query.Where(a => a.AnotherName.Contains(kw) || a.Address.Contains(kw) || a.Describe.Contains(kw));
            }
            total = query.Count();
            // 与 WPF 对齐：按更新时间倒序（最新编辑在前）
            return query.OrderByDescending(a => a.Time).Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList();
        }
    }

    /// <summary>批量插入（防重），返回统计</summary>
    public BatchInsertResult InsertUniqueAddresses(IEnumerable<AddressModel> items) =>
        ProjectHandlerCore.InsertUnique(Db, DbLock, items, null, x => x.AnotherName, x => x.Address);
    #endregion
}
