using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
namespace Snet.Iot.Daq.handler
{
    /// <summary>将地址读回的图片字节转换为可跨线程显示的冻结图片，加载完成后立即释放输入流。</summary>
    public class ByteArrayToImageConverterHandler : IValueConverter
    {
        /// <summary>同步解码图片；空或无法识别的内容返回 null，由绑定的默认图片接管显示。</summary>
        /// <param name="value">待解码的图片字节数组。</param>
        /// <param name="targetType">绑定目标类型，图片解码不依赖此值。</param>
        /// <param name="parameter">保留的绑定参数。</param>
        /// <param name="culture">绑定文化，二进制图片不依赖文化设置。</param>
        /// <returns>已加载并冻结的图片；输入无效时为空。</returns>
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is byte[] bytes && bytes.Length > 0)
            {
                try
                {
                    using var stream = new MemoryStream(bytes, writable: false);
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.StreamSource = stream;
                    image.CacheOption = BitmapCacheOption.OnLoad; // 完整解码后不再依赖输入流。
                    image.EndInit();
                    image.Freeze(); // 允许跨线程访问。
                    return image;
                }
                catch (NotSupportedException) { return null; }
                catch (IOException) { return null; }
            }
            return null; // 返回 null 可配合 TargetNullValue 显示默认图
        }

        /// <summary>图片仅用于显示，不把 WPF 图片反向写入地址数据。</summary>
        /// <param name="value">显示中的图片。</param>
        /// <param name="targetType">源绑定类型。</param>
        /// <param name="parameter">保留的绑定参数。</param>
        /// <param name="culture">绑定文化。</param>
        /// <returns>Binding.DoNothing，保持原始字节不变。</returns>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
