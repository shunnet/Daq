using Snet.Windows.Core.mvvm;

namespace Snet.Iot.Daq.data
{

    public class ProjectDetailsTabControlModel : BindNotify
    {
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
