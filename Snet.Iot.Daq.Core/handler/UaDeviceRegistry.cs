using System.Collections.Concurrent;

namespace Snet.Iot.Daq.Core.handler;

/// <summary>
/// 某一个 OPC UA 服务实例上“各设备是否已注入”的登记状态机。
/// <para>只做状态判断，不接触服务、插件或网络，所以可以脱离真实设备做单元测试。
/// 所有状态切换都是对不可变快照的原子替换（<see cref="ConcurrentDictionary{TKey,TValue}"/> 的 TryAdd / TryUpdate），不使用 lock。</para>
/// <para>状态：<c>InFlight</c> 注册进行中（同一设备只允许一个流程）→ <c>Registered</c> 已被服务端接管 / <c>Failed</c> 失败等待重试。
/// 每个状态都记着“是哪个 IDaq 对象”：设备重启后采集通道会换成新的 IDaq 对象，旧登记自然失效，新对象立即可以注册。</para>
/// </summary>
public sealed class UaDeviceRegistry
{
    /// <summary>失败后的首次重试间隔；连续失败按 2 的幂递增，避免确定性错误每 30 秒刷一次日志。</summary>
    public static readonly TimeSpan BaseRetry = TimeSpan.FromSeconds(30);

    /// <summary>失败重试间隔的上限。</summary>
    public static readonly TimeSpan MaxRetry = TimeSpan.FromMinutes(10);

    /// <summary><see cref="Begin"/> 的判定结果。</summary>
    public enum Decision
    {
        /// <summary>无需动作：同一对象已注册、已有流程进行中、或失败后仍在退避期。</summary>
        Skip,

        /// <summary>调用方获得了注册权，必须以 <see cref="Succeeded"/> / <see cref="Failed"/> / <see cref="Abandon"/> 之一结束。</summary>
        Attempt
    }

    /// <summary>单个设备的登记阶段。</summary>
    private enum Phase { InFlight, Registered, Failed }

    /// <summary>设备登记快照（不可变）。</summary>
    /// <param name="Identity">登记所针对的 IDaq 对象（按引用比较）。</param>
    /// <param name="Phase">当前阶段。</param>
    /// <param name="RetryAfter">失败后允许再次尝试的时刻（时钟刻度，毫秒）；永久失败为 long.MaxValue。</param>
    /// <param name="Failures">同一对象下的连续失败次数，用于退避递增。</param>
    private sealed record DeviceState(object Identity, Phase Phase, long RetryAfter, int Failures);

    /// <summary>设备键 → 登记快照。</summary>
    private readonly ConcurrentDictionary<string, DeviceState> states = new(StringComparer.Ordinal);

    /// <summary>毫秒级单调时钟；测试可注入假时钟控制退避。</summary>
    private readonly Func<long> clock;

    /// <summary>使用系统单调时钟创建登记表。</summary>
    public UaDeviceRegistry() : this(static () => Environment.TickCount64) { }

    /// <summary>使用指定时钟创建登记表（供测试）。</summary>
    /// <param name="clock">返回毫秒刻度的单调时钟。</param>
    public UaDeviceRegistry(Func<long> clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        this.clock = clock;
    }

    /// <summary>
    /// 热路径检查：该对象当前是否无需任何动作（不修改状态、不分配）。
    /// 返回 false 表示调用 <see cref="Begin"/> 可能得到 <see cref="Decision.Attempt"/>。
    /// </summary>
    /// <param name="key">设备键（插件配置 Guid）。</param>
    /// <param name="identity">设备当前的 IDaq 对象。</param>
    public bool IsSettled(string key, object identity)
    {
        if (!states.TryGetValue(key, out DeviceState? current)) return false;
        return current.Phase switch
        {
            Phase.InFlight => true,
            Phase.Registered => ReferenceEquals(current.Identity, identity),
            _ => ReferenceEquals(current.Identity, identity) && clock() < current.RetryAfter
        };
    }

    /// <summary>
    /// 申请对设备做一次注册；返回 <see cref="Decision.Attempt"/> 时已原子占位，其余调用方会得到 <see cref="Decision.Skip"/>。
    /// </summary>
    /// <param name="key">设备键（插件配置 Guid）。</param>
    /// <param name="identity">设备当前的 IDaq 对象。</param>
    public Decision Begin(string key, object identity)
    {
        while (true)
        {
            if (!states.TryGetValue(key, out DeviceState? current))
            {
                if (states.TryAdd(key, new DeviceState(identity, Phase.InFlight, 0, 0))) return Decision.Attempt;
                continue; // 输给了并发调用方：重新读取
            }

            if (current.Phase == Phase.InFlight) return Decision.Skip;
            bool sameObject = ReferenceEquals(current.Identity, identity);
            if (current.Phase == Phase.Registered && sameObject) return Decision.Skip;
            // 失败退避中：同一对象要等到期；换了新对象（设备重启）立即重试。
            if (current.Phase == Phase.Failed && sameObject && clock() < current.RetryAfter) return Decision.Skip;
            int failures = sameObject ? current.Failures : 0;
            if (states.TryUpdate(key, new DeviceState(identity, Phase.InFlight, 0, failures), current)) return Decision.Attempt;
        }
    }

    /// <summary>记录注册成功（服务端已接管该对象）；之后同一对象的调用一律 <see cref="Decision.Skip"/>。</summary>
    /// <param name="key">设备键。</param>
    /// <param name="identity">已注册的 IDaq 对象。</param>
    public void Succeeded(string key, object identity)
        => states[key] = new DeviceState(identity, Phase.Registered, 0, 0);

    /// <summary>记录注册失败，按连续失败次数计算下次重试时刻；永久失败只等设备换成新对象后再试。</summary>
    /// <param name="key">设备键。</param>
    /// <param name="identity">本次尝试的 IDaq 对象。</param>
    /// <param name="permanent">失败与时间无关（例如没有可注册的点位），不必按时间重试。</param>
    public void Failed(string key, object identity, bool permanent)
    {
        int failures = states.TryGetValue(key, out DeviceState? current) && ReferenceEquals(current.Identity, identity) ? current.Failures + 1 : 1;
        long retryAfter = permanent ? long.MaxValue : clock() + (long)NextDelay(failures).TotalMilliseconds;
        states[key] = new DeviceState(identity, Phase.Failed, retryAfter, failures);
    }

    /// <summary>放弃本次尝试（例如调用方正在停止）：撤销占位，下次调用立即重新尝试。</summary>
    /// <param name="key">设备键。</param>
    public void Abandon(string key) => states.TryRemove(key, out _);

    /// <summary>
    /// 设备停止或销毁前调用：清掉该设备的登记，并告诉调用方这个对象此前是否真的已注册到服务上。
    /// 返回 true 表示调用方必须随后向服务端取消注册。
    /// </summary>
    /// <param name="key">设备键。</param>
    /// <param name="identity">即将停止的 IDaq 对象。</param>
    public bool Release(string key, object identity)
    {
        if (!states.TryGetValue(key, out DeviceState? current)) return false;
        // 登记属于另一个（更新的）对象：不动它，也不需要取消。
        if (!ReferenceEquals(current.Identity, identity)) return false;
        if (!states.TryRemove(new KeyValuePair<string, DeviceState>(key, current))) return false;
        return current.Phase == Phase.Registered;
    }

    /// <summary>第 n 次连续失败后的等待时间：30 s、60 s、120 s…，不超过 <see cref="MaxRetry"/>。</summary>
    private static TimeSpan NextDelay(int failures)
    {
        double seconds = BaseRetry.TotalSeconds * Math.Pow(2, Math.Min(failures - 1, 16));
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxRetry.TotalSeconds));
    }
}
