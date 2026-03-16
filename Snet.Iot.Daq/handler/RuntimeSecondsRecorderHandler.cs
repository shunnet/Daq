namespace Snet.Iot.Daq.handler
{
    /// <summary>
    /// 运行时间记录器（单位：秒）
    /// 支持开始 / 停止 / 重置 / 累加
    /// 使用 Stopwatch 计时，不受系统时钟调整影响，精度更高
    /// </summary>
    public class RuntimeSecondsRecorderHandler
    {
        private readonly object _lock = new();

        private long _startTimestamp;
        private bool _isRunning;
        private double _totalSeconds;

        /// <summary>
        /// 是否正在运行
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (_lock)
                {
                    return _isRunning;
                }
            }
        }

        /// <summary>
        /// 累计运行秒数
        /// </summary>
        public double TotalSeconds
        {
            get
            {
                lock (_lock)
                {
                    if (_isRunning)
                    {
                        return _totalSeconds + System.Diagnostics.Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds;
                    }

                    return _totalSeconds;
                }
            }
        }

        /// <summary>
        /// 开始计时
        /// </summary>
        public void Start()
        {
            lock (_lock)
            {
                if (_isRunning)
                    return;

                _startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                _isRunning = true;
            }
        }

        /// <summary>
        /// 停止计时（会累加时间）
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRunning)
                    return;

                _totalSeconds += System.Diagnostics.Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds;
                _isRunning = false;
            }
        }

        /// <summary>
        /// 重置计时
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _isRunning = false;
                _totalSeconds = 0;
            }
        }
    }
}
