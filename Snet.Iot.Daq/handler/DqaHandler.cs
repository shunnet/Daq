using Snet.Core.extend;
using Snet.Iot.Daq.data;
using Snet.Model.data;
using Snet.Model.@interface;
using Snet.Utility;
using System.Collections.Concurrent;

namespace Snet.Iot.Daq.handler
{
    /// <summary>
    /// 采集处理<br/>
    /// 执行数据采集一系列操作
    /// </summary>
    public class DqaHandler : CoreUnify<DqaHandler, PluginConfigModel>, IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// 有参构造函数
        /// </summary>
        /// <param name = "basics">基础数据</param>
        public DqaHandler(PluginConfigModel basics) : base(basics) { }

        /// <summary>
        /// 已经打开的 - DAQ实例对象<br/>
        /// string = guid
        /// </summary>
        private readonly ConcurrentDictionary<string, IDaq> icoDaq = new();

        /// <inheritdoc/>
        public override void Dispose()
        {
            foreach (var item in icoDaq)
            {
                item.Value.Dispose();
            }
            icoDaq.Clear();

            base.Dispose();
        }

        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            foreach (var item in icoDaq)
            {
                await item.Value.DisposeAsync();
            }
            icoDaq.Clear();

            await base.DisposeAsync();
        }

        /// <summary>
        /// 匿名的数据事件
        /// </summary>
        private EventHandlerAsync<EventDataResult>? AnonymityDataEventHandlerAsync;

        /// <summary>
        /// 匿名的信息事件
        /// </summary>
        private EventHandlerAsync<EventInfoResult>? AnonymityInfoEventHandlerAsync;

        /// <summary>
        /// 数据事件
        /// </summary>
        private async Task Operate_OnDataEventAsync(object? sender, EventDataResult e, string guid)
        {
            await OnDataEventHandlerAsync(guid, e);
        }

        /// <summary>
        /// 信息事件
        /// </summary>
        private async Task Operate_OnInfoEventAsync(object? sender, EventInfoResult e, string guid)
        {
            await OnInfoEventHandlerAsync(guid, e);
        }

        /// <summary>
        /// 打开
        /// </summary>
        /// <param name="guid">唯一标识符</param>
        /// <returns>返回操作结果</returns>
        private async Task<(IDaq operate, OperateResult result)> OpenAsync(string guid)
        {
            if (!icoDaq.TryGetValue(guid, out IDaq? operate))
            {
                operate = await basics.CreateNewObjetcAsync<IDaq>();

                icoDaq.TryAdd(guid, operate);
            }
            //获取驱动状态
            OperateResult result = await operate.GetStatusAsync();
            //未连接
            if (!result.Status)
            {
                // 注销旧事件处理程序，避免重复订阅
                if (AnonymityDataEventHandlerAsync != null)
                    operate.OnDataEventAsync -= OnDataEventHandlerAsync;
                // 创建新的委托实例并缓存引用
                AnonymityDataEventHandlerAsync = async (sender, e) => await Operate_OnDataEventAsync(sender, e, guid);
                // 注册事件
                operate.OnDataEventAsync += AnonymityDataEventHandlerAsync;


                // 注销旧事件处理程序，避免重复订阅
                if (AnonymityInfoEventHandlerAsync != null)
                    operate.OnInfoEventAsync -= OnInfoEventHandlerAsync;
                // 创建新的委托实例并缓存引用
                AnonymityInfoEventHandlerAsync = async (sender, e) => await Operate_OnInfoEventAsync(sender, e, guid);
                // 注册事件
                operate.OnInfoEventAsync += AnonymityInfoEventHandlerAsync;

                //打开
                result = await operate.OnAsync();
            }
            return (operate, result);
        }

        /// <summary>
        /// 打开webapi
        /// </summary>
        /// <param name="guid">唯一标识符</param>
        /// <param name="model">入参</param>
        /// <returns>操作状态</returns>
        public async Task<OperateResult> WAOnAsync(string guid, WAModel model)
        {
            //打开
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            return await open.operate.WAOnAsync(model);
        }

        /// <summary>
        /// 打开webapi
        /// </summary>
        /// <param name="guid">唯一标识符</param>
        /// <param name="model">入参</param>
        /// <returns>操作状态</returns>
        public async Task<OperateResult> WAStatusAsync(string guid)
        {
            //打开
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            return await open.operate.WAStatusAsync();
        }

        /// <summary>
        /// 关闭webapi
        /// </summary>
        /// <param name="guid">唯一标识符</param>
        /// <returns>操作状态</returns>
        public async Task<OperateResult> WAOffAsync(string guid)
        {
            //打开
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            return await open.operate.WAOffAsync();
        }

        /// <summary>
        /// webapi示例
        /// </summary>
        /// <param name="guid">唯一标识符</param>
        /// <returns>操作状态</returns>
        public async Task<OperateResult> WARequestExampleAsync(string guid)
        {
            //打开
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            return await open.operate.WARequestExampleAsync();
        }

        /// <summary>
        /// 读取
        /// </summary>
        /// <param name="guid">唯一标识符</param>
        /// <param name="address">地址</param>
        /// <returns>操作状态</returns>
        public async Task<OperateResult> ReadAsync(string guid, AddressModel address)
        {
            //打开
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            //读取数据
            return await open.operate.ReadAsync(address.AddressConvert());
        }

        /// <summary>
        /// 读取
        /// </summary>
        /// <param name="guid">唯一标识符</param>
        /// <param name="address">地址集合</param>
        /// <returns>操作状态</returns>
        public async Task<OperateResult> ReadAsync(string guid, List<AddressModel> address)
        {
            //打开
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            //读取数据
            return await open.operate.ReadAsync(address.AddressConvert());
        }

        /// <summary>
        /// 写入
        /// </summary>
        /// <param name="guid">唯一标识符</param>
        /// <param name="address">地址</param>
        /// <param name="write">写入的数据</param>
        /// <returns>操作状态</returns>
        public async Task<OperateResult> WriteAsync(string guid, AddressModel address, WriteModel write)
        {
            //打开
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            //组织写入数据
            ConcurrentDictionary<string, WriteModel> keys = new()
            {
                [address.Address] = write
            };

            //写入数据
            return await open.operate.WriteAsync(keys);
        }


        /// <summary>
        /// 订阅
        /// </summary>
        /// <param name="guid">唯一标识符</param>
        /// <param name="address">地址</param>
        /// <returns>操作状态</returns>
        public async Task<OperateResult> SubscribeAsync(string guid, AddressModel address)
        {
            //打开
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            //读取数据
            return await open.operate.SubscribeAsync(address.AddressConvert());
        }

        /// <summary>
        /// 订阅
        /// </summary>
        /// <param name="guid">唯一标识符</param>
        /// <param name="address">地址集合</param>
        /// <returns>操作状态</returns>
        public async Task<OperateResult> SubscribeAsync(string guid, List<AddressModel> address)
        {
            //打开
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            //读取数据
            return await open.operate.SubscribeAsync(address.AddressConvert());
        }


        /// <summary>
        /// 取消订阅
        /// </summary>
        /// <param name="guid">唯一标识符</param>
        /// <param name="address">地址</param>
        /// <returns>操作状态</returns>
        public async Task<OperateResult> UnSubscribeAsync(string guid, AddressModel address)
        {
            //打开
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            //读取数据
            return await open.operate.UnSubscribeAsync(address.AddressConvert());
        }

        /// <summary>
        /// 取消订阅
        /// </summary>
        /// <param name="guid">唯一标识符</param>
        /// <param name="address">地址集合</param>
        /// <returns>操作状态</returns>
        public async Task<OperateResult> UnSubscribeAsync(string guid, List<AddressModel> address)
        {
            //打开
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            //读取数据
            return await open.operate.UnSubscribeAsync(address.AddressConvert());
        }

    }
}
