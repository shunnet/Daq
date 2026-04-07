using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Snet.Core.handler;
using Snet.Iot.Daq.data;
using Snet.Iot.Daq.@enum;
using Snet.Iot.Daq.handler;
using Snet.Model.data;
using Snet.Utility;
using Snet.Windows.Controls.data;
using Snet.Windows.Controls.@enum;
using Snet.Windows.Controls.handler;
using Snet.Windows.Controls.message;
using Snet.Windows.Core.mvvm;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Snet.Iot.Daq.viewModel
{
    /// <summary>
    /// 插件设置视图模型，提供插件上传/移除、配置新建/修改/删除、WebAPI 设置以及插件状态验证等功能。
    /// </summary>
    public class PluginSettingsModel : BindNotify
    {
        #region 构造函数
        public PluginSettingsModel()
        {
            _ = InitAsync();
        }
        #endregion

        #region 属性
        /// <summary>
        /// 下拉框数据源
        /// </summary>
        public ObservableCollection<ComboBoxModel> ComboBoxItemsSource
        {
            get => _ComboBoxItemsSource;
            set => SetProperty(ref _ComboBoxItemsSource, value);
        }
        private ObservableCollection<ComboBoxModel> _ComboBoxItemsSource = new ObservableCollection<ComboBoxModel>();

        /// <summary>
        /// 下拉框数选中的数据
        /// </summary>
        public ComboBoxModel ComboBoxSelectedItem
        {
            get => GetProperty(() => ComboBoxSelectedItem);
            set => SetProperty(() => ComboBoxSelectedItem, value);
        }

        /// <summary>
        /// 插件集合
        /// </summary>
        public ObservableCollection<PluginListModel> PluginList
        {
            get => _PluginList;
            set => SetProperty(ref _PluginList, value);
        }
        private ObservableCollection<PluginListModel> _PluginList = new ObservableCollection<PluginListModel>();

        /// <summary>
        /// 插件选中
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
        /// 启动自动组包
        /// </summary>
        public IAsyncRelayCommand StartAutoPack => p_StartAutoPack ??= new AsyncRelayCommand(StartAutoPackAsync);
        private IAsyncRelayCommand p_StartAutoPack;
        private async Task StartAutoPackAsync()
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

        /// <summary>
        /// 移除自动组包
        /// </summary>
        public IAsyncRelayCommand RemoveAutoPack => p_RemoveAutoPack ??= new AsyncRelayCommand(RemoveAutoPackAsync);
        private IAsyncRelayCommand p_RemoveAutoPack;
        private async Task RemoveAutoPackAsync()
        {
            PluginConfigSelectedItem?.AutoPack = null;
            PluginConfigSelectedItem?.SetPlugin();
            SavePluginConfig();
            await Windows.Controls.message.MessageBox.Show("移除成功".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
        }


        /// <summary>
        /// 设置WEBapi
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
        /// 修改WEBapi
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
        /// 删除WEBapi
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
        private async Task UploadPluginAsync()
        {
            PluginType plugin = ComboBoxSelectedItem.Value.GetSource<PluginType>();
            string path = Win32Handler.Select(App.LanguageOperate.GetLanguageValue("请选择文件"), false, new Dictionary<string, string> { { $"(*.zip)", $"*.zip" }, });
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
                if (directoryInfo.Exists)
                {
                    await MessageBox.Show("此插件已上传，请先移除".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                //解压zip到指定路径
                await ZipFile.ExtractToDirectoryAsync(path, libPath, true);

                //接口名称
                string iName = string.Format(GlobalConfigModel.InterfaceFullName, plugin);

                //获取基础数据
                List<(PluginDetailsModel Model, object? Param)> result = PluginHandler.InitPlugin(libPath, iName);
                if (result.Count > 0)
                {
                    foreach (var item in result)
                    {
                        //存入本地，方便下次初始化
                        PluginDetailsModel details = item.Model;

                        //设置插件路径
                        details.PluginPath = libPath;

                        //添加到集合
                        PluginList.Add(new PluginListModel(details.Name, plugin, details.Version, DateTime.Now, details));
                    }

                    await MessageBox.Show("插件上传成功".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    try
                    {
                        //移除错误的文件
                        Directory.Delete(libPath, true);
                    }
                    catch (Exception) { }
                    await MessageBox.Show("插件上传失败，未检索到对应接口".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Warning);

                }
            }

            SavePluginListConfig();
        }

        /// <summary>
        /// 移除插件
        /// </summary>
        public IAsyncRelayCommand RemovePlugin => removePlugin ??= new AsyncRelayCommand(RemovePluginAsync);
        private IAsyncRelayCommand? removePlugin;
        private async Task RemovePluginAsync()
        {
            //检索配置目录是否存在该插件的配置文件，存在则不允许移除
            if (PluginConfig.Any(p => p.Name == PluginListSelectedItem.Name))
            {
                await MessageBox.Show("请先移除该插件的所有配置文件".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string name = PluginListSelectedItem.Name;
            PluginDetailsModel details = PluginListSelectedItem.PluginDetails;

            //创建移除的任务
            if (await MessageBox.Show($"确定移除此插件吗？".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OKCancel, MessageBoxImage.Question))
            {
                //创建命令
                StringBuilder stringBuilder = new StringBuilder();
                stringBuilder.AppendLine($"rmdir /s /q {details.PluginPath}"); //删除插件
                stringBuilder.AppendLine("del /f /q \"%~f0\"");//删除自身
                if (!Directory.Exists(GlobalConfigModel.TaskPath))
                {
                    Directory.CreateDirectory(GlobalConfigModel.TaskPath);
                }
                FileHandler.StringToFile(Path.Combine(GlobalConfigModel.TaskPath, $"Remove{name}Plugin.bat"), stringBuilder.ToString());
                //查询插件路径是否还有一致的，有的话一并删除
                for (int i = PluginList.Count - 1; i >= 0; i--)
                {
                    if (PluginList[i].PluginDetails.PluginPath == details.PluginPath)
                    {
                        PluginList.RemoveAt(i);
                    }
                }
                PluginListSelectedItem = null;  //置空
                await MessageBox.Show($"插件移除任务创建成功，重启后生效！".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            SavePluginListConfig();
        }

        /// <summary>
        /// 添加插件配置
        /// </summary>
        public IAsyncRelayCommand AddPluginConfig => addPluginConfig ??= new AsyncRelayCommand(AddPluginConfigAsync);
        private IAsyncRelayCommand? addPluginConfig;
        private async Task AddPluginConfigAsync()
        {
            PluginDetailsModel details = PluginListSelectedItem.PluginDetails;
            object? obj = PluginHandler.GetPluginParamObject(string.Format(GlobalConfigModel.InterfaceFullName, PluginListSelectedItem.Type), details.Name);
            GlobalConfigModel.param.SetBasics(obj);
            if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
            {
                obj = GlobalConfigModel.param.GetBasics();  //用户已经修改好的参数

                PluginType plugin = PluginListSelectedItem.Type;   //选中的插件类型
                string name = plugin.ToString().ToLower();  //插件类型名称
                string libConfigPath = Path.Combine(GlobalConfigModel.ConfigPath, name);  //插件配置文件存储路径
                if (!Directory.Exists(libConfigPath))
                {
                    Directory.CreateDirectory(libConfigPath);
                }
                //获取唯一标识符
                Type type = obj.GetType();
                PropertyInfo? prop = type.GetProperty(GlobalConfigModel.LibConfigSNKey);
                object? snValue = prop?.GetValue(obj);
                //拼接数据
                string fileName = string.Format(details.ConfigFormat, snValue);
                string path = Path.Combine(libConfigPath, fileName);
                if (!File.Exists(path))
                {
                    FileHandler.StringToFile(path, obj.ToJson(true));
                    //添加到集合
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
        public async Task StatusVerificationAsync()
        {
            await Task.Run(async () =>
            {
                foreach (var item in PluginConfig)
                {
                    //插件类型
                    PluginType plugin = item.Type;
                    //接口名称
                    string iName = string.Format(GlobalConfigModel.InterfaceFullName, plugin);
                    item.Status = (await PluginHandler.StatusVerifyAsync(iName, item.Name, item.Param)).Status;
                }
            });
        }

        /// <summary>
        /// 修改插件配置
        /// </summary>
        public IAsyncRelayCommand UpdatePluginConfig => updatePluginConfig ??= new AsyncRelayCommand(UpdatePluginConfigAsync);
        private IAsyncRelayCommand? updatePluginConfig;
        private async Task UpdatePluginConfigAsync()
        {
            object? obj = PluginHandler.ConvertPluginJsonParam(PluginConfigSelectedItem.Name, PluginConfigSelectedItem.Param);

            //获取旧的唯一标识符
            Type type = obj.GetType();
            PropertyInfo? prop = type.GetProperty(GlobalConfigModel.LibConfigSNKey);
            string oldSN = prop?.GetValue(obj).ToString();

            GlobalConfigModel.param.SetBasics(obj);
            if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
            {
                obj = GlobalConfigModel.param.GetBasics();  //用户已经修改好的参数

                //获取新的唯一标识符
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
        private async Task CopySNAsync()
        {
            System.Windows.Clipboard.SetDataObject(PluginConfigSelectedItem.SN);
        }


        /// <summary>
        /// 移除插件配置
        /// </summary>
        public IAsyncRelayCommand RemovePluginConfig => removePluginConfig ??= new AsyncRelayCommand(RemovePluginConfigAsync);
        private IAsyncRelayCommand? removePluginConfig;
        private async Task RemovePluginConfigAsync()
        {
            //检索配置目录是否存在该插件的配置文件，存在则不允许移除
            if (UseCheck(PluginConfigSelectedItem))
            {
                await MessageBox.Show("该插件配置文件在项目设置中有使用".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (await MessageBox.Show($"确定移除此配置吗？".GetLanguageValue(App.LanguageOperate), "温馨提示".GetLanguageValue(App.LanguageOperate), MessageBoxButton.OKCancel, MessageBoxImage.Question))
            {
                string path = Path.Combine(GlobalConfigModel.ConfigPath, PluginConfigSelectedItem.Type.ToString().ToLower(), PluginConfigSelectedItem.SN);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                GlobalConfigModel.PluginDict.Remove(PluginConfigSelectedItem.Guid, out _);
                PluginConfig.Remove(PluginConfigSelectedItem);
                PluginConfigSelectedItem = null;//置空
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
                if (result.GetDetails(out string? msg, out ConcurrentDictionary<string, AddressValue>? data))
                {
                    AddressValue value = data[address.Address];
                    await Windows.Controls.message.MessageBox.Show($"{"读取成功".GetLanguageValue(App.LanguageOperate)}\r\n{value.AddressName}\r\n{value.ResultValue}", "结果".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Information);
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
        private async Task DataGrid_ContextMenuOpeningAsync(ContextMenuEventArgs? e)
        {
            if (e?.Source is not DataGrid dataGrid)
                return;

            // 最终裁决：
            // 只要当前不是“行右键”，就禁止弹出
            if (dataGrid.SelectedItem == null)
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// 鼠标右键点击触发
        /// </summary>
        public IAsyncRelayCommand DataGrid_PreviewMouseRightButtonDown => dataGrid_PreviewMouseRightButtonDown ??= new AsyncRelayCommand<MouseButtonEventArgs>(DataGrid_PreviewMouseRightButtonDownAsync);
        private IAsyncRelayCommand? dataGrid_PreviewMouseRightButtonDown;
        private async Task DataGrid_PreviewMouseRightButtonDownAsync(MouseButtonEventArgs? e)
        {
            if (e?.Source is not DataGrid dataGrid)
                return;

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
        }
        #endregion

        #region 方法
        /// <summary>
        /// 初始化
        /// </summary>
        /// <returns></returns>
        private async Task InitAsync()
        {
            //设置默认数据
            ComboBoxItemsSource.Add(new(PluginType.Daq.ToString(), PluginType.Daq));
            ComboBoxItemsSource.Add(new(PluginType.Mq.ToString(), PluginType.Mq));
            ComboBoxSelectedItem = ComboBoxItemsSource[0];

            //获取本地配置
            PluginList = PluginHandler.GetPluginUIConfig<ObservableCollection<PluginListModel>>(GlobalConfigModel.UI_PluginListConfigPath) ?? new();
            //插件配置
            if (GlobalConfigModel.PluginDict.Count > 0)
            {
                PluginConfig = new ObservableCollection<PluginConfigModel>(GlobalConfigModel.PluginDict.Values);
            }
            else
            {
                PluginConfig = new();
            }
        }

        /// <summary>
        /// 使用检查
        /// </summary>
        /// <returns>false:没有被使用  true:被使用了</returns>
        private bool UseCheck(PluginConfigModel model)
        {
            //检查是否有被使用
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
        /// 保存插件界面配置
        /// </summary>
        public void SavePluginConfig()
        {
            if (!Directory.Exists(GlobalConfigModel.UiConfigPath))
            {
                Directory.CreateDirectory(GlobalConfigModel.UiConfigPath);
            }
            PluginHandler.SavePluginUIConfig(PluginConfig, GlobalConfigModel.UI_PluginConfigPath);
        }

        /// <summary>
        /// 保存插件集合配置
        /// </summary>
        public void SavePluginListConfig()
        {
            if (!Directory.Exists(GlobalConfigModel.UiConfigPath))
            {
                Directory.CreateDirectory(GlobalConfigModel.UiConfigPath);
            }
            PluginHandler.SavePluginUIConfig(PluginList, GlobalConfigModel.UI_PluginListConfigPath);
        }
        #endregion

    }
}
