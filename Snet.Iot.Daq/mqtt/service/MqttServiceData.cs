using Snet.Model.attribute;
using Snet.Model.data;
using Snet.Utility;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Snet.Iot.Daq.mqtt.service
{
    /// <summary>
    /// MQTT 服务端数据配置类，封装 MQTT Broker 的连接参数（端口、认证信息、最大连接数等）和步骤枚举。
    /// </summary>
    public class MqttServiceData
    {
        /// <summary>
        /// 基础数据
        /// </summary>
        public class Basics
        {
            /// <summary>
            /// 唯一标识符
            /// </summary>
            [Category("基础数据")]
            [Description("唯一标识符")]
            public string SN { get; set; } = Guid.NewGuid().ToUpperNString();

            /// <summary>
            /// 端口
            /// </summary>
            [Description("端口")]
            [Display(true, true, true, ParamModel.dataCate.unmber)]
            public int Port { get; set; } = 6688;

            /// <summary>
            /// 用户
            /// </summary>
            [Description("用户名")]
            [Display(true, true, false, ParamModel.dataCate.text)]
            public string? UserName { get; set; } = "sample";

            /// <summary>
            /// 密码
            /// </summary>
            [Description("密码")]
            [Display(true, true, false, ParamModel.dataCate.text)]
            public string? Password { get; set; } = "sample";

            /// <summary>
            /// 最大连接数
            /// </summary>
            [Description("最大连接数")]
            [Display(true, true, true, ParamModel.dataCate.text)]
            public int MaxNumber { get; set; } = 10000;
        }

        /// <summary>
        /// 哪一个步骤
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum Steps
        {
            服务停止事件,
            服务启动事件,
            客户端取消订阅事件,
            客户端订阅事件,
            客户端消息事件,
            客户端断开事件,
            客户端连接事件,
            客户端身份验证事件
        }

        /// <summary>
        /// 状态
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum States
        {
            /// <summary>
            /// 已启动
            /// </summary>
            On,

            /// <summary>
            /// 已停止
            /// </summary>
            Off,

            /// <summary>
            /// 啥也没干
            /// </summary>
            Null
        }
    }
}