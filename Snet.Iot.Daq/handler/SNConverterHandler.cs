using System.Globalization;
using System.Windows.Data;

namespace Snet.Iot.Daq.handler
{
    public class SNConverterHandler : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return string.Empty;

            var parts = value.ToString()?.Split('.') ?? [];
            return parts.Length >= 4 ? parts[^4] : value.ToString() ?? string.Empty;
        }

        // 从界面 -> 数据源（双向绑定时回写，如果只显示可以不实现）
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
