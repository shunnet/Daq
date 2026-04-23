using CommunityToolkit.Mvvm.Input;
using Snet.Iot.Daq.Core.@interface;
using Snet.Model.data;
using Snet.Utility;
using SQLite;
using System.ComponentModel;

namespace Snet.Iot.Daq.Core.data
{
    /// <summary>
    /// 地址的模型
    /// </summary>
    public class AddressModelCore : DaqPluginOperateModel.ReadModel, IAddressModel
    {
        /// <summary>
        /// 信息传递包装器异步
        /// </summary>
        private EventingWrapperAsync<EventInfoResult> OnInfoEventWrapperAsync;

        /// <inheritdoc/>
        public event EventHandlerAsync<EventInfoResult> OnInfoEventAsync
        {
            add => OnInfoEventWrapperAsync.AddHandler(value);
            remove => OnInfoEventWrapperAsync.RemoveHandler(value);
        }
        /// <inheritdoc/>
        public Task OnInfoEventHandlerAsync(object? sender, EventInfoResult e)
        {
            OnInfoEventWrapperAsync.InvokeAsync(sender, e);
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        [PrimaryKey, AutoIncrement]
        [Browsable(false)]
        [Description("序号")]
        [System.Text.Json.Serialization.JsonIgnore]
        public int Index
        {
            get => GetProperty(() => Index);
            set => SetProperty(() => Index, value);
        }

        /// <inheritdoc/>
        [Description("唯一标识")]
        [Browsable(false)]
        public string Guid { get; set; } = System.Guid.NewGuid().ToString();

        /// <inheritdoc/>
        [Description("别名")]
        [Indexed(Unique = true)]
        public string AnotherName
        {
            get => GetProperty(() => AnotherName);
            set => SetProperty(() => AnotherName, value);
        }

        /// <inheritdoc/>
        [Description("描述")]
        public string Describe
        {
            get => GetProperty(() => Describe);
            set => SetProperty(() => Describe, value);
        }

        /// <inheritdoc/>
        [Description("数据传输主题")]
        public string Topic
        {
            get => GetProperty(() => Topic);
            set => SetProperty(() => Topic, value);
        }

        /// <inheritdoc/>
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
        /// [Height(200, 80, double.NaN)]
        [Description("扩展参数")]
        public virtual string ExpandParam
        {
            get => GetProperty(() => ExpandParam);
            set => SetProperty(() => ExpandParam, value);
        }

        /// <inheritdoc/>
        [Browsable(false)]
        [Description("最后更新时间")]
        public DateTime Time
        {
            get => _time;
            set => SetProperty(ref _time, value);
        }
        [System.Text.Json.Serialization.JsonIgnore]
        private DateTime _time = DateTime.Now;

        /// <inheritdoc/>
        [Browsable(false)]
        [SQLite.Ignore]
        [System.Text.Json.Serialization.JsonIgnore]
        [Description("选中")]
        public bool IsSelected
        {
            get => GetProperty(() => IsSelected);
            set => SetProperty(() => IsSelected, value);
        }

        /// <inheritdoc/>
        [Browsable(false)]
        [SQLite.Ignore]
        [System.Text.Json.Serialization.JsonIgnore]
        public IAsyncRelayCommand Update
        {
            get => update ??= new AsyncRelayCommand(UpdateAsync);
            set => SetProperty(ref update, value);
        }
        private IAsyncRelayCommand? update;

        /// <inheritdoc/>
        public virtual async Task UpdateAsync() { }

        /// <inheritdoc/>
        public virtual void Revoke(int index) { }

        /// <inheritdoc/>
        public AddressDetails Convert()
        {
            AddressDetails address = new AddressDetails(Address, Type, Length, EncodingType);
            address.AddressAnotherName = AnotherName;
            address.AddressExtendParam = ExpandParam;
            return address;
        }

    }
}
