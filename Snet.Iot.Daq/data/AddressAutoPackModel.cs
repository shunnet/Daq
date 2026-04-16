using Snet.Iot.Daq.Core.mvvm;
using Snet.Model.@enum;
using System.ComponentModel;

namespace Snet.Iot.Daq.data
{
    /// <summary>
    /// 地址自动组包模型
    /// </summary>
    public class AddressAutoPackModel : BindNotify
    {
        /// <summary>
        /// 单次批量读取的最大字节数
        /// </summary>
        [Description("单次批量读取的最大字节数")]
        public int MaxByteLength
        {
            get => maxByteLength;
            set => SetProperty(ref maxByteLength, value);
        }
        private int maxByteLength = 200;

        /// <summary>
        /// 数据字节序格式
        /// </summary>
        [Description("数据字节序格式")]
        public DataFormat Format
        {
            get => format;
            set => SetProperty(ref format, value);
        }
        private DataFormat format = DataFormat.ABCD;
    }
}
