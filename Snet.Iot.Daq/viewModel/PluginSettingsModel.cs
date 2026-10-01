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
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Snet.Iot.Daq.viewModel
{
    /// <summary>
    /// 插件设置视图模型，提供插件上传/移除、配置新建/修改/删除、WebAPI 设置以及状态验证等功能。
    /// </summary>
    public class PluginSettingsModel : BindNotify
    {
        #region 构造函数
        /// <summary>
        /// 构造函数，用于初始化集合和数据
        /// </summary>
        public PluginSettingsModel()
        {
            _ = InitAsync();
        }
        #endregion

        #region 属性
        /// <summary>
        /// 插件类型下拉框数据源
        /// </summary>
        public ObservableCollection<ComboBoxModel> ComboBoxItemsSource
        {
            get => _ComboBoxItemsSource;
            set => SetProperty(ref _ComboBoxItemsSource, value);
        }
        private ObservableCollection<ComboBoxModel> _ComboBoxItemsSource = new ObservableCollection<ComboBoxModel>();

        /// <summary>
        /// 插件类型下拉框选中的项
        /// </summary>
        public ComboBoxModel ComboBoxSelectedItem
        {
            get => GetProperty(() => ComboBoxSelectedItem);
            set => SetProperty(() => ComboBoxSelectedItem, value);
        }

        /// <summary>
        /// 插件列表
        /// </summary>
        public ObservableCollection<PluginListModel> PluginList
        {
            get => _PluginList;
            set => SetProperty(ref _PluginList, value);
        }
        private ObservableCollection<PluginListModel> _PluginList = new ObservableCollection<PluginListModel>();

        /// <summary>
        /// 列表选中项
        /// </summary>
        public PluginListModel PluginListSelectedItem
        {
            get => GetProperty(() => PluginListSelectedItem);
            set => SetProperty(() => PluginListSelectedItem, value);
        }

        /// <summary>
        /// 插件配置集合
        /// </summary>
        public ObservableCollection<PluginConfigModel> PluginConfig
        {
            get => _PluginConfig;
            set => SetProperty(ref _PluginConfig, value);
        }
        private ObservableCollection<PluginConfigModel> _PluginConfig = new ObservableCollection<PluginConfigModel>();

        /// <summary>
        /// 选中的插件配置
        /// </summary>
        public PluginConfigModel PluginConfigSelectedItem
        {
            get => GetProperty(() => PluginConfigSelectedItem);
            set => SetProperty(() => PluginConfigSelectedItem, value);
        }
        #endregion

        #region 命令
        /// <summary>
        /// 设置自动组包
        /// </summary>
        public IAsyncRelayCommand SettingsAutoPack => p_SettingsAutoPack ??= new AsyncRelayCommand(SettingsAutoPackAsync);
        private IAsyncRelayCommand p_SettingsAutoPack;
        private async Task SettingsAutoPackAsync()
        {
            if (PluginConfigSelectedItem?.AutoPack is not null)
            {
                await Windows.Controls.message.MessageBox.Show("设置失败，已存在".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
            }
            else
            {
                string[] keys = PackerHandler.GetSupportAutoPackDeviceTypes();
                string? key = keys.FirstOrDefault(k => PluginConfigSelectedItem.Param.Contains(k));
                if (key != null)
                {
                    GlobalConfigModel.param.SetBasics(new AddressAutoPackModel());
                    if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
                    {
                        PluginConfigSelectedItem?.AutoPack = GlobalConfigModel.param.GetBasics().GetSource<AddressAutoPackModel>();
                        PluginConfigSelectedItem?.SetPlugin();
                        SavePluginConfig();
                        await Windows.Controls.message.MessageBox.Show("设置成功".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                    }
                }
                else
                {
                    await Windows.Controls.message.MessageBox.Show("此驱动目前不支持地址自动组包".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 修改自动组包
        /// </summary>
        public IAsyncRelayCommand UpdateAutoPack => p_UpdateAutoPack ??= new AsyncRelayCommand(UpdateAutoPackAsync);
        private IAsyncRelayCommand p_UpdateAutoPack;
        private async Task UpdateAutoPackAsync()
        {
            if (PluginConfigSelectedItem?.AutoPack is null)
            {
                await Windows.Controls.message.MessageBox.Show("修改失败，尚未添加".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
            }
            else
            {
                GlobalConfigModel.param.SetBasics(PluginConfigSelectedItem?.AutoPack);
                if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
                {
                    PluginConfigSelectedItem?.AutoPack = GlobalConfigModel.param.GetBasics().GetSource<AddressAutoPackModel>();
                    PluginConfigSelectedItem?.SetPlugin();
                    SavePluginConfig();
                    await Windows.Controls.message.MessageBox.Show("修改成功".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                }
            }
        }

        /// <summary>
        /// 移除自动组包
        /// </summary>
        public IAsyncRelayCommand RemoveAutoPack => p_RemoveAutoPack ??= new AsyncRelayCommand(RemoveAutoPackAsync);
        private IAsyncRelayCommand p_RemoveAutoPack;
        private async Task RemoveAutoPackAsync()
        {
            if (PluginConfigSelectedItem?.AutoPack is null)
            {
                await Windows.Controls.message.MessageBox.Show("移除失败，尚未添加".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
            }
            else
            {
                PluginConfigSelectedItem?.AutoPack = null;
                PluginConfigSelectedItem?.SetPlugin();
                SavePluginConfig();
                await Windows.Controls.message.MessageBox.Show("移除成功".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
            }
        }


        /// <summary>
        /// 设置 WEBapi
        /// </summary>
        public IAsyncRelayCommand SettingsWebApi => p_SettingsWebApi ??= new AsyncRelayCommand(SettingsWebApiAsync);
        private IAsyncRelayCommand p_SettingsWebApi;
        private async Task SettingsWebApiAsync()
        {
            if (PluginConfigSelectedItem?.WebApi is not null)
            {
                await Windows.Controls.message.MessageBox.Show("设置失败，已存在".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
            }
            else
            {
                GlobalConfigModel.param.SetBasics(new WAModel());
                if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
                {
                    PluginConfigSelectedItem?.WebApi = GlobalConfigModel.param.GetBasics().GetSource<WAModel>();
                    PluginConfigSelectedItem?.SetPlugin();
                    SavePluginConfig();
                    await Windows.Controls.message.MessageBox.Show("设置成功".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                }
            }
        }

        /// <summary>
        /// 修改 WEBapi
        /// </summary>
        public IAsyncRelayCommand UpdateWebApi => p_UpdateWebApi ??= new AsyncRelayCommand(UpdateWebApiAsync);
        private IAsyncRelayCommand p_UpdateWebApi;
        private async Task UpdateWebApiAsync()
        {
            if (PluginConfigSelectedItem?.WebApi is null)
            {
                await Windows.Controls.message.MessageBox.Show("修改失败，尚未添加".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
            }
            else
            {
                GlobalConfigModel.param.SetBasics(PluginConfigSelectedItem?.WebApi);
                if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
                {
                    PluginConfigSelectedItem?.WebApi = GlobalConfigModel.param.GetBasics().GetSource<WAModel>();
                    PluginConfigSelectedItem?.SetPlugin();
                    SavePluginConfig();
                    await Windows.Controls.message.MessageBox.Show("修改成功".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
                }
            }
        }

        /// <summary>
        /// 删除 WEBapi
        /// </summary>
        public IAsyncRelayCommand RemoveWebApi => p_RemoveWebApi ??= new AsyncRelayCommand(RemoveWebApiAsync);
        private IAsyncRelayCommand p_RemoveWebApi;
        private async Task RemoveWebApiAsync()
        {
            if (PluginConfigSelectedItem?.WebApi is null)
            {
                await Windows.Controls.message.MessageBox.Show("移除失败，尚未添加".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
            }
            else
            {
                PluginConfigSelectedItem?.WebApi = null;
                PluginConfigSelectedItem?.SetPlugin();
                SavePluginConfig();
                await Windows.Controls.message.MessageBox.Show("移除成功".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// 上传插件
        /// </summary>
        public IAsyncRelayCommand UploadPlugin => uploadPlugin ??= new AsyncRelayCommand(UploadPluginAsync);
        private IAsyncRelayCommand? uploadPlugin;
        /// <summary>有界解压并独立探测后安装；目录及清单失败时恢复备份，运行设备在清理结束后按原状态恢复。</summary>
        private async Task UploadPluginAsync()
        {
            PluginType type = ComboBoxSelectedItem.Value.GetSource<PluginType>();
            // 该外部 Win32 API 要求具体 Dictionary 类型；过滤器仅作为本次对话框的局部只读参数使用。
            string path = Win32Handler.Select(App.LanguageOperate.GetLanguageValue("请选择文件"), false,
                new Dictionary<string, string> { { "(*.zip)", "*.zip" } });
            if (string.IsNullOrWhiteSpace(path)) return;
            var typePath = Path.Combine(GlobalConfigModel.FilePath, type.ToString().ToLowerInvariant());
            var zipName = Path.GetFileNameWithoutExtension(path);
            var libPath = Path.Combine(typePath, zipName);
            var stagingPath = Path.Combine(typePath, ".upload-tmp-" + Guid.NewGuid().ToString("N"));
            var interfaceName = string.Format(GlobalConfigModel.InterfaceFullName, type);
            var stopped = new List<ConsoleDeviceModel>();
            var previousStates = new ConcurrentDictionary<string, (string type, bool status)>();
            var oldList = PluginList.ToList();
            List<(PluginModel Model, object? Param)> discovered = new();
            string? backupPath = null;
            var entered = false;
            var replaced = false;
            var registered = false;
            var committed = false;
            string? feedback = null;
            var feedbackImage = MessageBoxImage.Information;
            try
            {
                if (zipName is "." or ".." || !Path.GetFullPath(libPath).StartsWith(
                    Path.GetFullPath(typePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("插件路径不合法");
                await PluginArchive.ExtractAsync(path, stagingPath);
                discovered = await PluginHandlerCore.ProbePluginAsync(stagingPath, interfaceName);
                if (discovered.Count == 0)
                {
                    await MessageBox.Show("插件上传失败，未检索到对应接口".GetLanguageValue(App.LanguageOperate),
                        "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                var hotUpdate = Directory.Exists(libPath) || oldList.Any(item => item.PluginDetails.Path == libPath);
                if (hotUpdate && !await MessageBox.Show("此插件已上传，是否进行热更新？".GetLanguageValue(App.LanguageOperate),
                    "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.YesNo, MessageBoxImage.Question)) return;
                await PluginHandlerCore.MutationGate.WaitAsync();
                entered = true;
                oldList = PluginList.ToList();
                var oldItems = oldList.Where(item => item.PluginDetails.Path == libPath).ToList();
                foreach (var device in GlobalConfigModel.TrayDevices.Where(device => type == PluginType.Daq
                    ? device.DaqPluginPath == libPath : device.MqPluginPath.Contains(libPath)).ToList())
                {
                    previousStates[device.ToString()] = (device.DeviceType, device.IsRun);
                    stopped.Add(device);
                    await device.Stop.ExecuteAsync(null);
                }
                foreach (var name in oldItems.Select(item => item.Name).Concat(discovered.Select(item => item.Model.Name)).Distinct())
                    await PluginHandlerCore.PluginOperate.RemovePluginAsync(name);
                if (Directory.Exists(libPath))
                {
                    backupPath = libPath + ".backup-" + Guid.NewGuid().ToString("N");
                    Directory.Move(libPath, backupPath);
                }
                Directory.Move(stagingPath, libPath);
                replaced = true;
                // 注册可能在抛出异常前完成部分接口，回滚仍须卸载本次接口。
                registered = true;
                var loaded = await PluginHandlerCore.PluginOperate.InitPluginAsync(libPath, interfaceName);
                if (loaded.Count == 0) throw new InvalidDataException("正式目录未检索到插件接口");
                var newList = oldList.Where(item => item.PluginDetails.Path != libPath
                    && !loaded.Any(plugin => plugin.Model.Name == item.Name)).ToList();
                foreach (var item in loaded)
                {
                    item.Model.Path = libPath;
                    newList.Add(new PluginListModel(item.Model.Name, type, item.Model.Version, DateTime.Now, item.Model));
                }
                if (!await ProjectHandlerCore.WriteToFileWithRetryAsync(GlobalConfigModel.UI_PluginListConfigPath, newList.ToJson(true)))
                    throw new IOException("插件清单写入失败");
                committed = true;
                PluginList.Clear();
                foreach (var item in newList) PluginList.Add(item);
                feedback = (hotUpdate ? "插件热更新成功" : "插件上传成功").GetLanguageValue(App.LanguageOperate);
            }
            catch (Exception ex)
            {
                if (!committed && entered)
                {
                    try
                    {
                        if (registered)
                            foreach (var item in discovered) await PluginHandlerCore.PluginOperate.RemovePluginAsync(item.Model.Name);
                        if (replaced && Directory.Exists(libPath)) Directory.Delete(libPath, true);
                        if (backupPath is not null && Directory.Exists(backupPath))
                        {
                            Directory.Move(backupPath, libPath);
                            backupPath = null;
                        }
                        if (Directory.Exists(libPath)) await PluginHandlerCore.PluginOperate.InitPluginAsync(libPath, interfaceName);
                    }
                    catch (Exception rollback) { Snet.Log.LogHelper.Error($"插件回滚失败，备份保留在 {backupPath}: {rollback.Message}"); }
                }
                feedback = ex.Message;
                feedbackImage = MessageBoxImage.Error;
            }
            finally
            {
                if (entered) PluginHandlerCore.MutationGate.Release();
                foreach (var device in stopped)
                {
                    try { await PrivateInitAsync(device, previousStates); }
                    catch (Exception ex) { Snet.Log.LogHelper.Error($"恢复设备失败: {device.DeviceType}, {ex.Message}"); }
                }
                foreach (var directory in new[] { stagingPath, committed ? backupPath : null })
                {
                    if (directory is null || !Directory.Exists(directory)) continue;
                    try { Directory.Delete(directory, true); }
                    catch (Exception ex) { Snet.Log.LogHelper.Error($"插件暂存或备份清理失败: {directory}, {ex.Message}"); }
                }
                await GlobalConfigModel.RefreshAsync();
            }
            // 提示框等待用户操作之前，已经完成设备恢复并释放变更门闩。
            if (feedback is not null)
                await MessageBox.Show(feedback, "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, feedbackImage);
        }

        /// <summary>
        /// 按更新前状态异步恢复设备，确保调用方等待恢复结束
        /// </summary>
        /// <param name="d">控制台设备对象</param>
        /// <param name="pluginStatus">插件状态</param>
        private async Task PrivateInitAsync(ConsoleDeviceModel d, ConcurrentDictionary<string, (string type, bool status)> pluginStatus)
        {
            bool status = pluginStatus.TryGetValue(d.ToString(), out (string type, bool status) plugin) ? plugin.status : false;
            if (status)
            {
                //采集
                await d.Retry.ExecuteAsync(null);
            }
            else
            {
                //停止
                await d.Stop.ExecuteAsync(null);
            }
        }

        /// <summary>
        /// 移除插件
        /// </summary>
        public IAsyncRelayCommand RemovePlugin => removePlugin ??= new AsyncRelayCommand(RemovePluginAsync);
        private IAsyncRelayCommand? removePlugin;
        private async Task RemovePluginAsync()
        {
            var selected = PluginListSelectedItem;
            if (selected is null || !await MessageBox.Show("确定移除此插件吗？".GetLanguageValue(App.LanguageOperate),
                "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OKCancel, MessageBoxImage.Question)) return;
            try
            {
                await PrivateRemovalPlugin(selected);
                await MessageBox.Show("插件移除成功".GetLanguageValue(App.LanguageOperate),
                    "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await MessageBox.Show(ex.Message, "异常".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>串行停止相关设备、卸载整个插件包并保存清单；卸载失败时保留原清单，不显示成功。</summary>
        /// <param name="selected">确认对话框打开前捕获的插件，避免等待过程中选择变化导致误删。</param>
        /// <returns>完成目录清理、清单落盘与控制台刷新的任务。</returns>
        private async Task PrivateRemovalPlugin(PluginListModel selected)
        {
            await PluginHandlerCore.MutationGate.WaitAsync();
            try
            {
                var details = selected.PluginDetails;
                var affected = GlobalConfigModel.TrayDevices.Where(d => selected.Type == PluginType.Daq
                    ? d.DeviceType == selected.Name || details.Path == d.DaqPluginPath
                    : d.MqPluginPath.Contains(details.Path)).ToList();
                foreach (var device in affected) await device.Stop.ExecuteAsync(null);
                var removed = await PluginHandlerCore.PluginOperate.RemovePluginAsync(details.Name);
                if (!removed && Directory.Exists(details.Path)) throw new IOException("插件卸载失败，已保留插件清单");
                if (Directory.Exists(details.Path)) Directory.Delete(details.Path, true);
                var remaining = PluginList.Where(item => item.PluginDetails.Path != details.Path).ToList();
                if (!await ProjectHandlerCore.WriteToFileWithRetryAsync(GlobalConfigModel.UI_PluginListConfigPath, remaining.ToJson(true)))
                    throw new IOException("插件清单写入失败，请刷新后重试");
                PluginList.Clear();
                foreach (var item in remaining) PluginList.Add(item);
                PluginListSelectedItem = null;
            }
            finally { PluginHandlerCore.MutationGate.Release(); }
            await GlobalConfigModel.RefreshAsync();
        }

        /// <summary>
        /// 添加插件配置
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
                obj = GlobalConfigModel.param.GetBasics();  //用户已经修改好的参数

                PluginType plugin = PluginListSelectedItem.Type;   //选中的插件类型
                string name = plugin.ToString().ToLower();  //插件类型转小写
                string libConfigPath = Path.Combine(GlobalConfigModel.ConfigPath, name);  //插件配置文件存储路径
                if (!Directory.Exists(libConfigPath))
                {
                    Directory.CreateDirectory(libConfigPath);
                }
                //获取唯一标识符
                Type type = obj.GetType();
                PropertyInfo? prop = type.GetProperty(GlobalConfigModel.LibConfigSNKey);
                object? snValue = prop?.GetValue(obj);
                //拼接文件名
                string fileName = string.Format(details.ConfigFormat, snValue);
                if (!PluginConfigModel.TryResolveConfigFilePath(libConfigPath, fileName, out string path))
                {
                    await MessageBox.Show("SN 不能包含路径或非法文件名字符".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (!File.Exists(path))
                {
                    FileHandler.StringToFile(path, obj.ToJson(true));
                    //添加到列表
                    PluginConfigModel p = new PluginConfigModel(PluginConfig.Count + 1, false, fileName, plugin, details.Name, DateTime.Now, obj.ToJson(), libConfigPath);
                    //添加到全局集合
                    p.SetPlugin();
                    PluginConfig.Add(p);
                }
                else
                {
                    await MessageBox.Show($"添加失败，插件配置文件已经存在！".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            SavePluginConfig();
        }

        /// <summary>
        /// 状态验证
        /// </summary>
        public IAsyncRelayCommand StatusVerification => statusVerification ??= new AsyncRelayCommand(StatusVerificationAsync);
        private IAsyncRelayCommand? statusVerification;
        /// <summary>在界面线程捕获配置快照，逐个等待插件验证，再在界面上下文更新状态；不在线程池枚举绑定集合。</summary>
        public async Task StatusVerificationAsync()
        {
            foreach (var item in PluginConfig.ToList())
            {
                string interfaceName = string.Format(GlobalConfigModel.InterfaceFullName, item.Type);
                item.Status = (await PluginHandlerCore.PluginOperate.StatusVerifyAsync(interfaceName, item.Name, item.Param)).Status;
            }
        }

        /// <summary>
        /// 修改插件配置
        /// </summary>
        public IAsyncRelayCommand UpdatePluginConfig => updatePluginConfig ??= new AsyncRelayCommand(UpdatePluginConfigAsync);
        private IAsyncRelayCommand? updatePluginConfig;
        private async Task UpdatePluginConfigAsync()
        {
            object? obj = PluginHandlerCore.PluginOperate.ConvertPluginJsonParam(PluginConfigSelectedItem.Name, PluginConfigSelectedItem.Param);
            if (obj is null)
            {
                await MessageBox.Show("插件参数无效或插件尚未加载".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            //获取旧的唯一标识符
            Type type = obj.GetType();
            PropertyInfo? prop = type.GetProperty(GlobalConfigModel.LibConfigSNKey);
            string? oldSN = prop?.GetValue(obj)?.ToString();
            if (string.IsNullOrWhiteSpace(oldSN))
            {
                await MessageBox.Show("插件参数中缺少 SN".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            GlobalConfigModel.param.SetBasics(obj);
            if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
            {
                obj = GlobalConfigModel.param.GetBasics();  //用户已经修改好的参数

                //获取新的唯一标识符
                type = obj.GetType();
                prop = type.GetProperty(GlobalConfigModel.LibConfigSNKey);
                string? newSN = prop?.GetValue(obj)?.ToString();
                if (string.IsNullOrWhiteSpace(newSN) ||
                    !PluginConfigModel.TryResolveConfigFilePath(PluginConfigSelectedItem.ConfigPath, PluginConfigSelectedItem.SN.Replace(oldSN, newSN), out _))
                {
                    await MessageBox.Show("SN 不能包含路径或非法文件名字符".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }


                if (PluginConfigSelectedItem.Check(newSN, oldSN))
                {
                    PluginConfigSelectedItem.Param = obj.ToJson();
                    PluginConfigSelectedItem.Time = DateTime.Now;
                    PluginConfigSelectedItem.UpdateSnAndFileName(newSN, oldSN);
                    PluginConfigSelectedItem.SetPlugin();
                }
                else
                {
                    await MessageBox.Show($"修改失败，插件配置文件名称已经存在，请修改SN！".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            SavePluginConfig();
        }

        /// <summary>
        /// 复制唯一标识符
        /// </summary>
        public IAsyncRelayCommand CopySN => copySN ??= new AsyncRelayCommand(CopySNAsync);
        private IAsyncRelayCommand? copySN;
        private Task CopySNAsync()
        {
            System.Windows.Clipboard.SetDataObject(PluginConfigSelectedItem.SN);
            return Task.CompletedTask;
        }


        /// <summary>
        /// 移除插件配置
        /// </summary>
        public IAsyncRelayCommand RemovePluginConfig => removePluginConfig ??= new AsyncRelayCommand(RemovePluginConfigAsync);
        private IAsyncRelayCommand? removePluginConfig;
        private async Task RemovePluginConfigAsync()
        {
            //检查项目目录是否存在该插件配置文件，如果存在则不能移除
            if (UseCheck(PluginConfigSelectedItem))
            {
                await MessageBox.Show("该插件配置文件在项目设置中有使用".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (await MessageBox.Show($"确定移除插件配置？".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OKCancel, MessageBoxImage.Question))
            {
                string configDir = Path.Combine(GlobalConfigModel.ConfigPath, PluginConfigSelectedItem.Type.ToString().ToLower());
                if (!PluginConfigModel.TryResolveConfigFilePath(configDir, PluginConfigSelectedItem.SN, out string path))
                {
                    await MessageBox.Show("插件配置文件路径不合法".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                GlobalConfigModel.PluginDict.Remove(PluginConfigSelectedItem.Guid, out _);
                PluginConfig.Remove(PluginConfigSelectedItem);
                PluginConfigSelectedItem = null;//清空
            }
            SavePluginConfig();
        }


        /// <summary>
        /// 读取
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
                if (result.GetDetails(out string? msg, out ConcurrentDictionary<string, AddressValue>? data)
                && data is not null && data.TryGetValue(address.Address, out var value))
                {
                    if (value.Quality == QualityType.Normal)
                    {
                        await Windows.Controls.message.MessageBox.Show($"{"读取成功".GetLanguageValue(App.LanguageOperate)}\r\n{value.AddressName}\r\n{value.ResultValue}", "结果".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);

                    }
                    else
                    {
                        msg = value.Message;
                        await Windows.Controls.message.MessageBox.Show($"{"读取失败".GetLanguageValue(App.LanguageOperate)}:{msg}", "结果".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                    }
                }
                else
                {
                    await Windows.Controls.message.MessageBox.Show($"{"读取失败".GetLanguageValue(App.LanguageOperate)}:{msg}", "结果".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 写入
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
                await Windows.Controls.message.MessageBox.Show($"{"写入".GetLanguageValue(App.LanguageOperate)}{(result.Status ? "成功".GetLanguageValue(App.LanguageOperate) : "失败".GetLanguageValue(App.LanguageOperate) + $":{result.Message}")}", "结果".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, result.Status ? Windows.Controls.@enum.MessageBoxImage.Information : Windows.Controls.@enum.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 生产
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
                await Windows.Controls.message.MessageBox.Show($"{"生产".GetLanguageValue(App.LanguageOperate)}{(result.Status ? "成功".GetLanguageValue(App.LanguageOperate) : "失败".GetLanguageValue(App.LanguageOperate) + $":{result.Message}")}", "结果".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, result.Status ? Windows.Controls.@enum.MessageBoxImage.Information : Windows.Controls.@enum.MessageBoxImage.Error);
            }
        }

        #endregion

        #region 界面事件
        /// <summary>
        /// 内容菜单打开触发
        /// </summary>
        public IAsyncRelayCommand DataGrid_ContextMenuOpening => dataGrid_ContextMenuOpening ??= new AsyncRelayCommand<ContextMenuEventArgs>(DataGrid_ContextMenuOpeningAsync);
        private IAsyncRelayCommand? dataGrid_ContextMenuOpening;
        private Task DataGrid_ContextMenuOpeningAsync(ContextMenuEventArgs? e)
        {
            if (e?.Source is not DataGrid dataGrid)
                return Task.CompletedTask;

            // 最终裁决：
            // 只要当前不是"行右键"，就禁止弹出
            if (dataGrid.SelectedItem == null)
            {
                e.Handled = true;
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 鼠标右键点击触发
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
                // 右键在行上
                dataGrid.SelectedItem = row.Item;
                row.IsSelected = true;
                row.Focus();
            }
            else
            {
                // 右键空白：清空选择
                dataGrid.SelectedItem = null;
                e.Handled = true; // 阻止默认右键
            }
            return Task.CompletedTask;
        }
        #endregion

        #region 方法
        /// <summary>
        /// 初始化
        /// </summary>
        /// <returns></returns>
        private Task InitAsync()
        {
            //设置默认选项
            ComboBoxItemsSource.Add(new(PluginType.Daq.ToString(), PluginType.Daq));
            ComboBoxItemsSource.Add(new(PluginType.Mq.ToString(), PluginType.Mq));
            ComboBoxSelectedItem = ComboBoxItemsSource[0];

            //读取插件列表
            PluginList = PluginHandlerCore.GetPluginUIConfig<ObservableCollection<PluginListModel>>(GlobalConfigModel.UI_PluginListConfigPath) ?? new();
            //读取配置
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
        /// 使用检查
        /// </summary>
        /// <returns>false:没有被使用  true:正在使用中</returns>
        private bool UseCheck(PluginConfigModel model)
        {
            //判断是否有被使用
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
        /// 保存插件配置
        /// </summary>
        public void SavePluginConfig()
        {
            if (!Directory.Exists(GlobalConfigModel.UiConfigPath))
            {
                Directory.CreateDirectory(GlobalConfigModel.UiConfigPath);
            }
            PluginHandlerCore.SavePluginUIConfig(PluginConfig, GlobalConfigModel.UI_PluginConfigPath);
            GlobalConfigModel.RefreshAsync();
        }

        /// <summary>
        /// 保存插件列表配置
        /// </summary>
        public void SavePluginListConfig()
        {
            if (!Directory.Exists(GlobalConfigModel.UiConfigPath))
            {
                Directory.CreateDirectory(GlobalConfigModel.UiConfigPath);
            }
            PluginHandlerCore.SavePluginUIConfig(PluginList, GlobalConfigModel.UI_PluginListConfigPath);
            GlobalConfigModel.RefreshAsync();
        }
        #endregion

    }
}
