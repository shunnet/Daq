
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Serilog;
using Serilog.Events;
using Snet.Log;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Reflection;

namespace Snet.Iot.Daq.Core.opc.core
{
    /// <summary>
    /// OPC UA 遥测上下文
    /// 
    /// 统一封装并管理：
    /// 1. 日志系统（ILogger / Serilog）
    /// 2. 分布式追踪（ActivitySource）
    /// 3. 指标采集（Meter）
    /// 
    /// 设计目标：
    /// - 可通过 DI 注入（推荐）
    /// - 也可独立运行（自建 LoggerFactory）
    /// - 生命周期清晰、安全
    /// </summary>
    public sealed class UaTelemetry : ITelemetryContext, IDisposable
    {
        /// <summary>
        /// opc类型
        /// </summary>
        public enum OpcType
        {
            /// <summary>
            /// 客户端
            /// </summary>
            Client,
            /// <summary>
            /// 服务端
            /// </summary>
            Service
        }
        /// <summary>
        /// 应用名称（用于日志、追踪、指标命名）
        /// </summary>
        private const string AppName = "Snet.Opc";

        /// <summary>
        /// 当前程序集版本号（优先取 InformationalVersion）
        /// </summary>
        private static readonly string Version = GetInformationalVersion();

        private readonly ILoggerFactory _loggerFactory;
        private readonly ActivitySource _activitySource;
        private readonly Meter _meter;
        private static OpcType _opcType = OpcType.Client;

        /// <summary>
        /// 标识 LoggerFactory 是否由本类创建
        /// 用于正确处理 Dispose 所有权
        /// </summary>
        private readonly bool _ownsLoggerFactory;

        private bool _disposed;

        /// <summary>
        /// 分布式追踪源（OpenTelemetry / DiagnosticSource）
        /// </summary>
        public ActivitySource ActivitySource => _activitySource;

        /// <summary>
        /// 指标采集器（OpenTelemetry Metrics）
        /// </summary>
        public Meter Meter => _meter;

        /// <summary>
        /// 日志工厂（ILoggerFactory）
        /// </summary>
        public ILoggerFactory LoggerFactory => _loggerFactory;

        /// <summary>
        /// 推荐构造函数（通过依赖注入）
        /// 
        /// LoggerFactory 的生命周期由外部 Host 管理，
        /// 本类不会 Dispose 它。
        /// </summary>
        /// <param name="loggerFactory">宿主注入的日志工厂</param>
        /// <param name="type">类型</param>
        public UaTelemetry(ILoggerFactory loggerFactory, OpcType type)
        {
            ArgumentNullException.ThrowIfNull(loggerFactory);

            _opcType = type;

            _loggerFactory = loggerFactory;
            _ownsLoggerFactory = false;

            _activitySource = new ActivitySource(AppName, Version);
            _meter = new Meter(AppName, Version);

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += Unobserved_TaskException;

            LogHelper.Info($"UaTelemetry（DI 模式）初始化完成 v{Version}", Path.Combine("Opc.Ua.Telemetry", _opcType.ToString()), null, false);
        }

        /// <summary>
        /// 备选构造函数（独立运行 / 无 Host 场景）
        /// 
        /// 内部会创建 LoggerFactory，并负责释放
        /// </summary>
        /// <param name="type">类型</param>
        public UaTelemetry(OpcType type)
        {
            _opcType = type;

            _loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            {
                // 使用 Serilog 作为日志后端
                builder.AddSerilog(CreateProductionLogger(), dispose: true);
            });

            _ownsLoggerFactory = true;

            _activitySource = new ActivitySource(AppName, Version);
            _meter = new Meter(AppName, Version);

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += Unobserved_TaskException;

            LogHelper.Info($"UaTelemetry（自建模式）初始化完成 v{Version}", Path.Combine("Opc.Ua.Telemetry", _opcType.ToString()), null, false);
        }

        /// <summary>
        /// 创建生产环境可用的 Serilog Logger
        /// 
        /// 日志路径规范：
        /// logs/
        ///   └─ yyyy-MM-dd/
        ///        └─ yyyyMMddHH.log （按小时滚动）
        /// </summary>
        private static Serilog.Core.Logger CreateProductionLogger()
        {
            var baseDir = AppContext.BaseDirectory;
            var logRoot = Path.Combine(baseDir, "logs");

            return new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", AppName)
                .Enrich.WithProperty("Version", Version)
                .WriteTo.Console()
                .WriteTo.File(
                    path: Path.Combine(logRoot, "{Date:yyyy-MM-dd}", "Opc.Ua.Telemetry".ToLower(), _opcType.ToString().ToLower(), "{Date:yyyyMMddHH}.log"),
                    rollingInterval: RollingInterval.Hour,
                    retainedFileCountLimit: 31 * 24,
                    fileSizeLimitBytes: 50 * 1024 * 1024,
                    buffered: true,
                    flushToDiskInterval: TimeSpan.FromSeconds(5),
                    outputTemplate:
                        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] {Level:u3} {Message:lj}{NewLine}{Exception}"
                )
                .CreateLogger();
        }

        /// <summary>
        /// 创建或返回当前使用的 Meter
        /// 
        /// 目前设计为：
        /// - 每个 UaTelemetry 实例持有一个 Meter
        /// - 避免重复创建导致指标分散
        /// </summary>
        public Meter CreateMeter()
        {
            return _meter;
        }

        /// <summary>
        /// 释放遥测相关资源
        /// 
        /// 注意：
        /// - 仅在“自建模式”下释放 LoggerFactory
        /// - 外部注入的 LoggerFactory 不归本类管理
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                LogHelper.Debug("UaTelemetry 开始释放资源", Path.Combine("Opc.Ua.Telemetry", _opcType.ToString()), null, false);

                _meter?.Dispose();
                _activitySource?.Dispose();

                if (_ownsLoggerFactory)
                {
                    _loggerFactory?.Dispose();
                }
            }
            catch (Exception ex)
            {
                // Dispose 阶段不允许抛异常，兜底处理
                try
                {
                    LogHelper.Error(ex, "UaTelemetry 释放资源时发生异常", Path.Combine("Opc.Ua.Telemetry", _opcType.ToString()), null, false);
                }
                catch
                {
                    // 最终兜底，静默
                }
            }
            finally
            {
                _disposed = true;
                AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
                TaskScheduler.UnobservedTaskException -= Unobserved_TaskException;
            }
        }

        /// <summary>
        /// 获取程序集的 InformationalVersion
        /// 
        /// 优先级：
        /// 1. AssemblyInformationalVersionAttribute
        /// 2. AssemblyVersion
        /// 3. fallback
        /// </summary>
        private static string GetInformationalVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();

                if (assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>() is
                    { InformationalVersion: { } ver } && !string.IsNullOrWhiteSpace(ver))
                {
                    return ver.Split('+')[0];
                }

                return assembly.GetName().Version?.ToString() ?? "0.0.0-dev";
            }
            catch
            {
                return "0.0.0-unknown";
            }
        }

        private void CurrentDomain_UnhandledException(
            object sender,
            UnhandledExceptionEventArgs args)
        {
            LogHelper.Error($"Unhandled Exception: (IsTerminating: {args.IsTerminating})", Path.Combine("Opc.Ua.Telemetry", _opcType.ToString()), args.ExceptionObject as Exception, false);
        }

        private void Unobserved_TaskException(
            object sender,
            UnobservedTaskExceptionEventArgs args)
        {
            LogHelper.Error($"Unobserved Task Exception (Observed: {args.Observed})", Path.Combine("Opc.Ua.Telemetry", _opcType.ToString()), args.Exception, false);
        }
    }
}
