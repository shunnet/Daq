using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Snet.Core.handler;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.mvvm;
using Snet.Iot.Daq.data;
using Snet.Iot.Daq.handler;
using Snet.Model.data;
using Snet.Model.@enum;
using Snet.Utility;
using Snet.Windows.Controls.@enum;
using Snet.Windows.Controls.handler;
using Snet.Windows.Controls.message;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Snet.Iot.Daq.viewModel
{
    /// <summary>
    /// ���������ͼģ�ͣ��ṩ����ϴ�/�Ƴ��������½�/�޸�/ɾ����WebAPI �����Լ����״̬��֤�ȹ��ܡ�
    /// </summary>
    public class PluginSettingsModel : BindNotify
    {
        #region ���캯��
        /// <summary>
        /// ���캯������ʼ�������������
        /// </summary>
        public PluginSettingsModel()
        {
            _ = InitAsync();
        }
        #endregion

        #region ����
        /// <summary>
        /// ����������Դ
        /// </summary>
        public ObservableCollection<ComboBoxModel> ComboBoxItemsSource
        {
            get => _ComboBoxItemsSource;
            set => SetProperty(ref _ComboBoxItemsSource, value);
        }
        private ObservableCollection<ComboBoxModel> _ComboBoxItemsSource = new ObservableCollection<ComboBoxModel>();

        /// <summary>
        /// ��������ѡ�е�����
        /// </summary>
        public ComboBoxModel ComboBoxSelectedItem
        {
            get => GetProperty(() => ComboBoxSelectedItem);
            set => SetProperty(() => ComboBoxSelectedItem, value);
        }

        /// <summary>
        /// �������
        /// </summary>
        public ObservableCollection<PluginListModel> PluginList
        {
            get => _PluginList;
            set => SetProperty(ref _PluginList, value);
        }
        private ObservableCollection<PluginListModel> _PluginList = new ObservableCollection<PluginListModel>();

        /// <summary>
        /// ���ѡ��
        /// </summary>
        public PluginListModel PluginListSelectedItem
        {
            get => GetProperty(() => PluginListSelectedItem);
            set => SetProperty(() => PluginListSelectedItem, value);
        }

        /// <summary>
        /// ������ü���
        /// </summary>
        public ObservableCollection<PluginConfigModel> PluginConfig
        {
            get => _PluginConfig;
            set => SetProperty(ref _PluginConfig, value);
        }
        private ObservableCollection<PluginConfigModel> _PluginConfig = new ObservableCollection<PluginConfigModel>();

        /// <summary>
        /// ѡ�еĲ������
        /// </summary>
        public PluginConfigModel PluginConfigSelectedItem
        {
            get => GetProperty(() => PluginConfigSelectedItem);
            set => SetProperty(() => PluginConfigSelectedItem, value);
        }
        #endregion

        #region ����
        /// <summary>
        /// �����Զ����
        /// </summary>
        public IAsyncRelayCommand SettingsAutoPack => p_SettingsAutoPack ??= new AsyncRelayCommand(SettingsAutoPackAsync);
        private IAsyncRelayCommand p_SettingsAutoPack;
        private async Task SettingsAutoPackAsync()
        {
            if (PluginConfigSelectedItem?.AutoPack is not null)
            {
                await Windows.Controls.message.MessageBox.Show("����ʧ�ܣ��Ѵ���".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
            }
            else
            {
                GlobalConfigModel.param.SetBasics(new AddressAutoPackModel());
                if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
                {
                    PluginConfigSelectedItem?.AutoPack = GlobalConfigModel.param.GetBasics().GetSource<AddressAutoPackModel>();
                    PluginConfigSelectedItem?.SetPlugin();
                    SavePluginConfig();
                    await Windows.Controls.message.MessageBox.Show("���óɹ�".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                }
            }
        }

        /// <summary>
        /// �޸��Զ����
        /// </summary>
        public IAsyncRelayCommand UpdateAutoPack => p_UpdateAutoPack ??= new AsyncRelayCommand(UpdateAutoPackAsync);
        private IAsyncRelayCommand p_UpdateAutoPack;
        private async Task UpdateAutoPackAsync()
        {
            if (PluginConfigSelectedItem?.AutoPack is null)
            {
                await Windows.Controls.message.MessageBox.Show("�޸�ʧ�ܣ���δ����".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
            }
            else
            {
                GlobalConfigModel.param.SetBasics(PluginConfigSelectedItem?.AutoPack);
                if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
                {
                    PluginConfigSelectedItem?.AutoPack = GlobalConfigModel.param.GetBasics().GetSource<AddressAutoPackModel>();
                    PluginConfigSelectedItem?.SetPlugin();
                    SavePluginConfig();
                    await Windows.Controls.message.MessageBox.Show("�޸ĳɹ�".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                }
            }
        }

        /// <summary>
        /// �Ƴ��Զ����
        /// </summary>
        public IAsyncRelayCommand RemoveAutoPack => p_RemoveAutoPack ??= new AsyncRelayCommand(RemoveAutoPackAsync);
        private IAsyncRelayCommand p_RemoveAutoPack;
        private async Task RemoveAutoPackAsync()
        {
            if (PluginConfigSelectedItem?.AutoPack is null)
            {
                await Windows.Controls.message.MessageBox.Show("�Ƴ�ʧ�ܣ���δ����".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
            }
            else
            {
                PluginConfigSelectedItem?.AutoPack = null;
                PluginConfigSelectedItem?.SetPlugin();
                SavePluginConfig();
                await Windows.Controls.message.MessageBox.Show("�Ƴ��ɹ�".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
            }
        }


        /// <summary>
        /// ����WEBapi
        /// </summary>
        public IAsyncRelayCommand SettingsWebApi => p_SettingsWebApi ??= new AsyncRelayCommand(SettingsWebApiAsync);
        private IAsyncRelayCommand p_SettingsWebApi;
        private async Task SettingsWebApiAsync()
        {
            if (PluginConfigSelectedItem?.WebApi is not null)
            {
                await Windows.Controls.message.MessageBox.Show("����ʧ�ܣ��Ѵ���".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
            }
            else
            {
                GlobalConfigModel.param.SetBasics(new WAModel());
                if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
                {
                    PluginConfigSelectedItem?.WebApi = GlobalConfigModel.param.GetBasics().GetSource<WAModel>();
                    PluginConfigSelectedItem?.SetPlugin();
                    SavePluginConfig();
                    await Windows.Controls.message.MessageBox.Show("���óɹ�".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                }
            }
        }

        /// <summary>
        /// �޸�WEBapi
        /// </summary>
        public IAsyncRelayCommand UpdateWebApi => p_UpdateWebApi ??= new AsyncRelayCommand(UpdateWebApiAsync);
        private IAsyncRelayCommand p_UpdateWebApi;
        private async Task UpdateWebApiAsync()
        {
            if (PluginConfigSelectedItem?.WebApi is null)
            {
                await Windows.Controls.message.MessageBox.Show("�޸�ʧ�ܣ���δ����".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
            }
            else
            {
                GlobalConfigModel.param.SetBasics(PluginConfigSelectedItem?.WebApi);
                if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
                {
                    PluginConfigSelectedItem?.WebApi = GlobalConfigModel.param.GetBasics().GetSource<WAModel>();
                    PluginConfigSelectedItem?.SetPlugin();
                    SavePluginConfig();
                    await Windows.Controls.message.MessageBox.Show("�޸ĳɹ�".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                }
            }
        }

        /// <summary>
        /// ɾ��WEBapi
        /// </summary>
        public IAsyncRelayCommand RemoveWebApi => p_RemoveWebApi ??= new AsyncRelayCommand(RemoveWebApiAsync);
        private IAsyncRelayCommand p_RemoveWebApi;
        private async Task RemoveWebApiAsync()
        {
            if (PluginConfigSelectedItem?.WebApi is null)
            {
                await Windows.Controls.message.MessageBox.Show("�Ƴ�ʧ�ܣ���δ����".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
            }
            else
            {
                PluginConfigSelectedItem?.WebApi = null;
                PluginConfigSelectedItem?.SetPlugin();
                SavePluginConfig();
                await Windows.Controls.message.MessageBox.Show("�Ƴ��ɹ�".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// �ϴ����
        /// </summary>
        public IAsyncRelayCommand UploadPlugin => uploadPlugin ??= new AsyncRelayCommand(UploadPluginAsync);
        private IAsyncRelayCommand? uploadPlugin;
        private async Task UploadPluginAsync()
        {
            PluginType plugin = ComboBoxSelectedItem.Value.GetSource<PluginType>();
            string path = Win32Handler.Select(App.LanguageOperate.GetLanguageValue("��ѡ���ļ�"), false, new Dictionary<string, string> { { $"(*.zip)", $"*.zip" }, });
            if (!path.IsNullOrWhiteSpace())
            {
                string typePath = Path.Combine(GlobalConfigModel.FilePath, plugin.ToString().ToLower());
                string zipName = System.IO.Path.GetFileName(path).Replace(".zip", string.Empty);
                string libPath = Path.Combine(typePath, zipName);
                DirectoryInfo directoryInfo = new(typePath);
                if (!directoryInfo.Exists)
                {
                    directoryInfo.Create();
                }
                directoryInfo = new(libPath);
                //�ȼ��ز����״̬
                ConcurrentDictionary<string, (string type, bool status)> pluginStatus = new();
                //�Ƿ����
                bool exists = directoryInfo.Exists;
                //���Ҳ���б����Ƿ��Ѵ���ͬ����ͬ·�����
                PluginListSelectedItem = PluginList.FirstOrDefault(p => p.PluginDetails.Path == libPath || p.Name == zipName);
                if (PluginListSelectedItem != null || exists)
                {
                    if (!await MessageBox.Show("�˲�����ϴ����Ƿ�����ȸ��£�".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), MessageBoxButton.YesNo, MessageBoxImage.Question))
                    {
                        return;
                    }
                    switch (plugin)
                    {
                        case PluginType.Daq:
                            //ֹͣ��������������ֹͣ�ɼ�
                            GlobalConfigModel.TrayDevices.Where(d => libPath == d.DaqPluginPath).ToList().ForEach(d =>
                            {
                                pluginStatus.AddOrUpdate(d.ToString(), (d.DeviceType, d.IsRun), (k, v) => (d.DeviceType, d.IsRun));
                            });
                            break;
                        case PluginType.Mq:
                            GlobalConfigModel.TrayDevices.Where(d => d.MqPluginPath.Contains(libPath)).ToList().ForEach(d =>
                            {
                                pluginStatus.AddOrUpdate(d.ToString(), (d.DeviceType, d.IsRun), (k, v) => (d.DeviceType, d.IsRun));
                            });
                            break;
                    }
                    if (PluginListSelectedItem != null)
                    {
                        await PrivateRemovalPlugin();
                    }
                }

                //��ѹzip��ָ��·��
                await ZipFile.ExtractToDirectoryAsync(path, libPath, true);

                //�ӿ�����
                string iName = string.Format(GlobalConfigModel.InterfaceFullName, plugin);

                //��ȡ��������
                List<(PluginModel Model, object? Param)> result = PluginHandlerCore.PluginOperate.InitPlugin(libPath, iName);
                if (result.Count > 0)
                {
                    foreach (var item in result)
                    {
                        //���뱾�أ������´γ�ʼ��
                        PluginModel details = item.Model;

                        //���ò��·��
                        details.Path = libPath;

                        //���ӵ�����
                        PluginList.Add(new PluginListModel(details.Name, plugin, details.Version, DateTime.Now, details));
                    }

                    SavePluginListConfig();

                    if (pluginStatus.Count > 0 || exists)
                    {
                        await MessageBox.Show("����ȸ��³ɹ�".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Information);

                        //�ȸ��º����֮ǰ������״̬���������
                        switch (plugin)
                        {
                            case PluginType.Daq:
                                //ֹͣ��������������ֹͣ�ɼ�
                                GlobalConfigModel.TrayDevices.Where(d => libPath == d.DaqPluginPath).ToList().ForEach(d =>
                                {
                                    PrivateInit(d, pluginStatus);
                                });
                                break;
                            case PluginType.Mq:
                                //ֹͣ��������������ֹͣ�ɼ�
                                GlobalConfigModel.TrayDevices.Where(d => d.MqPluginPath.Contains(libPath)).ToList().ForEach(d =>
                                {
                                    PrivateInit(d, pluginStatus);
                                });
                                break;
                        }
                    }
                    else
                    {
                        await MessageBox.Show("����ϴ��ɹ�".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    try
                    {
                        //�Ƴ�������ļ�
                        Directory.Delete(libPath, true);
                    }
                    catch (Exception) { }
                    await MessageBox.Show("����ϴ�ʧ�ܣ�δ��������Ӧ�ӿ�".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Warning);

                }
            }

            SavePluginListConfig();
        }

        /// <summary>
        /// ˽�г�ʼ��
        /// </summary>
        /// <param name="d">����̨�豸����</param>
        /// <param name="pluginStatus">���״̬</param>
        private void PrivateInit(ConsoleDeviceModel d, ConcurrentDictionary<string, (string type, bool status)> pluginStatus)
        {
            bool status = pluginStatus.TryGetValue(d.ToString(), out (string type, bool status) plugin) ? plugin.status : false;
            if (status)
            {
                //�ɼ�
                d.Retry.ExecuteAsync(null);
            }
            else
            {
                //ֹͣ
                d.Stop.Execute(null);
            }
        }

        /// <summary>
        /// �Ƴ����
        /// </summary>
        public IAsyncRelayCommand RemovePlugin => removePlugin ??= new AsyncRelayCommand(RemovePluginAsync);
        private IAsyncRelayCommand? removePlugin;
        private async Task RemovePluginAsync()
        {
            if (await MessageBox.Show($"ȷ���Ƴ��˲����".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OKCancel, MessageBoxImage.Question))
            {
                await PrivateRemovalPlugin();
                await MessageBox.Show($"����Ƴ��ɹ�".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            SavePluginListConfig();
        }

        /// <summary>
        /// ˽���Ƴ����
        /// </summary>
        /// <returns></returns>
        private async Task PrivateRemovalPlugin()
        {
            if (PluginListSelectedItem == null) return;
            string name = PluginListSelectedItem.Name;
            PluginModel details = PluginListSelectedItem.PluginDetails;

            switch (PluginListSelectedItem.Type)
            {
                case PluginType.Daq:
                    //ֹͣ��������������ֹͣ�ɼ�
                    GlobalConfigModel.TrayDevices.Where(d => d.DeviceType == PluginListSelectedItem.Name || details.Path == d.DaqPluginPath).ToList().ForEach(d =>
                    {
                        d.Stop.Execute(null);
                    });
                    break;
                case PluginType.Mq:
                    GlobalConfigModel.TrayDevices.Where(d => d.MqPluginPath.Contains(details.Path)).ToList().ForEach(d =>
                    {
                        d.Stop.Execute(null);
                    });
                    break;
            }

            if (await PluginHandlerCore.PluginOperate.RemovePluginAsync(details.Name))
            {
                //ǿ�� GC ������ж�صĳ��������ģ�ȷ���ͷ����в�������
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Directory.Delete(details.Path, true);  //ɾ������ļ���
            }

            //��ѯ���·���Ƿ���һ�µģ��еĻ�һ��ɾ��
            for (int i = PluginList.Count - 1; i >= 0; i--)
            {
                if (PluginList[i].PluginDetails.Path == details.Path)
                {
                    PluginList.RemoveAt(i);
                }
            }
            PluginListSelectedItem = null;  //�ÿ�
            SavePluginListConfig();
        }

        /// <summary>
        /// ���Ӳ������
        /// </summary>
        public IAsyncRelayCommand AddPluginConfig => addPluginConfig ??= new AsyncRelayCommand(AddPluginConfigAsync);
        private IAsyncRelayCommand? addPluginConfig;
        private async Task AddPluginConfigAsync()
        {
            PluginModel details = PluginListSelectedItem.PluginDetails;
            object? obj = PluginHandlerCore.PluginOperate.GetPluginParamObject(string.Format(GlobalConfigModel.InterfaceFullName, PluginListSelectedItem.Type), details.Name);
            GlobalConfigModel.param.SetBasics(obj);
            if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
            {
                obj = GlobalConfigModel.param.GetBasics();  //�û��Ѿ��޸ĺõĲ���

                PluginType plugin = PluginListSelectedItem.Type;   //ѡ�еĲ������
                string name = plugin.ToString().ToLower();  //�����������
                string libConfigPath = Path.Combine(GlobalConfigModel.ConfigPath, name);  //��������ļ��洢·��
                if (!Directory.Exists(libConfigPath))
                {
                    Directory.CreateDirectory(libConfigPath);
                }
                //��ȡΨһ��ʶ��
                Type type = obj.GetType();
                PropertyInfo? prop = type.GetProperty(GlobalConfigModel.LibConfigSNKey);
                object? snValue = prop?.GetValue(obj);
                //ƴ������
                string fileName = string.Format(details.ConfigFormat, snValue);
                string path = Path.Combine(libConfigPath, fileName);
                if (!File.Exists(path))
                {
                    FileHandler.StringToFile(path, obj.ToJson(true));
                    //���ӵ�����
                    PluginConfigModel p = new PluginConfigModel(PluginConfig.Count + 1, false, fileName, plugin, details.Name, DateTime.Now, obj.ToJson(), libConfigPath);
                    //���ӵ�ȫ�ּ���
                    p.SetPlugin();
                    PluginConfig.Add(p);
                }
                else
                {
                    await MessageBox.Show($"����ʧ�ܣ���������ļ��Ѿ����ڣ�".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            SavePluginConfig();
        }

        /// <summary>
        /// ״̬��֤
        /// </summary>
        public IAsyncRelayCommand StatusVerification => statusVerification ??= new AsyncRelayCommand(StatusVerificationAsync);
        private IAsyncRelayCommand? statusVerification;
        public async Task StatusVerificationAsync()
        {
            await Task.Run(async () =>
            {
                foreach (var item in PluginConfig)
                {
                    //�������
                    PluginType plugin = item.Type;
                    //�ӿ�����
                    string iName = string.Format(GlobalConfigModel.InterfaceFullName, plugin);
                    item.Status = (await PluginHandlerCore.PluginOperate.StatusVerifyAsync(iName, item.Name, item.Param)).Status;
                }
            });
        }

        /// <summary>
        /// �޸Ĳ������
        /// </summary>
        public IAsyncRelayCommand UpdatePluginConfig => updatePluginConfig ??= new AsyncRelayCommand(UpdatePluginConfigAsync);
        private IAsyncRelayCommand? updatePluginConfig;
        private async Task UpdatePluginConfigAsync()
        {
            object? obj = PluginHandlerCore.PluginOperate.ConvertPluginJsonParam(PluginConfigSelectedItem.Name, PluginConfigSelectedItem.Param);

            //��ȡ�ɵ�Ψһ��ʶ��
            Type type = obj.GetType();
            PropertyInfo? prop = type.GetProperty(GlobalConfigModel.LibConfigSNKey);
            string oldSN = prop?.GetValue(obj).ToString();

            GlobalConfigModel.param.SetBasics(obj);
            if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
            {
                obj = GlobalConfigModel.param.GetBasics();  //�û��Ѿ��޸ĺõĲ���

                //��ȡ�µ�Ψһ��ʶ��
                type = obj.GetType();
                prop = type.GetProperty(GlobalConfigModel.LibConfigSNKey);
                string newSN = prop?.GetValue(obj).ToString();


                if (PluginConfigSelectedItem.Check(newSN, oldSN))
                {
                    PluginConfigSelectedItem.Param = obj.ToJson();
                    PluginConfigSelectedItem.Time = DateTime.Now;
                    PluginConfigSelectedItem.UpdateSnAndFileName(newSN, oldSN);
                    PluginConfigSelectedItem.SetPlugin();
                }
                else
                {
                    await MessageBox.Show($"�޸�ʧ�ܣ���������ļ������Ѿ����ڣ����޸�SN��".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            SavePluginConfig();
        }

        /// <summary>
        /// ����Ψһ��ʶ��
        /// </summary>
        public IAsyncRelayCommand CopySN => copySN ??= new AsyncRelayCommand(CopySNAsync);
        private IAsyncRelayCommand? copySN;
        private Task CopySNAsync()
        {
            System.Windows.Clipboard.SetDataObject(PluginConfigSelectedItem.SN);
            return Task.CompletedTask;
        }


        /// <summary>
        /// �Ƴ��������
        /// </summary>
        public IAsyncRelayCommand RemovePluginConfig => removePluginConfig ??= new AsyncRelayCommand(RemovePluginConfigAsync);
        private IAsyncRelayCommand? removePluginConfig;
        private async Task RemovePluginConfigAsync()
        {
            //��������Ŀ¼�Ƿ���ڸò���������ļ��������������Ƴ�
            if (UseCheck(PluginConfigSelectedItem))
            {
                await MessageBox.Show("�ò�������ļ�����Ŀ��������ʹ��".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (await MessageBox.Show($"ȷ���Ƴ���������".GetLanguageValue(App.LanguageOperate), "��ܰ��ʾ".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OKCancel, MessageBoxImage.Question))
            {
                string path = Path.Combine(GlobalConfigModel.ConfigPath, PluginConfigSelectedItem.Type.ToString().ToLower(), PluginConfigSelectedItem.SN);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                GlobalConfigModel.PluginDict.Remove(PluginConfigSelectedItem.Guid, out _);
                PluginConfig.Remove(PluginConfigSelectedItem);
                PluginConfigSelectedItem = null;//�ÿ�
            }
            SavePluginConfig();
        }


        /// <summary>
        /// ��ȡ
        /// </summary>
        public IAsyncRelayCommand Read => read ??= new AsyncRelayCommand(ReadAsync);
        private IAsyncRelayCommand? read;
        public async Task ReadAsync()
        {
            GlobalConfigModel.param.SetBasics(new DaqPluginOperateModel.ReadModel());
            if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
            {
                DaqPluginOperateModel.ReadModel model = GlobalConfigModel.param.GetBasics().GetSource<DaqPluginOperateModel.ReadModel>();
                AddressModel address = new AddressModel() { Address = model.Address, Type = model.Type, EncodingType = model.EncodingType, Length = model.Length };
                PluginConfigModel daq = PluginConfigSelectedItem;
                OperateResult result = await address.TestReadAddressAsync(daq);
                if (result.Status)
                    PluginConfigSelectedItem.Status = result.Status;
                if (result.GetDetails(out string? msg, out ConcurrentDictionary<string, AddressValue>? data))
                {
                    AddressValue value = data[address.Address];
                    await Windows.Controls.message.MessageBox.Show($"{"��ȡ�ɹ�".GetLanguageValue(App.LanguageOperate)}\r\n{value.AddressName}\r\n{value.ResultValue}", "���".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                }
                else
                {
                    await Windows.Controls.message.MessageBox.Show($"{"��ȡʧ��".GetLanguageValue(App.LanguageOperate)}:{msg}", "���".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// д��
        /// </summary>
        public IAsyncRelayCommand Write => write ??= new AsyncRelayCommand(WriteAsync);
        private IAsyncRelayCommand? write;
        public async Task WriteAsync()
        {
            GlobalConfigModel.param.SetBasics(new DaqPluginOperateModel.WriteModel());
            if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
            {
                DaqPluginOperateModel.WriteModel model = GlobalConfigModel.param.GetBasics().GetSource<DaqPluginOperateModel.WriteModel>();
                AddressModel address = new AddressModel() { Address = model.Address, Type = model.AddressDataType, EncodingType = model.EncodingType }; ;
                PluginConfigModel daq = PluginConfigSelectedItem;
                OperateResult result = await address.TestWriteAddressAsync(daq, model);
                if (result.Status)
                    PluginConfigSelectedItem.Status = result.Status;
                await Windows.Controls.message.MessageBox.Show($"{"д��".GetLanguageValue(App.LanguageOperate)}{(result.Status ? "�ɹ�".GetLanguageValue(App.LanguageOperate) : "ʧ��".GetLanguageValue(App.LanguageOperate) + $":{result.Message}")}", "���".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, result.Status ? Windows.Controls.@enum.MessageBoxImage.Information : Windows.Controls.@enum.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// ����
        /// </summary>
        public IAsyncRelayCommand Produce => produce ??= new AsyncRelayCommand(ProduceAsync);
        private IAsyncRelayCommand? produce;
        public async Task ProduceAsync()
        {
            GlobalConfigModel.param.SetBasics(new MqPluginOperateModel());
            if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
            {
                MqPluginOperateModel model = GlobalConfigModel.param.GetBasics().GetSource<MqPluginOperateModel>();
                OperateResult result = await PluginConfigSelectedItem.TestProduceAsync(model.Topic, model.Content.ToString());
                if (result.Status)
                    PluginConfigSelectedItem.Status = result.Status;
                await Windows.Controls.message.MessageBox.Show($"{"����".GetLanguageValue(App.LanguageOperate)}{(result.Status ? "�ɹ�".GetLanguageValue(App.LanguageOperate) : "ʧ��".GetLanguageValue(App.LanguageOperate) + $":{result.Message}")}", "���".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, result.Status ? Windows.Controls.@enum.MessageBoxImage.Information : Windows.Controls.@enum.MessageBoxImage.Error);
            }
        }

        #endregion

        #region �����¼�
        /// <summary>
        /// ���ݲ˵��򿪴���
        /// </summary>
        public IAsyncRelayCommand DataGrid_ContextMenuOpening => dataGrid_ContextMenuOpening ??= new AsyncRelayCommand<ContextMenuEventArgs>(DataGrid_ContextMenuOpeningAsync);
        private IAsyncRelayCommand? dataGrid_ContextMenuOpening;
        private Task DataGrid_ContextMenuOpeningAsync(ContextMenuEventArgs? e)
        {
            if (e?.Source is not DataGrid dataGrid)
                return Task.CompletedTask;

            // ���ղþ���
            // ֻҪ��ǰ���ǡ����Ҽ������ͽ�ֹ����
            if (dataGrid.SelectedItem == null)
            {
                e.Handled = true;
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// ����Ҽ��������
        /// </summary>
        public IAsyncRelayCommand DataGrid_PreviewMouseRightButtonDown => dataGrid_PreviewMouseRightButtonDown ??= new AsyncRelayCommand<MouseButtonEventArgs>(DataGrid_PreviewMouseRightButtonDownAsync);
        private IAsyncRelayCommand? dataGrid_PreviewMouseRightButtonDown;
        private Task DataGrid_PreviewMouseRightButtonDownAsync(MouseButtonEventArgs? e)
        {
            if (e?.Source is not DataGrid dataGrid)
                return Task.CompletedTask;

            System.Windows.DependencyObject dep = (System.Windows.DependencyObject)e.OriginalSource;

            while (dep != null && dep is not DataGridRow)
                dep = VisualTreeHelper.GetParent(dep);

            if (dep is DataGridRow row)
            {
                // �Ҽ�������
                dataGrid.SelectedItem = row.Item;
                row.IsSelected = true;
                row.Focus();
            }
            else
            {
                // �Ҽ��հף����ѡ��
                dataGrid.SelectedItem = null;
                e.Handled = true; // ��ֹĬ���Ҽ�
            }
            return Task.CompletedTask;
        }
        #endregion

        #region ����
        /// <summary>
        /// ��ʼ��
        /// </summary>
        /// <returns></returns>
        private Task InitAsync()
        {
            //����Ĭ������
            ComboBoxItemsSource.Add(new(PluginType.Daq.ToString(), PluginType.Daq));
            ComboBoxItemsSource.Add(new(PluginType.Mq.ToString(), PluginType.Mq));
            ComboBoxSelectedItem = ComboBoxItemsSource[0];

            //��ȡ��������
            PluginList = PluginHandlerCore.GetPluginUIConfig<ObservableCollection<PluginListModel>>(GlobalConfigModel.UI_PluginListConfigPath) ?? new();
            //�������
            if (GlobalConfigModel.PluginDict.Count > 0)
            {
                PluginConfig = new ObservableCollection<PluginConfigModel>(GlobalConfigModel.PluginDict.Values);
            }
            else
            {
                PluginConfig = new();
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// ʹ�ü��
        /// </summary>
        /// <returns>false:û�б�ʹ��  true:��ʹ����</returns>
        private bool UseCheck(PluginConfigModel model)
        {
            //����Ƿ��б�ʹ��
            string checkFile = GlobalConfigModel.UI_ProjectConfigPath;
            if (File.Exists(checkFile))
            {
                string content = FileHandler.FileToString(checkFile);
                if (content.Contains(model.Guid))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// ��������������
        /// </summary>
        public void SavePluginConfig()
        {
            if (!Directory.Exists(GlobalConfigModel.UiConfigPath))
            {
                Directory.CreateDirectory(GlobalConfigModel.UiConfigPath);
            }
            PluginHandlerCore.SavePluginUIConfig(PluginConfig, GlobalConfigModel.UI_PluginConfigPath);
        }

        /// <summary>
        /// ��������������
        /// </summary>
        public void SavePluginListConfig()
        {
            if (!Directory.Exists(GlobalConfigModel.UiConfigPath))
            {
                Directory.CreateDirectory(GlobalConfigModel.UiConfigPath);
            }
            PluginHandlerCore.SavePluginUIConfig(PluginList, GlobalConfigModel.UI_PluginListConfigPath);
        }
        #endregion

    }
}
