using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@interface;
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
        /// 批量插入（防重复），基于指定字段查重
        /// </summary>
        /// <typeparam name="T">实体类型</typeparam>
        /// <typeparam name="TKey">查重字段类型</typeparam>
        /// <param name="db">SQLite 连接</param>
        /// <param name="items">待插入数据</param>
        /// <param name="keySelectors">查重字段选择器（可多个）</param>
        public static BatchInsertResult InsertUnique<T, TKey>(SQLiteConnection db, IEnumerable<T> items, params Func<T, TKey>[] keySelectors) where T : class, new()
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
                        (item as IAddressModel).SetAddress();

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
        public static ConcurrentDictionary<string, IAddressModel> GetAllAddress()
        {
            foreach (var item in GlobalConfigModel.sqliteOperate.Table<AddressModel>())
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
        public static void SetAddress(this IAddressModel address)
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
        public static IAddressModel? GetAddress(this string guid)
        {
            if (GlobalConfigModel.AddressDict.TryGetValue(guid, out IAddressModel? model))
            {
                return model;
            }
            return null;
        }
    }
}
