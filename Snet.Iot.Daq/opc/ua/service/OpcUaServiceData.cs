using Snet.Model.attribute;
using Snet.Model.data;
using Snet.Utility;
using System.ComponentModel;
using static Snet.Iot.Daq.opc.core.Data;

namespace Snet.Iot.Daq.opc.ua.service
{
    /// <summary>
    /// OPCUA服务端 数据
    /// </summary>
    public class OpcUaServiceData
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
            /// 标识
            /// </summary>
            [Description("标识")]
            [Display(true, true, false, ParamModel.dataCate.text)]
            public string Tag { get; set; } = "Opc.Ua.Service";

            /// <summary>
            /// Ip地址
            /// </summary>
            [Description("Ip地址")]
            [Verify(@"^((25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(25[0-5]|2[0-4]\d|[01]?\d\d?)$", "输入有误")]
            [Display(true, true, true, ParamModel.dataCate.text)]
            public string? IpAddress { get; set; } = "127.0.0.1";

            /// <summary>
            /// 端口
            /// </summary>
            [Description("端口")]
            [Display(true, true, false, ParamModel.dataCate.unmber)]
            public int Port { get; set; } = 6688;

            /// <summary>
            /// 认证类型
            /// </summary>
            [Description("认证类型")]
            [Display(true, true, false, ParamModel.dataCate.select)]
            public AuType AType { get; set; } = AuType.UserName;

            /// <summary>
            /// 用户
            /// </summary>
            [Description("用户名")]
            [Display(true, true, false, ParamModel.dataCate.text)]
            public string? UserName { get; set; } = "samples";

            /// <summary>
            /// 密码
            /// </summary>
            [Description("密码")]
            [Display(true, true, false, ParamModel.dataCate.text)]
            public string? Password { get; set; } = "samples";

            /// <summary>
            /// 地址空间名称
            /// </summary>
            [Description("地址空间名称")]
            [Display(true, true, false, ParamModel.dataCate.text)]
            public string? AddressSpaceName { get; set; } = "Snet";

            /// <summary>
            /// 自动创建地址
            /// </summary>
            [Description("自动创建地址")]
            [Display(true, true, false, ParamModel.dataCate.radio)]
            public bool AutoCreateAddress { get; set; } = false;
        }


    }
}