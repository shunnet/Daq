using Snet.Iot.Daq.Core.@enum;
using Snet.Iot.Daq.Core.@interface;
using Snet.Iot.Daq.Core.mvvm;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Snet.Iot.Daq.Core.data
{
    /// <summary>
    /// 项目树控件视图模型，表示项目树中的描述节点或设备节点，支持展开/折叠、选中、父子关系、随软启动等功能。
    /// </summary>
    public class ProjectTreeViewModelCore : BindNotify, IProjectTreeViewModel
    {
        /// <summary>
        /// 无参构造函数
        /// </summary>
        public ProjectTreeViewModelCore() { }

        /// <summary>
        /// 创建描述类型的树节点
        /// </summary>
        /// <param name="name">节点名称</param>
        /// <param name="children">子节点集合，默认为 null 则使用空集合</param>
        /// <param name="isExpanded">是否展开</param>
        public ProjectTreeViewModelCore(string name, ObservableCollection<IProjectTreeViewModel>? children = null, bool isExpanded = false)
        {
            Name = name;
            NodeType = ProjectNodeType.Describe;
            Children = children ?? Children;
            IsExpanded = isExpanded;
            UpdateSpecialData();
        }

        /// <summary>
        /// 创建设备类型的树节点
        /// </summary>
        /// <param name="deviceDetails">采集设备插件配置</param>
        public ProjectTreeViewModelCore(PluginConfigModel deviceDetails)
        {
            Name = deviceDetails.GetObjSn();
            NodeType = ProjectNodeType.Device;
            DaqDetails = deviceDetails;
            UpdateSpecialData();
        }

        /// <summary>
        /// 节点图片
        /// </summary>
        [Browsable(false)]
        [Description("节点图片")]
        public object Icon
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
        public ObservableCollection<IProjectTreeViewModel> Children
        {
            get => children;
            set => SetProperty(ref children, value);
        }
        private ObservableCollection<IProjectTreeViewModel> children = new ObservableCollection<IProjectTreeViewModel>();


        /// <summary>
        /// 父级节点
        /// </summary>
        [Browsable(false)]
        [Description("父级节点")]
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public IProjectTreeViewModel Parent
        {
            get => GetProperty(() => Parent);
            set => SetProperty(() => Parent, value);
        }

        /// <summary>
        /// 子设备详情
        /// </summary>
        [Browsable(false)]
        [Description("子设备详情")]
        public ObservableCollection<IProjectDetailsTreeViewModel> Details
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
        /// 设备详情 - 采集设备
        /// </summary>
        [Browsable(false)]
        [Description("设备详情")]
        public virtual PluginConfigModel DaqDetails { get; set; }


        /// <summary>
        /// 设置<br/>
        /// 唯一选中<br/>
        /// 父级关系<br/>
        /// 展开所有父级<br/>
        /// </summary>
        /// <param name="models">外部的集合</param>
        public virtual async Task SetAsync(ObservableCollection<IProjectTreeViewModel> models)
        {
            //外部重写
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
