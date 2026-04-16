using Snet.Core.extend;
using Snet.Core.handler;
using Snet.Iot.Daq.data;
using Snet.Model.data;
using Snet.Model.@interface;
using Snet.Utility;
using System.Collections.Concurrent;

namespace Snet.Iot.Daq.handler
{
    /// <summary>
    /// 数据采集处理器<br/>
    /// 管理 IDaq 实例的生命周期，执行读取、写入、订阅、WebAPI 等数据采集操作。
    /// 每个设备通过 guid 唯一标识，内部使用 ConcurrentDictionary 缓存已打开的实例。
    /// </summary>
    public class DqaHandler : CoreUnify<DqaHandler, PluginConfigModel>, IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// 有参构造函数
        /// </summary>
        /// <param name="basics">插件配置基础数据，包含设备类型、连接参数等</param>
        public DqaHandler(PluginConfigModel basics) : base(basics) { }

        /// <summary>
        /// 已打开的 DAQ 实例缓存<br/>
        /// Key = 设备 guid，Value = 对应的 IDaq 实例
        /// </summary>
        private readonly ConcurrentDictionary<string, IDaq> icoDaq = new();

        /// <summary>
        /// 每个 guid 对应的数据事件委托缓存<br/>
        /// 修复：原先使用单个共享字段，多设备场景下会导致事件泄漏和委托覆盖
        /// </summary>
        private readonly ConcurrentDictionary<string, EventHandlerAsync<EventDataResult>> _dataHandlers = new();

        /// <summary>
        /// 每个 guid 对应的信息事件委托缓存<br/>
        /// 修复：原先使用单个共享字段，多设备场景下会导致事件泄漏和委托覆盖
        /// </summary>
        private readonly ConcurrentDictionary<string, EventHandlerAsync<EventInfoResult>> _infoHandlers = new();

        /// <inheritdoc/>
        /// <summary>
        /// 同步释放所有已打开的 DAQ 实例并清空事件委托缓存
        /// </summary>
        public override void Dispose()
        {
            // 快照遍历，避免在迭代期间集合被修改
            foreach (var item in icoDaq.ToArray())
            {
                item.Value.Dispose();
            }
            icoDaq.Clear();
            _dataHandlers.Clear();
            _infoHandlers.Clear();

            base.Dispose();
        }

        /// <inheritdoc/>
        /// <summary>
        /// 异步释放所有已打开的 DAQ 实例并清空事件委托缓存
        /// </summary>
        public override async ValueTask DisposeAsync()
        {
            // 快照遍历，避免在迭代期间集合被修改
            foreach (var item in icoDaq.ToArray())
            {
                if (item.Value != null)
                {
                    await item.Value.DisposeAsync();
                }
            }
            icoDaq.Clear();
            _dataHandlers.Clear();
            _infoHandlers.Clear();

            await base.DisposeAsync();
        }

        /// <summary>
        /// 数据事件回调：将底层 IDaq 的数据事件转发到上层
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">数据事件结果</param>
        /// <param name="guid">触发事件的设备 guid</param>
        private async Task Operate_OnDataEventAsync(object? sender, EventDataResult e, string guid)
        {
            await OnDataEventHandlerAsync(guid, e);
        }

        /// <summary>
        /// 信息事件回调：将底层 IDaq 的信息事件转发到上层
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">信息事件结果</param>
        /// <param name="guid">触发事件的设备 guid</param>
        private async Task Operate_OnInfoEventAsync(object? sender, EventInfoResult e, string guid)
        {
            await OnInfoEventHandlerAsync(guid, e);
        }

        /// <summary>
        /// 打开或获取指定 guid 的 DAQ 实例<br/>
        /// 1. 若缓存中不存在则通过插件工厂创建新实例<br/>
        /// 2. 检查连接状态，未连接时注册事件并执行打开操作<br/>
        /// 3. 使用 per-guid 字典管理事件委托，确保多设备场景下事件正确注册和注销
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <returns>DAQ 实例和操作结果的元组</returns>
        private async Task<(IDaq operate, OperateResult result)> OpenAsync(string guid)
        {
            if (!icoDaq.TryGetValue(guid, out IDaq? operate))
            {
                operate = await basics.CreateNewObjetcAsync<IDaq>();
                icoDaq.TryAdd(guid, operate);
            }

            if (operate == null)
            {
                return (default, OperateResult.CreateFailureResult("插件尚未加载".GetLanguageValue(App.LanguageOperate)));
            }

            // 获取驱动状态
            OperateResult result = await operate.GetStatusAsync();

            // 未连接时执行事件注册和打开操作
            if (!result.Status)
            {
                // 注销该 guid 旧的数据事件处理程序，避免重复订阅
                if (_dataHandlers.TryRemove(guid, out var oldDataHandler))
                    operate.OnDataEventAsync -= oldDataHandler;

                // 创建并缓存该 guid 专属的数据事件委托
                EventHandlerAsync<EventDataResult> newDataHandler = async (sender, e) => await Operate_OnDataEventAsync(sender, e, guid);
                _dataHandlers[guid] = newDataHandler;
                operate.OnDataEventAsync += newDataHandler;

                // 注销该 guid 旧的信息事件处理程序，避免重复订阅
                if (_infoHandlers.TryRemove(guid, out var oldInfoHandler))
                    operate.OnInfoEventAsync -= oldInfoHandler;

                // 创建并缓存该 guid 专属的信息事件委托
                EventHandlerAsync<EventInfoResult> newInfoHandler = async (sender, e) => await Operate_OnInfoEventAsync(sender, e, guid);
                _infoHandlers[guid] = newInfoHandler;
                operate.OnInfoEventAsync += newInfoHandler;

                // 执行打开操作
                result = await operate.OnAsync();
            }
            return (operate, result);
        }

        /// <summary>
        /// 打开 WebAPI 服务<br/>
        /// 先确保设备已连接，再启动 WebAPI 功能
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <param name="model">WebAPI 配置参数</param>
        /// <returns>操作结果，包含成功/失败状态及消息</returns>
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
        /// 查询 WebAPI 服务状态
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <returns>操作结果，包含 WebAPI 运行状态信息</returns>
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
        /// 关闭 WebAPI 服务
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <returns>操作结果，包含关闭状态信息</returns>
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
        /// 获取 WebAPI 请求示例<br/>
        /// 返回当前设备支持的 WebAPI 请求格式示例
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <returns>操作结果，ResultData 中包含请求示例</returns>
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
        /// 读取单个地址数据<br/>
        /// 将 AddressModel 转换为底层 Address 后执行读取
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <param name="address">待读取的地址模型</param>
        /// <returns>操作结果，ResultData 中包含读取到的数据</returns>
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
        /// 批量读取多个地址数据<br/>
        /// 将 AddressModel 集合转换为底层 Address 后执行批量读取
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <param name="address">待读取的地址模型集合</param>
        /// <returns>操作结果，ResultData 中包含批量读取到的数据</returns>
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
        /// 向指定地址写入数据<br/>
        /// 将单个地址和写入值封装为字典后执行写入操作
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <param name="address">目标地址模型</param>
        /// <param name="write">待写入的数据模型</param>
        /// <returns>操作结果，包含写入成功/失败状态</returns>
        public async Task<OperateResult> WriteAsync(string guid, AddressModel address, WriteModel write)
        {
            // 打开或获取设备实例
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            // 设备未就绪，直接返回失败结果
            if (!open.result.Status)
            {
                return open.result;
            }

            // 组织写入数据：以地址字符串为 Key，写入模型为 Value
            var keys = new ConcurrentDictionary<string, WriteModel>();
            keys[address.Address] = write;

            // 执行写入操作
            return await open.operate.WriteAsync(keys);
        }


        /// <summary>
        /// 订阅单个地址的数据变化<br/>
        /// 订阅后设备将持续推送该地址的数据变更事件
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <param name="address">待订阅的地址模型</param>
        /// <returns>操作结果，包含订阅成功/失败状态</returns>
        public async Task<OperateResult> SubscribeAsync(string guid, AddressModel address)
        {
            // 打开或获取设备实例
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            if (!open.result.Status)
            {
                return open.result;
            }

            // 订阅地址
            return await open.operate.SubscribeAsync(address.AddressConvert());
        }

        /// <summary>
        /// 批量订阅多个地址的数据变化
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <param name="address">待订阅的地址模型集合</param>
        /// <returns>操作结果，包含批量订阅成功/失败状态</returns>
        public async Task<OperateResult> SubscribeAsync(string guid, List<AddressModel> address)
        {
            // 打开或获取设备实例
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            if (!open.result.Status)
            {
                return open.result;
            }

            // 批量订阅地址
            return await open.operate.SubscribeAsync(address.AddressConvert());
        }

        /// <summary>
        /// 取消订阅单个地址的数据变化
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <param name="address">待取消订阅的地址模型</param>
        /// <returns>操作结果，包含取消订阅成功/失败状态</returns>
        public async Task<OperateResult> UnSubscribeAsync(string guid, AddressModel address)
        {
            // 打开或获取设备实例
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            if (!open.result.Status)
            {
                return open.result;
            }

            // 取消订阅地址
            return await open.operate.UnSubscribeAsync(address.AddressConvert());
        }

        /// <summary>
        /// 批量取消订阅多个地址的数据变化
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <param name="address">待取消订阅的地址模型集合</param>
        /// <returns>操作结果，包含批量取消订阅成功/失败状态</returns>
        public async Task<OperateResult> UnSubscribeAsync(string guid, List<AddressModel> address)
        {
            // 打开或获取设备实例
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            if (!open.result.Status)
            {
                return open.result;
            }

            // 批量取消订阅地址
            return await open.operate.UnSubscribeAsync(address.AddressConvert());
        }

        /// <summary>
        /// 获取指定设备的连接状态
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <returns>操作结果，包含当前设备连接状态信息</returns>
        public async Task<OperateResult> GetStatusAsync(string guid)
        {
            // 打开或获取设备实例
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            if (!open.result.Status)
            {
                return open.result;
            }

            // 查询设备状态
            return await open.operate.GetStatusAsync();
        }

    }
}
