using Snet.Iot.Daq.Core.handler;
using Snet.Iot.Daq.Core.@interface;
using Snet.Iot.Daq.data;
using Snet.Iot.Daq.view;
using Snet.Iot.Daq.viewModel;
using Snet.Log;
using Snet.Utility;
using System.Collections.ObjectModel;
using System.IO;

namespace Snet.Iot.Daq.handler
{

    /// <summary>
    /// 项目树形结构处理器，提供树节点的选中、展开、查找、移除、配置保存与加载等操作。
    /// </summary>
    public static class ProjectHandler
    {
        /// <summary>
        /// 回灌项目详情树节点的全局数据（地址、MQ插件等）
        /// </summary>
        /// <param name="nodes">详情树节点集合</param>
        public static void RebindGlobals(this IEnumerable<IProjectDetailsTreeViewModel> nodes)
        {
            foreach (var node in nodes)
            {
                RebindDetailNode(node);
            }
        }

        /// <summary>
        /// 回灌项目树节点的全局数据（采集设备、地址、MQ 等）
        /// </summary>
        /// <param name="trees">项目树根节点集合</param>
        public static void RebindGlobals(this ObservableCollection<IProjectTreeViewModel> trees)
        {
            foreach (var node in trees)
            {
                RebindProjectNode(node);
            }
        }

        /// <summary>
        /// 异步保存项目配置到 JSON 文件<br/>
        /// 包含重试机制（最多5次，间隔500ms），用于处理文件被占用的情况
        /// </summary>
        /// <param name="data">待保存的项目树数据</param>
        /// <param name="path">配置文件保存路径</param>
        public static async Task SaveConfigAsync(ObservableCollection<IProjectTreeViewModel> data, string path)
        {
            // 重试策略参数
            const int maxRetries = 5;
            const int delayMilliseconds = 500;
            int retries = 0;
            bool fileWritten = false;

            while (retries < maxRetries && !fileWritten)
            {
                try
                {
                    // 序列化并写入文件
                    await ProjectHandlerCore.WriteToFileAsync(path, data.ToJson(true));
                    fileWritten = true;
                    _ = GlobalConfigModel.RefreshAsync().ConfigureAwait(false);
                }
                catch (IOException ex)
                {
                    // 文件被占用时记录详细异常信息并重试
                    retries++;
                    LogHelper.Error($"文件被占用，重试 {retries}/{maxRetries}：{ex.Message}");
                    await Task.Delay(delayMilliseconds);
                }
                catch (Exception ex)
                {
                    // 其他异常直接记录并中止重试
                    LogHelper.Error($"配置保存失败: {ex.Message}", exception: ex);
                    break;
                }
            }

            if (!fileWritten)
            {
                LogHelper.Error("配置文件写入失败，文件可能被长时间占用。");
            }
        }

        /// <summary>
        /// 创建项目详情界面实例及其 ViewModel<br/>
        /// 用于双击树节点时动态创建设备详情页
        /// </summary>
        /// <param name="bossProject">项目树根集合</param>
        /// <param name="project">当前选中的项目节点</param>
        /// <returns>界面实例和 ViewModel 的元组</returns>
        public static (ProjectDetails view, ProjectDetailsModel model) CreateDetails(this ObservableCollection<IProjectTreeViewModel> bossProject, IProjectTreeViewModel project)
        {
            ProjectDetails p = new ProjectDetails();
            ProjectDetailsModel pModel = p.DataContext.GetSource<ProjectDetailsModel>();
            pModel.SetValue(project, bossProject);
            return (p, pModel);
        }

        /// <summary>
        /// 回灌单个项目节点的全局数据（采集设备、地址、MQ 等），并递归处理子节点。
        /// </summary>
        /// <param name="node">待回灌的项目树节点</param>
        public static void RebindProjectNode(this IProjectTreeViewModel node)
        {
            // 🔥 回灌采集设备 / 插件（PluginConfigModel）
            if (node.DaqDetails != null)
            {
                var guid = node.DaqDetails.Guid;

                if (GlobalConfigModel.PluginDict.TryGetValue(guid, out var global))
                {
                    node.DaqDetails = global;
                }
                else
                {
                    GlobalConfigModel.PluginDict[guid] = node.DaqDetails;
                }

                node.UpdateName();
            }

            // 🔥 回灌地址 / MQ 等详情
            if (node.Details != null)
            {
                node.Details.RebindGlobals();
            }

            // 递归子节点
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    RebindProjectNode(child);
                }
            }
        }
        /// <summary>
        /// 回灌单个详情树节点的全局数据（地址、MQ 插件等），并递归处理子节点。
        /// </summary>
        /// <param name="node">待回灌的详情树节点</param>
        public static void RebindDetailNode(this IProjectDetailsTreeViewModel node)
        {
            // 🔥 AddressModel 回灌
            if (node.AddressDetails != null)
            {
                var guid = node.AddressDetails.Guid;

                if (GlobalConfigModel.AddressDict.TryGetValue(guid, out var global))
                {
                    node.AddressDetails = global;
                }
                else
                {
                    GlobalConfigModel.AddressDict[guid] = node.AddressDetails;
                }

                node.UpdateAddressName();
            }

            // 🔥 PluginConfigModel（MQ / 设备）回灌
            if (node.MqDetails != null)
            {
                var guid = node.MqDetails.Guid;

                if (GlobalConfigModel.PluginDict.TryGetValue(guid, out var global))
                {
                    node.MqDetails = global;
                }
                else
                {
                    GlobalConfigModel.PluginDict[guid] = node.MqDetails;
                }

                node.UpdateMqName();
            }

            // 递归子节点
            if (node.Children != null && node.Children.Count > 0)
            {
                foreach (var child in node.Children)
                {
                    RebindDetailNode(child);
                }
            }
        }

        /// <summary>
        /// 获取所有已经存在的
        /// </summary>
        /// <param name="obj"></param>
        public static ObservableCollection<IProjectTreeViewModel> GetAllProject()
        {
            GlobalConfigModel.ProjectDict = ProjectHandlerCore.GetConfig<ObservableCollection<IProjectTreeViewModel>>(GlobalConfigModel.UI_ProjectConfigPath)?.GetSource<ObservableCollection<IProjectTreeViewModel>>() ?? new();
            GlobalConfigModel.ProjectDict.InitChildrenParent();
            GlobalConfigModel.ProjectDict.RebindGlobals();
            return GlobalConfigModel.ProjectDict;
        }


    }
}
