using MaterialDesignThemes.Wpf;
using Snet.Iot.Daq.@enum;
using Snet.Iot.Daq.handler;
using Snet.Iot.Daq.@interface;
using Snet.Windows.Core.mvvm;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Snet.Iot.Daq.data
{
    /// <summary>
    /// 项目详情树控件视图模型，表示项目中地址和传输设备的树形节点，支持展开/折叠、选中、父子关系等功能。
    /// </summary>
    public class ProjectDetailsTreeViewModel : BindNotify, ITreeViewModel<ProjectDetailsTreeViewModel>
    {
        /// <summary>
        /// 无参构造函数
        /// </summary>
        public ProjectDetailsTreeViewModel()
        { }

        /// <summary>
        /// 创建地址类型的详情树节点
        /// </summary>
        /// <param name="address">地址模型</param>
        /// <param name="children">子节点集合，默认为 null 则使用空集合</param>
        /// <param name="isExpanded">是否展开</param>
        public ProjectDetailsTreeViewModel(AddressModel address, ObservableCollection<ProjectDetailsTreeViewModel>? children = null, bool isExpanded = false)
        {
            Name = $"[ {address.AnotherName} ] {address.Address}";
            NodeType = ProjectDetailsNodeType.Address;
            Icon = PackIconKind.MapMarkerOutline;
            Children = children ?? Children;
            AddressDetails = address;
            IsExpanded = isExpanded;
            Guid = address.Guid;
            UpdateSpecialData();
        }

        /// <summary>
        /// 添加传输设备
        /// </summary>
        /// <param name="deviceDetails">传输插件</param>
        public ProjectDetailsTreeViewModel(PluginConfigModel deviceDetails)
        {
            Name = deviceDetails.GetObjSn();
            NodeType = ProjectDetailsNodeType.Mq;
            Icon = PackIconKind.Transfer;
            MqDetails = deviceDetails;
            Guid = deviceDetails.Guid;
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
        public ProjectDetailsNodeType NodeType
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
        public ObservableCollection<ProjectDetailsTreeViewModel> Children
        {
            get => children;
            set => SetProperty(ref children, value);
        }
        private ObservableCollection<ProjectDetailsTreeViewModel> children = new ObservableCollection<ProjectDetailsTreeViewModel>();

        /// <summary>
        /// 父级节点
        /// </summary>
        [Browsable(false)]
        [Description("父级节点")]
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public ProjectDetailsTreeViewModel Parent
        {
            get => GetProperty(() => Parent);
            set => SetProperty(() => Parent, value);
        }

        /// <summary>
        /// 唯一标识
        /// </summary>
        [Description("唯一标识")]
        [Browsable(false)]
        public string Guid { get; set; }

        /// <summary>
        /// 地址详情        
        /// /// </summary>
        [Browsable(false)]
        [Description("地址详情")]
        public AddressModel AddressDetails
        {
            get => _addressDetails;
            set
            {
                if (_addressDetails != null)
                {
                    _addressDetails.OnInfoEventAsync -= AddressDetails_OnInfoEventAsync;
                }
                _addressDetails = value;
                if (_addressDetails != null)
                {
                    _addressDetails.OnInfoEventAsync += AddressDetails_OnInfoEventAsync;
                }
            }
        }
        [Browsable(false)]
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        private AddressModel _addressDetails;
        private Task AddressDetails_OnInfoEventAsync(object? sender, Model.data.EventInfoResult e)
        {
            UpdateAddressName();
            return Task.CompletedTask;
        }


        /// <summary>
        /// 设备详情 - 采集设备
        /// </summary>
        [Browsable(false)]
        [Description("设备详情")]
        public PluginConfigModel MqDetails
        {
            get => _mqDetails;
            set
            {
                if (_mqDetails != null)
                {
                    _mqDetails.OnInfoEventAsync -= MqDetails_OnInfoEventAsync;
                }
                _mqDetails = value;
                if (_mqDetails != null)
                {
                    _mqDetails.OnInfoEventAsync += MqDetails_OnInfoEventAsync;
                }
            }
        }
        [Browsable(false)]
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        private PluginConfigModel _mqDetails;
        private async Task MqDetails_OnInfoEventAsync(object? sender, Model.data.EventInfoResult e)
        {
            UpdateMqName();
        }

        /// <summary>
        /// 设置<br/>
        /// 唯一选中<br/>
        /// 父级关系<br/>
        /// 展开所有父级<br/>
        /// </summary>
        /// <param name="models">外部的集合</param>
        public async Task SetAsync(ObservableCollection<ProjectDetailsTreeViewModel> models)
        {
            //设置父级关系
            models.InitChildrenParent();
            //让程序都响应完成后
            await Task.Delay(50);
            await Task.Run(async () =>
            {
                //设置选中
                models.EnsureSingleSelection(this);
                //展开父级
                this.ExpandParents();
            });
        }

        /// <summary>
        /// 更新特殊数据
        /// </summary>
        public void UpdateSpecialData()
        {
            switch (NodeType)
            {
                case ProjectDetailsNodeType.Mq:
                    SpecialData = $"[ {MqDetails.Name} ]";
                    break;
                case ProjectDetailsNodeType.Address:
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
        public void UpdateMqName()
        {
            if (NodeType == ProjectDetailsNodeType.Mq)
            {
                Name = MqDetails.GetObjSn();
                UpdateSpecialData();
            }
        }

        /// <summary>
        /// 更新名称
        /// </summary>
        public void UpdateAddressName()
        {
            if (NodeType != ProjectDetailsNodeType.Mq)
            {
                Name = $"[ {AddressDetails.AnotherName} ] {AddressDetails.Address}";
                UpdateSpecialData();
            }
        }
    }
}