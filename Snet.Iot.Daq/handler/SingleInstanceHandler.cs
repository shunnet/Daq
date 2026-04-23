using Snet.Log;
using System.IO;
using System.IO.Pipes;
using System.Windows;

namespace Snet.Iot.Daq.Handler
{
    /// <summary>
    /// 单实例管理器
    /// </summary>
    public sealed class SingleInstanceHandler : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly bool _isFirstInstance;
        private readonly string _pipeName;

        private CancellationTokenSource _cts;
        private Task _listenerTask;

        private bool _disposed;

        /// <summary>
        /// 主窗口引用（用于恢复窗口）
        /// </summary>
        private Window _mainWindow;

        /// <summary>
        /// 收到信号事件（UI线程触发）
        /// </summary>
        public event Action<string[]> SignalReceived;

        /// <summary>
        /// 构造函数
        /// </summary>
        public SingleInstanceHandler(string appName, out bool isFirstInstance)
        {
            string mutexName = $"Global\\{appName}_{Environment.UserName}";
            _pipeName = $"{appName}_{Environment.UserName}_Pipe";

            _mutex = new Mutex(true, mutexName, out _isFirstInstance);
            isFirstInstance = _isFirstInstance;

            if (_isFirstInstance)
            {
                StartPipeListener();
            }
        }

        /// <summary>
        /// 注册主窗口
        /// </summary>
        public void RegisterMainWindow(Window window)
        {
            _mainWindow = window;
        }

        /// <summary>
        /// 向首实例发送信号（由第二实例调用）
        /// </summary>
        public void SignalFirstInstance(string[] args)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                client.Connect(1000); // 最多等待1秒

                using var writer = new StreamWriter(client) { AutoFlush = true };
                writer.Write(string.Join("\0", args));
            }
            catch
            {
                // 忽略异常（目标实例可能正在关闭）
            }
        }

        /// <summary>
        /// 恢复并激活窗口
        /// </summary>
        public void BringToFront()
        {
            if (_mainWindow == null)
                return;

            _mainWindow.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    // 已激活不处理
                    if (_mainWindow.IsActive)
                        return;

                    // ❗🔥 关键修复：不要随便 Show()
                    // 只在真正不可见时才调用
                    if (!_mainWindow.IsVisible)
                    {
                        _mainWindow.ShowInTaskbar = true;
                        _mainWindow.Show();
                    }

                    // 恢复最小化
                    if (_mainWindow.WindowState == WindowState.Minimized)
                    {
                        _mainWindow.WindowState = WindowState.Normal;
                    }

                    // 🔥 只用 Focus
                    _mainWindow.Focus();
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"[SingleInstance] BringToFront异常: {ex.Message}");
                }

            }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// 启动管道监听线程
        /// </summary>
        private void StartPipeListener()
        {
            _cts = new CancellationTokenSource();

            _listenerTask = Task.Factory.StartNew(
                PipeListenerLoop,
                _cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        /// <summary>
        /// 管道监听循环
        /// </summary>
        private async Task PipeListenerLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                NamedPipeServerStream server = null;

                try
                {
                    server = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(_cts.Token);

                    using var reader = new StreamReader(server);
                    var data = await reader.ReadToEndAsync();

                    var args = data.Split('\0', StringSplitOptions.RemoveEmptyEntries);

                    // 切回UI线程触发事件
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        SignalReceived?.Invoke(args);
                        BringToFront();
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (IOException)
                {
                    // 客户端异常断开，忽略
                }
                catch (Exception ex)
                {
                    LogHelper.Error($"[SingleInstance] 管道异常: {ex.Message}");
                }
                finally
                {
                    try { server?.Dispose(); } catch { }
                }
            }
        }

        /// <summary>
        /// 释放所有资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                _cts?.Cancel();
                _listenerTask?.Wait(1000);
            }
            catch { }

            _cts?.Dispose();

            if (_isFirstInstance)
            {
                try { _mutex.ReleaseMutex(); } catch { }
            }

            _mutex.Dispose();
        }
    }
}