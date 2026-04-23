using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.data;
using Snet.Model.data;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;

namespace Snet.Iot.Daq.handler
{
    /// <summary>
    /// 插件参数处理类<br/>
    /// 负责插件的加载、移除、实例化、参数转换、状态验证、配置读写等核心操作。
    /// 内部使用 ConcurrentDictionary 缓存程序集类型和实例，支持线程安全的并发访问。
    /// </summary>
    public static class PluginHandler
    {
        /// <summary>
        /// 获取所有已保存的插件配置<br/>
        /// 从本地 JSON 文件加载插件列表并注册到全局字典中，同时触发信息事件通知
        /// </summary>
        /// <returns>全局插件配置字典</returns>
        public static ConcurrentDictionary<string, PluginConfigModel> GetAllPlugin()
        {
            if (!File.Exists(GlobalConfigModel.UI_PluginConfigPath))
            {
                return GlobalConfigModel.PluginDict;
            }
            foreach (var item in PluginHandlerCore.GetPluginUIConfig<ObservableCollection<PluginConfigModel>>(GlobalConfigModel.UI_PluginConfigPath))
            {
                GlobalConfigModel.PluginDict[item.Guid] = item;
                GlobalConfigModel.PluginDict[item.Guid].OnInfoEventHandlerAsync(item, EventInfoResult.CreateSuccessResult("set enevt"));
            }
            return GlobalConfigModel.PluginDict;
        }

        /// <summary>
        /// 添加或更新插件到全局统一集合<br/>
        /// 同时触发信息事件通知并异步刷新界面
        /// </summary>
        /// <param name="plugin">待注册的插件配置对象</param>
        public static void SetPlugin(this PluginConfigModel plugin)
        {
            GlobalConfigModel.PluginDict[plugin.Guid] = plugin;
            GlobalConfigModel.PluginDict[plugin.Guid].OnInfoEventHandlerAsync(plugin, EventInfoResult.CreateSuccessResult("set enevt"));
            _ = GlobalConfigModel.RefreshAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 从统一集合中获取插件
        /// </summary>
        /// <param name="guid">唯一标识</param>
        /// <returns>插件对象</returns>
        public static PluginConfigModel? GetPlugin(this string guid)
        {
            if (GlobalConfigModel.PluginDict.TryGetValue(guid, out PluginConfigModel? model))
            {
                return model;
            }
            return null;
        }
    }
}
