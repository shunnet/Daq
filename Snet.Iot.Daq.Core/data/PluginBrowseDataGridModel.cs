using Snet.Iot.Daq.Core.mvvm;

namespace Snet.Iot.Daq.Core.data
{
    /// <summary>
    /// 插件浏览模型
    /// </summary>
    public class PluginBrowseDataGridModel : BindNotify
    {
        /// <summary>
        /// 插件浏览模型构造函数
        /// </summary>
        public PluginBrowseDataGridModel()
        {
        }
        /// <summary>
        /// 插件浏览模型构造函数
        /// </summary>
        /// <param name="index">序号</param>
        /// <param name="icon">图标</param>
        /// <param name="packName">包名</param>
        /// <param name="version">版本</param>
        /// <param name="describe">描述</param>
        /// <param name="updateTime">更新时间</param>
        public PluginBrowseDataGridModel(int index, byte[] icon, string packName, string version, string describe, DateTime updateTime)
        {
            Index = index;
            Icon = icon;
            PackName = packName;
            Version = version;
            Describe = describe;
            UpdateTime = updateTime;
        }

        /// <summary>
        /// 是否选中
        /// </summary>
        public bool IsSelected
        {
            get => GetProperty(() => IsSelected);
            set => SetProperty(() => IsSelected, value);
        }

        /// <summary>
        /// 序号
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 图标
        /// </summary>
        public byte[] Icon { get; set; }

        /// <summary>
        /// 包名
        /// </summary>
        public string PackName { get; set; }

        /// <summary>
        /// 版本
        /// </summary>
        public string Version { get; set; }
        /// <summary>
        /// 描述
        /// </summary>
        public string Describe { get; set; }
        /// <summary>
        /// 更新时间
        /// </summary>
        public DateTime UpdateTime { get; set; }
    }
}
