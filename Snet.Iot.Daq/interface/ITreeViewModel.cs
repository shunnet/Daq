using MaterialDesignThemes.Wpf;
using System.Collections.ObjectModel;

namespace Snet.Iot.Daq.@interface
{
    /// <summary>
    /// 树节点模型接口
    /// </summary>
    public interface ITreeViewModel<T>
    {
        /// <summary>
        /// 节点图片
        /// </summary>
        PackIconKind Icon { get; set; }

        /// <summary>
        /// 节点名称
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// 特殊数据
        /// </summary>
        string SpecialData { get; set; }

        /// <summary>
        /// 是否展开
        /// </summary>
        bool IsExpanded { get; set; }

        /// <summary>
        /// 是否选中
        /// </summary>
        bool IsSelected { get; set; }

        /// <summary>
        /// 下一级
        /// </summary>
        ObservableCollection<T> Children { get; set; }

        /// <summary>
        /// 父级节点
        /// </summary>
        T? Parent { get; set; }

        /// <summary>
        /// 设置<br/>
        /// 唯一选中<br/>
        /// 父级关系<br/>
        /// 展开所有父级<br/>
        /// </summary>
        /// <param name="models">外部的集合</param>
        Task SetAsync(ObservableCollection<T> models);

        /// <summary>
        /// 更新特殊数据
        /// </summary>
        void UpdateSpecialData();
    }
}
