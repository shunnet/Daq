namespace Snet.Iot.Daq.handler
{
    /// <summary>
    /// 运行时间记录器（单位：秒）
    /// 支持开始 / 停止 / 重置 / 累加
    /// </summary>
    public class RuntimeSecondsRecorderHandler
    {
        private readonly object _lock = new();

        private DateTime? _startTime;
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
                    return _startTime.HasValue;
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
                    if (_startTime.HasValue)
                    {
                        return _totalSeconds + (DateTime.Now - _startTime.Value).TotalSeconds;
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
                if (_startTime != null)
                    return;

                _startTime = DateTime.Now;
            }
        }

        /// <summary>
        /// 停止计时（会累加时间）
        /// </summary>
        public void Stop()
        {
            lock (_lock)
            {
                if (_startTime == null)
                    return;

                _totalSeconds += (DateTime.Now - _startTime.Value).TotalSeconds;
                _startTime = null;
            }
        }

        /// <summary>
        /// 重置计时
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _startTime = null;
                _totalSeconds = 0;
            }
        }
    }
}
