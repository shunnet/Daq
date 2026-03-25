using MQTTnet.Server;
using Snet.Core.extend;
using Snet.Model.data;
using Snet.Model.@interface;
using System.Dynamic;
using System.Text;
using static Snet.Iot.Daq.mqtt.service.MqttServiceData;

namespace Snet.Iot.Daq.mqtt.service
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
            switch (arg.ApplicationMessage.PayloadFormatIndicator)
            {
                case MQTTnet.Protocol.MqttPayloadFormatIndicator.Unspecified:
                    break;

                case MQTTnet.Protocol.MqttPayloadFormatIndicator.CharacterData:
                    dynamic DynamicObj = new ExpandoObject();
                    DynamicObj.Messages = arg.ApplicationMessage;
                    DynamicObj.SenderID = arg.SenderId;
                    OnInfoEventHandler(this, new EventInfoResult(true, $"[ {Steps.客户端消息事件} ]( {arg.SenderId} ) 发布了主题 ( {arg.ApplicationMessage.Topic} ) 内容 ( {Encoding.UTF8.GetString(arg.ApplicationMessage.Payload)} )"));
                    break;
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
        public OperateResult On()
        {
            //开始记录运行时间
            BegOperate();
            try
            {
                //实例化对象
                MqttServerOptionsBuilder mqttServerOptionsBuilder = new MqttServerOptionsBuilder();
                //设置默认的本地IP
                mqttServerOptionsBuilder.WithDefaultEndpoint();
                //设置端口
                mqttServerOptionsBuilder.WithDefaultEndpointPort(Convert.ToInt32(basics.Port));
                //最大连接数
                mqttServerOptionsBuilder.WithConnectionBacklog(basics.MaxNumber);
                //创建MQTT服务
                mqttServer = new MqttServerFactory().CreateMqttServer(mqttServerOptionsBuilder.Build());
                //身份验证（异步）
                mqttServer.ValidatingConnectionAsync += MqttServer_ValidatingConnectionAsync;
                //消息接收（异步）
                mqttServer.ApplicationMessageNotConsumedAsync += MqttServer_ApplicationMessageNotConsumedAsync;
                //客户端连接（异步）
                mqttServer.ClientConnectedAsync += MqttServer_ClientConnectedAsync;
                //客户端断开（异步）
                mqttServer.ClientDisconnectedAsync += MqttServer_ClientDisconnectedAsync;
                //客户端订阅事件（异步）
                mqttServer.ClientSubscribedTopicAsync += MqttServer_ClientSubscribedTopicAsync;
                //客户端取消订阅（异步）
                mqttServer.ClientUnsubscribedTopicAsync += MqttServer_ClientUnsubscribedTopicAsync;
                //启动后事件（异步）
                mqttServer.StartedAsync += MqttServer_StartedAsync;
                //停止后事件（异步）
                mqttServer.StoppedAsync += MqttServer_StoppedAsync;
                //启动服务（异步）等待执行完成
                mqttServer.StartAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                //设置状态
                states = States.On;
                return EndOperate(true);
            }
            catch (Exception ex)
            {
                Off(true);
                return EndOperate(false, ex.Message, exception: ex);
            }
        }
        /// <inheritdoc/>
        public OperateResult Off(bool hardClose = false)
        {
            //开始记录运行时间
            BegOperate();
            try
            {
                if (!hardClose)
                {
                    if (mqttServer == null)
                    {
                        return EndOperate(false, "未连接");
                    }
                }
                //关闭服务（异步）等待执行完成
                mqttServer?.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                //释放
                mqttServer?.Dispose();
                //设置状态
                states = States.Off;
                return EndOperate(true);
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }
        /// <inheritdoc/>
        public OperateResult GetStatus()
        {
            return EndOperate(states == States.On, states == States.On ? "已启动" : "未启动", methodName: BegOperate());
        }
        /// <inheritdoc/>
        public async Task<OperateResult> OffAsync(bool hardClose = false, CancellationToken token = default) => await Task.Run(() => Off(hardClose), token);
        /// <inheritdoc/>
        public async Task<OperateResult> OnAsync(CancellationToken token = default) => await Task.Run(() => On(), token);
        /// <inheritdoc/>
        public async Task<OperateResult> GetStatusAsync(CancellationToken token = default) => await Task.Run(() => GetStatus(), token);
    }
}