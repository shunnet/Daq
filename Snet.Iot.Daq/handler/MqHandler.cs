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
    /// 消息传输处理器<br/>
    /// 管理 IMq 实例的生命周期，执行生产、消费等消息传输操作。
    /// 每个传输设备通过 guid 唯一标识，内部使用 ConcurrentDictionary 缓存已打开的实例。
    /// </summary>
    public class MqHandler : CoreUnify<MqHandler, PluginConfigModel>, IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// 有参构造函数
        /// </summary>
        /// <param name="basics">插件配置基础数据，包含传输设备类型、连接参数等</param>
        public MqHandler(PluginConfigModel basics) : base(basics) { }

        /// <summary>
        /// 已打开的 MQ 实例缓存<br/>
        /// Key = 设备 guid，Value = 对应的 IMq 实例
        /// </summary>
        private readonly ConcurrentDictionary<string, IMq> icoMq = new();

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

        /// <summary>
        /// 数据事件回调：将底层 IMq 的数据事件转发到上层
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">数据事件结果</param>
        /// <param name="guid">触发事件的设备 guid</param>
        private async Task Operate_OnDataEventAsync(object? sender, EventDataResult e, string guid)
        {
            await OnDataEventHandlerAsync(guid, e);
        }

        /// <summary>
        /// 信息事件回调：将底层 IMq 的信息事件转发到上层
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">信息事件结果</param>
        /// <param name="guid">触发事件的设备 guid</param>
        private async Task Operate_OnInfoEventAsync(object? sender, EventInfoResult e, string guid)
        {
            await OnInfoEventHandlerAsync(guid, e);
        }

        /// <inheritdoc/>
        /// <summary>
        /// 同步释放所有已打开的 MQ 实例并清空事件委托缓存
        /// </summary>
        public override void Dispose()
        {
            // 快照遍历，避免在迭代期间集合被修改
            foreach (var item in icoMq.ToArray())
            {
                item.Value.Dispose();
            }
            icoMq.Clear();
            _dataHandlers.Clear();
            _infoHandlers.Clear();

            base.Dispose();
        }

        /// <inheritdoc/>
        /// <summary>
        /// 异步释放所有已打开的 MQ 实例并清空事件委托缓存
        /// </summary>
        public override async ValueTask DisposeAsync()
        {
            // 快照遍历，避免在迭代期间集合被修改
            foreach (var item in icoMq.ToArray())
            {
                await item.Value.DisposeAsync();
            }
            icoMq.Clear();
            _dataHandlers.Clear();
            _infoHandlers.Clear();

            await base.DisposeAsync();
        }

        /// <summary>
        /// 打开或获取指定 guid 的 MQ 实例<br/>
        /// 1. 若缓存中不存在则通过插件工厂创建新实例<br/>
        /// 2. 检查连接状态，未连接时注册事件并执行打开操作<br/>
        /// 3. 使用 per-guid 字典管理事件委托，确保多设备场景下事件正确注册和注销
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <returns>MQ 实例和操作结果的元组</returns>
        private async Task<(IMq operate, OperateResult result)> OpenAsync(string guid)
        {
            if (!icoMq.TryGetValue(guid, out IMq? operate))
            {
                operate = await basics.CreateNewObjetcAsync<IMq>();
                icoMq.TryAdd(guid, operate);
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
        /// 向指定主题生产单条消息<br/>
        /// 根据地址配置决定是否精简数据，序列化后发送
        /// </summary>
        /// <param name="guid">传输设备唯一标识符</param>
        /// <param name="address">地址模型，包含主题、精简值设置、编码类型等</param>
        /// <param name="value">待发送的地址值数据</param>
        /// <returns>操作结果，包含生产成功/失败状态</returns>
        public async Task<OperateResult> ProduceAsync(string guid, AddressModel address, AddressValue value)
        {
            //打开
            (IMq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            //传输的内容
            string content = address.SimplifyValue ? value.GetSimplify().ToJson(true) : value.ToJson(true);

            //生产数据
            return await open.operate.ProduceAsync(address.Topic, content, address.EncodingType.GetEncoding());
        }

        /// <summary>
        /// 批量并行生产多条消息<br/>
        /// 使用 Task.WhenAll 并行发送，ConcurrentBag 收集错误，确保线程安全
        /// </summary>
        /// <param name="guid">传输设备唯一标识符</param>
        /// <param name="values">地址与值的映射字典</param>
        /// <returns>操作结果，包含批量生产状态，失败时返回详细错误信息</returns>
        public async Task<OperateResult> ProduceAsync(string guid, IDictionary<AddressModel, AddressValue> values)
        {
            //打开
            (IMq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            IMq mq = open.operate;
            ConcurrentBag<string> errors = new();
            List<Task> tasks = new(values.Count);
            foreach (var item in values)
            {
                var key = item.Key;
                var value = item.Value;

                tasks.Add(ProduceSingleAsync(mq, key, value, errors));
            }
            await Task.WhenAll(tasks);
            if (!errors.IsEmpty)
            {
                return OperateResult.CreateFailureResult(string.Join(Environment.NewLine, errors));
            }
            return OperateResult.CreateSuccessResult("Produce success");
        }

        /// <summary>
        /// 单条生产（提取自批量循环，避免 Task.Run 包装 IO 密集型操作）
        /// </summary>
        private static async Task ProduceSingleAsync(IMq mq, AddressModel key, AddressValue value, ConcurrentBag<string> errors)
        {
            try
            {
                string content = key.SimplifyValue
                    ? value.GetSimplify().ToJson(true)
                    : value.ToJson(true);

                var result = await mq.ProduceAsync(
                    key.Topic,
                    content,
                    key.EncodingType.GetEncoding());

                if (!result.GetDetails(out string? msg))
                {
                    errors.Add($"{key.Address} {msg}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{key.Address} {ex.Message}");
            }
        }

        /// <summary>
        /// 从指定主题消费消息<br/>
        /// 订阅后设备将持续推送该主题的消息到数据事件
        /// </summary>
        /// <param name="guid">传输设备唯一标识符</param>
        /// <param name="topic">订阅的消息主题</param>
        /// <returns>操作结果，包含消费订阅成功/失败状态</returns>
        public async Task<OperateResult> ConsumerAsync(string guid, string topic)
        {
            // 打开或获取传输设备实例
            (IMq operate, OperateResult result) open = await OpenAsync(guid);

            if (!open.result.Status)
            {
                return open.result;
            }

            // 执行消费订阅
            return await open.operate.ConsumeAsync(topic);
        }


    }
}
