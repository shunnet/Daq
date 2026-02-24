using ScottPlot.Plottables;
using ScottPlot.WPF;
using Snet.Log;
using Snet.Utility;
using System.Text.Json.Serialization;

namespace Snet.Iot.Daq.chart
{
    /// <summary>
    /// 图标数据
    /// </summary>
    public class ChartData
    {
        /// <summary>
        /// 基础数据
        /// </summary>
        public class Basics
        {
            /// <summary>
            /// 唯一标识符
            /// </summary>
            public string SN { get; set; } = Guid.NewGuid().ToUpperNString();
            /// <summary>
            /// 图表控件，不能为空
            /// </summary>
            [JsonIgnore]
            public required WpfPlot ChartControl { get; set; }
            /// <summary>
            /// X轴标题
            /// </summary>
            public string? XTitle { get; set; }
            /// <summary>
            /// Y轴标题
            /// </summary>
            public string? YTitle { get; set; }
            /// <summary>
            /// X轴标题英语
            /// </summary>
            public string? XTitleEN { get; set; }
            /// <summary>
            /// Y轴标题英语
            /// </summary>
            public string? YTitleEN { get; set; }

            /// <summary>
            /// 隐藏grid线条
            /// </summary>
            public bool HideGrid { get; set; } = true;

            /// <summary>
            /// 线条标题在最右侧<br/>
            /// 线条过多时使用
            /// </summary>
            public bool LegendRight { get; set; }

            /// <summary>
            /// 移除所有线条右键菜单
            /// </summary>
            public bool LineRemove { get; set; }

            /// <summary>
            /// 是否需要线条调整右键菜单
            /// </summary>
            public bool LineAdjust { get; set; }

            /// <summary>
            /// 刷新时间
            /// </summary>
            public int RefreshTime { get; set; } = 100;

            /// <summary>
            /// Y轴的十字准线数据
            /// </summary>
            public bool YCrosshairText { get; set; }

            /// <summary>
            /// X轴的十字准线数据
            /// </summary>
            public bool XCrosshairText { get; set; }
        }

        /// <summary>
        /// 模型基类
        /// </summary>
        public class ModelBase
        {
            /// <summary>
            /// 唯一标识符
            /// </summary>
            public string SN { get; set; } = Guid.NewGuid().ToUpperNString();
            /// <summary>
            /// 标题
            /// </summary>
            public string? Title { get; set; }

            /// <summary>
            /// 标题英语
            /// </summary>
            public string? TitleEN { get; set; }

            /// <summary>
            /// 线条颜色<br/>
            /// 16进制的颜色数据<br/>
            /// #00FFFF
            /// </summary>
            public string? Color { get; set; }

            /// <summary>
            /// 线条宽度
            /// </summary>
            public float Width { get; set; } = 1.5f;
        }
        /// <summary>
        /// 创建一个只有Y轴的实时数据展示
        /// </summary>
        public class DataLoggerModel : ModelBase
        {
            /// <summary>
            /// 最大值<br/>
            /// 超过则不做显示<br/>
            /// nan表示不限制
            /// </summary>
            public double MaxValue { get; set; } = double.NaN;

            /// <summary>
            /// 最小值<br/>
            /// 超过则不做显示<br/>
            /// nan表示不限制
            /// </summary>
            public double MinValue { get; set; } = double.NaN;
        }

        /// <summary>
        /// DataLogger数据源<br/>
        /// 对内的保护对象
        /// </summary>
        protected internal class DataLoggerSource
        {
            /// <summary>
            /// 控件
            /// </summary>
            public required WpfPlot plot { get; set; }

            /// <summary>
            /// 数据传入的模型
            /// </summary>
            public required DataLoggerModel model { get; set; }
            /// <summary>
            /// 数据记录器
            /// </summary>
            public required DataLogger logger { get; set; }

            /// <summary>
            /// 数据
            /// </summary>
            private List<double> data = new List<double>();

            /// <summary>
            /// 更新数据
            /// </summary>
            /// <param name="v">值</param>
            public void Update(double v)
            {
                if (!double.IsNaN(model.MaxValue) && !double.IsNaN(model.MinValue))
                {
                    if (v >= model.MinValue && v <= model.MaxValue)
                    {
                        logger.Add(v);
                        data.Add(v);
                    }
                    else
                    {
                        LogHelper.Verbose(message: $"{model.SN} {v} exceed limit value", foldername: "Chart\\Source");
                    }
                }
                else if (!double.IsNaN(model.MaxValue) && double.IsNaN(model.MinValue))
                {
                    if (v <= model.MaxValue)
                    {
                        logger.Add(v);
                        data.Add(v);
                    }
                    else
                    {
                        LogHelper.Verbose(message: $"{model.SN} {v} exceed max value", foldername: "Chart\\Source");
                    }
                }
                else if (double.IsNaN(model.MaxValue) && !double.IsNaN(model.MinValue))
                {
                    if (v >= model.MinValue)
                    {
                        logger.Add(v);
                        data.Add(v);
                    }
                    else
                    {
                        LogHelper.Verbose(message: $"{model.SN} {v} exceed min value", foldername: "Chart\\Source");
                    }
                }
                else
                {
                    logger.Add(v);
                    data.Add(v);
                }
            }

            /// <summary>
            /// 清空当前线条数据
            /// </summary>
            public void Clear()
            {
                data.Clear();
                logger.Data.Clear();
                logger.Clear();
            }

            /// <summary>
            /// 获取XY轴数据
            /// </summary>
            /// <returns></returns>
            public (double[] Ys, double[] Xs) Get()
            {
                if (data != null && data.Count > 0)
                {
                    return (data.ToArray(), Enumerable.Range(0, data.Count()).Select(i => (double)i).ToArray());
                }
                return ([], []);
            }


        }
    }
}
