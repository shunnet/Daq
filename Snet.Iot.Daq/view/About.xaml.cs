using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Snet.Windows.Controls.message;
using System.Windows.Controls;

namespace Snet.Iot.Daq.view
{
    /// <summary>
    /// About.xaml 的交互逻辑，关于页面，内嵌 WebView2 显示官网内容
    /// </summary>
    public partial class About : UserControl
    {
        /// <summary>
        /// 构造函数，初始化关于页面组件并异步加载 WebView2 控件
        /// </summary>
        public About()
        {
            InitializeComponent();
            _ = InitWebViewAsync(webView, "cache", "https://shunnet.top").ConfigureAwait(false);
        }

        /// <summary>
        /// 异步初始化 WebView2 控件，指定缓存目录并导航到目标网址
        /// </summary>
        /// <param name="webView">WebView2 控件实例</param>
        /// <param name="cacheFolder">缓存目录路径</param>
        /// <param name="url">要加载的网页地址</param>
        private static async Task InitWebViewAsync(WebView2 webView, string cacheFolder, string url)
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, cacheFolder);
                await webView.EnsureCoreWebView2Async(env);

                if (!string.IsNullOrWhiteSpace(url))
                {
                    webView.Source = new Uri(url);
                }
            }
            catch (Exception ex)
            {
                await MessageBox.Show($"WebView2 Init Fail: {ex.Message}", "Tips");
            }
        }
    }
}
