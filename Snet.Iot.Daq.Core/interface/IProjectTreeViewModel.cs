using Snet.Iot.Daq.Core.converter;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@enum;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Snet.Iot.Daq.Core.@interface
{
    [JsonConverter(typeof(ProjectTreeViewModelJsonConverter))]
    public interface IProjectTreeViewModel : ITreeViewModel<IProjectTreeViewModel>
    {
        /// <summary>
        /// 项目节点类型
        /// </summary>
        ProjectNodeType NodeType { get; set; }

        /// <summary>
        /// 随软启动状态<br/>
        /// true:软件打开则启动采集
        /// </summary>
        bool IsSoftStart { get; set; }

        /// <summary>
        /// 设备详情 - 采集设备
        /// </summary>
        PluginConfigModel DaqDetails { get; set; }

        /// <summary>
        /// 子设备详情
        /// </summary>
        public ObservableCollection<IProjectDetailsTreeViewModel> Details { get; set; }

        /// <summary>
        /// 更新名称
        /// </summary>
        void UpdateName();
    }
}
