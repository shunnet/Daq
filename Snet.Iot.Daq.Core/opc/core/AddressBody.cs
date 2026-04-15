using Opc.Ua;

namespace Snet.Iot.Daq.Core.opc.core
{
    /// <summary>
    /// 地址结构体<br/>
    /// 用于创建地址以及修改地址值
    /// </summary>
    public class AddressBody
    {
        /// <summary>
        /// 地址名称
        /// </summary>
        public string AddressName { get; set; }

        /// <summary>
        /// 动态的
        /// </summary>
        public bool Dynamic { get; set; } = false;

        /// <summary>
        /// 描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 访问级别；<br/>
        /// 1：读取；<br/>
        /// 2：写入；<br/>
        /// 3：读取或写入；
        /// </summary>
        public byte AccessLevel { get; set; } = 3;

        /// <summary>
        /// 默认值；<br/>
        /// IsDynamic == true；无须赋值
        /// </summary>
        public object DefaultValue { get; set; }

        /// <summary>
        /// 数据类型
        /// </summary>
        public BuiltInType DataType { get; set; }
    }
}
