using ScottPlot;
using ScottPlot.WPF;

namespace Snet.Iot.Daq.chart
{
    /// <summary>
    /// 图表处理
    /// </summary>
    public static class ChartHandler
    {
        /// <summary>
        /// 创建一条线，里面包含XY轴数据集合
        /// </summary>
        /// <param name="control">控件</param>
        /// <param name="xs">x轴数据集</param>
        /// <param name="ys">y轴数据集</param>
        /// <param name="title">标题</param>
        /// <param name="color">颜色</param>
        /// <returns>返回接口对象</returns>
        public static IPlottable Create<T>(this WpfPlot control, T[] xs, T[] ys, string? title = null, Color? color = null)
        {
            var result = control.Plot.Add.ScatterLine(xs, ys, color);
            if (title != null)
            {
                result.LegendText = title;
            }
            return result;
        }

        /// <summary>
        /// 创建一条线，里面包含XY轴数据集合
        /// </summary>
        /// <param name="control">控件</param>
        /// <param name="ys">y轴数据集</param>
        /// <param name="title">标题</param>
        /// <param name="color">颜色</param>
        /// <returns>返回接口对象</returns>
        public static IPlottable Create<T>(this WpfPlot control, T[] ys, string? title = null, Color? color = null)
        {
            var result = control.Plot.Add.Signal(ys, color: color);
            if (title != null)
            {
                result.LegendText = title;
            }
            return result;
        }


        /// <summary>
        /// 创建一条线，里面包含XY轴数据集合
        /// </summary>
        /// <param name="control">控件</param>
        /// <param name="xs">x轴数据集</param>
        /// <param name="ys">y轴数据集</param>
        /// <param name="title">标题</param>
        /// <param name="color">颜色</param>
        /// <returns>返回接口对象</returns>
        public static IPlottable Create<T>(this WpfPlot control, List<T> xs, List<T> ys, string? title = null, Color? color = null)
        {
            var result = control.Plot.Add.ScatterLine(xs, ys, color);
            if (title != null)
            {
                result.LegendText = title;
            }
            return result;
        }

        /// <summary>
        /// 创建一条线，里面包含XY轴数据集合
        /// </summary>
        /// <param name="control">控件</param>
        /// <param name="ys">y轴数据集</param>
        /// <param name="title">标题</param>
        /// <param name="color">颜色</param>
        /// <returns>返回接口对象</returns>
        public static IPlottable Create<T>(this WpfPlot control, List<T> ys, string? title = null, Color? color = null)
        {
            var result = control.Plot.Add.Signal(ys, color: color);
            if (title != null)
            {
                result.LegendText = title;
            }
            return result;
        }



        /// <summary>
        /// 移除指定线条
        /// </summary>
        /// <param name="control">控件</param>
        /// <param name="obj">要移除的对象</param>
        /// <param name="ex">异常信息</param>
        /// <returns>移除状态</returns>
        public static bool Remove(this WpfPlot control, IPlottable obj, out Exception? ex)
        {
            ex = null;
            try
            {
                control.Plot.Remove(obj);
                return true;
            }
            catch (Exception e)
            {
                ex = e;
            }
            return false;
        }

        /// <summary>
        /// 移除所有线条
        /// </summary>
        /// <param name="control">控件</param>
        /// <returns>移除状态</returns>
        public static bool RemoveAll(this WpfPlot control)
        {
            try
            {
                control.Plot.Clear();
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 调整
        /// </summary>
        /// <param name="control"></param>
        /// <returns></returns>
        public static bool Adjust(this WpfPlot control)
        {
            try
            {
                control.Plot.Axes.AutoScale();
                control.Plot.PlotControl?.Refresh();
                return true;
            }
            catch (Exception)
            {
                return false;
            }

        }
    }
}
