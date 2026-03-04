using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Server;
using Snet.Core.extend;
using Snet.Iot.Daq.opc.core;
using Snet.Iot.Daq.opc.ua.service.core.ReferenceServer;
using Snet.Model.data;
using Snet.Model.@interface;
using Snet.Utility;
using System.Collections.Concurrent;
using System.IO;

namespace Snet.Iot.Daq.opc.ua.service
{
    public class OpcUaServiceOperate : CoreUnify<OpcUaServiceOperate, OpcUaServiceData.Basics>, IOn, IOff, IRead, IWrite, IGetStatus, IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// 无参构造函数
        /// </summary>
        public OpcUaServiceOperate() : base() { }
        /// <summary>
        /// 有参构造函数
        /// </summary>
        /// <param name="basics">基础数据</param>
        public OpcUaServiceOperate(OpcUaServiceData.Basics basics) : base(basics) { }

        /// <summary>
        /// OPCUA 安装、配置、运行
        /// </summary>
        private ApplicationInstance AI { get; set; }

        /// <summary>
        /// opcua服务
        /// </summary>
        private ReferenceServer service;

        /// <summary>
        /// 遥测
        /// </summary>
        private UaTelemetry Telemetry { get; set; } = new UaTelemetry(UaTelemetry.OpcType.Service);

        /// <summary>
        /// 最后活动时间
        /// </summary>
        private DateTime LastEventTime;

        /// <summary>
        /// 全局生命周期令牌
        /// </summary>
        private CancellationTokenSource tokenSource;

        /// <summary>
        /// 是否已启动
        /// </summary>
        public bool IsStart { get; set; }

        #region 私有函数

        /// <summary>
        /// 启动线程
        /// </summary>
        private async Task StatusThread(CancellationToken token)
        {
            await Task.Run(async () =>
            {
                while (service != null)
                {
                    if (DateTime.UtcNow - LastEventTime > TimeSpan.FromMilliseconds(5000))
                    {
                        IList<ISession> sessions = service.CurrentInstance.SessionManager.GetSessions();
                        for (int ii = 0; ii < sessions.Count; ii++)
                        {
                            ISession session = sessions[ii];
                            PrintSessionStatus(session, "心跳包", true);
                        }
                        LastEventTime = DateTime.UtcNow;
                    }
                    await Task.Delay(1000, token).ConfigureAwait(false);
                }
            }, token);
        }

        /// <summary>
        /// 会话状态
        /// </summary>
        private void SessionManager_Session(ISession session, SessionEventReason reason)
        {
            LastEventTime = DateTime.UtcNow;
            PrintSessionStatus(session, reason.ToString());
        }

        /// <summary>
        /// 订阅状态
        /// </summary>
        private void SubscriptionManager_Subscription(ISubscription subscription, bool deleted)
        {
            LastEventTime = DateTime.UtcNow;
            PrintSessionStatus(subscription.Session, deleted ? "取消订阅" : "订阅");
        }

        /// <summary>
        /// 打印会话状态
        /// </summary>
        /// <param name="session">会话对象</param>
        /// <param name="reason">原因</param>
        /// <param name="IsHeartbeatPacket">是不是心跳包</param>
        private void PrintSessionStatus(ISession session, string reason, bool IsHeartbeatPacket = false)
        {
            if (session == null) return;
            string ClientID = session.Id.ToString();
            string item = String.Format("[ {0} ] ( {1} ) {2}", session.SessionDiagnostics.SessionName, ClientID, reason.Equals("Created") ? "创建" : reason.Equals("Activated") ? "激活" : reason.Equals("Closing") ? "结束" : reason.Equals("Impersonating") ? "新身份激活" : reason);
            if (IsHeartbeatPacket)
            {
                item += String.Format(":{0:HH:mm:ss}", session.SessionDiagnostics.ClientLastContactTime.ToLocalTime());
            }

            if (reason.Equals("Closing"))
            {
                //当会话关闭 则释放此会话
                session.Dispose();
            }

            //事件抛出
            OnInfoEventHandler(this, new EventInfoResult(true, item));
        }

        #endregion 私有函数

        /// <summary>
        /// 文件夹信息
        /// </summary>
        ConcurrentDictionary<string, FolderState> FolderInfo = new ConcurrentDictionary<string, FolderState>();

        /// <summary>
        /// 创建文件夹
        /// </summary>
        /// <returns></returns>
        public OperateResult CreateFolder(string folderName, FolderState? fs = null)
        {
            //开始记录运行时间
            BegOperate();
            try
            {
                if (!GetStatus().GetDetails(out string? message))
                {
                    return EndOperate(false, message);
                }

                string key = folderName;
                if (fs != null)
                {
                    key = $"{fs.NodeId.Identifier}.{folderName}";
                }
                else
                {
                    key = $"{basics.AddressSpaceName}.{folderName}";
                }
                //不存在此节点，创建一个
                if (!FolderInfo.ContainsKey(key))
                {
                    FolderState folder = service.NodeManage.CreateFolder(folderName, fs);
                    if (folder == null)
                    {
                        return EndOperate(false, $"{folderName} 文件夹创建失败，原因未知");
                    }
                    FolderInfo.AddOrUpdate(folder.NodeId.Identifier.ToString(), folder, (k, v) => folder);
                    return EndOperate(true, resultData: folder);
                }
                else
                {
                    return EndOperate(false, $"文件夹创建失败，已存在此同名文件夹");
                }
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        /// <summary>
        /// 移除文件夹
        /// </summary>
        /// <param name="folderNameArray">文件夹集合</param>
        /// <returns>统一出参</returns>
        public OperateResult RemoveFolder(List<NodeId> folderNameArray)
        {
            //开始记录运行时间
            BegOperate();
            try
            {
                if (!GetStatus().GetDetails(out string? message))
                {
                    return EndOperate(false, message);
                }

                OperateResult result = service.NodeManage.RemoveFolder(folderNameArray);
                if (result.Status)
                {
                    List<string> FailMessage = new List<string>();
                    //在看外部是否存在此文件夹，有的话就移除
                    foreach (NodeId item in folderNameArray)
                    {
                        List<KeyValuePair<string, FolderState>> pair = FolderInfo.Where(c => c.Value.NodeId.ToString() == item.ToString() || c.Value.NodeId.ToString().Contains(item.ToString())).ToList();
                        foreach (var index in pair)
                        {
                            lock (FolderInfo)
                            {
                                if (!FolderInfo.TryRemove(index))
                                {
                                    FailMessage.Add($"{index.Value.NodeId.Identifier} 删除失败");
                                }
                            }
                        }
                    }
                    if (FailMessage.Count > 0)
                    {
                        return EndOperate(false, $"内部异常：{FailMessage.ToJson(true)}");
                    }
                    return EndOperate(true);
                }
                else
                {
                    return EndOperate(result);
                }
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        /// <summary>
        /// 创建地址，通过对应文件夹创建
        /// </summary>
        /// <param name="addresses">地址集合</param>
        /// <param name="folderState">文件夹对象</param>
        /// <returns>统一出参</returns>
        public OperateResult CreateAddress(List<AddressBody> addresses, FolderState? folderState = null)
        {
            //开始记录运行时间
            BegOperate();
            try
            {
                if (!GetStatus().GetDetails(out string? message))
                {
                    return EndOperate(false, message);
                }
                //创建节点
                OperateResult result = service.NodeManage.CreateAddress(addresses, folderState);
                if (result.Status)
                {
                    //把点位信息存入内存
                    List<AddressBody>? resultData = result.GetSource<List<AddressBody>>();
                    if (resultData == null)
                    {
                        return EndOperate(false, $"地址创建失败，原因未知");
                    }
                }
                else
                {
                    return EndOperate(false, result.Message);
                }
                return EndOperate(true);
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        /// <summary>
        /// 导入地址
        /// </summary>
        /// <returns></returns>
        public OperateResult IncAddress(NodeBody node, FolderState? folder = null)
        {
            //开始记录运行时间
            BegOperate();
            try
            {
                if (!GetStatus().GetDetails(out string? message))
                {
                    return EndOperate(false, message);
                }
                //创建节点
                OperateResult result = service.NodeManage.StructuralBodyCreateAddress(node, folder);
                FolderState? folderState = result.GetSource<FolderState>();
                if (folderState == null)
                {
                    return EndOperate(false, $"导入地址失败，原因未知");
                }
                FolderInfo.AddOrUpdate(folderState.NodeId.Identifier.ToString(), folderState, (k, v) => folderState);
                return EndOperate(true, resultData: folderState);
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        /// <summary>
        /// 获取已创建的地址
        /// </summary>
        /// <returns>统一出参</returns>
        public OperateResult GetAddressArray()
        {
            //开始记录运行时间
            BegOperate();
            try
            {
                if (!GetStatus().GetDetails(out string? message))
                {
                    return EndOperate(false, message);
                }
                //地址名称集合
                List<string> addresss = service.NodeManage.GetAddressArray();
                if (addresss.Count > 0)
                {
                    return EndOperate(true, "地址获取成功", addresss);
                }
                return EndOperate(false, "地址获取失败，地址尚未创建");
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }


        /// <summary>
        /// 移除地址
        /// </summary>
        /// <param name="addressNames">地址名称</param>
        /// <returns>统一出参</returns>
        public OperateResult RemoveAddress(List<AddressBody> addressNames)
        {
            //开始记录运行时间
            BegOperate();
            try
            {
                if (!IsStart)
                {
                    return EndOperate(false, "未启动");
                }
                return EndOperate(service.NodeManage.RemoveAddress(addressNames));
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        /// <inheritdoc/>
        public OperateResult On()
        {
            //开始记录运行时间
            BegOperate();
            try
            {
                if (GetStatus().GetDetails(out string? message))
                {
                    return EndOperate(false, message);
                }
                string tag = basics.Tag;
                //实例化对象
                AI = new ApplicationInstance(Telemetry)
                {
                    ApplicationName = basics.Tag,
                    ApplicationType = ApplicationType.Server,
                    ConfigSectionName = basics.Tag,
                    CertificatePasswordProvider = new CertificatePasswordProvider(basics.Password.ToCharArray())
                };

                //拼接地址
                string uri = $"{Utils.UriSchemeOpcTcp}://{basics.IpAddress}:{basics.Port}/{tag}";
                //为UA应用配置创建一个构建器
                var serverConfig = AI.Build($"urn:localhost:UA:{tag}", $"uri:opcfoundation.org:{tag}")
                    .SetOperationTimeout(120000)
                    .SetMaxStringLength(1048576)
                    .SetMaxByteStringLength(1048576)
                    .SetMaxArrayLength(65535)
                    .SetMaxMessageSize(4194304)
                    .SetMaxBufferSize(65535)
                    .SetChannelLifetime(30000)
                    .SetSecurityTokenLifetime(3600000)
                    .AsServer([uri]);

                //添加验证方式
                //serverConfig.AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256)
                //   .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.ECC_nistP256)
                //   .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.ECC_nistP384)
                //   .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.ECC_brainpoolP256r1)
                //   .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.ECC_brainpoolP384r1)
                //   .AddPolicy(MessageSecurityMode.Sign, SecurityPolicies.Basic256)
                //   .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.ECC_nistP256)
                //   .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.ECC_nistP384)
                //   .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.ECC_brainpoolP256r1)
                //   .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.ECC_brainpoolP384r1)
                //   .AddPolicy(MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256)
                //   .AddSignPolicies()
                //   .AddSignAndEncryptPolicies();

                //设置参数
                serverConfig.SetOperationLimits(new OperationLimits()
                {
                    MaxNodesPerBrowse = 2500,
                    MaxNodesPerRead = 2500,
                    MaxNodesPerWrite = 2500,
                    MaxNodesPerMethodCall = 2500,
                    MaxMonitoredItemsPerCall = 2500,
                    MaxNodesPerHistoryReadData = 1000,
                    MaxNodesPerHistoryReadEvents = 2500,
                    MaxNodesPerHistoryUpdateData = 2500,
                    MaxNodesPerHistoryUpdateEvents = 2500,
                    MaxNodesPerNodeManagement = 2500,
                    MaxNodesPerRegisterNodes = 2500,
                    MaxNodesPerTranslateBrowsePathsToNodeIds = 2500,
                });
                serverConfig.SetAvailableSamplingRates(new SamplingRateGroupCollection(new List<SamplingRateGroup>
                {
                    new SamplingRateGroup(5, 5, 20),
                    new SamplingRateGroup(100, 100, 4),
                    new SamplingRateGroup(500, 250, 2),
                    new SamplingRateGroup(1000, 500, 20),
                }));

                //设置其他参数
                serverConfig.SetMaxChannelCount(1000);
                serverConfig.SetAuditingEnabled(true);
                serverConfig.SetHttpsMutualTls(true);
                serverConfig.SetDiagnosticsEnabled(true);
                serverConfig.SetMaxSessionCount(75);
                serverConfig.SetMinSessionTimeout(1000);
                serverConfig.SetMaxSessionTimeout(3600000);
                serverConfig.SetMaxBrowseContinuationPoints(10);
                serverConfig.SetMaxQueryContinuationPoints(10);
                serverConfig.SetMaxHistoryContinuationPoints(100);
                serverConfig.SetMaxRequestAge(600000);
                serverConfig.SetMinPublishingInterval(50);
                serverConfig.SetMaxPublishingInterval(3600000);
                serverConfig.SetPublishingResolution(50);
                serverConfig.SetMaxSubscriptionLifetime(3600000);
                serverConfig.SetMaxMessageQueueSize(100);
                serverConfig.SetMaxNotificationQueueSize(100);
                serverConfig.SetMaxNotificationsPerPublish(1000);
                serverConfig.SetMinMetadataSamplingInterval(1000);
                serverConfig.SetMaxRegistrationInterval(0);
                serverConfig.SetNodeManagerSaveFile($"{tag}.Nodes.Json");
                serverConfig.SetMinSubscriptionLifetime(10000);
                serverConfig.SetMaxPublishRequestCount(20);
                serverConfig.SetMaxSubscriptionCount(100);
                serverConfig.SetMaxEventQueueSize(10000);
                serverConfig.SetDurableSubscriptionsEnabled(true);
                serverConfig.SetMaxDurableNotificationQueueSize(10000);
                serverConfig.SetMaxDurableEventQueueSize(10000);
                serverConfig.SetMaxDurableSubscriptionLifetime(10);

                var cerRoot = Data.CerPath;
                ApplicationConfiguration config = serverConfig.AddSecurityConfiguration(new CertificateIdentifierCollection(new List<CertificateIdentifier>
                {
                    new CertificateIdentifier{StoreType="Directory", StorePath=cerRoot,SubjectName=$"CN={tag}, C=US, S=Arizona, O=OPC Foundation, DC=localhost",CertificateTypeString="RsaSha256"},
                    new CertificateIdentifier{StoreType="Directory", StorePath=cerRoot,SubjectName=$"CN={tag}, C=US, S=Arizona, O=OPC Foundation, DC=localhost",CertificateTypeString="NistP256"},
                    new CertificateIdentifier{StoreType="Directory", StorePath=cerRoot,SubjectName=$"CN={tag}, C=US, S=Arizona, O=OPC Foundation, DC=localhost",CertificateTypeString="NistP384"},
                    new CertificateIdentifier{StoreType="Directory", StorePath=cerRoot,SubjectName=$"CN={tag}, C=US, S=Arizona, O=OPC Foundation, DC=localhost",CertificateTypeString="BrainpoolP256r1"},
                    new CertificateIdentifier{StoreType="Directory", StorePath=cerRoot,SubjectName=$"CN={tag}, C=US, S=Arizona, O=OPC Foundation, DC=localhost",CertificateTypeString="BrainpoolP384r1"},
                })).SetAutoAcceptUntrustedCertificates(true)
                    .SetRejectSHA1SignedCertificates(true)
                    .SetRejectUnknownRevocationStatus(true)
                    .SetMinimumCertificateKeySize(2048)
                    .SetMaxRejectedCertificates(5)
                    .SetAddAppCertToTrustedStore(false)
                    .SetSendCertificateChain(true)
                    .SetOutputFilePath(Path.Combine("logs", $"{tag}.log"))
                   .CreateAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                //设置 Nonce 长度
                config.SecurityConfiguration.NonceLength = 32;
                //添加权限
                switch (basics.AType)
                {
                    case Data.AuType.Anonymous:
                        serverConfig.AddUserTokenPolicy(UserTokenType.Anonymous);
                        break;
                    case Data.AuType.UserName:
                        if (string.IsNullOrWhiteSpace(basics.UserName) || string.IsNullOrWhiteSpace(basics.Password))
                        {
                            Off(true);
                            return EndOperate(false, "账号或密码不能为空");
                        }
                        serverConfig.AddUserTokenPolicy(UserTokenType.UserName);
                        break;
                    case Data.AuType.Certificate:
                        Off(true);
                        return EndOperate(false, "当前库服务端不支持证书认证");
                }

                //检查是否有有效的应用实例证书。
                bool haveAppCertificate = AI.CheckApplicationInstanceCertificatesAsync(true).ConfigureAwait(false).GetAwaiter().GetResult();
                if (!haveAppCertificate)
                {
                    Off(true);
                    return EndOperate(false, "应用实例证书无效");
                }

                //实例化
                service = new ReferenceServer(basics.UserName, basics.Password, basics.AType, basics.AutoCreateAddress, basics.AddressSpaceName, OnDataEventHandler);

                //启动服务
                AI.StartAsync(service).ConfigureAwait(false).GetAwaiter().GetResult();

                //打印信息
                var endpoints = AI.Server.GetEndpoints().Select(e => e.EndpointUrl).Distinct();
                foreach (var endpoint in endpoints)
                {
                    //事件抛出
                    OnInfoEventHandler(this, new EventInfoResult(true, endpoint));
                }
                if (tokenSource == null)
                {
                    tokenSource = new CancellationTokenSource();
                }

                // 启动状态线程
                _ = StatusThread(tokenSource.Token);

                //激活
                service.CurrentInstance.SessionManager.SessionActivated += SessionManager_Session;
                //关闭
                service.CurrentInstance.SessionManager.SessionClosing += SessionManager_Session;
                //创建
                service.CurrentInstance.SessionManager.SessionCreated += SessionManager_Session;
                //创建订阅
                service.CurrentInstance.SubscriptionManager.SubscriptionCreated += SubscriptionManager_Subscription;
                //删除订阅
                service.CurrentInstance.SubscriptionManager.SubscriptionDeleted += SubscriptionManager_Subscription;

                IsStart = true;
                return EndOperate(true);
            }
            catch (Exception ex)
            {
                Off(true);
                return EndOperate(false, ex.Message, exception: ex);
            }
        }
        /// <inheritdoc/>
        public OperateResult Off(bool HardClose = false)
        {
            //开始记录运行时间
            BegOperate();
            try
            {
                if (!HardClose)
                {
                    if (!GetStatus().GetDetails(out string? message))
                    {
                        return EndOperate(false, message);
                    }
                }
                if (service != null)
                {
                    FolderInfo.Clear();
                    //停止节点管理服务
                    service?.NodeManage.Dispose();
                    service?.NodeManage.DeleteAddressSpace();
                    // 停止服务并处理
                    service?.Stop();
                    service?.Dispose();
                    // 停止状态线程
                    service = null;
                }
                IsStart = false;
                return EndOperate(true);
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        public OperateResult Write(ConcurrentDictionary<string, (object value, Model.@enum.EncodingType? encodingType)> values)
        {
            //开始记录运行时间
            BegOperate();
            try
            {
                if (!GetStatus().GetDetails(out string? message))
                {
                    return EndOperate(false, message);
                }
                // 创建目标字典
                ConcurrentDictionary<string, object> targetDict = new ConcurrentDictionary<string, object>();
                // 并行遍历原始字典
                Parallel.ForEach(values, kvp =>
                {
                    targetDict.TryAdd(kvp.Key, kvp.Value); // 线程安全添加‌:ml-citation{ref="1,2" data="citationList"}
                });
                return Write(targetDict);
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }

        /// <inheritdoc/>
        public OperateResult Write(ConcurrentDictionary<string, object> Values)
        {
            //开始记录运行时间
            BegOperate();
            try
            {
                if (!GetStatus().GetDetails(out string? message))
                {
                    return EndOperate(false, message);
                }
                return EndOperate(service.NodeManage.WriteAddress(Values));
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }
        /// <inheritdoc/>
        public OperateResult Write(ConcurrentDictionary<string, WriteModel> values)
        {
            if (values == null || values.Count <= 0)
            {
                return OperateResult.CreateFailureResult("数据不能为空");
            }
            ConcurrentDictionary<string, object> param = new ConcurrentDictionary<string, object>();
            foreach (var item in values)
            {
                try
                {
                    switch (item.Value.AddressDataType)
                    {
                        case Model.@enum.DataType.Bool:
                            param.TryAdd(item.Key, Convert.ToBoolean(item.Value.Value));
                            break;
                        case Model.@enum.DataType.BoolArray:
                            param.TryAdd(item.Key, item.Value.Value.GetSource<bool[]>());
                            break;
                        case Model.@enum.DataType.String:
                            param.TryAdd(item.Key, Convert.ToString(item.Value.Value));
                            break;
                        case Model.@enum.DataType.Char:
                            param.TryAdd(item.Key, Convert.ToChar(item.Value.Value));
                            break;
                        case Model.@enum.DataType.Double:
                            param.TryAdd(item.Key, Convert.ToDouble(item.Value.Value));
                            break;
                        case Model.@enum.DataType.DoubleArray:
                            param.TryAdd(item.Key, item.Value.Value.GetSource<double[]>());
                            break;
                        case Model.@enum.DataType.Single:
                        case Model.@enum.DataType.Float:
                            param.TryAdd(item.Key, Convert.ToSingle(item.Value.Value));
                            break;
                        case Model.@enum.DataType.SingleArray:
                        case Model.@enum.DataType.FloatArray:
                            param.TryAdd(item.Key, item.Value.Value.GetSource<float[]>());
                            break;
                        case Model.@enum.DataType.Int:
                        case Model.@enum.DataType.Int32:
                            param.TryAdd(item.Key, Convert.ToInt32(item.Value.Value));
                            break;
                        case Model.@enum.DataType.IntArray:
                        case Model.@enum.DataType.Int32Array:
                            param.TryAdd(item.Key, item.Value.Value.GetSource<int[]>());
                            break;
                        case Model.@enum.DataType.Uint:
                        case Model.@enum.DataType.UInt32:
                            param.TryAdd(item.Key, Convert.ToUInt32(item.Value.Value));
                            break;
                        case Model.@enum.DataType.UintArray:
                        case Model.@enum.DataType.UInt32Array:
                            param.TryAdd(item.Key, item.Value.Value.GetSource<uint[]>());
                            break;
                        case Model.@enum.DataType.Long:
                        case Model.@enum.DataType.Int64:
                            param.TryAdd(item.Key, Convert.ToInt64(item.Value.Value));
                            break;
                        case Model.@enum.DataType.LongArray:
                        case Model.@enum.DataType.Int64Array:
                            param.TryAdd(item.Key, item.Value.Value.GetSource<long[]>());
                            break;
                        case Model.@enum.DataType.Ulong:
                        case Model.@enum.DataType.UInt64:
                            param.TryAdd(item.Key, Convert.ToUInt64(item.Value.Value));
                            break;
                        case Model.@enum.DataType.UlongArray:
                        case Model.@enum.DataType.UInt64Array:
                            param.TryAdd(item.Key, item.Value.Value.GetSource<ulong[]>());
                            break;
                        case Model.@enum.DataType.Short:
                        case Model.@enum.DataType.Int16:
                            param.TryAdd(item.Key, Convert.ToInt16(item.Value.Value));
                            break;
                        case Model.@enum.DataType.ShortArray:
                        case Model.@enum.DataType.Int16Array:
                            param.TryAdd(item.Key, item.Value.Value.GetSource<short[]>());
                            break;
                        case Model.@enum.DataType.Ushort:
                        case Model.@enum.DataType.UInt16:
                            param.TryAdd(item.Key, Convert.ToUInt16(item.Value.Value));
                            break;
                        case Model.@enum.DataType.UshortArray:
                        case Model.@enum.DataType.UInt16Array:
                            param.TryAdd(item.Key, item.Value.Value.GetSource<ushort[]>());
                            break;
                        case Model.@enum.DataType.DateTime:
                        case Model.@enum.DataType.Date:
                        case Model.@enum.DataType.Time:
                            param.TryAdd(item.Key, Convert.ToDateTime(item.Value.Value));
                            break;
                    }
                }
                catch (Exception ex)
                {
                    return OperateResult.CreateFailureResult($"{item.Key} 地址数据类型转换异常:{ex.Message}");
                }
            }
            return Write(param);
        }
        /// <inheritdoc/>
        public OperateResult Read(Address address)
        {
            //开始记录运行时间
            BegOperate();
            List<AddressDetails> Nodes = address.AddressArray;
            try
            {
                if (!GetStatus().GetDetails(out string? message))
                {
                    return EndOperate(false, message);
                }
                return EndOperate(service.NodeManage.ReadAddress(address));
            }
            catch (Exception ex)
            {
                return EndOperate(false, ex.Message, exception: ex);
            }
        }
        /// <inheritdoc/>
        public OperateResult GetStatus()
        {
            return EndOperate(IsStart, IsStart ? "已启动" : "未启动", methodName: BegOperate(), logOutput: false);
        }


        /// <inheritdoc/>
        public async Task<OperateResult> OffAsync(bool hardClose = false, CancellationToken token = default) => await Task.Run(() => Off(hardClose), token);
        /// <inheritdoc/>
        public async Task<OperateResult> OnAsync(CancellationToken token = default) => await Task.Run(() => On(), token);
        /// <inheritdoc/>
        public async Task<OperateResult> ReadAsync(Address address, CancellationToken token = default) => await Task.Run(() => Read(address), token);
        /// <inheritdoc/>
        public async Task<OperateResult> WriteAsync(ConcurrentDictionary<string, (object value, Snet.Model.@enum.EncodingType? encodingType)> values, CancellationToken token = default) => await Task.Run(() => Write(values), token);
        /// <inheritdoc/>
        public async Task<OperateResult> WriteAsync(ConcurrentDictionary<string, object> values, CancellationToken token = default) => await Task.Run(() => Write(values), token);
        /// <inheritdoc/>
        public async Task<OperateResult> WriteAsync(ConcurrentDictionary<string, WriteModel> values, CancellationToken token = default) => await Task.Run(() => Write(values), token);
        /// <inheritdoc/>
        public async Task<OperateResult> GetStatusAsync(CancellationToken token = default) => await Task.Run(() => GetStatus(), token);
        /// <inheritdoc/>
        public override void Dispose()
        {
            Off(true);
            base.Dispose();
        }
        /// <inheritdoc/>
        public override async ValueTask DisposeAsync()
        {
            await OffAsync(true);
            await base.DisposeAsync();
        }
    }
}
