
using Snet.Iot.Daq.Core.mvvm;
using System.ComponentModel;

namespace Snet.Iot.Daq.Core.data
{
    /// <summary>
    /// 地址源模型<br/>
    /// 导入源地址时需要用到该模型，包含数据传输主题和数据传输精简值
    /// </summary>
    public class AddressSourceModel : BindNotify
    {
        /// <summary>
        /// 数据传输主题
        /// </summary>
        [Description("数据传输主题")]
        public string Topic
        {
            get => _topic;
            set => SetProperty(ref _topic, value);
        }
        private string _topic = "Snet";

        /// <summary>
        /// 数据传输精简值
        /// </summary>
        [Description("数据传输精简值")]
        public bool SimplifyValue
        {
            get => GetProperty(() => SimplifyValue);
            set => SetProperty(() => SimplifyValue, value);
        }
    }
}
