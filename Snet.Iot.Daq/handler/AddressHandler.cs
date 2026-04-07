using Snet.Iot.Daq.data;
using Snet.Model.data;
using SQLite;
using System.Collections.Concurrent;

namespace Snet.Iot.Daq.handler
{
    /// <summary>
    /// 地址处理静态类<br/>
    /// 提供地址数据的 CRUD 操作，包括 SQLite 批量插入、全局字典管理等功能
    /// </summary>
    public static class AddressHandler
    {
        /// <summary>
        /// 批量插入结果统计模型
        /// <para>
        /// 用于记录一次批量插入（通常在事务中）的执行结果，
        /// 常见于：
        /// <list type="bullet">
        /// <item>Excel / CSV 导入数据库</item>
        /// <item>批量同步数据</item>
        /// <item>防重复插入操作</item>
        /// </list>
        /// </para>
        /// </summary>
        /// <remarks>
        /// 该模型只负责结果统计，不参与任何业务逻辑判断，
        /// 便于在 UI 层、日志系统或调用方统一展示和处理。
        /// </remarks>
        public sealed class BatchInsertResult
        {
            /// <summary>
            /// 成功插入的数据条数
            /// <para>
            /// 表示实际执行 <c>INSERT</c> 并成功写入数据库的记录数量。
            /// </para>
            /// <para>
            /// 注意：
            /// <list type="bullet">
            /// <item>只统计数据库返回成功的插入</item>
            /// <item>不会包含被判定为重复而跳过的记录</item>
            /// </list>
            /// </para>
            /// </summary>
            public int Success { get; set; }

            /// <summary>
            /// 被判定为重复而未插入的数据条数
            /// <para>
            /// 通常是通过以下方式判定为重复：
            /// <list type="bullet">
            /// <item>与数据库中已有数据重复</item>
            /// <item>与同一批次导入的数据发生重复</item>
            /// </list>
            /// </para>
            /// <para>
            /// 被计入该数量的记录不会触发数据库插入操作。
            /// </para>
            /// </summary>
            public int Duplicate { get; set; }

            /// <summary>
            /// 插入失败的数据条数
            /// <para>
            /// 表示已经尝试执行插入操作，但由于异常或返回结果失败而未能写入数据库的记录数量。
            /// </para>
            /// <para>
            /// 常见原因包括：
            /// <list type="bullet">
            /// <item>数据库约束冲突（唯一索引、外键等）</item>
            /// <item>字段数据格式错误</item>
            /// <item>SQLite 异常或事务回滚</item>
            /// </list>
            /// </para>
            /// </summary>
            public int Failed { get; set; }
        }

        /// <summary>
        /// 批量插入（防重复），基于指定字段查重
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <typeparam name="TKey">查重字段类型</typeparam>
        /// <param name="db">SQLite 连接</param>
        /// <param name="items">待插入数据</param>
        /// <param name="keySelectors">查重字段选择器（可多个）</param>
        public static BatchInsertResult InsertUnique<T, TKey>(
            SQLiteConnection db,
            IEnumerable<T> items,
            params Func<T, TKey>[] keySelectors)
            where T : class, new()
        {
            var result = new BatchInsertResult();

            // 1️⃣ 读取已有数据
            var existingKeys = keySelectors
                .Select(_ => new HashSet<TKey>())
                .ToArray();

            foreach (var item in db.Table<T>())
            {
                for (int i = 0; i < keySelectors.Length; i++)
                {
                    var key = keySelectors[i](item);
                    if (key != null)
                        existingKeys[i].Add(key);
                }
            }

            // 2️⃣ 事务插入
            db.RunInTransaction(() =>
            {
                foreach (var item in items)
                {
                    bool isDuplicate = false;

                    for (int i = 0; i < keySelectors.Length; i++)
                    {
                        var key = keySelectors[i](item);
                        if (key != null && existingKeys[i].Contains(key))
                        {
                            isDuplicate = true;
                            break;
                        }
                    }

                    if (isDuplicate)
                    {
                        result.Duplicate++;
                        continue;
                    }

                    if (db.Insert(item) > 0)
                    {
                        result.Success++;

                        //往全局集合中添加
                        (item as AddressModel).SetAddress();

                        // 插入成功，加入集合，防止同批次重复
                        for (int i = 0; i < keySelectors.Length; i++)
                        {
                            var key = keySelectors[i](item);
                            if (key != null)
                                existingKeys[i].Add(key);
                        }
                    }
                    else
                    {
                        result.Failed++;
                    }
                }
            });

            return result;
        }




        /// <summary>
        /// 从 SQLite 数据库加载所有地址记录到全局字典<br/>
        /// 并为每个地址触发信息事件，用于初始化界面绑定
        /// </summary>
        /// <returns>全局地址字典（Key = Guid，Value = AddressModel）</returns>
        public static ConcurrentDictionary<string, AddressModel> GetAllAddress()
        {
            foreach (var item in GlobalConfigModel.sqliteOperate.Table<Snet.Iot.Daq.data.AddressModel>())
            {
                GlobalConfigModel.AddressDict[item.Guid] = item;
                GlobalConfigModel.AddressDict[item.Guid].OnInfoEventHandlerAsync(item, EventInfoResult.CreateSuccessResult("set enevt"));
            }
            return GlobalConfigModel.AddressDict;
        }

        /// <summary>
        /// 添加或更新地址到全局统一集合<br/>
        /// 同时触发信息事件通知并异步刷新界面
        /// </summary>
        /// <param name="address">待注册的地址对象</param>
        public static void SetAddress(this AddressModel address)
        {
            GlobalConfigModel.AddressDict[address.Guid] = address;
            GlobalConfigModel.AddressDict[address.Guid].OnInfoEventHandlerAsync(address, EventInfoResult.CreateSuccessResult("set enevt"));
            _ = GlobalConfigModel.RefreshAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 根据 GUID 从全局字典获取地址对象
        /// </summary>
        /// <param name="guid">地址的唯一标识</param>
        /// <returns>对应的地址对象，未找到时返回 null</returns>
        public static AddressModel? GetAddress(this string guid)
        {
            if (GlobalConfigModel.AddressDict.TryGetValue(guid, out AddressModel? model))
            {
                return model;
            }
            return null;
        }
    }
}
