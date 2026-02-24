using Snet.Iot.Daq.effects;
using System.Windows;
using System.Windows.Controls;

namespace Snet.Iot.Daq.view
{
    /// <summary>
    /// Home.xaml 的交互逻辑
    /// </summary>
    public partial class Home : UserControl
    {
        public Home()
        {
            InitializeComponent();
            Loaded -= HandleLoaded;
            Loaded += HandleLoaded;
            //Unloaded += HandleUnloaded;
        }
        private SnowflakeEffect? _snowflake;
        private void HandleLoaded(object sender, RoutedEventArgs e)
        {
            if (_snowflake != null)
                return;

            _snowflake ??= new(MainCanvas);
            _snowflake.Start();
        }

        private void HandleUnloaded(object sender, RoutedEventArgs e)
        {
            _snowflake?.Stop();
            _snowflake = null;
            Loaded -= HandleLoaded;
            Unloaded -= HandleUnloaded;
        }
    }
}
