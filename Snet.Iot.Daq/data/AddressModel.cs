using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Snet.Core.handler;
using Snet.Iot.Daq.handler;
using Snet.Model.data;
using Snet.Utility;
using Snet.Windows.Controls.message;
using Snet.Windows.Controls.property.core.DataAnnotations;
using SQLite;

namespace Snet.Iot.Daq.data
{
    /// <summary>
    /// 地址的模型
    /// </summary>
    public class AddressModel : DaqPluginOperateModel.ReadModel
    {
        /// <summary>
        /// 信息事件
        /// </summary>
        public event EventHandlerAsync<EventInfoResult> OnInfoEventAsync
        {
            add => OnInfoEventWrapperAsync.AddHandler(value);
            remove => OnInfoEventWrapperAsync.RemoveHandler(value);
        }
        /// <summary>
        /// 信息传递包装器异步
        /// </summary>
        private EventingWrapperAsync<EventInfoResult> OnInfoEventWrapperAsync;
        /// <summary>
        /// 异步消息源传递
        /// </summary>
        /// <param name="sender">自身对象</param>
        /// <param name="e">事件结果</param>
        public Task OnInfoEventHandlerAsync(object? sender, EventInfoResult e)
        {
            OnInfoEventWrapperAsync.InvokeAsync(sender, e);
            return Task.CompletedTask;
        }

        /// <summary>
        /// 序号
        /// </summary>
        [PrimaryKey, AutoIncrement]
        [Browsable(false)]
        [Description("序号")]
        [System.Text.Json.Serialization.JsonIgnore]
        public int Index
        {
            get => GetProperty(() => Index);
            set => SetProperty(() => Index, value);
        }

        /// <summary>
        /// 唯一标识
        /// </summary>
        [Description("唯一标识")]
        [Browsable(false)]
        public string Guid { get; set; } = System.Guid.NewGuid().ToString();

        /// <summary>
        /// 别名
        /// </summary>
        [Description("别名")]
        [Indexed(Unique = true)]
        public string AnotherName
        {
            get => GetProperty(() => AnotherName);
            set => SetProperty(() => AnotherName, value);
        }

        /// <summary>
        /// 描述
        /// </summary>
        [Description("描述")]
        public string Describe
        {
            get => GetProperty(() => Describe);
            set => SetProperty(() => Describe, value);
        }

        /// <summary>
        /// 数据传输主题
        /// </summary>
        [Description("数据传输主题")]
        public string Topic
        {
            get => GetProperty(() => Topic);
            set => SetProperty(() => Topic, value);
        }

        /// <summary>
        /// 数据传输精简值
        /// </summary>
        [Description("数据传输精简值")]
        public bool SimplifyValue
        {
            get => GetProperty(() => SimplifyValue);
            set => SetProperty(() => SimplifyValue, value);
        }

        /// <summary>
        /// 扩展参数
        /// </summary>
        /// [InputFilePath(".json", "json files (*.json)|*.json")]  选择文件路径的输入框
        [Description("扩展参数")]
        [Height(200, 80, double.NaN)]
        public string ExpandParam
        {
            get => GetProperty(() => ExpandParam);
            set => SetProperty(() => ExpandParam, value);
        }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        [Browsable(false)]
        [Description("最后更新时间")]
        public DateTime Time
        {
            get => _time;
            set => SetProperty(ref _time, value);
        }
        [System.Text.Json.Serialization.JsonIgnore]
        private DateTime _time = DateTime.Now;

        /// <summary>
        /// 选中
        /// </summary>
        [Browsable(false)]
        [SQLite.Ignore]
        [System.Text.Json.Serialization.JsonIgnore]
        [Description("选中")]
        public bool IsSelected
        {
            get => GetProperty(() => IsSelected);
            set => SetProperty(() => IsSelected, value);
        }

        /// <summary>
        /// 修改地址
        /// </summary>
        [Browsable(false)]
        [SQLite.Ignore]
        [System.Text.Json.Serialization.JsonIgnore]
        public IAsyncRelayCommand Update => update ??= new AsyncRelayCommand(UpdateAsync);
        private IAsyncRelayCommand? update;
        private async Task UpdateAsync()
        {
            AddressModel? model = null;
            GlobalConfigModel.param.SetBasics(this);
            if ((await DialogHost.Show(GlobalConfigModel.param, GlobalConfigModel.DialogHostTag)).ToBool())
            {
                AddressModel param = GlobalConfigModel.param.GetBasics().GetSource<AddressModel>();
                if (param != null)
                {
                    model = param.Guid.GetAddress();
                    if (model != null)
                    {
                        model.Address = param.Address;
                        model.AnotherName = param.AnotherName;
                        model.Describe = param.Describe;
                        model.Time = param.Time = DateTime.Now;
                        model.Length = param.Length;
                        model.Type = param.Type;
                        model.SimplifyValue = param.SimplifyValue;
                        model.EncodingType = param.EncodingType;
                        model.ExpandParam = param.ExpandParam;
                        model.Topic = param.Topic;

                        try
                        {
                            model.SetAddress();
                            GlobalConfigModel.sqliteOperate.Update(model);
                        }
                        catch (Exception ex)
                        {
                            await RevokeAsync(this.Index);
                            await MessageBox.Show(ex.Message, "异常".GetLanguageValue(App.LanguageOperate), Windows.Controls.@enum.MessageBoxButton.OK, Windows.Controls.@enum.MessageBoxImage.Error);
                        }
                    }
                }
            }
            else
            {
                await RevokeAsync(this.Index);
            }
        }

        /// <summary>
        /// 撤销修改
        /// </summary>
        private Task RevokeAsync(int index)
        {
            AddressModel model = GlobalConfigModel.sqliteOperate.Table<AddressModel>().FirstOrDefault(x => x.Index == index);
            if (model != null)
            {
                this.Address = model.Address;
                this.AnotherName = model.AnotherName;
                this.Describe = model.Describe;
                this.Time = model.Time;
                this.Length = model.Length;
                this.Type = model.Type;
                this.EncodingType = model.EncodingType;
                this.IsSelected = model.IsSelected;
                this.ExpandParam = model.ExpandParam;
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 模型转换成地址类型
        /// </summary>
        /// <returns>地址详情</returns>
        public AddressDetails Convert()
        {
            AddressDetails address = new AddressDetails(Address, Type, Length, EncodingType);
            address.AddressAnotherName = AnotherName;
            address.AddressExtendParam = ExpandParam;
            return address;
        }

    }
}
