using Snet.Model.@enum;
using Snet.Windows.Core.mvvm;
using SQLite;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Snet.Iot.Daq.data
{
    /// <summary>
    /// 采集插件操作模型
    /// </summary>
    public class DaqPluginOperateModel
    {
        /// <summary>
        /// 读取模型
        /// </summary>
        public class ReadModel : BindNotify
        {
            /// <summary>
            /// 地址
            /// </summary>
            [Description("地址")]
            [Indexed(Unique = true)]
            public string Address
            {
                get => GetProperty(() => Address);
                set => SetProperty(() => Address, value);
            }

            /// <summary>
            /// 数据类型
            /// </summary>
            [Description("数据类型")]
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public DataType Type
            {
                get => GetProperty(() => Type);
                set => SetProperty(() => Type, value);
            }

            /// <summary>
            /// 长度
            /// </summary>
            [Description("长度")]
            public ushort Length
            {
                get => _length;
                set => SetProperty(ref _length, value);
            }
            private ushort _length = 1;

            /// <summary>
            /// 编码类型
            /// </summary>
            [Description("编码类型")]
            [JsonConverter(typeof(JsonStringEnumConverter))]
            public EncodingType EncodingType
            {
                get => _encodingType;
                set => SetProperty(ref _encodingType, value);
            }
            private EncodingType _encodingType = EncodingType.UTF8;
        }

        /// <summary>
        /// 写入模型
        /// </summary>
        public class WriteModel : Snet.Model.data.WriteModel
        {
            /// <summary>
            /// 地址
            /// </summary>
            [Description("地址")]
            public string Address { get; set; }
        }

    }
}
