using Snet.Core.extend;
using Snet.Core.handler;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@interface;
using Snet.Model.data;
using Snet.Model.@event;
using Snet.Model.@interface;
using Snet.Utility;
using System.Collections.Concurrent;

namespace Snet.Iot.Daq.Core.handler
{
    /// <summary>
    /// 数据采集处理器<br/>
    /// 管理 IDaq 实例的生命周期，执行读取、写入、订阅、WebAPI 等数据采集操作。
    /// 每个设备通过 guid 唯一标识，内部使用 ConcurrentDictionary 缓存已打开的实例。
    /// </summary>
    public class DqaHandler : CoreUnify<DqaHandler, PluginConfigModel>, IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// 有参构造函数
        /// </summary>
        /// <param name="basics">插件配置基础数据，包含设备类型、连接参数等</param>
        public DqaHandler(PluginConfigModel basics) : base(basics) { }

        /// <summary>
        /// 已打开的 DAQ 实例缓存<br/>
        /// Key = 设备 guid，Value = 对应的 IDaq 实例
        /// </summary>
        private readonly ConcurrentDictionary<string, IDaq> icoDaq = new();
        /// <summary>释放为终态；禁止处理器在释放完成后再次创建连接。</summary>
        private int disposed;
        /// <summary>仅用于发布唯一释放任务；插件调用与异步等待均在锁外执行。</summary>
        private readonly object disposalGate = new();
        private Task? disposalTask;

        /// <summary>
        /// 每个 guid 对应的数据事件委托缓存<br/>
        /// 修复：原先使用单个共享字段，多设备场景下会导致事件泄漏和委托覆盖
        /// </summary>
        private readonly ConcurrentDictionary<string, EventHandlerAsync<EventDataResult>> _dataHandlers = new();

        /// <summary>
        /// 每个 guid 对应的信息事件委托缓存<br/>
        /// 修复：原先使用单个共享字段，多设备场景下会导致事件泄漏和委托覆盖
        /// </summary>
        private readonly ConcurrentDictionary<string, EventHandlerAsync<EventInfoResult>> _infoHandlers = new();

        /// <summary>
        /// 每个 guid 组包成功后的地址集合缓存<br/>
        /// 取消订阅时需用同一组包结果匹配，避免按原始地址取消失败导致订阅项残留
        /// </summary>
        private readonly ConcurrentDictionary<string, Address> _packedAdd = new();

        /// <summary>同步兼容入口，等待同一释放任务结束。UI 线程应使用 DisposeAsync，避免阻塞需要该线程完成的插件操作。</summary>
        public override void Dispose() => GetDisposalTask().GetAwaiter().GetResult();

        /// <summary>终止连接创建，等待当前打开操作退出，退订事件并释放全部连接；重复调用等待同一任务。</summary>
        /// <returns>所有连接和基类缓存清理完成后的任务；插件异常汇总为 AggregateException。</returns>
        public override ValueTask DisposeAsync() => new(GetDisposalTask());

        /// <summary>先发布唯一任务和终态，再在锁外执行清理，避免同步回调重入时重复释放。</summary>
        private Task GetDisposalTask()
        {
            TaskCompletionSource completion;
            lock (disposalGate)
            {
                if (disposalTask is not null) return disposalTask;
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposalTask = completion.Task;
                Volatile.Write(ref disposed, 1);
            }
            _ = CompleteDisposalAsync(completion);
            return completion.Task;
        }

        /// <summary>由公开释放任务拥有的清理流程；一个插件失败仍继续释放其他连接，并最终报告汇总异常。</summary>
        /// <param name="completion">将成功或失败传递给全部释放调用方的完成源。</param>
        private async Task CompleteDisposalAsync(TaskCompletionSource completion)
        {
            try
            {
                await _openGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    var errors = new List<Exception>();
                    foreach (var item in icoDaq.ToArray())
                    {
                        if (!icoDaq.TryRemove(item.Key, out var connection)) continue;
                        try
                        {
                            if (_dataHandlers.TryRemove(item.Key, out var dataHandler)) connection.OnDataEventAsync -= dataHandler;
                            if (_infoHandlers.TryRemove(item.Key, out var infoHandler)) connection.OnInfoEventAsync -= infoHandler;
                        }
                        catch (Exception ex) { errors.Add(ex); }
                        try { await connection.DisposeAsync().ConfigureAwait(false); }
                        catch (Exception ex) { errors.Add(ex); }
                    }
                    _dataHandlers.Clear();
                    _infoHandlers.Clear();
                    _packedAdd.Clear();
                    try { await base.DisposeAsync().ConfigureAwait(false); }
                    catch (Exception ex) { errors.Add(ex); }
                    if (errors.Count > 0) throw new AggregateException("插件连接释放失败", errors);
                }
                finally { _openGate.Release(); }
                completion.SetResult();
            }
            catch (Exception ex) { completion.SetException(ex); }
        }

        /// <summary>
        /// 数据事件回调：将底层 IDaq 的数据事件转发到上层
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">数据事件结果</param>
        /// <param name="guid">触发事件的设备 guid</param>
        private async Task Operate_OnDataEventAsync(object? sender, EventDataResult e, string guid)
        {
            await OnDataEventHandlerAsync(guid, e);
        }

        /// <summary>
        /// 信息事件回调：将底层 IDaq 的信息事件转发到上层
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">信息事件结果</param>
        /// <param name="guid">触发事件的设备 guid</param>
        private async Task Operate_OnInfoEventAsync(object? sender, EventInfoResult e, string guid)
        {
            await OnInfoEventHandlerAsync(guid, e);
        }

        /// <summary>连接创建与释放共用的异步门，不能在处理器释放后留下迟到的新连接。</summary>
        private readonly SemaphoreSlim _openGate = new(1, 1);

        /// <summary>
        /// 打开或获取指定 guid 的 DAQ 实例<br/>
        /// 1. 若缓存中不存在则通过插件工厂创建新实例<br/>
        /// 2. 检查连接状态，未连接时注册事件并执行打开操作<br/>
        /// 3. 使用 per-guid 字典管理事件委托，确保多设备场景下事件正确注册和注销
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <returns>DAQ 实例和操作结果的元组</returns>
        private async Task<(IDaq operate, OperateResult result)> OpenAsync(string guid)
        {
            await _openGate.WaitAsync();
            try
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
                if (!icoDaq.TryGetValue(guid, out IDaq? operate))
                {
                    IDaq? newOperate = await basics.CreateNewObjectAsync<IDaq>();
                    if (newOperate is null)
                        return (default!, OperateResult.CreateFailureResult("插件尚未加载".GetLanguageValue(Core.LanguageOperate)));
                    if (Volatile.Read(ref disposed) != 0)
                    {
                        await newOperate.DisposeAsync();
                        throw new ObjectDisposedException(GetType().Name);
                    }
                    operate = icoDaq.GetOrAdd(guid, newOperate!);
                    // 若竞态导致当前实例未被采用，释放多余实例
                    if (!ReferenceEquals(operate, newOperate) && newOperate != null)
                    {
                        await newOperate.DisposeAsync();
                    }
                }

                if (operate == null)
                {
                    return (default, OperateResult.CreateFailureResult("插件尚未加载".GetLanguageValue(Core.LanguageOperate)));
                }

                // 获取驱动状态
                OperateResult result = await operate.GetStatusAsync();

                // 未连接时执行事件注册和打开操作
                if (!result.Status)
                {
                    // 注销该 guid 旧的数据事件处理程序，避免重复订阅
                    if (_dataHandlers.TryRemove(guid, out var oldDataHandler))
                        operate.OnDataEventAsync -= oldDataHandler;

                    // 创建并缓存该 guid 专属的数据事件委托
                    EventHandlerAsync<EventDataResult> newDataHandler = async (sender, e) => await Operate_OnDataEventAsync(sender, e, guid);
                    _dataHandlers[guid] = newDataHandler;
                    operate.OnDataEventAsync += newDataHandler;

                    // 注销该 guid 旧的信息事件处理程序，避免重复订阅
                    if (_infoHandlers.TryRemove(guid, out var oldInfoHandler))
                        operate.OnInfoEventAsync -= oldInfoHandler;

                    // 创建并缓存该 guid 专属的信息事件委托
                    EventHandlerAsync<EventInfoResult> newInfoHandler = async (sender, e) => await Operate_OnInfoEventAsync(sender, e, guid);
                    _infoHandlers[guid] = newInfoHandler;
                    operate.OnInfoEventAsync += newInfoHandler;

                    // 执行打开操作
                    result = await operate.OnAsync();
                }
                return (operate, result);
            }
            finally { _openGate.Release(); }
        }

        /// <summary>
        /// 打开 WebAPI 服务<br/>
        /// 先确保设备已连接，再启动 WebAPI 功能
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <param name="model">WebAPI 配置参数</param>
        /// <returns>操作结果，包含成功/失败状态及消息</returns>
        public async Task<OperateResult> WAOnAsync(string guid, WAModel model)
        {
            //打开
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            return await open.operate.WAOnAsync(model);
        }

        /// <summary>
        /// 查询 WebAPI 服务状态
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <returns>操作结果，包含 WebAPI 运行状态信息</returns>
        public async Task<OperateResult> WAStatusAsync(string guid)
        {
            //打开
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            return await open.operate.WAStatusAsync();
        }

        /// <summary>
        /// 关闭 WebAPI 服务
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <returns>操作结果，包含关闭状态信息</returns>
        public async Task<OperateResult> WAOffAsync(string guid)
        {
            // 停止只作用于已存在的连接，不能为关闭 WebApi 再创建或重连设备。
            return icoDaq.TryGetValue(guid, out var operate)
                ? await operate.WAOffAsync()
                : OperateResult.CreateSuccessResult(string.Empty);
        }

        /// <summary>
        /// 获取 WebAPI 请求示例<br/>
        /// 返回当前设备支持的 WebAPI 请求格式示例
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <returns>操作结果，ResultData 中包含请求示例</returns>
        public async Task<OperateResult> WARequestExampleAsync(string guid)
        {
            //打开
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            return await open.operate.WARequestExampleAsync();
        }

        /// <summary>
        /// 读取单个地址数据<br/>
        /// 将 AddressModel 转换为底层 Address 后执行读取
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <param name="address">待读取的地址模型</param>
        /// <returns>操作结果，ResultData 中包含读取到的数据</returns>
        public async Task<OperateResult> ReadAsync(string guid, IAddressModel address)
        {
            //打开
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            //读取数据
            return await open.operate.ReadAsync(address.AddressConvert());
        }

        /// <summary>
        /// 批量读取多个地址数据<br/>
        /// 将 AddressModel 集合转换为底层 Address 后执行批量读取
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <param name="address">待读取的地址模型集合</param>
        /// <returns>操作结果，ResultData 中包含批量读取到的数据</returns>
        public async Task<OperateResult> ReadAsync(string guid, List<IAddressModel> address)
        {
            //打开
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            //状态
            if (!open.result.Status)
            {
                return open.result;
            }

            //读取数据
            return await open.operate.ReadAsync(address.AddressConvert());
        }

        /// <summary>
        /// 向指定地址写入数据<br/>
        /// 将单个地址和写入值封装为字典后执行写入操作
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <param name="address">目标地址模型</param>
        /// <param name="write">待写入的数据模型</param>
        /// <returns>操作结果，包含写入成功/失败状态</returns>
        public async Task<OperateResult> WriteAsync(string guid, IAddressModel address, WriteModel write)
        {
            // 打开或获取设备实例
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            // 设备未就绪，直接返回失败结果
            if (!open.result.Status)
            {
                return open.result;
            }

            // 组织写入数据：以地址字符串为 Key，写入模型为 Value
            var keys = new ConcurrentDictionary<string, WriteModel> { [address.Address] = write };

            // 执行写入操作
            return await open.operate.WriteAsync(keys);
        }


        /// <summary>
        /// 订阅单个地址的数据变化<br/>
        /// 订阅后设备将持续推送该地址的数据变更事件
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <param name="address">待订阅的地址模型</param>
        /// <returns>操作结果，包含订阅成功/失败状态</returns>
        public async Task<OperateResult> SubscribeAsync(string guid, IAddressModel address)
        {
            // 打开或获取设备实例
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            if (!open.result.Status)
            {
                return open.result;
            }

            // 订阅地址
            return await open.operate.SubscribeAsync(address.AddressConvert());
        }

        /// <summary>
        /// 批量订阅多个地址的数据变化（含自动组包）
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <param name="address">待订阅的地址模型集合</param>
        /// <param name="autoPack">自动组包参数；null 表示不组包</param>
        /// <returns>操作结果，包含批量订阅成功/失败状态</returns>
        public async Task<OperateResult> SubscribeAsync(string guid, List<IAddressModel> address, AddressAutoPackModel? autoPack = null)
        {
            // 打开或获取设备实例
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            if (!open.result.Status)
            {
                return open.result;
            }

            // 执行组包功能；组包成功后订阅组包后的地址（取消订阅时须用同一组包结果，否则订阅项残留）
            Address add = address.AddressConvert();
            if (autoPack != null)
            {
                string[] keys = PackerHandler.GetSupportAutoPackDeviceTypes();
                string? key = keys.FirstOrDefault(k => open.operate.GetBasicsArgs().ToJson().Contains(k));
                if (!string.IsNullOrWhiteSpace(key))   //支持组包
                {
                    OperateResult result = await open.operate.PackerAsync(add, key, autoPack.MaxByteLength, autoPack.Format);
                    if (result.Status)
                    {
                        Address? addPack = result.GetSource<Address>();
                        if (addPack != null)
                        {
                            // 缓存组包结果，供取消订阅匹配
                            _packedAdd[guid] = addPack;
                            return await open.operate.SubscribeAsync(addPack);
                        }
                        return OperateResult.CreateFailureResult("组包结果为空".GetLanguageValue(Core.LanguageOperate));
                    }
                    // 组包失败：返回明确错误（不静默降级为未组包订阅）
                    return OperateResult.CreateFailureResult("地址自动组包失败：".GetLanguageValue(Core.LanguageOperate) + result.Message);
                }
                return OperateResult.CreateFailureResult("此驱动目前不支持地址自动组包".GetLanguageValue(Core.LanguageOperate));
            }
            // 批量订阅地址（未组包）
            return await open.operate.SubscribeAsync(add);
        }

        /// <summary>
        /// 取消订阅单个地址的数据变化
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <param name="address">待取消订阅的地址模型</param>
        /// <returns>操作结果，包含取消订阅成功/失败状态</returns>
        public async Task<OperateResult> UnSubscribeAsync(string guid, IAddressModel address)
        {
            // 取消订阅仅操作已有驱动；停机流程不能为了取消订阅重新创建或连接设备。
            return icoDaq.TryGetValue(guid, out var operate)
                ? await operate.UnSubscribeAsync(GetUnsubscribeAddress(guid, address.AddressConvert()))
                : OperateResult.CreateSuccessResult(string.Empty);
        }

        /// <summary>
        /// 批量取消订阅多个地址的数据变化
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <param name="address">待取消订阅的地址模型集合</param>
        /// <returns>操作结果，包含批量取消订阅成功/失败状态</returns>
        public async Task<OperateResult> UnSubscribeAsync(string guid, List<IAddressModel> address)
        {
            // 取消订阅仅操作已有驱动；停机流程不能为了取消订阅重新创建或连接设备。
            return icoDaq.TryGetValue(guid, out var operate)
                ? await operate.UnSubscribeAsync(GetUnsubscribeAddress(guid, address.AddressConvert()))
                : OperateResult.CreateSuccessResult(string.Empty);
        }

        /// <summary>
        /// 获取用于取消订阅的地址集合<br/>
        /// 若该设备此前组包成功，使用缓存的组包结果；否则使用原始地址
        /// </summary>
        private Address GetUnsubscribeAddress(string guid, Address original)
        {
            if (_packedAdd.TryGetValue(guid, out var packed))
                return packed;
            return original;
        }

        /// <summary>
        /// 获取指定设备的连接状态
        /// </summary>
        /// <param name="guid">设备唯一标识符</param>
        /// <returns>操作结果，包含当前设备连接状态信息</returns>
        public async Task<OperateResult> GetStatusAsync(string guid)
        {
            // 打开或获取设备实例
            (IDaq operate, OperateResult result) open = await OpenAsync(guid);

            if (!open.result.Status)
            {
                return open.result;
            }

            // 查询设备状态
            return await open.operate.GetStatusAsync();
        }

    }
}
