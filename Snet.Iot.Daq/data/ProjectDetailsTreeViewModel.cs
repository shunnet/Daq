using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.@interface;
using Snet.Windows.Controls.property.core.DataAnnotations;
using System.Collections.ObjectModel;
using Wpf.Ui.Controls;

namespace Snet.Iot.Daq.data
{
    /// <summary>
    /// 项目详情树控件视图模型，表示项目中地址和传输设备的树形节点，支持展开/折叠、选中、父子关系等功能。
    /// </summary>
    public class ProjectDetailsTreeViewModel : ProjectDetailsTreeViewModelCore
    {
        /// <summary>
        /// 无参构造函数
        /// </summary>
        public ProjectDetailsTreeViewModel()
        { }

        /// <summary>
        /// 节点图片
        /// </summary>
        [Browsable(false)]
        [Description("节点图片")]
        public SymbolRegular Icon
        {
            get => GetProperty(() => Icon);
            set => SetProperty(() => Icon, value);
        }

        /// <summary>
        /// 创建地址类型的详情树节点
        /// </summary>
        /// <param name="address">地址模型</param>
        /// <param name="children">子节点集合，默认为 null 则使用空集合</param>
        /// <param name="isExpanded">是否展开</param>
        public ProjectDetailsTreeViewModel(IAddressModel address, ObservableCollection<IProjectDetailsTreeViewModel>? children = null, bool isExpanded = false) : base(address, children, isExpanded)
        {
            Icon = SymbolRegular.Location12;
        }

        /// <summary>
        /// 添加传输设备
        /// </summary>
        /// <param name="deviceDetails">传输插件</param>
        public ProjectDetailsTreeViewModel(PluginConfigModel deviceDetails) : base(deviceDetails)
        {
            Icon = SymbolRegular.Pulse32;
        }

        /// <inheritdoc/>
        public override async Task SetAsync(ObservableCollection<IProjectDetailsTreeViewModel> models)
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
    }
}