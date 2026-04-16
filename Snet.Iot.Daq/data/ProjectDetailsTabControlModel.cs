using Snet.Iot.Daq.Core.mvvm;

namespace Snet.Iot.Daq.data
{

    /// <summary>
    /// 项目详情选项卡控件模型，封装单个选项卡页的头文本、特殊数据和内容，并监听设备详情变更自动刷新显示。
    /// </summary>
    public class ProjectDetailsTabControlModel : BindNotify
    {
        /// <summary>
        /// 构造函数，初始化设备详情和内容并刷新显示数据
        /// </summary>
        /// <param name="daqDetails">采集设备配置模型</param>
        /// <param name="content">选项卡页内容</param>
        public ProjectDetailsTabControlModel(PluginConfigModel daqDetails, object content)
        {
            DaqDetails = daqDetails;
            Content = content;
            UpdateData();
        }
        /// <summary>
        /// 设备详情 - 采集设备
        /// </summary>
        public PluginConfigModel DaqDetails
        {
            get => _daqDetails;
            set
            {
                if (_daqDetails != null)
                {
                    _daqDetails.OnInfoEventAsync -= DaqDetails_OnInfoEventAsync;
                }
                _daqDetails = value;
                if (_daqDetails != null)
                {
                    _daqDetails.OnInfoEventAsync += DaqDetails_OnInfoEventAsync;
                }
            }
        }
        private PluginConfigModel _daqDetails;

        /// <summary>
        /// 设备信息事件回调，收到设备信息变更时刷新显示数据
        /// </summary>
        private Task DaqDetails_OnInfoEventAsync(object? sender, Model.data.EventInfoResult e)
        {
            UpdateData();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 更新数据
        /// </summary>
        private void UpdateData()
        {
            SpecialData = $"[ {DaqDetails.Name} ]";
            Header = DaqDetails.GetObjSn();
        }

        /// <summary>
        /// 头文本
        /// </summary>
        public string Header
        {
            get => GetProperty(() => Header);
            set => SetProperty(() => Header, value);
        }
        /// <summary>
        /// 特殊数据
        /// </summary>
        public string SpecialData
        {
            get => GetProperty(() => SpecialData);
            set => SetProperty(() => SpecialData, value);
        }
        /// <summary>
        /// 内容
        /// </summary>
        public object Content
        {
            get => GetProperty(() => Content);
            set => SetProperty(() => Content, value);
        }
    }
}
