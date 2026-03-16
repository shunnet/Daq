using Snet.Core.extend;
using Snet.Iot.Daq.data;
using Snet.Model.data;
using Snet.Model.@interface;
using Snet.Utility;
using System.Collections.Concurrent;

namespace Snet.Iot.Daq.handler
{
    /// <summary>
    /// 传输处理<br/>
    /// 执行数据采集一系列操作
    /// </summary>
    public class MqHandler : CoreUnify<MqHandler, PluginConfigModel>, IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// 有参构造函数
        /// </summary>
        /// <param name = "basics">基础数据</param>
        public MqHandler(PluginConfigModel basics) : base(basics) { }

        /// <summary>
        /// 已经打开的 - DAQ实例对象<br/>
        /// string = guid
        /// </summary>
        private readonly ConcurrentDictionary<string, IMq> icoMq = new();

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

        /// <inheritdoc/>
        public override void Dispose()
        {
            foreach (var item in icoMq)
            {
                item.Value.Dispose();
            }
            icoMq.Clear();

            base.Dispose();
        }

        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            foreach (var item in icoMq)
            {
                await item.Value.DisposeAsync();
            }
            icoMq.Clear();

            await base.DisposeAsync();
        }

        /// <summary>
        /// 打开
        /// </summary>
        /// <param name="guid">唯一标识符</param>
        /// <returns>返回操作结果</returns>
        private async Task<(IMq operate, OperateResult result)> OpenAsync(string guid)
        {
            if (!icoMq.TryGetValue(guid, out IMq? operate))
            {
                operate = await basics.CreateNewObjetcAsync<IMq>();

                icoMq.TryAdd(guid, operate);
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
        /// 生产
        /// </summary>
        /// <param name="guid">唯一标识符</param>
        /// <param name="address">地址</param>
        /// <param name="value">地址值</param>
        /// <returns>操作状态</returns>
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
        /// 批量生产
        /// </summary>
        /// <param name="guid">唯一标识符</param>
        /// <param name="values">地址与值</param>
        /// <returns>操作状态</returns>
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
        /// 消费
        /// </summary>
        /// <param name="guid">唯一标识符</param>
        /// <param name="topic">主题</param>
        /// <returns>操作状态</returns>
        public async Task<OperateResult> ConsumerAsync(string guid, string topic)
        {
            //打开
            (IMq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            //消费
            return await open.operate.ConsumeAsync(topic);
        }


    }
}
