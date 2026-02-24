using MaterialDesignThemes.Wpf;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using Snet.Core.extend;
using Snet.Core.handler;
using Snet.Model.data;
using Snet.Model.@enum;
using Snet.Windows.Core.data;
using Snet.Windows.Core.@enum;
using Snet.Windows.Core.handler;
using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using static Snet.Iot.Daq.chart.ChartData;
using Image = ScottPlot.Image;
using MessageBox = Snet.Windows.Controls.message.MessageBox;
using MessageBoxButton = Snet.Windows.Controls.@enum.MessageBoxButton;
using MessageBoxImage = Snet.Windows.Controls.@enum.MessageBoxImage;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Snet.Iot.Daq.chart
{
    /// <summary>
    /// 图表操作类<br/>
    /// 一个操作类可实现一个图表对象的操作<br/>
    /// 支持在一个图表中添加多个动态的实时曲线<br/>
    /// 支持加载历史数据
    /// </summary>
    public class ChartOperate : CoreUnify<ChartOperate, ChartData.Basics>, IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// 无参构造函数<br/>
        /// </summary>
        public ChartOperate() : base() { }

        /// <summary>
        /// 有参构造函数<br/>
        /// </summary>
        /// <param name="basics">基础数据</param>
        public ChartOperate(ChartData.Basics basics) : base(basics) { }

        /// <summary>
        /// 皮肤切换事件处理器<br/>
        /// 当皮肤改变时调用 Style 切换 ScottPlot 样式<br/>
        /// </summary>
        private void SkinHandler_OnSkinEvent(object? sender, EventSkinResult e)
        {
            Style(e.Skin ??= SkinType.Dark, wpfPlot);
        }

        #region 接口重写参数
        /// <inheritdoc/>
        protected override string CD => "支持图表的部分操作";

        /// <inheritdoc/>
        protected override string CN => "Chart";

        /// <inheritdoc/>
        public override LanguageModel LanguageOperate { get; set; } = new("Snet.Windows.Controls", "Language", "Snet.Windows.Controls.dll");

        /// <inheritdoc/>
        public override void Dispose()
        {
            Off();
            base.Dispose();
        }
        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            Off();
            await base.DisposeAsync();
        }
        #endregion

        /// <summary>
        /// 图表控件（WPF ScottPlot 控件）<br/>
        /// 注意：对 WpfPlot 的大部分操作必须在 UI 线程执行。<br/>
        /// 如果外部可能从非 UI 线程调用本类方法，建议在调用处或在内部使用 Dispatcher.Invoke/BeginInvoke。
        /// </summary>
        private WpfPlot wpfPlot;

        /// <summary>
        /// 黑夜模式样式缓存（Lazy 延迟创建）<br/>
        /// </summary>
        private ScottPlot.PlotStyles.Dark? dark;

        /// <summary>
        /// 白天模式样式缓存（Lazy 延迟创建）<br/>
        /// </summary>
        private ScottPlot.PlotStyles.Light? light;

        /// <summary>
        /// 当前皮肤类型<br/>
        /// </summary>
        private SkinType CurrentSkinType;

        /// <summary>
        /// Edge 样式下的图例面板（可能为空）
        /// </summary>
        private ScottPlot.Panels.LegendPanel? legendPanel;

        /// <summary>
        /// 默认图例（可能为空）
        /// </summary>
        private Legend? legend;

        /// <summary>
        /// 最大的实时线条数量（保护性能）<br/>
        /// 可以根据需要暴露为配置项。
        /// </summary>
        private int MaxDataLoggerNum = 10;

        /// <summary>
        /// 数据流图表管理：使用 ConcurrentDictionary 保证线程安全<br/>
        /// key = DataLoggerModel.SN, value = DataLoggerSource（包含 model 和 logger）
        /// </summary>
        private ConcurrentDictionary<string, ChartData.DataLoggerSource> DataLoggerChartManage = new ConcurrentDictionary<string, ChartData.DataLoggerSource>();

        /// <summary>
        /// 自动刷新的 CancellationTokenSource（在 Off/Dispose 时释放）
        /// </summary>
        private CancellationTokenSource? AutoRefreshTokenSource;
        /// <summary>
        /// 自动刷新状态标识<br/>
        /// 仅用于避免重复启动刷新循环。
        /// </summary>
        private volatile bool AutoRefreshStatus = false;

        /// <summary>
        /// 十字线
        /// </summary>
        private ScottPlot.Plottables.Crosshair CH;

        /// <summary>
        /// 自动刷新循环<br/>
        /// 优化点：<br/>
        /// 1. 不再在内部额外使用 Task.Run 去包装 UI 相关判断；<br/>
        /// 2. 使用 token.ThrowIfCancellationRequested 简洁处理取消；<br/>
        /// 3. 使用 Any() 替代 Count() 枚举性能开销；<br/>
        /// 4. 在 Cancel 时 Dispose TokenSource，避免资源泄漏。<br/>
        /// 注意：此方法会在内部启动一个后台任务；如果外部已经在 UI 线程周期性刷新，则可不启用。<br/>
        /// </summary>
        private async Task AutoRefreshAsync(CancellationToken token, int millisecond)
        {
            // 防止并发调用同时启动多个循环
            if (AutoRefreshStatus)
                return;

            AutoRefreshStatus = true;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    // 避免每次都枚举完整集合，Any() 在 IEnumerable 上能尽早返回
                    if (wpfPlot?.Plot?.GetPlottables()?.Any() == true)
                    {
                        // Refresh 必须在 UI 线程
                        wpfPlot.Dispatcher.Invoke(() => wpfPlot.Refresh());
                    }

                    await Task.Delay(millisecond, token).ConfigureAwait(false);
                }
            }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            finally
            {
                AutoRefreshStatus = false;
                // 在外部取消后尽量释放 CTS
                try { AutoRefreshTokenSource?.Dispose(); } catch { }
                AutoRefreshTokenSource = null;
            }
        }

        /// <summary>
        /// 重置（为当前 wpfPlot 应用基础设置）<br/>
        /// 注意：本方法会引用成员 basics 等，请确保在 On() 成功后再调用或已初始化。
        /// </summary>
        private void Reset()
        {
            // 确保 wpfPlot 已经被赋值
            wpfPlot ??= basics.ChartControl;

            // 调用 ScottPlot 的 Reset 会清除所有图形元素和设置
            wpfPlot.Reset();

            // 根据当前语言设置坐标轴标题与字体
            switch (GetLanguage())
            {
                case Snet.Model.@enum.LanguageType.zh:
                    wpfPlot.Plot.XLabel(basics.XTitle ?? string.Empty, 13);
                    wpfPlot.Plot.YLabel(basics.YTitle ?? string.Empty, 13);
                    wpfPlot.Plot.Legend.FontName = ScottPlot.Fonts.Detect("微软雅黑");
                    wpfPlot.Plot.Font.Set(ScottPlot.Fonts.Detect("微软雅黑"));
                    break;
                case Snet.Model.@enum.LanguageType.en:
                    wpfPlot.Plot.XLabel(basics.XTitleEN ?? string.Empty, 13);
                    wpfPlot.Plot.YLabel(basics.YTitleEN ?? string.Empty, 13);
                    break;
            }
            // 显示图例（右侧或者右上）
            if (basics.LegendRight)
            {
                legendPanel = wpfPlot.Plot.ShowLegend(Edge.Right);
            }
            else
            {
                legend = wpfPlot.Plot.ShowLegend(Alignment.UpperRight);
            }

            //wpfPlot.Plot.Axes.Bottom.TickLabelStyle.IsVisible = false;
            //wpfPlot.Plot.Axes.Top.IsVisible = false;
            //wpfPlot.Plot.Axes.Bottom.IsVisible = false;
            //wpfPlot.Plot.Axes.Left.IsVisible = true;
            //wpfPlot.Plot.Axes.Right.IsVisible = false;

            if (basics.YCrosshairText || basics.XCrosshairText)
            {
                CH = wpfPlot.Plot.Add.Crosshair(0, 0);
                CH.TextColor = Colors.White;
                string colorHex = "#27A5F7";
                CH.HorizontalLine.Color = new(colorHex);
                CH.VerticalLine.Color = new(colorHex);
                CH.TextBackgroundColor = CH.HorizontalLine.Color;
                wpfPlot.MouseMove -= WpfPlot_MouseMove;
                wpfPlot.MouseMove += WpfPlot_MouseMove;
            }

            if (basics.HideGrid)
            {
                wpfPlot.Plot.HideGrid();
            }

            // 订阅语言事件（先取消再订阅以避免重复订阅）
            OnLanguageEventAsync -= ChartOperate_OnLanguageEventAsync;
            OnLanguageEventAsync += ChartOperate_OnLanguageEventAsync;
            // 订阅皮肤时间（先取消再订阅以避免重复订阅）
            SkinHandler.OnSkinEvent -= SkinHandler_OnSkinEvent;
            SkinHandler.OnSkinEvent += SkinHandler_OnSkinEvent;

            // 设置默认菜单及皮肤事件
            DefaultMenu(wpfPlot);

            // 如果没有启动自动刷新，则启动一个新 CTS 并运行 AutoRefreshAsync
            if (AutoRefreshTokenSource == null)
            {
                AutoRefreshTokenSource = new CancellationTokenSource();
                // 不在 UI 线程等待 AutoRefreshAsync 完成；让其后台运行
                _ = AutoRefreshAsync(AutoRefreshTokenSource.Token, basics.RefreshTime);
            }
        }
        /// <summary>
        /// 鼠标移动事件处理（更新十字线位置与文本）
        /// </summary>
        private void WpfPlot_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            Point p = e.GetPosition(wpfPlot);
            ScottPlot.Pixel mousePixel = new(p.X * wpfPlot.DisplayScale, p.Y * wpfPlot.DisplayScale);
            ScottPlot.Coordinates coordinates = wpfPlot.Plot.GetCoordinates(mousePixel);
            CH.Position = coordinates;
            if (basics.YCrosshairText)
            {
                CH.HorizontalLine.Text = $"{coordinates.Y:N3}";
            }
            if (basics.XCrosshairText)
            {
                CH.VerticalLine.Text = $"{coordinates.X:N3}";
            }
            wpfPlot.Refresh();
        }

        /// <summary>
        /// 打开图表操作（初始化）<br/>
        /// </summary>
        public OperateResult On()
        {
            BegOperate();
            try
            {
                if (GetStatus().GetDetails(out string? message))
                {
                    return EndOperate(false, message);
                }
                Reset();
                return EndOperate(true);
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        /// <summary>
        /// 关闭图表并释放资源<br/>
        /// 优化点：取消并 Dispose CancellationTokenSource，清理集合，确保在 UI 线程清理 Plot 相关对象。
        /// </summary>
        public OperateResult Off()
        {
            BegOperate();
            try
            {
                if (!GetStatus().GetDetails(out string? message))
                {
                    return EndOperate(false, message);
                }

                if (AutoRefreshTokenSource != null)
                {
                    AutoRefreshStatus = false;
                    try { AutoRefreshTokenSource.Cancel(); } catch { }
                    try { AutoRefreshTokenSource.Dispose(); } catch { }
                    AutoRefreshTokenSource = null;
                }

                // 清理 DataLogger：调用 Clear 并释放每个 logger（如果需要）
                foreach (var kv in DataLoggerChartManage)
                {
                    try { kv.Value.Clear(); } catch { }
                }
                DataLoggerChartManage.Clear();

                // 清空所有数据（必须在 UI 线程执行）
                if (wpfPlot != null)
                {
                    wpfPlot.Dispatcher.Invoke(() =>
                    {
                        try { wpfPlot.Plot.Clear(); } catch { }
                        try { Reset(wpfPlot.Plot); } catch { }
                        try { wpfPlot.Plot.PlotControl?.Refresh(); } catch { }
                    });
                }

                wpfPlot = null;

                return EndOperate(true);
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        /// <summary>
        /// 获取当前状态<br/>
        /// </summary>
        public OperateResult GetStatus()
        {
            BegOperate();
            try
            {
                if (wpfPlot != null)
                {
                    return EndOperate(true);
                }
                return EndOperate(false);
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        /// <summary>
        /// 创建数据线（DataLogger）<br/>
        /// 优化点：
        /// 1. 使用 TryAdd + Count 属性避免并发问题；
        /// 2. 确保 Add.DataLogger 在 UI 线程执行（Dispatcher.Invoke）；
        /// 3. 减少无谓的 ToArray/Count 枚举调用。
        /// </summary>
        public OperateResult Create(DataLoggerModel model)
        {
            BegOperate();
            try
            {
                if (!GetStatus().GetDetails(out string? message))
                {
                    return EndOperate(false, message);
                }

                if (DataLoggerChartManage.ContainsKey(model.SN))
                {
                    return EndOperate(false, $"{model.SN}{LanguageOperate.GetLanguageValue("已存在")}");
                }

                // 限制线条数量（Count 属性为 O(1)）
                if (DataLoggerChartManage.Count >= MaxDataLoggerNum)
                {
                    return EndOperate(false, LanguageOperate.GetLanguageValue("超过最大实时线条限制，实时线条过多会导致性能大幅度下降"));
                }

                DataLogger obj = null!;

                // Add.DataLogger 必须在 UI 线程执行
                wpfPlot.Dispatcher.Invoke(() =>
                {
                    obj = wpfPlot.Plot.Add.DataLogger();
                });

                switch (GetLanguage())
                {
                    case Snet.Model.@enum.LanguageType.zh:
                        obj.LegendText = model.Title ?? string.Empty;
                        break;
                    case Snet.Model.@enum.LanguageType.en:
                        obj.LegendText = model.TitleEN ?? string.Empty;
                        break;
                }

                obj.Color = model?.Color == null ? wpfPlot.Plot.Add.GetNextColor() : new ScottPlot.Color(model.Color);
                obj.LineWidth = model.Width;
                obj.ViewSlide();

                ChartData.DataLoggerSource source = new()
                {
                    model = model,
                    logger = obj,
                    plot = wpfPlot
                };

                if (!DataLoggerChartManage.TryAdd(model.SN, source))
                {
                    // 如果并发情况下插入失败，则移除刚刚创建的 logger（在 UI 线程）并返回失败
                    wpfPlot.Dispatcher.Invoke(() => wpfPlot.Plot.Remove(obj));
                    return EndOperate(false, $"{model.SN}{LanguageOperate.GetLanguageValue("已存在")}");
                }

                return EndOperate(true);
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        /// <summary>
        /// 移除指定 SN 的线条<br/>
        /// 优化点：使用 TryRemove（ConcurrentDictionary API）替代自定义 Remove。<br/>
        /// </summary>
        public OperateResult Remove(string sn)
        {
            BegOperate();
            try
            {
                if (!GetStatus().GetDetails(out string? message))
                {
                    return EndOperate(false, message);
                }

                if (DataLoggerChartManage.TryRemove(sn, out var data))
                {
                    // 先清空数据
                    if (Clear(sn).GetDetails(out message))
                    {
                        // 把此线条从控件中移除（UI 线程）
                        data.plot.Dispatcher.Invoke(() => data.plot.Plot.Remove(data.logger));
                        return EndOperate(true);
                    }
                    return EndOperate(false, message);
                }
                return EndOperate(false, $"{sn}{LanguageOperate.GetLanguageValue("不存在")}");
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        /// <summary>
        /// 只获取实时采集的数据<br/>
        /// </summary>
        public OperateResult GetValue(string sn)
        {
            BegOperate();
            try
            {
                if (!GetStatus().GetDetails(out string? message))
                {
                    return EndOperate(false, message);
                }
                if (DataLoggerChartManage.TryGetValue(sn, out var data))
                {
                    return EndOperate(true, resultData: data.Get());
                }
                return EndOperate(false, $"{sn}{LanguageOperate.GetLanguageValue("不存在")}");
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        /// <summary>
        /// 更新数据点（向指定 SN 的 logger 添加一个新值）
        /// </summary>
        public OperateResult Update(string sn, double value)
        {
            BegOperate();
            try
            {
                if (DataLoggerChartManage.TryGetValue(sn, out var data))
                {
                    data.Update(value);
                    return EndOperate(true);
                }
                return EndOperate(false, $"{sn}{LanguageOperate.GetLanguageValue("不存在")}");
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        /// <summary>
        /// 清空指定 SN 的线条数据<br/>
        /// </summary>
        public OperateResult Clear(string sn)
        {
            BegOperate();
            try
            {
                if (DataLoggerChartManage.TryGetValue(sn, out var data))
                {
                    data.Clear();
                    return EndOperate(true);
                }
                return EndOperate(false, $"{sn}{LanguageOperate.GetLanguageValue("不存在")}");
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        /// <summary>
        /// 清空所有线条数据<br/>
        /// </summary>
        public OperateResult Clear()
        {
            BegOperate();
            try
            {
                foreach (var data in DataLoggerChartManage)
                {
                    data.Value.Clear();
                }
                return EndOperate(true);
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        /// <summary>
        /// 获取指定 SN 线条的全部数据<br/>
        /// </summary>
        public OperateResult Get(string sn)
        {
            BegOperate();
            try
            {
                if (DataLoggerChartManage.TryGetValue(sn, out var data))
                {
                    return EndOperate(true, resultData: data.Get());
                }
                return EndOperate(false, $"{sn}{LanguageOperate.GetLanguageValue("不存在")}");
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        /// <summary>
        /// 将 WPF 的 Color 转换为 System.Drawing.Color（ScottPlot 使用）
        /// </summary>
        private System.Drawing.Color ToDrawingColor(System.Windows.Media.Color color)
        {
            return System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
        }

        /// <summary>
        /// 应用皮肤样式到 ScottPlot<br/>
        /// 优化点：延迟创建样式对象并复用，避免频繁 new 对象带来的开销。
        /// </summary>
        public bool Style(SkinType skin, WpfPlot? plot = null)
        {
            try
            {
                if (plot == null)
                {
                    plot = wpfPlot;
                }
                CurrentSkinType = skin;
                switch (skin)
                {
                    case SkinType.Dark:
                        if (dark == null)
                        {
                            dark = new ScottPlot.PlotStyles.Dark()
                            {
                                FigureBackgroundColor = new("#454545"),
                                DataBackgroundColor = new("#454545"),
                                LegendBackgroundColor = new("#454545"),
                                //LegendOutlineColor = new(ToDrawingColor(System.Windows.Media.Color.FromArgb(0, 255, 0, 0))),
                            };
                        }
                        plot?.Plot.SetStyle(dark);
                        break;
                    case SkinType.Light:
                        if (light == null)
                        {
                            light = new ScottPlot.PlotStyles.Light()
                            {
                                //LegendBackgroundColor = new("#454545"),
                                //LegendOutlineColor = new(ToDrawingColor(System.Windows.Media.Color.FromArgb(0, 255, 0, 0))),
                            };
                        }
                        plot?.Plot.SetStyle(light);
                        break;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 语言事件处理（切换线条 legend 文本与坐标轴标题）
        /// </summary>
        private Task ChartOperate_OnLanguageEventAsync(object? sender, EventLanguageResult e)
        {
            if (wpfPlot == null)
                return Task.CompletedTask;
            if (e.GetDetails(out string msg, out LanguageType? language))
            {
                foreach (var item in DataLoggerChartManage)
                {
                    switch (language ??= GetLanguage())
                    {
                        case Snet.Model.@enum.LanguageType.zh:
                            item.Value.logger.LegendText = item.Value.model.Title ?? string.Empty;
                            break;
                        case Snet.Model.@enum.LanguageType.en:
                            item.Value.logger.LegendText = item.Value.model.TitleEN ?? string.Empty;
                            break;
                    }
                }
                switch (language ??= GetLanguage())
                {
                    case Snet.Model.@enum.LanguageType.zh:
                        wpfPlot.Plot.XLabel(basics.XTitle ?? string.Empty, 13);
                        wpfPlot.Plot.YLabel(basics.YTitle ?? string.Empty, 13);
                        wpfPlot.Plot.Legend.FontName = ScottPlot.Fonts.Detect("微软雅黑");
                        wpfPlot.Plot.Font.Set(ScottPlot.Fonts.Detect("微软雅黑"));
                        break;
                    case Snet.Model.@enum.LanguageType.en:
                        wpfPlot.Plot.XLabel(basics.XTitleEN ?? string.Empty, 13);
                        wpfPlot.Plot.YLabel(basics.YTitleEN ?? string.Empty, 13);
                        break;
                }
                DefaultMenu(wpfPlot);
                wpfPlot.Refresh();
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 设置默认右键菜单（将外部调用委托给 ScottPlot 的 Menu API）
        /// </summary>
        public bool DefaultMenu(WpfPlot plot)
        {
            try
            {
                if (plot != null)
                {
                    plot.Menu?.Clear();

                    plot.Menu?.Add(LanguageOperate.GetLanguageValue("调整"), Adjust);
                    plot.Menu?.Add(LanguageOperate.GetLanguageValue("重置"), Reset);
                    plot.Menu?.AddSeparator();
                    plot.Menu?.Add(LanguageOperate.GetLanguageValue("保存图片"), SaveImage);
                    plot.Menu?.Add(LanguageOperate.GetLanguageValue("复制图片"), CopyImage);
                    if (basics.LineRemove)
                    {
                        plot.Menu?.AddSeparator();
                        plot.Menu?.Add(LanguageOperate.GetLanguageValue("移除线条"), RemoveLine);
                    }
                    if (basics.LineAdjust)
                    {
                        plot.Menu?.AddSeparator();
                        plot.Menu?.Add(LanguageOperate.GetLanguageValue("线条操作"), LineOperate);
                    }
                    return true;
                }
            }
            catch
            {
                // 建议在此处记录异常日志，避免吞掉所有错误信息
            }
            return false;
        }

        #region 右键菜单项

        /// <summary>
        /// 保存图片到文件<br/>
        /// 优化点：使用 RenderManager.LastRender 的尺寸来保存同等分辨率的图片；
        /// 捕获异常并提示用户。
        /// </summary>
        private void SaveImage(Plot plot)
        {
            SaveFileDialog dialog = new()
            {
                FileName = "chart.png",
                Filter = "PNG Files (*.png)|*.png" +
                         "|JPEG Files (*.jpg, *.jpeg)|*.jpg;*.jpeg" +
                         "|BMP Files (*.bmp)|*.bmp" +
                         "|WebP Files (*.webp)|*.webp" +
                         "|SVG Files (*.svg)|*.svg" +
                         "|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() is true)
            {
                if (string.IsNullOrEmpty(dialog.FileName))
                    return;

                ImageFormat format;

                try
                {
                    format = ImageFormats.FromFilename(dialog.FileName);
                }
                catch (ArgumentException)
                {
                    MessageBox.Show(LanguageOperate.GetLanguageValue("不支持的图像文件格式"), LanguageOperate.GetLanguageValue("异常"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                try
                {
                    PixelSize lastRenderSize = plot.RenderManager.LastRender.FigureRect.Size;
                    plot.Save(dialog.FileName, (int)lastRenderSize.Width, (int)lastRenderSize.Height, format);
                }
                catch (Exception)
                {
                    MessageBox.Show(LanguageOperate.GetLanguageValue("图像保存失败"), LanguageOperate.GetLanguageValue("异常"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
        }

        /// <summary>
        /// 复制图片到剪贴板<br/>
        /// 优化点：确保 MemoryStream Position 被重置并使用 BitmapImage.CacheOption.OnLoad 以便可以在流关闭后使用图像，
        /// 并在构建完成后调用 Freeze() 以便跨线程使用（剪贴板可能需要）。
        /// </summary>
        private void CopyImage(Plot plot)
        {
            PixelSize lastRenderSize = plot.RenderManager.LastRender.FigureRect.Size;
            Image bmp = plot.GetImage((int)lastRenderSize.Width, (int)lastRenderSize.Height);
            byte[] bmpBytes = bmp.GetImageBytes();

            using MemoryStream ms = new();
            ms.Write(bmpBytes, 0, bmpBytes.Length);
            ms.Position = 0; // 重置位置

            BitmapImage bmpImage = new();
            bmpImage.BeginInit();
            bmpImage.CacheOption = BitmapCacheOption.OnLoad; // 在流关闭后仍能使用
            bmpImage.StreamSource = ms;
            bmpImage.EndInit();
            bmpImage.Freeze();

            System.Windows.Clipboard.SetImage(bmpImage);
        }

        /// <summary>
        /// 调整坐标轴自适应<br/>
        /// </summary>
        private void Adjust(Plot plot)
        {
            plot.Axes.AutoScale();
            plot.PlotControl?.Refresh();
        }

        /// <summary>
        /// 重置坐标范围（用于右键菜单）<br/>
        /// </summary>
        private void Reset(Plot plot)
        {
            plot.Axes.SetLimitsX(-10, 10);
            plot.Axes.SetLimitsY(-10, 10);
            plot.PlotControl?.Refresh();
        }

        /// <summary>
        /// 移除所有线条并重置样式<br/>
        /// </summary>
        private void RemoveLine(Plot plot)
        {
            if (plot == null)
                return;
            plot.Clear();
            Reset(plot);
            // 重新应用当前皮肤样式，避免样式丢失
            switch (CurrentSkinType)
            {
                case SkinType.Dark: wpfPlot?.Plot.SetStyle(dark); break;
                case SkinType.Light: wpfPlot?.Plot.SetStyle(light); break;
            }
            plot.PlotControl?.Refresh();
        }

        /// <summary>
        /// 打开线条操作窗口（将图例分离成独立可交互画布）
        /// 注意：此功能涉及 SkiaSharp 的绘制与事件映射，保留原有逻辑，仅做少量整理。
        /// </summary>
        private void LineOperate(Plot plot)
        {
            plot.Legend.ShowItemsFromHiddenPlottables = true;
            plot.Legend.OutlineWidth = 0;
            plot.Legend.BackgroundColor = ScottPlot.Color.FromSDColor(System.Drawing.SystemColors.Control);
            plot.Legend.ShadowColor = ScottPlot.Colors.Transparent;
            if (legend != null)
            {
                legend.IsVisible = false;
            }
            wpfPlot?.Refresh();

            ChartLine form = new ChartLine()
            {
                Width = wpfPlot.Plot.Legend.LastRenderSize.Width,
                Height = wpfPlot.Plot.Legend.LastRenderSize.Height
            };

            SKElement sKElement = new()
            {
                Width = wpfPlot.Plot.Legend.LastRenderSize.Width,
                Height = wpfPlot.Plot.Legend.LastRenderSize.Height
            };
            sKElement.PaintSurface += (s, e) => { PaintDetachedLegend((SKElement)s!, e); };
            sKElement.MouseLeftButtonDown += (s, e) => { MouseLeftButtonDown((SKElement)s!, e); };
            form.Content = sKElement;
            form.Opacity = 0;
            void Loaded(object? sender, DialogOpenedEventArgs eventArgs)
            {
                switch (CurrentSkinType)
                {
                    case SkinType.Dark: wpfPlot?.Plot.SetStyle(dark); break;
                    case SkinType.Light: wpfPlot?.Plot.SetStyle(light); break;
                }
                wpfPlot?.Refresh();
                form.Opacity = 1;
            }
            void Closing(object? sender, DialogClosingEventArgs eventArgs)
            {
                if (legend != null)
                {
                    legend.IsVisible = true;
                    legend.OutlineWidth = 1;
                }

                switch (CurrentSkinType)
                {
                    case SkinType.Dark: wpfPlot?.Plot.SetStyle(dark); break;
                    case SkinType.Light: wpfPlot?.Plot.SetStyle(light); break;
                }
                wpfPlot?.Refresh();
            }

            DialogHost.Show(form, "DialogHost_ClickClose", Loaded, Closing);
        }

        /// <summary>
        /// 绘制独立图例（Detached Legend）的方法。<br/>
        /// 此函数在 SkiaSharp 的绘制回调中执行，用于在单独的 SKElement 上渲染图例，
        /// 而不与主绘图区域（wpfPlot）绑定，以便图例可以独立移动或显示。
        /// </summary>
        /// <param name="sender">触发绘制事件的 SKElement 控件</param>
        /// <param name="e">包含绘制上下文（SKCanvas）的事件参数</param>
        private void PaintDetachedLegend(SKElement sender, SKPaintSurfaceEventArgs e)
        {
            // 获取绘图区域的像素尺寸
            PixelSize size = new(sender.Width, sender.Height);
            PixelRect rect = new(Pixel.Zero, size);

            // 获取绘图画布
            SKCanvas canvas = e.Surface.Canvas;

            // 创建可释放的绘图笔刷（ScottPlot 封装）
            using Paint paint = ScottPlot.Paint.NewDisposablePaint();

            // 使用图表的 Legend 对象在指定区域绘制图例
            // Alignment.UpperLeft 表示图例绘制在左上角
            wpfPlot.Plot.Legend.Render(canvas, paint, rect, Alignment.UpperLeft);
        }

        /// <summary>
        /// 鼠标左键点击事件处理函数。<br/>
        /// 当用户点击图例项时，会自动切换该图例对应曲线（Plottable）的可见状态（显示/隐藏）。<br/>
        /// 实现图表交互式控制功能。
        /// </summary>
        /// <param name="sender">触发事件的 SKElement 控件</param>
        /// <param name="e">鼠标事件参数，包含点击位置</param>
        private void MouseLeftButtonDown(SKElement sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // 根据鼠标点击位置获取对应的图例项
            var item = GetLegendItemUnderMouse(sender, e.GetPosition(sender));

            // 获取被点击的可绘制对象（Plottable）
            var ClickedPlottable = item != null ? item.Plottable : null;

            // 如果存在对应项，则切换其显示状态
            if (ClickedPlottable != null)
            {
                ClickedPlottable.IsVisible = !ClickedPlottable.IsVisible;
            }

            // 刷新主图表和图例显示
            wpfPlot?.Refresh();
            sender.InvalidateVisual();
        }

        /// <summary>
        /// 根据鼠标位置判断当前位于哪一个图例项上方。<br/>
        /// 通过计算图例项的布局矩形（文本区域与符号区域）来检测命中位置。
        /// </summary>
        /// <param name="sender">触发检测的 SKElement 控件</param>
        /// <param name="e">鼠标在控件内的相对坐标</param>
        /// <returns>若命中图例项则返回 LegendItem，否则返回 null</returns>
        private LegendItem? GetLegendItemUnderMouse(SKElement sender, System.Windows.Point e)
        {
            // 计算当前绘图区域的像素大小
            PixelSize size = new(sender.Width, sender.Height);

            // 获取图例中的所有项
            LegendItem[] items = wpfPlot.Plot.Legend.GetItems();

            // 创建绘制工具用于布局计算
            using Paint paint = ScottPlot.Paint.NewDisposablePaint();

            // 获取图例布局信息（包含每个标签和符号的矩形范围）
            LegendLayout layout = wpfPlot.Plot.Legend.GetLayout(size, paint);

            // 若无图例项则直接返回
            if (items.Length == 0)
                return null;

            // 将每个图例项与其布局矩形配对（标签矩形 + 符号矩形）
            var itemslayout = Enumerable.Zip(items, layout.LabelRects, layout.SymbolRects);

            // 遍历所有图例项，检测鼠标是否位于其中某个区域
            foreach (var il in itemslayout)
            {
                var item = il.First;   // 图例项对象
                var lrect = il.Second; // 标签文本区域
                var srect = il.Third;  // 符号区域

                // 若鼠标位于标签或符号矩形范围内，则认为命中该图例项
                if (lrect.Contains((float)e.X, (float)e.Y) || srect.Contains((float)e.X, (float)e.Y))
                {
                    return item;
                }
            }

            // 未命中任何图例项则返回 null
            return null;
        }


        #endregion

    }
}
