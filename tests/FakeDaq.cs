using System.IO;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using Snet.Core.@abstract;
using Snet.Model.data;
using Snet.Model.@enum;

namespace Snet.Iot.Daq.Tests;

/// <summary>可观测实际设备调用和故障的 IDaq；组包与解包使用真实公共实现。</summary>
public sealed class FakeDaq : DaqAbstract<FakeDaq, FakeDaqBasics>
{
    /// <summary>通过 CoreUnify 的参数接口提供驱动名称。</summary>
    protected override string CN => "测试采集驱动";

    /// <summary>当前是否连接。</summary>
    private bool connected;

    /// <summary>当前订阅地址。</summary>
    private Address? subscription;

    /// <summary>创建独立测试设备。</summary>
    public FakeDaq(FakeDaqBasics? configuration = null) : base(configuration ?? new()) { }

    /// <summary>模拟 PLC 中的类型值。</summary>
    public ConcurrentDictionary<string, object> Values { get; } = new();

    /// <summary>实际写入调用数量。</summary>
    public int Writes { get; private set; }

    /// <summary>订阅启动次数。</summary>
    public int Subscriptions { get; private set; }

    /// <summary>打开次数。</summary>
    public int Opens { get; private set; }

    /// <summary>关闭次数。</summary>
    public int Closes { get; private set; }

    /// <summary>释放次数。</summary>
    public int Disposals { get; private set; }

    /// <summary>是否模拟设备拒绝写入。</summary>
    public bool FailWrites { get; set; }

    /// <summary>设备写入响应延迟。</summary>
    public TimeSpan WriteDelay { get; set; }

    /// <summary>最后一次写入的原始协议地址。</summary>
    public string? LastWriteAddress { get; private set; }

    /// <summary>最后一次读取的地址集合，供验证组包及输入隔离。</summary>
    public Address? LastRead { get; private set; }

    /// <summary>剩余的模拟订阅失败次数，供恢复回归使用。</summary>
    public int SubscribeFailures { get; set; }

    /// <summary>可控的设备写入完成信号，用于模拟忽略取消的底层协议。</summary>
    public TaskCompletionSource<bool>? WriteCompletion { get; set; }

    /// <summary>是否模拟底层 I/O 不响应 CancellationToken。</summary>
    public bool IgnoreWriteCancellation { get; set; }

    /// <summary>剩余的关闭异常次数，供验证其他设备仍被清理。</summary>
    public int CloseFailures { get; set; }

    /// <summary>改变网络状态而不释放实例，模拟现场断线。</summary>
    public void SetConnection(bool connected) => this.connected = connected;

    /// <inheritdoc/>
    public override Task<OperateResult> OnAsync(CancellationToken token = default)
    {
        Opens++;
        connected = true;
        return Task.FromResult(OperateResult.CreateSuccessResult("已连接"));
    }

    /// <inheritdoc/>
    public override Task<OperateResult> OffAsync(bool hardClose = false, CancellationToken token = default)
    {
        Closes++;
        if (CloseFailures-- > 0) throw new IOException("模拟设备关闭失败");
        connected = false;
        subscription = null;
        return Task.FromResult(OperateResult.CreateSuccessResult("已关闭"));
    }

    /// <inheritdoc/>
    public override Task<OperateResult> GetStatusAsync(CancellationToken token = default)
        => Task.FromResult(new OperateResult(connected, connected ? "已连接" : "未连接", 0));

    /// <inheritdoc/>
    public override Task<OperateResult> GetBaseObjectAsync(CancellationToken token = default)
        => Task.FromResult(OperateResult.CreateSuccessResult("测试设备", this));

    /// <inheritdoc/>
    public override Task<OperateResult> SubscribeAsync(Address address, CancellationToken token = default)
    {
        Subscriptions++;
        if (SubscribeFailures-- > 0) return Task.FromResult(OperateResult.CreateFailureResult("模拟订阅失败"));
        subscription = address;
        return Task.FromResult(OperateResult.CreateSuccessResult("已订阅"));
    }

    /// <inheritdoc/>
    public override Task<OperateResult> UnSubscribeAsync(Address address, CancellationToken token = default)
    {
        // 模拟真实 SubscribeOperate 对持有集合的修改，检查桥接快照能否用于重启。
        address.AddressArray.Clear();
        subscription = null;
        return Task.FromResult(OperateResult.CreateSuccessResult("已取消"));
    }

    /// <inheritdoc/>
    public override Task<OperateResult> ReadAsync(Address address, CancellationToken token = default)
    {
        LastRead = address;
        var result = new ConcurrentDictionary<string, AddressValue>();
        foreach (var point in address.AddressArray)
        {
            object? value = Values.GetValueOrDefault(point.AddressName);
            if (point.AddressExtendParam is List<BytesModel> models)
            {
                var bytes = new byte[point.Length];
                foreach (var model in models)
                    BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(model.StartBit), (short)Values[model.Address]);
                value = bytes;
            }
            result[point.AddressName] = new AddressValue(value == null ? QualityType.Exception : QualityType.Normal,
                value!, value!, value == null ? "点位不存在" : "成功", point) { Time = DateTime.UtcNow };
        }
        return Task.FromResult(OperateResult.CreateSuccessResult("读取完成", result));
    }

    /// <inheritdoc/>
    public override async Task<OperateResult> WriteAsync(
        ConcurrentDictionary<string, (object value, EncodingType? encodingType)> values,
        CancellationToken token = default)
    {
        Writes++;
        if (WriteCompletion != null)
        {
            if (IgnoreWriteCancellation) await WriteCompletion.Task;
            else await WriteCompletion.Task.WaitAsync(token);
        }
        if (WriteDelay > TimeSpan.Zero) await Task.Delay(WriteDelay, token);
        if (FailWrites) return OperateResult.CreateFailureResult("测试设备拒绝写入");
        foreach (var pair in values)
        {
            LastWriteAddress = pair.Key;
            Values[pair.Key] = pair.Value.value;
        }
        return OperateResult.CreateSuccessResult("设备写入完成");
    }

    /// <summary>通过驱动真实事件契约通知新的采样值。</summary>
    public async Task PublishAsync()
    {
        if (subscription == null) return;
        var data = await ReadAsync(subscription);
        await OnDataEventHandlerAsync(this, new EventDataResult(data.Status, data.Message ?? string.Empty, data.ResultData));
    }

    /// <summary>注入故障采样或非法载荷，验证桥接质量与最后成功值的处理。</summary>
    public Task PublishRawAsync(object data)
        => OnDataEventHandlerAsync(this, new EventDataResult(true, "测试采样", data));

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        Disposals++;
        await base.DisposeAsync();
    }
}
