using CommunityToolkit.Mvvm.Input;
using Opc.Ua;
using Snet.Core.handler;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.@interface;
using Snet.Iot.Daq.Core.mvvm;
using Snet.Iot.Daq.Core.opc.ua.service;
using Snet.Iot.Daq.data;
using Snet.Model.data;
using Snet.Model.@enum;
using Snet.Utility;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Channels;

namespace Snet.Iot.Daq.viewModel
{
    /// <summary>
    /// ����̨�豸��ͼģ�ͣ����𵥸��ɼ��豸�����п��ơ����ݶ�д�����Ĺ�����OPC UA ��ַ�ռ�ͬ���Լ� MQ ����ת����
    /// </summary>
    public class ConsoleDeviceModel : BindNotify, IDisposable, IAsyncDisposable
    {
        #region ���캯��
        /// <summary>
        /// �޲ι��캯��
        /// </summary>
        public ConsoleDeviceModel()
        {
            StartPolling(runtime);
            Snet.Core.handler.LanguageHandler.OnLanguageEventAsync += LanguageHandler_OnLanguageEventAsync;
        }
        #endregion

        #region ����
        /// <summary>
        /// �Զ��������
        /// </summary>
        AutoPackHandler autoPack;

        /// <summary>
        /// �ֽڴ���
        /// </summary>
        private BytesHandler bytesHandler;

        /// <summary>
        /// �ⲿ�ص���Ҫ��ʾ����Ϣ
        /// </summary>
        private Func<string, Task> ShowAsync;

        /// <summary>
        /// �ⲿ�ص���Ҫ��ʾ�Ľ����Ϣ
        /// </summary>
        private Func<PluginConfigModel, BaseModel, Task> ResultAsync;

        /// <summary>
        /// �ɼ�����
        /// </summary>
        private DqaHandler daqHandler;

        /// <summary>
        /// ���봦��
        /// </summary>
        private ConcurrentDictionary<string, MqHandler> mqHandlers = new();

        /// <summary>
        /// �ֽڴ���ģ��
        /// </summary>

        private ConcurrentDictionary<string, List<BytesModel>> bytesModels = new();

        /// <summary>
        /// ����ʱ���¼
        /// </summary>
        private RuntimeSecondsRecorderHandler runtime = new();

        /// <summary>
        /// ԭʼ��ַ -> OPCUA��ʵ��ַӳ��
        /// </summary>
        private readonly ConcurrentDictionary<string, string> _addressMap = new();

        /// <summary>
        /// UAд�븴���ֵ䣨������·���������䣩
        /// </summary>
        private readonly ConcurrentDictionary<string, WriteModel> _singleWriteDict = new();

        /// <summary>
        /// ����ռ�����
        /// </summary>
        private string uaServerAddressSpaceName;

        /// <summary>
        /// opcua����㼶
        /// </summary>
        private FolderState folderState;

        /// <summary>
        /// �㼶����
        /// </summary>
        private List<FolderState> folderStates = new();

        /// <summary>
        /// ��ַ�������棨������·��ÿ���ؽ���
        /// </summary>
        private Dictionary<string, IAddressModel> _addressIndex = new();

        /// <summary>
        /// MQ���ӳ�仺�棨������·��ÿ���ؽ���
        /// </summary>
        private Dictionary<string, List<PluginConfigModel>> _mqPluginMap = new();

        /// <summary>
        /// DataType �� BuiltInType ӳ�仺��
        /// </summary>
        private static readonly Dictionary<DataType, BuiltInType> _typeMap = new()
        {
            { Model.@enum.DataType.Bool, BuiltInType.Boolean },
            { Model.@enum.DataType.Double, BuiltInType.Double },
            { Model.@enum.DataType.Float, BuiltInType.Float },
            { Model.@enum.DataType.Single, BuiltInType.Float },
            { Model.@enum.DataType.Short, BuiltInType.Int16 },
            { Model.@enum.DataType.Int16, BuiltInType.Int16 },
            { Model.@enum.DataType.Ushort, BuiltInType.UInt16 },
            { Model.@enum.DataType.UInt16, BuiltInType.UInt16 },
            { Model.@enum.DataType.Int, BuiltInType.Int32 },
            { Model.@enum.DataType.Int32, BuiltInType.Int32 },
            { Model.@enum.DataType.Uint, BuiltInType.UInt32 },
            { Model.@enum.DataType.UInt32, BuiltInType.UInt32 },
            { Model.@enum.DataType.Long, BuiltInType.Int64 },
            { Model.@enum.DataType.Int64, BuiltInType.Int64 },
            { Model.@enum.DataType.Ulong, BuiltInType.UInt64 },
            { Model.@enum.DataType.UInt64, BuiltInType.UInt64 },
            { Model.@enum.DataType.String, BuiltInType.String },
            { Model.@enum.DataType.Char, BuiltInType.String },
        };


        /// <summary>
        /// ͨ�����ã��ӳٴ�����
        /// </summary>
        private BoundedChannelOptions channel => p_Channel ??= new BoundedChannelOptions(ushort.MaxValue)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = false
        };
        private BoundedChannelOptions? p_Channel;

        /// <summary>
        /// Ua����ͬ��ͨ��
        /// </summary>
        private Channel<AddressValue> UaSyncChannel;

        /// <summary>
        /// �����¼�ͨ��
        /// </summary>
        private Channel<EventDataResult> DataSyncChannel;

        /// <summary>
        /// ȫ����Ϣȡ��֪ͨ
        /// </summary>
        private CancellationTokenSource TokenSource;

        /// <summary>
        /// �Ƿ������вɼ�
        /// </summary>
        public bool IsRun = false;

        /// <summary>
        /// �ɼ�����
        /// </summary>
        private PluginConfigModel DaqData
        {
            get => GetProperty(() => DaqData);
            set => SetProperty(() => DaqData, value);
        }

        /// <summary>
        /// �ɼ����·��
        /// </summary>
        public string DaqPluginPath { get; set; }

        /// <summary>
        /// ������·��
        /// </summary>
        public List<string> MqPluginPath { get; set; }

        /// <summary>
        /// ��ַ����
        /// </summary>
        public int AddressCount
        {
            get => GetProperty(() => AddressCount);
            set => SetProperty(() => AddressCount, value);
        }

        /// <summary>
        /// ��ַ����
        /// </summary>
        private ConcurrentDictionary<IAddressModel, List<PluginConfigModel>> AddressDatas
        {
            get => GetProperty(() => AddressDatas);
            set => SetProperty(() => AddressDatas, value);
        }

        /// <summary>
        /// ��Ŀ����
        /// </summary>
        private IProjectTreeViewModel Project
        {
            get => GetProperty(() => Project);
            set => SetProperty(() => Project, value);
        }

        /// <summary>
        /// �豸ָʾ���Ƿ���˸
        /// </summary>
        public bool DeviceStatusFlashing
        {
            get => GetProperty(() => DeviceStatusFlashing);
            set => SetProperty(() => DeviceStatusFlashing, value);
        }

        /// <summary>
        /// �豸״̬���� �����ǰ�
        /// </summary>
        public bool DeviceStatusChangLiang
        {
            get => GetProperty(() => DeviceStatusChangLiang);
            set => SetProperty(() => DeviceStatusChangLiang, value);
        }

        /// <summary>
        /// �豸����
        /// </summary>
        public string DeviceName
        {
            get => GetProperty(() => DeviceName);
            set => SetProperty(() => DeviceName, value);
        }

        /// <summary>
        /// �豸����
        /// </summary>
        public string DeviceType
        {
            get => GetProperty(() => DeviceType);
            set => SetProperty(() => DeviceType, value);
        }

        /// <summary>
        /// �豸�㼶
        /// </summary>
        public string DeviceHierarchy
        {
            get => GetProperty(() => DeviceHierarchy);
            set => SetProperty(() => DeviceHierarchy, value);
        }
        /// <summary>
        /// �豸�㼶
        /// </summary>
        public string DeviceHierarchyToolTip
        {
            get => GetProperty(() => DeviceHierarchyToolTip);
            set => SetProperty(() => DeviceHierarchyToolTip, value);
        }


        /// <summary>
        /// �ɼ�ʱ��
        /// </summary>
        public int CollectTime
        {
            get => GetProperty(() => CollectTime);
            set => SetProperty(() => CollectTime, value);
        }

        /// <summary>
        /// �ɼ�״̬
        /// </summary>
        public string CollectStatus
        {
            get => collectStatus;
            set => SetProperty(ref collectStatus, value);
        }
        private string collectStatus = LanguageHandler.GetLanguageValue("δ֪", App.LanguageOperate);

        /// <summary>
        /// ����ʱ��
        /// </summary>
        public DateTime UpdateTime
        {
            get => GetProperty(() => UpdateTime);
            set => SetProperty(() => UpdateTime, value);
        }

        /// <summary>
        /// LED��ɫ
        /// </summary>
        public System.Windows.Media.Color LedColor
        {
            get => ledColor;
            set => SetProperty(ref ledColor, value);
        }
        private System.Windows.Media.Color ledColor = System.Windows.Media.Colors.Green;
        #endregion

        #region �¼�
        /// <summary>
        /// ��Ϣ�¼�
        /// </summary>
        private async Task DqaHandler_OnInfoEventAsync(object? sender, EventInfoResult e)
        {
            //��д�������
            await ResultMsgAsync(DaqData, new ResultModel(e.Status, e.Message) { Time = e.Time });
        }

        /// <summary>
        /// �����¼�
        /// </summary>
        private async Task DqaHandler_OnDataEventAsync(object? sender, EventDataResult e)
        {
            if (DataSyncChannel is null)
                return;
            if (TokenSource is null)
                return;

            await DataSyncChannel.Writer.WriteAsync(e, TokenSource.Token);
        }

        #endregion

        #region ����

        /// <summary>
        /// webapi����
        /// </summary>
        public IAsyncRelayCommand WASatrt => waStart ??= new AsyncRelayCommand(WASatrtAsync);
        private IAsyncRelayCommand? waStart;
        private async Task WASatrtAsync()
        {
            if (daqHandler == null)
            {
                return;
            }
            if (DaqData.WebApi == null)
            {
                if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + "δ����WebApi����".GetLanguageValue(App.LanguageOperate));
                return;
            }

            if ((await daqHandler.WAStatusAsync(DaqData.Guid)).GetDetails(out string? message))
            {
                if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + message);
                return;
            }

            OperateResult result = await daqHandler.WAOnAsync(DaqData.Guid, DaqData.WebApi);
            //��д�������
            if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + (result.Status ? "WebApi�����ɹ�".GetLanguageValue(App.LanguageOperate) : "WebApi����ʧ��".GetLanguageValue(App.LanguageOperate) + "," + result.Message));
        }

        /// <summary>
        /// webapiֹͣ
        /// </summary>
        public IAsyncRelayCommand WAStop => waStop ??= new AsyncRelayCommand(WAStopAsync);
        private IAsyncRelayCommand? waStop;
        private async Task WAStopAsync()
        {
            if (daqHandler == null)
            {
                return;
            }
            if (DaqData.WebApi == null)
            {
                if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + "δ����WebApi����".GetLanguageValue(App.LanguageOperate));
                return;
            }

            if (!(await daqHandler.WAStatusAsync(DaqData.Guid)).GetDetails(out string? message))
            {
                if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + message);
                return;
            }

            OperateResult result = await daqHandler.WAOffAsync(DaqData.Guid);
            //��д�������
            if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + (result.Status ? "WebApiֹͣ�ɹ�".GetLanguageValue(App.LanguageOperate) : "WebApiֹͣʧ��".GetLanguageValue(App.LanguageOperate) + "," + result.Message));
        }

        /// <summary>
        /// webapiʾ������
        /// </summary>
        public IAsyncRelayCommand WARequestExample => waRequestExample ??= new AsyncRelayCommand(WARequestExampleAsync);
        private IAsyncRelayCommand? waRequestExample;
        private async Task WARequestExampleAsync()
        {
            if (daqHandler == null)
            {
                return;
            }
            if (DaqData.WebApi == null)
            {
                if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + "δ����WebApi����".GetLanguageValue(App.LanguageOperate));
                return;
            }
            OperateResult result = await daqHandler.WARequestExampleAsync(DaqData.Guid);
            //��д�������
            if (ShowAsync != null) await ShowAsync($"{DeviceHierarchyToolTip}\r\n" + result.ResultData.ToString());
        }

        /// <summary>
        /// �ɼ�
        /// </summary>
        public IAsyncRelayCommand Collect => collect ??= new AsyncRelayCommand(CollectAsync);
        private IAsyncRelayCommand? collect;
        private async Task CollectAsync()
        {
            if (!IsRun)
            {
                if (daqHandler == null)
                {
                    daqHandler = await DqaHandler.InstanceAsync(DaqData);
                    daqHandler.OnDataEventAsync -= DqaHandler_OnDataEventAsync;
                    daqHandler.OnInfoEventAsync -= DqaHandler_OnInfoEventAsync;
                    daqHandler.OnDataEventAsync += DqaHandler_OnDataEventAsync;
                    daqHandler.OnInfoEventAsync += DqaHandler_OnInfoEventAsync;
                }

                //���Զ��������������PLCѹ��
                string[] keys = AutoPackHandler.GetSupportAutoPackDeviceTypes();
                string? key = keys.FirstOrDefault(k => DaqData.Param.Contains(k));
                OperateResult result = OperateResult.CreateFailureResult("�ɼ�ʧ��".GetLanguageValue(App.LanguageOperate));
                if (key != null && DaqData.AutoPack != null)
                {
                    autoPack ??= AutoPackHandler.Instance(key);
                    List<IAddressModel>? models = autoPack.AddressAutoPack(AddressDatas.Keys.ToList(), key, DaqData.AutoPack.MaxByteLength, DaqData.AutoPack.Format);
                    if (models != null)
                    {
                        result = await daqHandler.SubscribeAsync(DaqData.Guid, models);
                    }
                }
                else
                {
                    result = await daqHandler.SubscribeAsync(DaqData.Guid, AddressDatas.Keys.ToList());
                }


                if (result.Status)
                {
                    if (folderStates.Count > 0)
                    {
                        GlobalConfigModel.uaService.RemoveFolder([folderStates[0].NodeId]);
                        folderStates[0].Dispose();
                        folderStates.Clear();
                        folderState.Dispose();
                        folderState = null;
                    }

                    _addressMap.Clear();

                    if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + "�����ɼ�".GetLanguageValue(App.LanguageOperate));

                    CollectStatus = LanguageHandler.GetLanguageValue("����", App.LanguageOperate);
                    DeviceStatusFlashing = true;
                    DeviceStatusChangLiang = true;
                    runtime.Start();

                    if (DaqData.WebApi != null)
                    {
                        await WASatrtAsync();
                    }

                    if (TokenSource == null)
                    {
                        TokenSource = new CancellationTokenSource();
                    }

                    if (UaSyncChannel == null)
                    {
                        UaSyncChannel = Channel.CreateBounded<AddressValue>(channel);
                        _ = UaSyncChannelDataEventAsync(TokenSource.Token);
                    }

                    if (DataSyncChannel == null)
                    {
                        DataSyncChannel = Channel.CreateBounded<EventDataResult>(channel);
                        _ = DataSyncChannelDataEventAsync(TokenSource.Token);
                    }

                    IsRun = true;
                }
                else
                {
                    DeviceStatusFlashing = false;
                    DeviceStatusChangLiang = false;
                }
                //��д�������
                await ResultMsgAsync(DaqData, result);
            }
        }

        /// <summary>
        /// ֹͣ
        /// </summary>
        public IAsyncRelayCommand Stop => stop ??= new AsyncRelayCommand(StopAsync);
        private IAsyncRelayCommand? stop;
        private async Task StopAsync()
        {
            if (daqHandler == null)
            {
                return;
            }

            // ȡ��
            if (TokenSource != null)
            {
                TokenSource.Cancel();
                TokenSource.Dispose();
                TokenSource = null;
            }

            if (DaqData.WebApi != null)
            {
                await WAStopAsync();
            }

            daqHandler?.OnDataEventAsync -= DqaHandler_OnDataEventAsync;
            daqHandler?.OnInfoEventAsync -= DqaHandler_OnInfoEventAsync;
            if (daqHandler is not null)
            {
                await daqHandler.UnSubscribeAsync(DaqData.Guid, AddressDatas.Keys.ToList());
                await daqHandler.DisposeAsync();
            }
            daqHandler = null;

            foreach (var item in mqHandlers)
            {
                await item.Value.DisposeAsync();
            }
            mqHandlers.Clear();

            CollectStatus = LanguageHandler.GetLanguageValue("ֹͣ", App.LanguageOperate);
            DeviceStatusFlashing = false;
            DeviceStatusChangLiang = false;
            IsRun = false;
            runtime.Stop();

            if (UaSyncChannel != null)
            {
                //ֹͣ
                UaSyncChannel.Writer.TryComplete();
                //��������
                while (UaSyncChannel.Reader.TryRead(out AddressValue? item)) { }
                //�ÿ�
                UaSyncChannel = null;
            }

            if (DataSyncChannel != null)
            {
                //ֹͣ
                DataSyncChannel.Writer.TryComplete();
                //��������
                while (DataSyncChannel.Reader.TryRead(out EventDataResult? item)) { }
                //�ÿ�
                DataSyncChannel = null;
            }

            if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + "ֹͣ�ɼ�".GetLanguageValue(App.LanguageOperate));

        }

        /// <summary>
        /// ����
        /// </summary>
        public IAsyncRelayCommand Retry => retry ??= new AsyncRelayCommand(RetryAsync);
        private IAsyncRelayCommand? retry;
        private async Task RetryAsync()
        {
            runtime.Reset();
            await StopAsync();
            await CollectAsync();
            if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + "����".GetLanguageValue(App.LanguageOperate));
        }

        /// <summary>
        /// �����ɼ�
        /// </summary>
        public IAsyncRelayCommand OnSoftCollect => onSoftCollect ??= new AsyncRelayCommand(OnSoftCollectAsync);
        private IAsyncRelayCommand? onSoftCollect;
        private async Task OnSoftCollectAsync()
        {
            Project.IsSoftStart = true;
            await Project.SetAsync(GlobalConfigModel.ProjectDict);
            if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + "���������ɼ��ɹ�".GetLanguageValue(App.LanguageOperate));
        }

        /// <summary>
        /// ȡ�������ɼ�
        /// </summary>
        public IAsyncRelayCommand OffSoftCollect => offSoftCollect ??= new AsyncRelayCommand(OffSoftCollectAsync);
        private IAsyncRelayCommand? offSoftCollect;
        private async Task OffSoftCollectAsync()
        {
            Project.IsSoftStart = false;
            await Project.SetAsync(GlobalConfigModel.ProjectDict);
            if (ShowAsync != null) await ShowAsync(DeviceHierarchyToolTip + ", " + "ȡ�������ɼ��ɹ�".GetLanguageValue(App.LanguageOperate));
        }
        #endregion

        #region ����
        /// <summary>
        /// ͨ�������¼�����
        /// </summary>
        private async Task UaSyncChannelDataEventAsync(CancellationToken token)
        {
            try
            {
                while (await UaSyncChannel.Reader.WaitToReadAsync(token))
                {
                    while (UaSyncChannel.Reader.TryRead(out AddressValue? addressValue))
                    {
                        if (token.IsCancellationRequested)
                            continue;

                        FolderState fs = await UaCreateFolder();
                        if (fs == null)
                        {
                            continue;
                        }

                        //����Դ
                        string addressName = addressValue.AddressName;
                        DataType dataType = addressValue.AddressDataType;
                        object? value = addressValue.ResultValue;

                        //����
                        var service = GlobalConfigModel.uaService;
                        if (service is null || !service.GetStatus().Status)
                            continue;

                        if (!_addressMap.ContainsKey(addressName))
                        {
                            if (!_typeMap.TryGetValue(dataType, out var builtInType))
                                continue;

                            if (builtInType == BuiltInType.String)
                                value ??= string.Empty;

                            //������ַ
                            var createResult = service.CreateAddress(new()
                            {
                                new()
                                {
                                    AddressName = addressName,
                                    Dynamic = false,
                                    DefaultValue = value,
                                    DataType = builtInType,
                                    AccessLevel = 3
                                }
                            }, folderState);

                            if (!createResult.Status)
                            {
                                await ShowAsync?.Invoke(createResult.Message);
                                continue;
                            }

                            // ֻ�ڴ����ɹ���ˢ��һ�ε�ַ�б�
                            var res = service.GetAddressArray().GetSource<List<string>>();
                            string format = $"s={uaServerAddressSpaceName}.{Project.GetHierarchyPath(".")}.{addressName}";
                            if (res != null)
                            {
                                foreach (var nodeId in res)
                                {
                                    if (nodeId.Contains(format, StringComparison.Ordinal))
                                    {
                                        _addressMap[addressName] = nodeId;
                                        break;
                                    }
                                }
                            }
                        }

                        // д��
                        if (!_addressMap.TryGetValue(addressName, out var realAddress))
                            continue;

                        _singleWriteDict[realAddress] = new WriteModel(value, dataType);

                        var writeResult = await service.WriteAsync(_singleWriteDict);

                        _singleWriteDict.Clear();

                        if (!writeResult.Status && ShowAsync != null)
                            await ShowAsync.Invoke(writeResult.Message);
                    }
                }
            }
            catch (TaskCanceledException)
            {
            }
            catch (ChannelClosedException ex2)
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult("[ UaSyncChannelDataEventAsync ] ͨ���ѹرգ�" + ex2.Message));
            }
            catch (OperationCanceledException ex3)
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult("[ UaSyncChannelDataEventAsync ] ����ȡ����" + ex3.Message));
            }
            catch (Exception ex4)
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult("[ UaSyncChannelDataEventAsync ] �쳣��" + ex4.Message));
            }
        }
        /// <summary>
        /// ����UA�㼶
        /// </summary>
        /// <returns></returns>
        private async Task<FolderState?> UaCreateFolder()
        {
            try
            {
                if (GlobalConfigModel.uaService is null)
                    return null;

                if (folderState != null)
                {
                    return folderState;
                }

                //�ȶԲ㼶
                if (uaServerAddressSpaceName.IsNullOrWhiteSpace())
                {
                    uaServerAddressSpaceName = GlobalConfigModel.uaService.GetBasicsData().GetSource<OpcUaServiceData.Basics>().AddressSpaceName;
                }

                if (GlobalConfigModel.uaService != null && GlobalConfigModel.uaService.GetStatus().Status)
                {
                    FolderState folder = null;
                    //�����㼶
                    foreach (var item in DeviceHierarchyToolTip.TrimAll().Split('>'))
                    {
                        OperateResult operateResult = GlobalConfigModel.uaService.CreateFolder(item, folder);
                        if (operateResult.GetDetails(out string? msg))
                        {
                            folder = operateResult.GetSource<FolderState>();
                            folderStates.Add(folder);
                        }
                        else
                        {
                            await ShowAsync.Invoke(msg);
                        }
                    }
                    folderState = folder;
                }
                else
                {
                    return null;
                }
                return folderState;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// ͨ�������¼�����
        /// </summary>
        private async Task DataSyncChannelDataEventAsync(CancellationToken token)
        {
            try
            {
                while (await DataSyncChannel.Reader.WaitToReadAsync(token))
                {
                    if (DataSyncChannel is null)
                        return;
                    while (DataSyncChannel.Reader.TryRead(out EventDataResult? e))
                    {
                        if (token.IsCancellationRequested)
                            continue;

                        if (!e.Status)
                        {
                            await ResultMsgAsync(DaqData, e);
                            continue;
                        }

                        var keys = e.GetSource<ConcurrentDictionary<string, AddressValue>>();
                        if (keys == null || keys.Count == 0)
                            continue;

                        foreach (var kv in keys)
                        {
                            if (!_addressIndex.TryGetValue(kv.Key, out var addressModel) ||
                                !_mqPluginMap.TryGetValue(kv.Key, out var pluginConfigs))
                                continue;

                            // �����ֽڴ���ģ�ͣ����ȴ�JSON�ַ�����ȡ����δ��ļ���ȡ
                            List<BytesModel>? bm = null;
                            try
                            {
                                bm = kv.Value?.AddressExtendParam?.ToString()?.ToJsonEntity<List<BytesModel>>();
                            }
                            catch (System.Text.Json.JsonException)
                            {
                                // AddressExtendParam ������Ч�� BytesModel JSON�����˵��ļ�ģʽ
                            }

                            if (bm != null)
                            {
                                bm = bytesModels.GetOrAdd(kv.Value.AddressName, bm);
                            }
                            else if (addressModel.ExpandParam != null)
                            {
                                if (!File.Exists(addressModel.ExpandParam))
                                {
                                    ShowAsync?.Invoke(DeviceHierarchyToolTip + ", " + $" {addressModel.Address} -" + "��չ�����ļ�������".GetLanguageValue(App.LanguageOperate));
                                    continue;
                                }
                                if (!bytesModels.TryGetValue(addressModel.Address, out bm) || bm == null)
                                {
                                    bm = FileHandler.FileToString(addressModel.ExpandParam).ToJsonEntity<List<BytesModel>>();
                                    if (bm != null)
                                    {
                                        bytesModels[addressModel.Address] = bm;
                                    }
                                }
                            }

                            // ���ֽ�ģ�ͣ�ֱ��ת��
                            if (bm == null)
                            {
                                if (TokenSource is null)
                                    return;
                                await UaSyncChannel.Writer.WriteAsync(kv.Value, TokenSource.Token);
                                await MqTransmissionAsync(new() { [addressModel] = kv.Value }, pluginConfigs);
                                continue;
                            }

                            // �ֽ�ת����ת��
                            await TransformAndForwardAsync(kv.Value, bm, addressModel, pluginConfigs);
                        }
                    }
                }
            }
            catch (TaskCanceledException ex1)
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult("[ DataSyncChannelDataEventAsync ] ����ȡ����" + ex1.Message));
            }
            catch (ChannelClosedException ex2)
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult("[ DataSyncChannelDataEventAsync ] ͨ���ѹرգ�" + ex2.Message));
            }
            catch (OperationCanceledException ex3)
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult("[ DataSyncChannelDataEventAsync ] ����ȡ����" + ex3.Message));
            }
            catch (Exception ex4)
            {
                await ResultMsgAsync(DaqData, EventInfoResult.CreateFailureResult("[ DataSyncChannelDataEventAsync ] �쳣��" + ex4.Message));
            }
        }

        /// <summary>
        /// �ֽ�ת����ת������� UA ͨ���� MQ
        /// </summary>
        private async Task TransformAndForwardAsync(AddressValue addressValue, List<BytesModel> bm, IAddressModel addressModel, List<PluginConfigModel> pluginConfigs)
        {
            bytesHandler ??= await BytesHandler.InstanceAsync(DeviceName);

            OperateResult result = await bytesHandler.TransformAsync(addressValue.ResultValue.GetSource<byte[]>(), addressValue.Time, bm);
            if (!result.GetDetails(out ConcurrentDictionary<string, AddressValue>? res))
                return;

            foreach (var item in res)
            {
                AddressModel newModel = new()
                {
                    Address = item.Key,
                    Describe = item.Value.AddressDescribe,
                    EncodingType = item.Value.EncodingType,
                    Guid = item.Value.SN,
                    SimplifyValue = addressModel.SimplifyValue,
                    Length = item.Value.Length,
                    Time = item.Value.Time,
                    Topic = addressModel.Topic,
                    Type = item.Value.AddressDataType,
                };
                if (TokenSource is null)
                    return;
                await UaSyncChannel.Writer.WriteAsync(item.Value, TokenSource.Token);
                await MqTransmissionAsync(new() { [newModel] = item.Value }, pluginConfigs);
            }
        }

        /// <summary>
        /// MQ����
        /// </summary>
        private async Task MqTransmissionAsync(Dictionary<IAddressModel, AddressValue> inParam, List<PluginConfigModel> pluginConfigs)
        {
            foreach (var item in pluginConfigs)
            {
                if (!mqHandlers.TryGetValue(item.Guid, out var mq))
                {
                    mq = await MqHandler.InstanceAsync(item);
                    mqHandlers[item.Guid] = mq;
                }
                var result = await mq.ProduceAsync(item.Guid, inParam);
                await ResultMsgAsync(item, result);
            }
        }

        /// <summary>
        /// �ؽ���ַ���һ���
        /// </summary>
        private void RebuildAddressCache()
        {
            _addressIndex = AddressDatas.Keys
                .Where(a => !string.IsNullOrEmpty(a.Address))
                .ToDictionary(a => a.Address!);

            _mqPluginMap = AddressDatas
                .Where(kv => !string.IsNullOrEmpty(kv.Key.Address))
                .GroupBy(kv => kv.Key.Address!)
                .ToDictionary(g => g.Key, g => g.SelectMany(x => x.Value).ToList());

            //����MQ���·��
            foreach (var item in _mqPluginMap)
            {
                if (MqPluginPath is not null)
                    MqPluginPath.Clear();
                else
                    MqPluginPath = new();

                foreach (var model in item.Value)
                {
                    MqPluginPath.Add(PluginHandlerCore.PluginOperate.GetPluginPath(model.Name));
                }
            }
        }

        /// <summary>
        /// ����
        /// </summary>
        /// <param name="model">��Ŀ��Ϣ</param>
        public async Task SettingsAsync(IProjectTreeViewModel model, Func<PluginConfigModel, BaseModel, Task> resultAsync, Func<string, Task> showAsync)
        {
            DaqPluginPath = PluginHandlerCore.PluginOperate.GetPluginPath(model.DaqDetails.Name);
            ResultAsync = resultAsync;
            ShowAsync = showAsync;
            Project = model;
            DeviceName = model.Name;
            DeviceType = model.DaqDetails.Name;
            UpdateTime = model.DaqDetails.Time;
            DeviceHierarchyToolTip = model.GetHierarchyPath();
            DeviceHierarchy = DeviceHierarchyToolTip.TruncateByBytes(36);
            AddressDatas = model.Details.ToAddressMqDictionary();
            AddressCount = AddressDatas.Count;
            RebuildAddressCache();
            DaqData = model.DaqDetails;
            if (IsRun)
            {
                await RetryAsync();
            }
            if (model.IsSoftStart)
            {
                await CollectAsync();
            }
        }

        /// <summary>
        /// �����Ϣ�׳�
        /// </summary>
        public async Task ResultMsgAsync(PluginConfigModel pcm, BaseModel bm)
        {
            if (bm.Status)
            {
                LedColor = System.Windows.Media.Colors.Green;
                CollectStatus = LanguageHandler.GetLanguageValue("����", App.LanguageOperate);
            }
            else
            {
                LedColor = System.Windows.Media.Colors.Red;
                CollectStatus = LanguageHandler.GetLanguageValue("�쳣", App.LanguageOperate);
                DeviceStatusChangLiang = true;
            }
            await ResultAsync.Invoke(pcm, bm);
        }

        /// <summary>
        /// ��ʼÿ���ȡ����ʱ��
        /// </summary>
        public void StartPolling(RuntimeSecondsRecorderHandler recorder)
        {
            _cts = new CancellationTokenSource();

            _ = PollAsync(recorder, _cts.Token);
        }

        private async Task PollAsync(RuntimeSecondsRecorderHandler recorder, CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    CollectTime = (int)recorder.TotalSeconds;
                }
            }
            catch (OperationCanceledException) { }
        }
        private CancellationTokenSource _cts;
        /// <summary>
        /// ֹͣ��ѯ
        /// </summary>
        public void StopPolling()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        public override string ToString()
        {
            return DaqData.Guid;
        }

        public void Dispose()
        {
            // ȡ�����ƣ���ֹ�����첽�������ִ��
            if (TokenSource != null)
            {
                TokenSource.Cancel();
                TokenSource.Dispose();
                TokenSource = null;
            }
            daqHandler?.Dispose();
            daqHandler = null;
            foreach (var item in mqHandlers)
            {
                item.Value.Dispose();
            }
            mqHandlers.Clear();
            _mqPluginMap.Clear();
            runtime.Stop();
            StopPolling();
            bytesHandler?.Dispose();
            bytesModels.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            if (daqHandler != null)
                await daqHandler.DisposeAsync();
            daqHandler = null;
            foreach (var item in mqHandlers)
            {
                await item.Value.DisposeAsync();
            }
            mqHandlers.Clear();
            _mqPluginMap.Clear();
            runtime.Stop();
            StopPolling();
            if (bytesHandler != null)
            {
                await bytesHandler.DisposeAsync();
            }
            bytesModels.Clear();
            await StopAsync();
        }
        #endregion

        #region ״̬
        private Task LanguageHandler_OnLanguageEventAsync(object? sender, EventLanguageResult e)
        {
            string text = CollectStatus;
            CollectStatus = LanguageHandler.GetLanguageValue(text, App.LanguageOperate);
            return Task.CompletedTask;
        }

        #endregion
    }
}
