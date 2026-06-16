using MQTTnet.Server;
using Snet.Core.extend;
using Snet.Model.data;
using Snet.Model.@interface;
using System.Text;
using static Snet.Iot.Daq.Core.mqtt.service.MqttServiceData;

namespace Snet.Iot.Daq.Core.mqtt.service
{
    /// <summary>
    /// MQTT 服务端操作类，基于 MQTTnet 实现 MQTT Broker 服务。
    /// <para>支持客户端连接/断开监听、主题订阅/取消订阅、消息接收等功能。</para>
    /// </summary>
    public class MqttServiceOperate : CoreUnify<MqttServiceOperate, Basics>, IOn, IOff, IGetStatus, IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// 有参构造函数
        /// </summary>
        /// <param name="basics">基础数据</param>
        public MqttServiceOperate(Basics basics) : base(basics) { }

        #region 私有属性

        /// <summary>
        /// MQTT 服务
        /// </summary>
        private MqttServer? mqttServer;

        /// <summary>
        /// 状态
        /// </summary>
        private States states = States.Null;

        #endregion 私有属性

        #region 私有函数

        /// <summary>
        /// 服务停止后
        /// </summary>
        private Task MqttServer_StoppedAsync(EventArgs arg)
        {
            OnInfoEventHandler(this, new EventInfoResult(true, $"[ {Steps.服务停止事件} ]服务已停止"));
            return Task.CompletedTask;
        }

        /// <summary>
        /// 服务启动后
        /// </summary>
        private Task MqttServer_StartedAsync(EventArgs arg)
        {
            OnInfoEventHandler(this, new EventInfoResult(true, $"[ {Steps.服务启动事件} ]服务已启动"));
            return Task.CompletedTask;
        }

        /// <summary>
        /// 客户端取消订阅
        /// </summary>
        private Task MqttServer_ClientUnsubscribedTopicAsync(ClientUnsubscribedTopicEventArgs arg)
        {
            OnInfoEventHandler(this, new EventInfoResult(true, $"[ {Steps.客户端取消订阅事件} ]( {arg.ClientId} ) 取消了 ( {arg.TopicFilter} ) 的订阅"));
            return Task.CompletedTask;
        }

        /// <summary>
        /// 客户端订阅
        /// </summary>
        private Task MqttServer_ClientSubscribedTopicAsync(ClientSubscribedTopicEventArgs arg)
        {
            OnInfoEventHandler(this, new EventInfoResult(true, $"[ {Steps.客户端订阅事件} ]( {arg.ClientId} ) 订阅了主题 ( {arg.TopicFilter.Topic} )"));
            return Task.CompletedTask;
        }

        /// <summary>
        /// 客户端消息接收
        /// </summary>
        private Task MqttServer_ApplicationMessageNotConsumedAsync(ApplicationMessageNotConsumedEventArgs arg)
        {
            if (arg.ApplicationMessage.PayloadFormatIndicator == MQTTnet.Protocol.MqttPayloadFormatIndicator.CharacterData)
            {
                OnInfoEventHandler(this, new EventInfoResult(true, $"[ {Steps.客户端消息事件} ]( {arg.SenderId} ) 发布了主题 ( {arg.ApplicationMessage.Topic} ) 内容 ( {Encoding.UTF8.GetString(arg.ApplicationMessage.Payload)} )"));
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 客户端断开
        /// </summary>
        private Task MqttServer_ClientDisconnectedAsync(ClientDisconnectedEventArgs arg)
        {
            OnInfoEventHandler(this, new EventInfoResult(true, $"[ {Steps.客户端断开事件} ]( {arg.ClientId} ) 已断开"));
            return Task.CompletedTask;
        }

        /// <summary>
        /// 客户端连接
        /// </summary>
        private Task MqttServer_ClientConnectedAsync(ClientConnectedEventArgs arg)
        {
            OnInfoEventHandler(this, new EventInfoResult(true, $"[ {Steps.客户端连接事件} ]( {arg.ClientId} ) 已连接"));
            return Task.CompletedTask;
        }

        /// <summary>
        /// 身份验证
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        /// <exception cref="NotImplementedException"></exception>
        private Task MqttServer_ValidatingConnectionAsync(ValidatingConnectionEventArgs arg)
        {
            //验证账号密码是否正确
            if (arg.UserName != basics.UserName || arg.Password != basics.Password)
            {
                arg.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.BadUserNameOrPassword;
                OnInfoEventHandler(this, new EventInfoResult(false, $"[ {Steps.客户端身份验证事件} ]( {arg.ClientId} ) 身份验证异常：{arg.ReasonCode}"));
            }
            else
            {
                arg.ReasonCode = MQTTnet.Protocol.MqttConnectReasonCode.Success;
                OnInfoEventHandler(this, new EventInfoResult(true, $"[ {Steps.客户端身份验证事件} ]( {arg.ClientId} ) 身份验证成功"));
            }
            return Task.CompletedTask;
        }

        #endregion 私有函数

        /// <inheritdoc/>
        public override void Dispose()
        {
            Off(true);
            base.Dispose();
        }
        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            await OffAsync(true);
            await base.DisposeAsync();
        }

        /// <inheritdoc/>
        public OperateResult On() => Task.Run(() => OnAsync()).GetAwaiter().GetResult();

        /// <inheritdoc/>
        public OperateResult Off(bool hardClose = false) => Task.Run(() => OffAsync(hardClose)).GetAwaiter().GetResult();

        /// <inheritdoc/>
        public OperateResult GetStatus() => GetStatusAsync().GetAwaiter().GetResult();

        /// <inheritdoc/>
        public async Task<OperateResult> OnAsync(CancellationToken token = default)
        {
            await BegOperateAsync(token);
            try
            {
                MqttServerOptionsBuilder mqttServerOptionsBuilder = new MqttServerOptionsBuilder();
                mqttServerOptionsBuilder.WithDefaultEndpoint();
                mqttServerOptionsBuilder.WithDefaultEndpointPort(Convert.ToInt32(basics.Port));
                mqttServerOptionsBuilder.WithConnectionBacklog(basics.MaxNumber);
                mqttServer = new MqttServerFactory().CreateMqttServer(mqttServerOptionsBuilder.Build());
                mqttServer.ValidatingConnectionAsync += MqttServer_ValidatingConnectionAsync;
                mqttServer.ApplicationMessageNotConsumedAsync += MqttServer_ApplicationMessageNotConsumedAsync;
                mqttServer.ClientConnectedAsync += MqttServer_ClientConnectedAsync;
                mqttServer.ClientDisconnectedAsync += MqttServer_ClientDisconnectedAsync;
                mqttServer.ClientSubscribedTopicAsync += MqttServer_ClientSubscribedTopicAsync;
                mqttServer.ClientUnsubscribedTopicAsync += MqttServer_ClientUnsubscribedTopicAsync;
                mqttServer.StartedAsync += MqttServer_StartedAsync;
                mqttServer.StoppedAsync += MqttServer_StoppedAsync;
                await mqttServer.StartAsync();
                states = States.On;
                return await EndOperateAsync(true, token: token);
            }
            catch (Exception ex)
            {
                await OffAsync(true, token);
                return await EndOperateAsync(false, ex.Message, exception: ex, token: token);
            }
        }

        /// <inheritdoc/>
        public async Task<OperateResult> OffAsync(bool hardClose = false, CancellationToken token = default)
        {
            await BegOperateAsync(token);
            try
            {
                if (!hardClose && mqttServer == null)
                {
                    return await EndOperateAsync(false, "未连接", token: token);
                }
                if (mqttServer != null)
                {
                    await mqttServer.StopAsync();
                    mqttServer.Dispose();
                    mqttServer = null;
                }
                states = States.Off;
                return await EndOperateAsync(true, token: token);
            }
            catch (Exception ex)
            {
                return await EndOperateAsync(false, ex.Message, exception: ex, token: token);
            }
        }

        /// <inheritdoc/>
        public async Task<OperateResult> GetStatusAsync(CancellationToken token = default)
        {
            return await EndOperateAsync(states == States.On, states == States.On ? "已启动" : "未启动", methodName: await BegOperateAsync(token), token: token);
        }
    }
}