using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
namespace Snet.Iot.Daq.handler
{
    public class ByteArrayToImageConverterHandler : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is byte[] bytes && bytes.Length > 0)
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.StreamSource = new MemoryStream(bytes);
                image.CacheOption = BitmapCacheOption.OnLoad; // 加载后释放内存流
                image.EndInit();
                image.Freeze(); // 允许跨线程访问
                return image;
            }
            return null; // 返回 null 可配合 TargetNullValue 显示默认图
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
