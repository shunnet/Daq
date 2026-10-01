using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.@interface;
using Snet.Iot.Daq.data;
using Snet.Model.data;
using SQLite;
using System.Collections.Concurrent;

namespace Snet.Iot.Daq.handler
{
    /// <summary>
    /// 地址处理静态类
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
            var inserted = new List<T>();
            var result = ProjectHandlerCore.InsertUnique(
                db,
                GlobalConfigModel.DbLock,
                items,
                onInserted: inserted.Add,
                keySelectors);
            foreach (var item in inserted.OfType<IAddressModel>())
            {
                GlobalConfigModel.AddressDict[item.Guid] = item;
                _ = item.OnInfoEventHandlerAsync(item, EventInfoResult.CreateSuccessResult("set event"))
                    .ContinueWith(t => Snet.Log.LogHelper.Error(t.Exception?.Message ?? "异步通知失败"), TaskContinuationOptions.OnlyOnFaulted);
            }
            // 批量导入全部提交后只刷新一次，避免每个地址重复重载所有设备。
            if (inserted.Count > 0)
                _ = GlobalConfigModel.RefreshAsync()
                    .ContinueWith(t => Snet.Log.LogHelper.Error(t.Exception?.Message ?? "异步通知失败"), TaskContinuationOptions.OnlyOnFaulted);
            return result;
        }

        /// <summary>
        /// 从 SQLite 数据库加载所有地址记录到全局字典<br/>
        /// 并为每个地址触发信息事件，用于初始化界面绑定
        /// </summary>
        /// <returns>全局地址字典（Key = Guid，Value = AddressModel）</returns>
        public static ConcurrentDictionary<string, IAddressModel> GetAllAddress()
        {
            List<AddressModel> rows;
            lock (GlobalConfigModel.DbLock)
            {
                rows = GlobalConfigModel.sqliteOperate.Table<AddressModel>().ToList();
            }
            foreach (var item in rows)
            {
                GlobalConfigModel.AddressDict[item.Guid] = item;
                _ = GlobalConfigModel.AddressDict[item.Guid]
                    .OnInfoEventHandlerAsync(item, EventInfoResult.CreateSuccessResult("set event"))
                    .ContinueWith(t => Snet.Log.LogHelper.Error(t.Exception?.Message ?? "异步通知失败"), TaskContinuationOptions.OnlyOnFaulted);
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
            _ = GlobalConfigModel.AddressDict[address.Guid]
                .OnInfoEventHandlerAsync(address, EventInfoResult.CreateSuccessResult("set event"))
                .ContinueWith(t => Snet.Log.LogHelper.Error(t.Exception?.Message ?? "异步通知失败"), TaskContinuationOptions.OnlyOnFaulted);
            _ = GlobalConfigModel.RefreshAsync()
                .ContinueWith(t => Snet.Log.LogHelper.Error(t.Exception?.Message ?? "异步通知失败"), TaskContinuationOptions.OnlyOnFaulted);
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
