using MaterialDesignThemes.Wpf;
using Snet.Iot.Daq.@enum;
using Snet.Iot.Daq.handler;
using Snet.Iot.Daq.@interface;
using Snet.Windows.Controls.property.core.DataAnnotations;
using Snet.Windows.Core.mvvm;
using System.Collections.ObjectModel;

namespace Snet.Iot.Daq.data
{
    /// <summary>
    /// 项目的树控件
    /// </summary>
    public class ProjectTreeViewModel : BindNotify, ITreeViewModel<ProjectTreeViewModel>
    {
        public ProjectTreeViewModel() { }

        public ProjectTreeViewModel(string name, ObservableCollection<ProjectTreeViewModel>? children = null, bool isExpanded = false)
        {
            Name = name;
            NodeType = ProjectNodeType.Describe;
            Icon = PackIconKind.Server;
            Children = children ?? Children;
            IsExpanded = isExpanded;
            UpdateSpecialData();
        }

        public ProjectTreeViewModel(PluginConfigModel deviceDetails)
        {
            Name = deviceDetails.GetObjSn();
            NodeType = ProjectNodeType.Device;
            Icon = PackIconKind.ServerNetworkOutline;
            DaqDetails = deviceDetails;
            UpdateSpecialData();
        }

        /// <summary>
        /// 节点图片
        /// </summary>
        [Browsable(false)]
        [Description("节点图片")]
        public PackIconKind Icon
        {
            get => GetProperty(() => Icon);
            set => SetProperty(() => Icon, value);
        }

        /// <summary>
        /// 节点名称
        /// </summary>
        [Description("节点名称")]
        public string Name
        {
            get => GetProperty(() => Name);
            set => SetProperty(() => Name, value);
        }

        /// <summary>
        /// 项目节点类型
        /// </summary>
        [Browsable(false)]
        [Description("项目节点类型")]
        public ProjectNodeType NodeType
        {
            get => GetProperty(() => NodeType);
            set => SetProperty(() => NodeType, value);
        }

        /// <summary>
        /// 特殊数据
        /// </summary>
        [Browsable(false)]
        [Description("特殊数据")]
        public string SpecialData
        {
            get => GetProperty(() => SpecialData);
            set => SetProperty(() => SpecialData, value);
        }

        /// <summary>
        /// 是否展开
        /// </summary>
        [Browsable(false)]
        [Description("是否展开")]
        public bool IsExpanded
        {
            get => GetProperty(() => IsExpanded);
            set => SetProperty(() => IsExpanded, value);
        }

        /// <summary>
        /// 是否选中
        /// </summary>
        [Browsable(false)]
        [Description("是否选中")]
        public bool IsSelected
        {
            get => GetProperty(() => IsSelected);
            set => SetProperty(() => IsSelected, value);
        }

        /// <summary>
        /// 下一级
        /// </summary>
        [Browsable(false)]
        [Description("下一级")]
        public ObservableCollection<ProjectTreeViewModel> Children
        {
            get => children;
            set => SetProperty(ref children, value);
        }
        private ObservableCollection<ProjectTreeViewModel> children = new ObservableCollection<ProjectTreeViewModel>();


        /// <summary>
        /// 父级节点
        /// </summary>
        [Browsable(false)]
        [Description("父级节点")]
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ProjectTreeViewModel Parent
        {
            get => GetProperty(() => Parent);
            set => SetProperty(() => Parent, value);
        }

        /// <summary>
        /// 子设备详情
        /// </summary>
        [Browsable(false)]
        [Description("子设备详情")]
        public ObservableCollection<ProjectDetailsTreeViewModel> Details
        {
            get => GetProperty(() => Details);
            set => SetProperty(() => Details, value);
        }

        /// <summary>
        /// 随软启动状态<br/>
        /// true:软件打开则启动采集
        /// </summary>
        [Browsable(false)]
        [Description("随软启动状态")]
        public bool IsSoftStart
        {
            get => GetProperty(() => IsSoftStart);
            set => SetProperty(() => IsSoftStart, value);
        }

        /// <summary>
        /// 设置<br/>
        /// 唯一选中<br/>
        /// 父级关系<br/>
        /// 展开所有父级<br/>
        /// </summary>
        /// <param name="models">外部的集合</param>
        public async Task SetAsync(ObservableCollection<ProjectTreeViewModel> models)
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
        /// 更新特殊数据
        /// </summary>
        public void UpdateSpecialData()
        {
            switch (NodeType)
            {
                case ProjectNodeType.Device:
                    SpecialData = $"[ {DaqDetails.Name} ]";
                    break;
                case ProjectNodeType.Describe:
                    if (Children.Count > 0)
                    {
                        SpecialData = $"( {Children.Count} )";
                    }
                    else
                    {
                        SpecialData = string.Empty;
                    }
                    break;
            }
        }

        /// <summary>
        /// 重写
        /// </summary>
        /// <returns></returns>
        public override string ToString()
        {
            return Name;
        }

        /// <summary>
        /// 设备详情 - 采集设备
        /// </summary>
        [Browsable(false)]
        [Description("设备详情")]
        public PluginConfigModel DaqDetails
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

        /// <summary>
        /// 更新名称
        /// </summary>
        public void UpdateName()
        {
            if (NodeType == ProjectNodeType.Device)
            {
                Name = DaqDetails.GetObjSn();
            }
        }


    }
}
