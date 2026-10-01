using Opc.Ua;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.mvvm;
using Snet.Iot.Daq.viewModel;
using Snet.Iot.Daq.Web.Services;
using SQLite;
using System.Reflection;
using Xunit;

namespace Snet.Iot.Daq.Tests;

/// <summary>仅验证现有功能修复，使用内存数据库及模拟消费者，不启动真实采集、网络服务或窗口。</summary>
public sealed class CodeQualityTests : IDisposable
{
    /// <summary>本测试创建且独占的临时目录，测试结束后逐一清理。</summary>
    private readonly List<string> _fixtureDirectories = new();
    /// <summary>中文和 Emoji 截断保留完整 Unicode 标量，包含省略号的结果不超出 UTF-8 预算。</summary>
    [Theory]
    [InlineData("😀abcde", 7, "😀...")]
    [InlineData("中文ABC", 6, "中...")]
    [InlineData("😀abc", 7, "😀abc")]
    [InlineData("abcdef", 2, "..")]
    public void Utf8TruncationPreservesUnicode(string input, int budget, string expected)
    {
        var actual = input.TruncateByBytes(budget);
        Assert.Equal(expected, actual);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(actual) <= budget);
    }

    /// <summary>处理器为空仍必须取消并等待旧消费者；重复异步释放等待同一任务。</summary>
    [Fact]
    public async Task WpfDisposalStopsConsumersWhenHandlerIsMissing()
    {
        var model = new ConsoleDeviceModel();
        using var source = new CancellationTokenSource();
        var consumer = Task.Run(async () =>
        {
            try { await Task.Delay(Timeout.Infinite, source.Token); }
            catch (OperationCanceledException) when (source.IsCancellationRequested) { }
        });
        Field("TokenSource").SetValue(model, source);
        Field("_dataConsumerTask").SetValue(model, consumer);
        try
        {
            var first = model.DisposeAsync().AsTask();
            var second = model.DisposeAsync().AsTask();
            Assert.Same(first, second);
            await first.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(source.IsCancellationRequested);
            Assert.True(consumer.IsCompletedSuccessfully);
            Assert.Null(Field("_dataConsumerTask").GetValue(model));
            Assert.False(model.IsRun);
        }
        finally { if (!consumer.IsCompleted) source.Cancel(); await consumer; }
    }

    /// <summary>日志始终按原顺序保留最新 2000 行，外部修改快照不能污染缓冲。</summary>
    [Fact]
    public void LogsRetainBoundedIndependentSnapshot()
    {
        var buffer = new LoggerBuffer();
        for (var index = 0; index < 2500; index++) buffer.Push(index.ToString());
        var lines = buffer.Snapshot();
        Assert.Equal(2000, lines.Count);
        Assert.Equal("500", lines[0]);
        Assert.Equal("2499", lines[^1]);
        lines.Clear();
        Assert.Equal(2000, buffer.Snapshot().Count);
        buffer.Push(new string('x', 100_000));
        Assert.InRange(buffer.Snapshot()[^1].Length, 1, 16385);
        buffer.Clear();
        Assert.Empty(buffer.Snapshot());
    }

    /// <summary>UA 映射保留命名空间与原始名称，避免错误切分包含点或 Unicode 的协议地址。</summary>
    [Theory]
    [InlineData("DB1.DBW0")]
    [InlineData("温度;设备=一")]
    public void UaNodeMappingUsesParentIdentity(string address)
    {
        var parent = new NodeId("DAQ.项目.设备", 7);
        var actual = UaForwarding.CreateAddressNodeId(parent, address);
        Assert.Equal((ushort)7, actual.NamespaceIndex);
        Assert.True(actual.TryGetValue(out string identifier));
        Assert.Equal("DAQ.项目.设备." + address, identifier);
        Assert.Throws<ArgumentException>(() => UaForwarding.CreateAddressNodeId(new NodeId(12u, 7), address));
    }

    /// <summary>地址数量包含无转发插件的点位，深层结构及错误循环不会导致堆栈溢出。</summary>
    [Fact]
    public void AddressCountIncludesUnboundDeepNodes()
    {
        var first = new ProjectDetailsTreeViewModelCore { NodeType = Core.@enum.ProjectDetailsNodeType.Address };
        var current = first;
        for (var index = 0; index < 10000; index++)
        {
            var child = new ProjectDetailsTreeViewModelCore { NodeType = Core.@enum.ProjectDetailsNodeType.Address };
            current.Children.Add(child);
            current = child;
        }
        current.Children.Add(first);
        Assert.Equal(10001, ProjectHandlerCore.CountAddressNodes(new[] { first }));
        Assert.Equal(0, ProjectHandlerCore.CountAddressNodes(null));
    }

    /// <summary>SQL 事务回滚后不得向全局字典广播已回滚的地址，成功回调在提交完成后执行。</summary>
    [Fact]
    public void InsertCallbackRunsOnlyAfterCommit()
    {
        using var db = new SQLiteConnection(":memory:");
        db.CreateTable<TestRow>();
        var callbacks = new List<int>();
        Assert.ThrowsAny<Exception>(() => ProjectHandlerCore.InsertUnique(db, new object(),
            new[] { new TestRow { Id = 1, Name = "first" }, new TestRow { Id = 1, Name = "second" } }, row => callbacks.Add(row.Id), row => row.Name));
        Assert.Empty(callbacks);
        Assert.Empty(db.Table<TestRow>());
        ProjectHandlerCore.InsertUnique(db, new object(), new[] { new TestRow { Id = 2 } },
            row => Assert.Equal(1, db.Table<TestRow>().Count()), row => row.Id);
    }

    /// <summary>并发属性包支持空值及并发读写，重复赋同一值不产生额外通知。</summary>
    [Fact]
    public async Task PropertyBagPreservesNullAndConcurrentReads()
    {
        var model = new PropertyModel();
        var notifications = 0;
        model.PropertyChanged += (_, _) => Interlocked.Increment(ref notifications);
        model.Text = "initial";
        model.Text = "initial";
        model.Text = null;
        Assert.Null(model.Text);
        Assert.Equal(2, notifications);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(index => Task.Run(() =>
        {
            for (var count = 0; count < 1000; count++) { model.Text = index.ToString(); _ = model.Text; }
        })));
        Assert.NotNull(model.Text);
    }

    /// <summary>取得运行时字段，仅为模拟消费者任务的回归测试设置内部状态。</summary>
    private static FieldInfo Field(string name) => typeof(ConsoleDeviceModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>两端的更新规则拒绝重复地址及过期身份，删除同样不能误删被复用序号的记录。</summary>
    [Fact]
    public void AddressWritesPreserveIdentityAndUniqueness()
    {
        using var db = new SQLiteConnection(":memory:");
        db.CreateTable<AddressModelCore>();
        var gate = new object();
        var first = new AddressModelCore { Address = "D0", AnotherName = "first", Length = 1 };
        var second = new AddressModelCore { Address = "D1", AnotherName = "second", Length = 1 };
        db.Insert(first);
        db.Insert(second);
        var edit = new AddressModelCore { Index = second.Index, Guid = second.Guid, Address = "D0", AnotherName = "unique", Length = 1 };
        Assert.Equal(AddressStore.UpdateResult.Duplicate, AddressStore.Update(db, gate, edit));
        Assert.Equal("D1", db.Find<AddressModelCore>(second.Index).Address);
        edit.Address = "D2";
        edit.Guid = "stale-identity";
        Assert.Equal(AddressStore.UpdateResult.Missing, AddressStore.Update(db, gate, edit));
        Assert.Empty(AddressStore.Delete(db, gate, new[] { edit }));
        Assert.Equal(2, db.Table<AddressModelCore>().Count());
        edit.Guid = second.Guid;
        edit.Length = 0;
        Assert.Equal(AddressStore.UpdateResult.Invalid, AddressStore.Update(db, gate, edit));
        edit.Length = 1;
        Assert.Equal(AddressStore.UpdateResult.Updated, AddressStore.Update(db, gate, edit));
        Assert.Single(AddressStore.Delete(db, gate, new[] { edit }));
        Assert.Single(db.Table<AddressModelCore>());
    }

    /// <summary>引用检查只匹配详情中的完整身份，忽略描述子串并兼容循环；两端共用相同判定。</summary>
    [Fact]
    public void AddressReferencesUseCurrentStructure()
    {
        var root = new ProjectTreeViewModelCore { Name = "guid-reference-is-not-a-description" };
        var child = new ProjectTreeViewModelCore();
        root.Children.Add(child);
        child.Children.Add(root);
        var detail = new ProjectDetailsTreeViewModelCore { AddressDetails = new AddressModelCore { Guid = "address-id" } };
        detail.Children.Add(detail);
        child.Details.Add(detail);
        Assert.True(ProjectHandlerCore.IsAddressReferenced(new[] { root }, "address-id"));
        Assert.False(ProjectHandlerCore.IsAddressReferenced(new[] { root }, "address"));
        Assert.False(ProjectHandlerCore.IsAddressReferenced(new[] { root }, "guid-reference"));
        Assert.False(ProjectHandlerCore.IsAddressReferenced(new[] { root }, ""));
    }

    /// <summary>无 MQ 的地址仍在订阅集内，同一地址重复绑定同一 MQ 不应产生重复消息。</summary>
    [Fact]
    public void SubscriptionIncludesAddressesWithoutMq()
    {
        var address = new AddressModelCore { Address = "D0", AnotherName = "unbound" };
        var detail = new ProjectDetailsTreeViewModelCore(address);
        var map = new[] { detail }.ToAddressMqDictionary();
        Assert.Empty(Assert.Single(map).Value);
        var mq = new PluginConfigModel { Name = "fixture", SN = "fixture" };
        detail.Children.Add(new ProjectDetailsTreeViewModelCore(mq));
        detail.Children.Add(new ProjectDetailsTreeViewModelCore(mq));
        Assert.Single(Assert.Single(new[] { detail }.ToAddressMqDictionary()).Value);
    }

    /// <summary>关闭其他菜单不改变调用方状态；再次点击调用方可以自行切换为关闭。</summary>
    [Fact]
    public void MenuCoordinationPreservesCallerToggleState()
    {
        var coordinator = new MenuCoordinator();
        var firstOpen = true;
        var secondOpen = true;
        Action first = () => firstOpen = false;
        Action second = () => secondOpen = false;
        coordinator.Register(first);
        coordinator.Register(second);
        coordinator.CloseAllOthers(first);
        Assert.True(firstOpen);
        Assert.False(secondOpen);
        firstOpen = !firstOpen;
        Assert.False(firstOpen);
        coordinator.Unregister(second);
        coordinator.CloseAllOthers();
    }

    /// <summary>下载管理器释放是终态，重复调用等待同一任务，释放后入队必须立即拒绝。</summary>
    [Fact]
    public async Task DownloadDisposalRejectsNewJobs()
    {
        var manager = new DownloadTaskManager(new LoggerBuffer(), null!, null!, null!);
        var first = manager.DisposeAsync().AsTask();
        var second = manager.DisposeAsync().AsTask();
        Assert.Same(first, second);
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => manager.EnqueueAsync(new[]
        {
            new PluginBrowseDataGridModel { PackName = "fixture", Version = "1.0.0" }
        }));
    }

    /// <summary>Web 驱动错误与恢复事件同步指示灯，仅状态翻转推送，释放后迟到事件不再更改状态。</summary>
    [Fact]
    public async Task WebDriverStatusTracksFailureAndRecovery()
    {
        var updates = 0;
        var node = new ProjectTreeViewModelCore { Name = "fixture", DaqDetails = new PluginConfigModel() };
        var runtime = new DeviceRuntime(node, () => null, _ => { }, _ => updates++, new LocalizationService(), new object());
        var info = typeof(DeviceRuntime).GetMethod("OnInfoEvent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        async Task Send(Snet.Model.data.EventInfoResult result) => await (Task)info.Invoke(runtime, new object?[] { null, result })!;
        await Send(Snet.Model.data.EventInfoResult.CreateFailureResult("fixture error"));
        Assert.True(runtime.LedRed);
        Assert.False(runtime.LedGreen);
        Assert.Equal("异常", runtime.CollectStatus);
        await Send(Snet.Model.data.EventInfoResult.CreateFailureResult("same error"));
        Assert.Equal(1, updates);
        await Send(Snet.Model.data.EventInfoResult.CreateSuccessResult("recovered"));
        Assert.True(runtime.LedGreen);
        Assert.Equal("正常", runtime.CollectStatus);
        var first = runtime.DisposeAsync().AsTask();
        Assert.Same(first, runtime.DisposeAsync().AsTask());
        await first;
        await Send(Snet.Model.data.EventInfoResult.CreateFailureResult("late"));
        Assert.False(runtime.LedRed);
        Assert.Equal("未采集", runtime.CollectStatus);
    }

    /// <summary>真实 WPF 图表在 STA 测试线程中移除曲线，不打开窗口；索引和绘图对象必须同时移除。</summary>
    [Fact]
    public async Task ChartRemovalClearsIndexAndPlot()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var plot = new ScottPlot.WPF.WpfPlot();
                using var chart = new Snet.Iot.Daq.chart.ChartOperate(new Snet.Iot.Daq.chart.ChartData.Basics
                { ChartControl = plot, RefreshTime = 0 });
                Assert.True(chart.On().Status);
                var model = new Snet.Iot.Daq.chart.ChartData.DataLoggerModel { SN = "fixture", Color = "#00BFFF" };
                Assert.True(chart.Create(model).Status);
                Assert.Single(plot.Plot.GetPlottables());
                Assert.True(chart.Remove(model.SN).Status);
                Assert.Empty(plot.Plot.GetPlottables());
                Assert.True(chart.Create(model).Status);
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    /// <summary>关闭尚未连接设备的 WebApi 是空操作，不应为了关闭服务创建或重新连接插件。</summary>
    [Fact]
    public async Task WebApiStopDoesNotOpenDisconnectedDevice()
    {
        await using var handler = new DqaHandler(new PluginConfigModel { Name = "fixture-missing-plugin" });
        Assert.True((await handler.WAOffAsync("fixture")).Status);
    }

    /// <summary>未绑定 MQ 的地址取消订阅不会创建驱动，单个和批量入口遵循相同停机语义。</summary>
    [Fact]
    public async Task UnsubscribeDoesNotOpenDisconnectedDevice()
    {
        await using var handler = new DqaHandler(new PluginConfigModel { Name = "fixture-missing-plugin" });
        var address = new AddressModelCore { Address = "D0", Length = 1 };
        Assert.True((await handler.UnSubscribeAsync("fixture", address)).Status);
        Assert.True((await handler.UnSubscribeAsync("fixture", new List<Snet.Iot.Daq.Core.@interface.IAddressModel> { address })).Status);
    }

    /// <summary>同一配置的两个设备拥有不同 MQ 连接；停止一个后另一个仍可消费，且不移除配置的全局缓存实例。</summary>
    [Fact]
    public async Task MqConnectionsAreOwnedByEachDevice()
    {
        var config = new PluginConfigModel { Name = "fixture", SN = "fixture" };
        var cached = await MqHandler.InstanceAsync(config);
        await using var first = MqHandler.CreateScoped(config);
        await using var second = MqHandler.CreateScoped(config);
        var one = DispatchProxy.Create<Snet.Model.@interface.IMq, FakeMq>();
        var two = DispatchProxy.Create<Snet.Model.@interface.IMq, FakeMq>();
        MqConnections(first)[config.Guid] = one;
        MqConnections(second)[config.Guid] = two;
        await first.DisposeAsync();
        Assert.Equal(1, ((FakeMq)one).DisposeCount);
        Assert.Equal(0, ((FakeMq)two).DisposeCount);
        Assert.True((await second.ConsumerAsync(config.Guid, "fixture")).Status);
        Assert.Same(cached, await MqHandler.InstanceAsync(config));
        await cached.DisposeAsync();
    }

    /// <summary>清理失败仍释放其他连接，重复释放等待同一任务，终态禁止重新消费并创建连接。</summary>
    [Fact]
    public async Task MqDisposalContinuesAfterPluginFailure()
    {
        var handler = MqHandler.CreateScoped(new PluginConfigModel());
        var bad = DispatchProxy.Create<Snet.Model.@interface.IMq, FakeMq>();
        var good = DispatchProxy.Create<Snet.Model.@interface.IMq, FakeMq>();
        ((FakeMq)bad).FailDisposal = true;
        MqConnections(handler)["bad"] = bad;
        MqConnections(handler)["good"] = good;
        var first = handler.DisposeAsync().AsTask();
        Assert.Same(first, handler.DisposeAsync().AsTask());
        await Assert.ThrowsAsync<AggregateException>(() => first);
        Assert.Equal(1, ((FakeMq)good).DisposeCount);
        Assert.Equal(1, ((FakeMq)bad).DisposeCount);
        Assert.Empty(MqConnections(handler));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.ConsumerAsync("late", "fixture"));
    }

    /// <summary>两端配置签名忽略排序和 UI 状态，包含地址别名、MQ 参数及独立 WebApi 配置。</summary>
    [Fact]
    public void SettingsSignatureTracksBehaviorOnly()
    {
        var device = new ProjectTreeViewModelCore { Name = "fixture", DaqDetails = new PluginConfigModel() };
        var first = new AddressModelCore { Address = "D0", AnotherName = "one", Length = 1 };
        var second = new AddressModelCore { Address = "D1", AnotherName = "two", Length = 1 };
        var mq = new PluginConfigModel { Param = "before" };
        var map = new System.Collections.Concurrent.ConcurrentDictionary<Snet.Iot.Daq.Core.@interface.IAddressModel, List<PluginConfigModel>>();
        map[first] = new() { mq };
        map[second] = new();
        var signature = DeviceSettings.CreateSignature(device, map);
        map.TryRemove(first, out var bindings);
        map[first] = bindings!;
        device.IsSelected = true;
        device.DaqDetails.Time = DateTime.Now;
        Assert.Equal(signature, DeviceSettings.CreateSignature(device, map));
        first.AnotherName = "changed";
        Assert.NotEqual(signature, DeviceSettings.CreateSignature(device, map));
        first.AnotherName = "one";
        mq.Param = "after";
        Assert.NotEqual(signature, DeviceSettings.CreateSignature(device, map));
        mq.Param = "before";
        device.DaqDetails.WebApi = new Snet.Model.data.WAModel { Port = 18081 };
        Assert.NotEqual(signature, DeviceSettings.CreateSignature(device, map));
    }

    /// <summary>取得真实连接缓存，避免为了测试改变处理器的生产 API。</summary>
    private static System.Collections.Concurrent.ConcurrentDictionary<string, Snet.Model.@interface.IMq> MqConnections(MqHandler handler)
        => (System.Collections.Concurrent.ConcurrentDictionary<string, Snet.Model.@interface.IMq>)typeof(MqHandler)
            .GetField("icoMq", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(handler)!;

    /// <summary>以真实插件接口构造的最小替身，仅模拟已连接、消费及资源释放，其他调用明确失败。</summary>
    public class FakeMq : DispatchProxy
    {
        /// <summary>成功或失败的释放尝试次数，用于核查清理只发生一次。</summary>
        public int DisposeCount { get; private set; }
        /// <summary>模拟一个插件释放失败，验证处理器仍清理其他插件。</summary>
        public bool FailDisposal { get; set; }
        /// <summary>执行插件接口方法，将失败配置转换为真实的异步故障任务。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            var name = targetMethod!.Name;
            if (name.StartsWith("add_", StringComparison.Ordinal) || name.StartsWith("remove_", StringComparison.Ordinal)) return null;
            if (name == "DisposeAsync")
            {
                DisposeCount++;
                return FailDisposal ? new ValueTask(Task.FromException(new System.IO.IOException("fixture disposal failure"))) : ValueTask.CompletedTask;
            }
            if (name is "GetStatusAsync" or "ConsumeAsync")
            {
                if (DisposeCount > 0) throw new ObjectDisposedException(nameof(FakeMq));
                return Task.FromResult(Snet.Model.data.OperateResult.CreateSuccessResult(string.Empty));
            }
            throw new NotSupportedException(name);
        }
    }

    /// <summary>等待落盘时后续项目编辑不能被较旧实体快照覆盖，真实 JSON 文件必须包含后来加入的项目。</summary>
    [Fact]
    public async Task QueuedPersistenceDoesNotOverwriteNewerProjectEdits()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "daq-review-tests", System.Guid.NewGuid().ToString("N"));
        var property = typeof(WebPaths).GetProperty(nameof(WebPaths.DataDir))!;
        var original = WebPaths.DataDir;
        property.SetValue(null, path);
        try
        {
            using var db = new DbGate();
            var state = new AppStateService(db, new LoggerBuffer());
            state.ProjectDict.Add(new ProjectTreeViewModelCore { Name = "first" });
            await state.ConfigSaveGate.WaitAsync();
            try
            {
                state.NotifyEntityChanged();
                // 模拟直接项目编辑在写门内保存，比待执行的实体更新更晚。
                state.ProjectDict.Add(new ProjectTreeViewModelCore { Name = "second" });
                System.IO.Directory.CreateDirectory(WebPaths.UiConfigPath);
                await System.IO.File.WriteAllTextAsync(WebPaths.ProjectConfigPath, "manual-newer-state");
            }
            finally { state.ConfigSaveGate.Release(); }
            await state.FlushPendingChangesAsync();
            var json = await System.IO.File.ReadAllTextAsync(WebPaths.ProjectConfigPath);
            Assert.Contains("first", json);
            Assert.Contains("second", json);
        }
        finally { property.SetValue(null, original); }
    }

    /// <summary>损坏或空的密码摘要不能通过固定时间比较；长度正确的摘要才进入 PBKDF2 校验。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("AA==")]
    public void InvalidCredentialHashCannotAuthenticate(string hash)
    {
        var recordType = typeof(AuthService).GetNestedType("UserRecord", BindingFlags.NonPublic)!;
        var record = Activator.CreateInstance(recordType, new object?[] { "fixture", hash,
            Convert.ToBase64String(new byte[16]), false, 0, null, AuthService.RoleAdmin, false, null });
        var verify = typeof(AuthService).GetMethod("VerifyPassword", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.False((bool)verify.Invoke(null, new[] { "arbitrary-password", record })!);
    }

    /// <summary>UA 文件夹归属使用真实父节点，不把 AB 中的地址误认为 A 的后代；索引与节点列表同步移除。</summary>
    [Fact]
    public void UaFolderRemovalUsesActualAncestry()
    {
        var managerType = typeof(Snet.Iot.Daq.Core.opc.ua.service.core.ReferenceServer.ReferenceNodeManager);
        var manager = (Snet.Iot.Daq.Core.opc.ua.service.core.ReferenceServer.ReferenceNodeManager)
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(managerType);
        SetPrivateField(manager, "<Lock>k__BackingField", new object());
        var a = new FolderState(null) { NodeId = new NodeId("A", 7) };
        var ab = new FolderState(null) { NodeId = new NodeId("AB", 7) };
        var first = new BaseDataVariableState(a) { NodeId = new NodeId("A.D0", 7) };
        var second = new BaseDataVariableState(ab) { NodeId = new NodeId("AB.D0", 7) };
        SetPrivateField(manager, "m_staticNodes", new List<BaseDataVariableState> { first, second });
        SetPrivateField(manager, "m_dynamicNodes", new List<BaseDataVariableState>());
        SetPrivateField(manager, "m_staticIndex", new System.Collections.Concurrent.ConcurrentDictionary<string, BaseDataVariableState>
        { [first.NodeId.ToString()] = first, [second.NodeId.ToString()] = second });
        SetPrivateField(manager, "m_dynamicIndex", new System.Collections.Concurrent.ConcurrentDictionary<string, BaseDataVariableState>());
        Assert.Equal(first.NodeId, Assert.Single(manager.GetFolderAddress(new List<NodeId> { a.NodeId })));
        Assert.True(manager.RemoveNodeId(first.NodeId, null));
        Assert.Equal(second.NodeId.ToString(), Assert.Single(manager.GetAddressArray()));
        Assert.Null(manager.GetNodeId(first.NodeId.ToString()));
        Assert.Equal(second.NodeId, manager.GetNodeId(second.NodeId.ToString()));
    }

    /// <summary>测试在不启动网络服务的情况下设置 SDK 内部节点索引，保留真实节点、父子关系及公开管理方法。</summary>
    private static void SetPrivateField(object target, string name, object value)
    {
        for (Type? type = target.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is null) continue;
            field.SetValue(target, value);
            return;
        }
        throw new MissingFieldException(target.GetType().Name, name);
    }

    /// <summary>多个启动请求只产生一个采样任务，释放调用共同等待清理，终态不能再次启动。</summary>
    [Fact]
    public async Task MonitorSamplerStartAndDisposalAreSerialized()
    {
        var sampler = new MonitorSampler();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(sampler.Start)));
        var task = sampler.DisposeAsync().AsTask();
        Assert.Same(task, sampler.DisposeAsync().AsTask());
        await task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<ObjectDisposedException>(sampler.Start);
    }

    /// <summary>压缩包包含越界路径时，在创建解压目录和写出任何条目前拒绝整个包。</summary>
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("sub/../../outside.txt")]
    [InlineData("..\\outside.txt")]
    public async Task PluginArchiveRejectsEscapingPaths(string entryName)
    {
        var root = NewArchiveFixture();
        var zipPath = System.IO.Path.Combine(root, "fixture.zip");
        CreateArchive(zipPath, entryName);
        var destination = System.IO.Path.Combine(root, "extract");
        await Assert.ThrowsAsync<System.IO.InvalidDataException>(() => PluginArchive.ExtractAsync(zipPath, destination));
        Assert.False(System.IO.Directory.Exists(destination));
        Assert.False(System.IO.File.Exists(System.IO.Path.Combine(root, "outside.txt")));
    }

    /// <summary>解压遵循真实 ZIP 内容、支持子目录与中文文件名，取消后不创建目标目录。</summary>
    [Fact]
    public async Task PluginArchiveExtractsNestedFilesAndHonorsCancellation()
    {
        var root = NewArchiveFixture();
        var zipPath = System.IO.Path.Combine(root, "fixture.zip");
        CreateArchive(zipPath, "sub/中文.txt");
        var destination = System.IO.Path.Combine(root, "extract");
        await PluginArchive.ExtractAsync(zipPath, destination);
        Assert.Equal("fixture", await System.IO.File.ReadAllTextAsync(System.IO.Path.Combine(destination, "sub", "中文.txt")));
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PluginArchive.ExtractAsync(zipPath, destination + "-cancelled", source.Token));
        Assert.False(System.IO.Directory.Exists(destination + "-cancelled"));
    }

    /// <summary>超大声明长度和符号链接条目均在解压前拒绝，无需写入大文件或启动任何程序集。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PluginArchiveRejectsOversizedAndLinkEntries(bool link)
    {
        var root = NewArchiveFixture();
        var zipPath = System.IO.Path.Combine(root, "fixture.zip");
        CreateArchive(zipPath, "fixture.txt");
        var bytes = await System.IO.File.ReadAllBytesAsync(zipPath);
        var central = Enumerable.Range(0, bytes.Length - 4).First(i => bytes[i] == 0x50 && bytes[i + 1] == 0x4B && bytes[i + 2] == 1 && bytes[i + 3] == 2);
        if (link) System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(central + 38), unchecked((int)0xA0000000));
        else System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(central + 24), (uint)PluginArchive.MaxExtractBytes + 1);
        await System.IO.File.WriteAllBytesAsync(zipPath, bytes);
        var destination = System.IO.Path.Combine(root, "extract");
        await Assert.ThrowsAsync<System.IO.InvalidDataException>(() => PluginArchive.ExtractAsync(zipPath, destination));
        Assert.False(System.IO.Directory.Exists(destination));
    }

    /// <summary>创建本测试独占的临时目录；其中只保存生成的 ZIP 与文本，不接触现有插件目录。</summary>
    private string NewArchiveFixture()
    {
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "daq-review-tests", System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        _fixtureDirectories.Add(root);
        return root;
    }

    /// <summary>写入包含一个小文本条目的真实压缩包，供路径、解压与中心目录元数据边界测试使用。</summary>
    private static void CreateArchive(string path, string entryName)
    {
        using var archive = System.IO.Compression.ZipFile.Open(path, System.IO.Compression.ZipArchiveMode.Create);
        using var writer = new System.IO.StreamWriter(archive.CreateEntry(entryName).Open());
        writer.Write("fixture");
    }

    /// <summary>客户端保持管道连接时，释放必须取消正在读取的监听任务；系统互斥量始终由创建线程释放。</summary>
    [Fact]
    public async Task SingleInstanceDisposalCancelsConnectedReader()
    {
        await Task.Run(() =>
        {
            using var handler = new Snet.Iot.Daq.handler.SingleInstanceHandler("daq-review-" + System.Guid.NewGuid().ToString("N"), out var first);
            Assert.True(first);
            var pipe = (string)typeof(Snet.Iot.Daq.handler.SingleInstanceHandler).GetField("_pipeName", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(handler)!;
            using var client = new System.IO.Pipes.NamedPipeClientStream(".", pipe, System.IO.Pipes.PipeDirection.Out, System.IO.Pipes.PipeOptions.Asynchronous);
            client.Connect(3000);
            handler.Dispose();
            var listener = (Task)typeof(Snet.Iot.Daq.handler.SingleInstanceHandler).GetField("_listenerTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(handler)!;
            Assert.True(listener.IsCompletedSuccessfully);
        }).WaitAsync(TimeSpan.FromSeconds(8));
    }

    /// <summary>默认浏览处理器拥有可释放的非空身份；不发起网络请求，重复同步及异步释放均不抛出空键异常。</summary>
    [Fact]
    public async Task DefaultPluginBrowseCanBeDisposed()
    {
        var handler = new PluginBrowseHandler();
        handler.Dispose();
        await handler.DisposeAsync();
    }

    /// <summary>界面状态回调在锁外触发，失败订阅者不会遗失作业；下一订阅者仍能取消作业并等待完整退出。</summary>
    [Fact]
    public async Task DownloadSubscriberFailureDoesNotLoseOwnedJob()
    {
        await using var manager = new DownloadTaskManager(new LoggerBuffer(), null!, null!, null!);
        var notifications = 0;
        manager.JobChanged += _ => throw new InvalidOperationException("fixture subscriber");
        manager.JobChanged += job =>
        {
            var gate = typeof(DownloadTaskManager).GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
            Assert.False(Monitor.IsEntered(gate));
            Interlocked.Increment(ref notifications);
            if (job.Status == "排队") manager.StopAll();
        };
        var id = await manager.EnqueueAsync(new[] { new PluginBrowseDataGridModel { PackName = "fixture-not-downloaded", Version = "1.0.0" } });
        await manager.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal("已取消", Assert.Single(manager.Jobs).Status);
        Assert.True(notifications >= 2);
        Assert.Equal(id, Assert.Single(manager.Jobs).Id);
    }

    /// <summary>两端实体共用导入事务：既有身份回灌本机主键，新地址丢弃外来主键，冲突整批回滚。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectImportAddressesStayInSyncWithDatabase(bool wpf)
    {
        if (wpf) VerifyImport<Snet.Iot.Daq.data.AddressModel>();
        else VerifyImport<Snet.Iot.Daq.Web.Data.AddressModel>();
    }

    /// <summary>使用真实 SQLite 表映射验证相同 Core 规则适用于 WPF 和 Web 地址实体。</summary>
    private static void VerifyImport<T>() where T : class, Snet.Iot.Daq.Core.@interface.IAddressModel, new()
    {
        using var db = new SQLiteConnection(":memory:");
        db.CreateTable<T>();
        var gate = new object();
        var existing = new T { Address = "D0", AnotherName = "first", Guid = "existing", Length = 1 };
        db.Insert(existing);
        var imported = new AddressModelCore { Index = 999, Address = "D1", AnotherName = "second", Guid = "imported", Length = 1 };
        var conflict = new AddressModelCore { Address = "D0", AnotherName = "third", Guid = "conflict", Length = 1 };
        Assert.Throws<System.IO.InvalidDataException>(() => AddressStore.Import<T>(db, gate, new[] { imported, conflict }));
        Assert.Single(db.Table<T>());
        var stored = Assert.Single(AddressStore.Import<T>(db, gate, new[] { imported }));
        Assert.NotEqual(999, stored.Index);
        Assert.Equal(stored.Guid, db.Find<T>(stored.Index).Guid);
        var embedded = new AddressModelCore { Index = 998, Address = "different-source", AnotherName = "old-source", Guid = existing.Guid, Length = 1 };
        var resolved = Assert.Single(AddressStore.Import<T>(db, gate, new[] { embedded }));
        Assert.Equal(existing.Index, resolved.Index);
        Assert.Equal(existing.Address, resolved.Address);
        Assert.Equal(2, db.Table<T>().Count());
    }

    /// <summary>下载令牌在停止、创建链接与释放间受到一致保护；预取消批次不执行 CLI，所有释放共同等待同一终态。</summary>
    [Fact]
    public async Task CoreDownloadsHonorCancellationAndTerminalDisposal()
    {
        var handler = new PluginDownloadHandler(NewArchiveFixture());
        using var source = new CancellationTokenSource();
        source.Cancel();
        var jobs = Enumerable.Range(0, 12).Select(_ => handler.DownloadAsync("fixture-never-published", false, source.Token)).ToArray();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(handler.Stop)));
        var first = handler.DisposeAsync().AsTask();
        Assert.Same(first, handler.DisposeAsync().AsTask());
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(jobs, job => Assert.False(job.Result));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => handler.DownloadAsync("fixture", false));
    }

    /// <summary>SQL 查询保留关键词与排序，只返回当前页；极端页码定位最后一页，空搜索清空结果。</summary>
    [Fact]
    public void AddressQueriesFilterAndPageInDatabase()
    {
        using var db = new SQLiteConnection(":memory:");
        db.CreateTable<AddressModelCore>();
        db.InsertAll(Enumerable.Range(0, 100).Select(i => new AddressModelCore
        { Address = "D" + i, AnotherName = "point" + i, Describe = i % 2 == 0 ? "match" : "other", Time = DateTime.UnixEpoch.AddSeconds(i) }));
        var rows = AddressStore.Query<AddressModelCore>(db, new object(), "match", 2, 10, out var total, out var page);
        Assert.Equal(50, total);
        Assert.Equal(2, page);
        Assert.Equal(10, rows.Count);
        Assert.Equal("D78", rows[0].Address);
        Assert.All(rows, row => Assert.Equal("match", row.Describe));
        Assert.Equal(10, AddressStore.Query<AddressModelCore>(db, new object(), "match", int.MaxValue, 10, out _, out page).Count);
        Assert.Equal(5, page);
        Assert.Empty(AddressStore.Query<AddressModelCore>(db, new object(), "missing", 100, 10, out total, out page));
        Assert.Equal(0, total);
        Assert.Equal(1, page);
        Assert.Throws<ArgumentOutOfRangeException>(() => AddressStore.Query<AddressModelCore>(db, new object(), null, 1, 0, out _, out _));
    }

    /// <summary>日志读取截断超长行后仍能读取后续行；目录越界拒绝读取，预取消必须传播取消。</summary>
    [Fact]
    public async Task OperateLogsBoundLinesAndPropagateCancellation()
    {
        var originalDirectory = WebPaths.DataDir;
        typeof(WebPaths).GetProperty(nameof(WebPaths.DataDir))!.SetValue(null, NewArchiveFixture());
        try
        {
            var directory = System.IO.Path.Combine(WebPaths.DataDir, "logs", "2026-10-02", "operate", "fixture");
            System.IO.Directory.CreateDirectory(directory);
            var path = System.IO.Path.Combine(directory, "fixture.log");
            await System.IO.File.WriteAllTextAsync(path, "2026-10-02 12:00:00.000 | INF | " + new string('x', 100_000)
                + "\n2026-10-02 12:00:01.000 | INF | after-long-line\n");
            var lines = await OperateLogReader.GetLinesAsync("2026-10-02", "fixture");
            Assert.Equal(2, lines.Count);
            Assert.True(lines[0].Length <= 16384);
            Assert.Contains("after-long-line", lines[1]);
            Assert.Empty(await OperateLogReader.GetLinesAsync("..", "fixture"));
            using var source = new CancellationTokenSource();
            source.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OperateLogReader.GetLinesAsync("2026-10-02", "fixture", source.Token));
        }
        finally { typeof(WebPaths).GetProperty(nameof(WebPaths.DataDir))!.SetValue(null, originalDirectory); }
    }

    /// <summary>两端地址表遵循相同新增规则，空字段及零长度计失败，不提交也不通知缓存。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidAddressesCannotBeInserted(bool wpf)
    {
        if (wpf) VerifyInvalidInsert<Snet.Iot.Daq.data.AddressModel>();
        else VerifyInvalidInsert<Snet.Iot.Daq.Web.Data.AddressModel>();
    }

    /// <summary>真实 SQLite 分别映射两端实体，验证验证失败不会进入事务回调。</summary>
    private static void VerifyInvalidInsert<T>() where T : class, Snet.Iot.Daq.Core.@interface.IAddressModel, new()
    {
        using var db = new SQLiteConnection(":memory:");
        db.CreateTable<T>();
        var notified = 0;
        var result = ProjectHandlerCore.InsertUnique(db, new object(), new[]
        {
            new T { Address = "D0", AnotherName = "zero", Length = 0 },
            new T { Address = "", AnotherName = "empty", Length = 1 },
            new T { Address = "D1", AnotherName = " ", Length = 1 },
            new T { Address = "D2", AnotherName = "valid", Length = 1 }
        }, _ => notified++, row => row.Guid, row => row.Address, row => row.AnotherName);
        Assert.Equal(1, result.Success);
        Assert.Equal(3, result.Failed);
        Assert.Equal(1, notified);
        Assert.Single(db.Table<T>());
    }

    /// <summary>导入配置不能利用 ../ 或外部绝对路径写出数据目录，合法相对路径仍兼容 WPF 格式。</summary>
    [Fact]
    public void PluginConfigPathsStayInsideDataDirectory()
    {
        var outside = new PluginConfigModel { Type = Snet.Model.@enum.PluginType.Daq, ConfigPath = "../outside" };
        AppStateService.NormalizeConfigPath(outside);
        Assert.Equal(System.IO.Path.GetFullPath(WebPaths.DaqPluginConfigPath), outside.ConfigPath);
        outside.ConfigPath = System.IO.Path.GetPathRoot(WebPaths.DataDir)!;
        AppStateService.NormalizeConfigPath(outside);
        Assert.Equal(System.IO.Path.GetFullPath(WebPaths.DaqPluginConfigPath), outside.ConfigPath);
        var relative = new PluginConfigModel { ConfigPath = "config/mq", Type = Snet.Model.@enum.PluginType.Mq };
        AppStateService.NormalizeConfigPath(relative);
        Assert.Equal(System.IO.Path.GetFullPath(WebPaths.MqPluginConfigPath), relative.ConfigPath);
    }

    /// <summary>MQ 热更新只匹配设备实际绑定的插件类名，不影响使用其他 MQ 包的设备。</summary>
    [Fact]
    public async Task PluginMatchingIsSpecificToDeviceBindings()
    {
        var node = new ProjectTreeViewModelCore { Name = "fixture", DaqDetails = new PluginConfigModel { Name = "fixture-daq" } };
        var detail = new ProjectDetailsTreeViewModelCore(new AddressModelCore { Address = "D0", AnotherName = "point" });
        detail.Children.Add(new ProjectDetailsTreeViewModelCore(new PluginConfigModel { Name = "fixture-mq", SN = "fixture", Type = Snet.Model.@enum.PluginType.Mq }));
        node.Details.Add(detail);
        await using var runtime = new DeviceRuntime(node, () => null, _ => { }, _ => { }, new LocalizationService(), new object());
        var method = typeof(DeviceRuntime).GetMethod("UsesPlugin", BindingFlags.Instance | BindingFlags.NonPublic)!;
        bool Matches(Snet.Model.@enum.PluginType type, string name) => (bool)method.Invoke(runtime, new object[] { type, new HashSet<string> { name } })!;
        Assert.True(Matches(Snet.Model.@enum.PluginType.Mq, "fixture-mq"));
        Assert.False(Matches(Snet.Model.@enum.PluginType.Mq, "other-mq"));
        Assert.True(Matches(Snet.Model.@enum.PluginType.Daq, "fixture-daq"));
        Assert.False(Matches(Snet.Model.@enum.PluginType.Daq, "fixture-mq"));
    }

    /// <summary>压缩完整后替换已有文件；压缩途中取消必须保留旧文件并清理独占暂存文件。</summary>
    [Fact]
    public async Task ZipCreationCommitsOnlyCompleteArchive()
    {
        var root = NewArchiveFixture();
        try
        {
            var directory = System.IO.Path.Combine(root, "source");
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(directory, "empty"));
            var zip = System.IO.Path.Combine(root, "source.zip");
            await System.IO.File.WriteAllTextAsync(zip, "previous-valid-output");
            var bytes = new byte[8 * 1024 * 1024];
            new Random(42).NextBytes(bytes);
            await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(directory, "fixture.bin"), bytes);
            using (var source = new CancellationTokenSource())
            {
                source.CancelAfter(TimeSpan.FromMilliseconds(5));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => PluginArchive.CreateAsync(directory, zip, source.Token));
            }
            Assert.Equal("previous-valid-output", await System.IO.File.ReadAllTextAsync(zip));
            Assert.Empty(System.IO.Directory.EnumerateFiles(root, "*.tmp-*"));
            await PluginArchive.CreateAsync(directory, zip);
            using var archive = System.IO.Compression.ZipFile.OpenRead(zip);
            Assert.NotNull(archive.GetEntry("empty/"));
            using var input = archive.GetEntry("fixture.bin")!.Open();
            using var contents = new System.IO.MemoryStream();
            await input.CopyToAsync(contents);
            Assert.Equal(bytes, contents.ToArray());
        }
        finally { System.IO.Directory.Delete(root, true); }
    }

    /// <summary>消息通知失败不破坏其他订阅者；并发释放等待全部任务，释放后拒绝添加消息。</summary>
    [Fact]
    public async Task ToastDisposalWaitsForOwnedTasks()
    {
        var service = new Snet.Iot.Daq.Web.Components.Shared.ToastService();
        var notified = 0;
        service.OnChanged += () => throw new InvalidOperationException("fixture subscriber");
        service.OnChanged += () => notified++;
        service.Show("fixture", durationMs: 60_000);
        Assert.Equal(1, notified);
        var first = service.DisposeAsync().AsTask();
        Assert.Same(first, service.DisposeAsync().AsTask());
        await first.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Empty(service.Items);
        Assert.Throws<ObjectDisposedException>(() => service.Show("after disposal"));
    }

    /// <summary>点号包名不得映射到插件根目录；拒绝输入前后已有文件完整保留，不执行 CLI。</summary>
    [Fact]
    public async Task InvalidPackageCannotDeletePluginStorage()
    {
        var root = NewArchiveFixture();
        var marker = System.IO.Path.Combine(root, "installed-marker.txt");
        await System.IO.File.WriteAllTextAsync(marker, "existing-installed-plugin");
        await using var handler = new PluginDownloadHandler(root);
        Assert.False(await handler.DownloadAsync(".", false));
        Assert.Equal("existing-installed-plugin", await System.IO.File.ReadAllTextAsync(marker));
    }

    /// <summary>仅删除本测试创建的 GUID 临时目录；所有异步工作已由测试等待完成。</summary>
    public void Dispose()
    {
        var parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "daq-review-tests"));
        foreach (var directory in _fixtureDirectories)
        {
            var resolved = System.IO.Path.GetFullPath(directory);
            if (!string.Equals(System.IO.Path.GetDirectoryName(resolved), parent, StringComparison.OrdinalIgnoreCase)
                || !System.Guid.TryParseExact(System.IO.Path.GetFileName(resolved), "N", out _))
                throw new InvalidOperationException("测试目录越界，拒绝清理");
            if (System.IO.Directory.Exists(resolved)) System.IO.Directory.Delete(resolved, true);
        }
    }

    /// <summary>具有真实主键约束的内存数据库实体。</summary>
    public sealed class TestRow
    {
        /// <summary>模拟不可重复的数据库主键。</summary>
        [PrimaryKey] public int Id { get; set; }
        /// <summary>用于查重的业务标识，与主键约束独立。</summary>
        public string Name { get; set; } = "";
    }

    /// <summary>使用真实属性包访问路径的测试模型。</summary>
    private sealed class PropertyModel : BindNotify
    {
        /// <summary>允许空值的属性，覆盖缺省、赋值与并发读取路径。</summary>
        public string? Text { get => GetProperty(() => Text); set => SetProperty(() => Text, value); }
    }
}
