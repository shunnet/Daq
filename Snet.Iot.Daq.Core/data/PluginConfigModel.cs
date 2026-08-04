using Snet.Core.handler;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.mvvm;
using Snet.Model.data;
using Snet.Model.@enum;
using Snet.Model.@event;
using Snet.Utility;

namespace Snet.Iot.Daq.Core.data
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
        /// 信息传递包装器异步<br/>
        /// 包含事件委托，序列化会循环引用或报错，必须忽略
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        private EventingWrapperAsync<EventInfoResult> OnInfoEventWrapperAsync;
        /// <summary>
        /// 异步消息源传递
        /// </summary>
        /// <param name="sender">自身对象</param>
        /// <param name="e">事件结果</param>
        public async Task OnInfoEventHandlerAsync(object? sender, EventInfoResult e)
        {
            await OnInfoEventWrapperAsync.InvokeAsync(sender, e);
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
                StatusMessage = value ? Core.LanguageOperate.GetLanguageValue("正常") : Core.LanguageOperate.GetLanguageValue("异常");
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
        public string ConfigPath { get; set; } = string.Empty;

        /// <summary>
        /// 参数
        /// </summary>
        public string Param { get; set; } = string.Empty;

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
            catch (Exception ex)
            {
                Snet.Log.LogHelper.Error($"插件配置写入失败：{SN}，{ex.Message}", "PluginConfigModel", ex);
                return false;
            }
        }

        /// <summary>
        /// 获取对象SN
        /// </summary>
        /// <returns>SN</returns>
        public string GetObjSn()
        {
            return PluginHandlerCore.PluginFileNameToSN(SN, Type);
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
        /// 更新SN与文件名称<br/>
        /// 先写新文件、成功后删除旧文件，避免删除后写入失败导致配置丢失
        /// </summary>
        /// <param name="newSn">新的SN</param>
        /// <param name="oldSn">旧的SN</param>
        /// <returns>更新状态</returns>
        public bool UpdateSnAndFileName(string newSn, string oldSn)
        {
            string oldFileName = SN;
            string newFileName = SN.Replace(oldSn, newSn);

            SN = newFileName;
            if (!UpdateLocalConfig())
            {
                // 写入失败：回滚文件名，保留旧配置
                SN = oldFileName;
                return false;
            }

            if (!string.Equals(oldFileName, newFileName, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Delete(Path.Combine(ConfigPath, oldFileName));
                }
                catch (Exception ex)
                {
                    // 旧文件删除失败不影响新配置，仅记录（命名参数消除泛型重载二义性）
                    Snet.Log.LogHelper.Warning(message: $"旧插件配置删除失败：{oldFileName}，{ex.Message}", foldername: "PluginConfigModel");
                }
            }
            return true;
        }
    }
}
