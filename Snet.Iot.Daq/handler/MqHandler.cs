using Snet.Core.extend;
using Snet.Iot.Daq.data;
using Snet.Model.data;
using Snet.Model.@interface;
using Snet.Utility;
using System.Collections.Concurrent;
using System.Text;

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
            string content = address.SimplifyValue ? value.GetSimplify().ToJson() : value.ToJson();

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

            //生产结果
            StringBuilder builder = new StringBuilder();

            //遍历
            foreach (var item in values)
            {
                //传输的内容
                string content = item.Key.SimplifyValue ? item.Value.GetSimplify().ToJson() : item.Value.ToJson();

                //生产数据
                OperateResult result = await open.operate.ProduceAsync(item.Key.Topic, content, item.Key.EncodingType.GetEncoding());

                //组织结果
                if (!result.GetDetails(out string? msg))
                {
                    builder.AppendLine($"{item.Key.Address} {msg}");
                }
            }
            //返回结果
            if (builder.Length > 0)
            {
                return OperateResult.CreateFailureResult(builder.ToString());
            }
            return OperateResult.CreateSuccessResult("Produce success");
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
