using Snet.Iot.Daq.Core.converter;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@enum;
using System.Text.Json.Serialization;

namespace Snet.Iot.Daq.Core.@interface
{
    [JsonConverter(typeof(ProjectDetailsTreeViewModelJsonConverter))]
    public interface IProjectDetailsTreeViewModel : ITreeViewModel<IProjectDetailsTreeViewModel>
    {
        /// <summary>
        /// 项目节点类型
        /// </summary>
        ProjectDetailsNodeType NodeType { get; set; }

        /// <summary>
        /// 唯一标识
        /// </summary>
        string Guid { get; set; }

        /// <summary>
        /// 地址详情        
        /// /// </summary>
        IAddressModel AddressDetails { get; set; }

        /// <summary>
        /// 设备详情 - 采集设备
        /// </summary>
        PluginConfigModel MqDetails { get; set; }

        /// <summary>
        /// 更新名称
        /// </summary>
        void UpdateMqName();

        /// <summary>
        /// 更新名称
        /// </summary>
        void UpdateAddressName();
    }
}
