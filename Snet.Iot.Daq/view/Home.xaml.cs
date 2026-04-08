using Snet.Iot.Daq.effects;
using System.Windows;
using System.Windows.Controls;

namespace Snet.Iot.Daq.view
{
    /// <summary>
    /// Home.xaml 的交互逻辑，主页视图，包含雪花粒子动画效果
    /// </summary>
    public partial class Home : UserControl
    {
        /// <summary>
        /// 构造函数，初始化主页组件并注册加载事件
        /// </summary>
        public Home()
        {
            InitializeComponent();
            Loaded -= HandleLoaded;
            Loaded += HandleLoaded;
        }

        /// <summary>
        /// 雪花动画效果实例（懒加载，仅在首次加载时创建）
        /// </summary>
        private SnowflakeEffect? _snowflake;

        /// <summary>
        /// 控件加载完成事件处理：启动雪花粒子动画
        /// </summary>
        private void HandleLoaded(object sender, RoutedEventArgs e)
        {
            if (_snowflake != null)
                return;

            _snowflake ??= new(MainCanvas);
            _snowflake.Start();
        }

        /// <summary>
        /// 控件卸载事件处理：停止并释放雪花动画，取消事件订阅
        /// </summary>
        private void HandleUnloaded(object sender, RoutedEventArgs e)
        {
            _snowflake?.Stop();
            _snowflake = null;
            Loaded -= HandleLoaded;
            Unloaded -= HandleUnloaded;
        }
    }
}
