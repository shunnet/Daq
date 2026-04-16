using Snet.Core.handler;
using Snet.Iot.Daq.Core.@enum;
using Snet.Iot.Daq.handler;
using Snet.Model.data;
using Snet.Utility;
using Snet.Iot.Daq.Core.mvvm;
using System.IO;

namespace Snet.Iot.Daq.data
{
    /// <summary>
    /// 插件配置模型
    /// </summary>
    public class PluginConfigModel : BindNotify
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
        /// 构造函数
        /// </summary>
        public PluginConfigModel()
        {
            Status = false;
        }
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="index">序号</param>
        /// <param name="status">状态</param>
        /// <param name="sn">唯一标识符</param>
        /// <param name="type">类型</param>
        /// <param name="name">名称</param>
        /// <param name="time">最后更新时间</param>
        /// <param name="param">参数</param>
        /// <param name="configPath">配置文件路径</param>
        public PluginConfigModel(int index, bool status, string sn, PluginType type, string name, DateTime time, string param, string configPath)
        {
            Index = index;
            Status = status;
            SN = sn;
            Type = type;
            Time = time;
            Name = name;
            Param = param;
            ConfigPath = configPath;
        }

        /// <summary>
        /// 唯一标识
        /// </summary>
        public string Guid { get; set; } = System.Guid.NewGuid().ToString();

        /// <summary>
        /// 序号
        /// </summary>
        public int Index
        {
            get => GetProperty(() => Index);
            set => SetProperty(() => Index, value);
        }

        /// <summary>
        /// 状态
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public bool Status
        {
            get => GetProperty(() => Status);
            set
            {
                StatusMessage = value ? App.LanguageOperate.GetLanguageValue("正常") : App.LanguageOperate.GetLanguageValue("异常");
                SetProperty(() => Status, value);
            }
        }

        /// <summary>
        /// 状态消息
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }
        private string _statusMessage = string.Empty;

        /// <summary>
        /// 唯一标识符
        /// </summary>
        public string SN
        {
            get => GetProperty(() => SN);
            set => SetProperty(() => SN, value);
        }

        /// <summary>
        /// 类型
        /// </summary>
        public PluginType Type
        {
            get => GetProperty(() => Type);
            set => SetProperty(() => Type, value);
        }


        /// <summary>
        /// 配置名称
        /// </summary>
        public string Name
        {
            get => GetProperty(() => Name);
            set => SetProperty(() => Name, value);
        }

        /// <summary>
        /// 最后更新时间
        /// </summary>
        public DateTime Time
        {
            get => GetProperty(() => Time);
            set => SetProperty(() => Time, value);
        }

        /// <summary>
        /// 配置路径
        /// </summary>
        public string ConfigPath { get; set; }

        /// <summary>
        /// 参数
        /// </summary>
        public string Param { get; set; }

        /// <summary>
        /// webapi数据
        /// </summary>
        public WAModel? WebApi { get; set; }

        /// <summary>
        /// 自动组包数据
        /// </summary>
        public AddressAutoPackModel? AutoPack { get; set; }

        /// <summary>
        /// 修改本地参数配置：将当前参数写入到本地配置文件。
        /// </summary>
        /// <returns>是否修改成功</returns>
        public bool UpdateLocalConfig()
        {
            try
            {
                FileHandler.StringToFile(Path.Combine(ConfigPath, SN), Param);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 获取对象SN
        /// </summary>
        /// <returns>SN</returns>
        public string GetObjSn()
        {
            return PluginHandler.PluginFileNameToSN(SN, Type);
        }

        /// <summary>
        /// 检查文件修改或重命名的合法性
        /// </summary>
        /// <param name="newSn">新的SN</param>
        /// <param name="oldSn">旧的SN</param>
        /// <returns>返回 true 表示校验通过（无冲突）；返回 false 表示文件名已存在（有冲突）</returns>
        public bool Check(string newSn, string oldSn)
        {
            if (string.Equals(newSn, oldSn, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            string newFileName = SN.Replace(oldSn, newSn);
            if (File.Exists(Path.Combine(ConfigPath, newFileName)))
            {
                return false;  // 文件名已存在，不能修改
            }
            return true;  // 文件可以修改
        }

        /// <summary>
        /// 更新SN与文件名称
        /// </summary>
        /// <param name="newSn">新的SN</param>
        /// <param name="oldSn">旧的SN</param>
        /// <returns>更新状态</returns>
        public bool UpdateSnAndFileName(string newSn, string oldSn)
        {
            //先移除旧的文件
            File.Delete(Path.Combine(ConfigPath, SN));
            //更新新的SN
            SN = SN.Replace(oldSn, newSn);
            //保存并返回状态
            return UpdateLocalConfig();
        }
    }
}
