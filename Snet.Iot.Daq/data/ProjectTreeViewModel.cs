using MaterialDesignThemes.Wpf;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@enum;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.@interface;
using Snet.Iot.Daq.handler;
using Snet.Windows.Controls.property.core.DataAnnotations;
using System.Collections.ObjectModel;

namespace Snet.Iot.Daq.data
{
    /// <summary>
    /// 项目树控件视图模型，表示项目树中的描述节点或设备节点，支持展开/折叠、选中、父子关系、随软启动等功能。
    /// </summary>
    public class ProjectTreeViewModel : ProjectTreeViewModelCore
    {
        /// <summary>
        /// 无参构造函数
        /// </summary>
        public ProjectTreeViewModel() { }

        /// <summary>
        /// 创建描述类型的树节点
        /// </summary>
        /// <param name="name">节点名称</param>
        /// <param name="children">子节点集合，默认为 null 则使用空集合</param>
        /// <param name="isExpanded">是否展开</param>
        public ProjectTreeViewModel(string name, ObservableCollection<IProjectTreeViewModel>? children = null, bool isExpanded = false) : base(name, children, isExpanded)
        {
            Name = name;
            NodeType = ProjectNodeType.Describe;
            Icon = PackIconKind.Server;
            Children = children ?? Children;
            IsExpanded = isExpanded;
            UpdateSpecialData();
        }

        /// <summary>
        /// 创建设备类型的树节点
        /// </summary>
        /// <param name="deviceDetails">采集设备插件配置</param>
        public ProjectTreeViewModel(PluginConfigModel deviceDetails) : base(deviceDetails)
        {
            Name = deviceDetails.GetObjSn();
            NodeType = ProjectNodeType.Device;
            Icon = PackIconKind.ServerNetworkOutline;
            DaqDetails = deviceDetails;
            UpdateSpecialData();
        }

        /// <summary>
        /// 设置<br/>
        /// 唯一选中<br/>
        /// 父级关系<br/>
        /// 展开所有父级<br/>
        /// </summary>
        /// <param name="models">外部的集合</param>
        public override async Task SetAsync(ObservableCollection<IProjectTreeViewModel> models)
        {
            //设置父级关系
            models.InitChildrenParent();
            //让程序都响应完成后
            await Task.Delay(50);
            await Task.Run((Func<Task?>)(async () =>
            {
                //设置选中
                models.EnsureSingleSelection(this);
                //展开父级
                this.ExpandParents();
                //保存配置
                await ProjectHandler.SaveConfigAsync(models, GlobalConfigModel.UI_ProjectConfigPath);
            }));
        }

        /// <summary>
        /// 设备详情 - 采集设备
        /// </summary>
        [Browsable(false)]
        [Description("设备详情")]
        public override PluginConfigModel DaqDetails
        {
            get => _daqDetails;
            set
            {
                if (_daqDetails != null)
                {
                    _daqDetails.OnInfoEventAsync -= DaqDetails_OnInfoEventAsync;
                }
                _daqDetails = value;
                if (_daqDetails != null)
                {
                    _daqDetails.OnInfoEventAsync += DaqDetails_OnInfoEventAsync;
                }
            }
        }

        /// <summary>
        /// 事件触发
        /// </summary>
        [Browsable(false)]
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        private PluginConfigModel _daqDetails;
        private async Task DaqDetails_OnInfoEventAsync(object? sender, Model.data.EventInfoResult e)
        {
            UpdateName();
            UpdateSpecialData();
            await SetAsync(GlobalConfigModel.ProjectDict);
        }




    }
}
