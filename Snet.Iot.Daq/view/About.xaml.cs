using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Snet.Windows.Controls.message;
using System.Windows.Controls;

namespace Snet.Iot.Daq.view
{
    /// <summary>
    /// About.xaml 的交互逻辑
    /// </summary>
    public partial class About : UserControl
    {
        public About()
        {
            InitializeComponent();
            _ = InitWebViewAsync(webView, "cache", "https://shunnet.top").ConfigureAwait(false);
        }

        /// <summary>
        /// 初始化 WebView2 控件
        /// </summary>
        /// <param name="webView">WebView2 控件</param>
        /// <param name="cacheFolder">缓存目录，建议绝对路径</param>
        /// <param name="url">要加载的网页地址</param>
        private async Task InitWebViewAsync(WebView2 webView, string cacheFolder, string url)
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
                // 出错时提示（实际项目里可写到日志）
                await MessageBox.Show($"WebView2 Init Fail: {ex.Message}", "Tips");
            }
        }
    }
}
