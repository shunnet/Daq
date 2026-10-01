using Snet.Log;
using System.IO;
using System.IO.Pipes;
using System.Windows;

namespace Snet.Iot.Daq.handler
{
    /// <summary>
    /// 单实例管理器
    /// </summary>
    public sealed class SingleInstanceHandler : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly bool _isFirstInstance;
        private readonly string _pipeName;

        private CancellationTokenSource? _cts;
        private Task? _listenerTask;

        private int _disposed;

        /// <summary>
        /// 主窗口引用（用于恢复窗口）
        /// </summary>
        private Window? _mainWindow;

        /// <summary>
        /// 收到信号事件（UI线程触发）
        /// </summary>
        public event Action<string[]>? SignalReceived;

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

            // Task.Factory.StartNew 传入 async 方法会产生 Task<Task>，内层 Task 被丢弃。
            // 使用 Task.Run 可正确展开异步方法返回的 Task。
            _listenerTask = Task.Run(() => PipeListenerLoop(_cts.Token));
        }

        /// <summary>监听当前用户的唤醒信号；每个客户端最多占用五秒、发送 64 KiB 字符，停机可取消连接及读取。</summary>
        /// <param name="token">管理器拥有的停止令牌。</param>
        /// <returns>监听循环的完整生命周期任务；不等待 UI 回调，不依赖 UI 线程继续执行。</returns>
        private async Task PipeListenerLoop(CancellationToken token)
        {
            var buffer = new char[4096];
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    using var reader = new StreamReader(server);
                    var text = new System.Text.StringBuilder();
                    int count;
                    while ((count = await reader.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false)) != 0)
                    {
                        if (text.Length + count > 64 * 1024)
                            throw new InvalidDataException("单实例唤醒信号超过大小限制");
                        text.Append(buffer, 0, count);
                    }
                    var args = text.ToString().Split('\0', StringSplitOptions.RemoveEmptyEntries);
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        if (Volatile.Read(ref _disposed) != 0) return;
                        SignalReceived?.Invoke(args);
                        BringToFront();
                    });
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (OperationCanceledException) { /* 单个客户端超时，继续接收其他信号。 */ }
                catch (IOException) { /* 客户端异常断开或信号超限，继续监听。 */ }
                catch (Exception ex)
                {
                    LogHelper.Error($"[SingleInstance] 管道异常: {ex.Message}");
                    // 创建管道失败时避免无等待循环占满处理器。
                    try { await Task.Delay(100, token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                }
            }
        }

        /// <summary>取消并等待监听退出后释放资源；必须由创建实例的线程调用，以保持系统互斥量的线程归属。</summary>
        /// <remarks>监听采用可取消的异步 I/O 且只投递 UI 回调，因此此同步兼容入口不等待 UI 工作。</remarks>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try
            {
                _cts?.Cancel();
                _listenerTask?.GetAwaiter().GetResult();
            }
            finally
            {
                _cts?.Dispose();
                if (_isFirstInstance) _mutex.ReleaseMutex();
                _mutex.Dispose();
                GC.SuppressFinalize(this);
            }
        }
    }
}
