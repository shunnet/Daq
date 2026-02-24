using Snet.Windows.Controls.handler;
using Snet.Windows.Core;
namespace Snet.Iot.Daq
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : WindowBase
    {
        public MainWindow()
        {
            InitializeComponent();
            //NavigationViewControls.SelectNavigationViewDefaultItem(this, typeof(Snet.Iot.Daq.view.PluginSettings), App.LanguageOperate, "mainGrid");
            //NavigationViewControls.SelectNavigationViewDefaultItem(this, typeof(Snet.Iot.Daq.view.AddressSettings), App.LanguageOperate, "mainGrid");
            //NavigationViewControls.SelectNavigationViewDefaultItem(this, typeof(Snet.Iot.Daq.view.ProjectSettings), App.LanguageOperate, "mainGrid");
            NavigationViewControls.SelectNavigationViewDefaultItem(this, typeof(Snet.Iot.Daq.view.Console), App.LanguageOperate, "mainGrid");
        }
    }
}