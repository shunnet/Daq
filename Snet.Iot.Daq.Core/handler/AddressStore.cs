using Snet.Iot.Daq.Core.@interface;
using SQLite;

namespace Snet.Iot.Daq.Core.handler;

/// <summary>两端共用的地址持久化规则；先提交数据库，再由调用方发布内存状态。</summary>
public static class AddressStore
{
    /// <summary>验证新增、修改和导入共用的地址字段，拒绝空身份、地址、别名及零长度。</summary>
    /// <param name="address">待校验的地址模型；空对象视为无效。</param>
    /// <returns>必填字段完整且长度大于零时返回 true。</returns>
    public static bool IsValid(IAddressModel? address) => address is not null
        && !string.IsNullOrWhiteSpace(address.Guid) && !string.IsNullOrWhiteSpace(address.Address)
        && !string.IsNullOrWhiteSpace(address.AnotherName) && address.Length > 0;

    /// <summary>两端共用的 SQL 分页查询，在数据库中筛选、计数和排序，仅物化当前页；空结果也返回有效页码。</summary>
    /// <typeparam name="T">端侧地址表实体类型。</typeparam>
    /// <param name="db">共享数据库连接。</param>
    /// <param name="dbLock">连接共用的同步锁。</param>
    /// <param name="keyword">地址、别名或描述的关键词；空白查询全部。</param>
    /// <param name="pageIndex">从 1 开始的页码；越界时定位到有效页。</param>
    /// <param name="pageSize">正数页容量；导出可显式请求全部记录。</param>
    /// <param name="total">筛选后的总记录数。</param>
    /// <param name="actualPage">本次查询的有效页码。</param>
    /// <returns>按更新时间倒序的独立实体列表。</returns>
    public static List<T> Query<T>(SQLiteConnection db, object dbLock, string? keyword, int pageIndex, int pageSize,
        out int total, out int actualPage) where T : class, IAddressModel, new()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        lock (dbLock)
        {
            var query = db.Table<T>();
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var term = keyword.Trim();
                query = query.Where(row => row.AnotherName.Contains(term) || row.Address.Contains(term) || row.Describe.Contains(term));
            }
            total = query.Count();
            var pages = (int)Math.Max(1, (total + (long)pageSize - 1) / pageSize);
            actualPage = Math.Clamp(pageIndex, 1, pages);
            return query.OrderByDescending(row => row.Time).Skip((actualPage - 1) * pageSize).Take(pageSize).ToList();
        }
    }

    /// <summary>地址更新结果；失败时数据库保持原值。</summary>
    public enum UpdateResult
    {
        /// <summary>已更新唯一匹配的地址。</summary>
        Updated,
        /// <summary>地址或别名与其他记录重复。</summary>
        Duplicate,
        /// <summary>原记录已删除，或主键已被其他身份复用。</summary>
        Missing,
        /// <summary>必填地址、别名、身份或长度无效。</summary>
        Invalid
    }

    /// <summary>校验业务唯一性及 Index/Guid 身份后更新；同步 SQLite 访问由两端提供的同一把锁串行化。</summary>
    /// <typeparam name="T">具有相同 AddressModel 表映射的端侧实体。</typeparam>
    /// <param name="db">共享数据库连接。</param>
    /// <param name="dbLock">所有连接操作共用的同步锁。</param>
    /// <param name="candidate">独立编辑副本；不得在成功前替换内存中的现有记录。</param>
    /// <returns>成功、重复、原记录不存在或参数无效。</returns>
    /// <exception cref="SQLiteException">数据库操作失败；调用方应显示错误并保持原内存对象。</exception>
    public static UpdateResult Update<T>(SQLiteConnection db, object dbLock, T candidate) where T : class, IAddressModel, new()
    {
        if (!IsValid(candidate))
            return UpdateResult.Invalid;
        lock (dbLock)
        {
            var current = db.Find<T>(candidate.Index);
            if (current is null || !string.Equals(current.Guid, candidate.Guid, StringComparison.Ordinal))
                return UpdateResult.Missing;
            if (db.Table<T>().Where(row => row.Index != candidate.Index
                && (row.AnotherName == candidate.AnotherName || row.Address == candidate.Address || row.Guid == candidate.Guid)).Count() > 0)
                return UpdateResult.Duplicate;
            return db.Update(candidate) == 1 ? UpdateResult.Updated : UpdateResult.Missing;
        }
    }

    /// <summary>在同一事务中解析项目内嵌地址：已有 Guid 使用数据库记录，缺失 Guid 插入完整副本，任何地址或别名冲突都回滚整批。</summary>
    /// <typeparam name="T">端侧 SQLite 地址实体；新副本不继承外部环境的主键。</typeparam>
    /// <param name="db">共享数据库连接。</param>
    /// <param name="dbLock">该连接的同步锁。</param>
    /// <param name="items">项目内嵌地址快照；同一身份只解析一次。</param>
    /// <returns>已提交的数据库实体，调用方据此回灌内存引用；本方法不修改项目树或全局字典。</returns>
    /// <exception cref="InvalidDataException">新增地址的身份、必填字段、长度或业务唯一性无效。</exception>
    /// <exception cref="SQLiteException">事务失败；不会暴露部分提交。</exception>
    public static List<T> Import<T>(SQLiteConnection db, object dbLock, IEnumerable<IAddressModel> items)
        where T : class, IAddressModel, new()
    {
        var resolved = new List<T>();
        lock (dbLock)
        {
            db.RunInTransaction(() =>
            {
                var existing = db.Table<T>().ToList();
                var byGuid = new System.Collections.Concurrent.ConcurrentDictionary<string, T>(StringComparer.Ordinal);
                foreach (var row in existing) byGuid[row.Guid] = row;
                var addresses = existing.Select(row => row.Address).ToHashSet(StringComparer.Ordinal);
                var aliases = existing.Select(row => row.AnotherName).ToHashSet(StringComparer.Ordinal);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var source in items)
                {
                    if (string.IsNullOrWhiteSpace(source.Guid)) throw new InvalidDataException("导入地址的身份不能为空");
                    if (!seen.Add(source.Guid)) continue;
                    if (byGuid.TryGetValue(source.Guid, out var stored))
                    {
                        resolved.Add(stored);
                        continue;
                    }
                    if (!IsValid(source))
                        throw new InvalidDataException("导入地址的地址、别名和长度必须有效");
                    if (!addresses.Add(source.Address) || !aliases.Add(source.AnotherName))
                        throw new InvalidDataException("导入地址或别名与其他身份冲突");
                    var entity = new T
                    {
                        Guid = source.Guid,
                        Address = source.Address,
                        AnotherName = source.AnotherName,
                        Type = source.Type,
                        Length = source.Length,
                        EncodingType = source.EncodingType,
                        Describe = source.Describe,
                        Topic = source.Topic,
                        SimplifyValue = source.SimplifyValue,
                        ExpandParam = source.ExpandParam,
                        Time = source.Time
                    };
                    db.Insert(entity);
                    byGuid[entity.Guid] = entity;
                    resolved.Add(entity);
                }
            });
        }
        return resolved;
    }

    /// <summary>按主键和 Guid 双重身份批量删除，返回已提交的行；事务回滚时抛出异常，不向调用方暴露部分删除。</summary>
    /// <typeparam name="T">端侧地址实体类型。</typeparam>
    /// <param name="db">共享数据库连接。</param>
    /// <param name="dbLock">所有连接操作共用的同步锁。</param>
    /// <param name="items">待删除的快照；项目引用检查必须由调用方在其项目结构同步边界内完成。</param>
    /// <returns>实际删除的实体，供提交后同步内存字典；过期身份不会删除其他记录。</returns>
    public static List<T> Delete<T>(SQLiteConnection db, object dbLock, IEnumerable<T> items) where T : class, IAddressModel, new()
    {
        var deleted = new List<T>();
        lock (dbLock)
        {
            db.RunInTransaction(() =>
            {
                foreach (var item in items)
                {
                    var current = db.Find<T>(item.Index);
                    if (current is not null && string.Equals(current.Guid, item.Guid, StringComparison.Ordinal)
                        && db.Delete(current) == 1)
                        deleted.Add(item);
                }
            });
        }
        return deleted;
    }
}
