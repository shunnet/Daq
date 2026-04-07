using Snet.Iot.Daq.data;
using Snet.Iot.Daq.@enum;
using Snet.Iot.Daq.@interface;
using Snet.Iot.Daq.view;
using Snet.Iot.Daq.viewModel;
using Snet.Log;
using Snet.Utility;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace Snet.Iot.Daq.handler
{

    /// <summary>
    /// 项目树形结构处理器，提供树节点的选中、展开、查找、移除、配置保存与加载等操作。
    /// </summary>
    public static class ProjectHandler
    {
        /// <summary>
        /// 确保整棵树中只有一个节点被选中
        /// （selectedNode 为 null 时全部取消选中）
        /// </summary>
        public static void EnsureSingleSelection<T>(this ObservableCollection<T> nodes, T? selectedNode = default) where T : class, ITreeViewModel<T>
        {
            if (nodes == null)
                return;

            foreach (var node in nodes)
            {
                node.IsSelected = selectedNode != null && ReferenceEquals(node, selectedNode);
                node.UpdateSpecialData();
                node.Children.EnsureSingleSelection(selectedNode);
            }
        }

        /// <summary>
        /// 展开或折叠整棵树
        /// </summary>
        public static void IsExpandedAll<T>(this ObservableCollection<T> nodes, bool status = true) where T : class, ITreeViewModel<T>
        {
            if (nodes == null)
                return;

            foreach (var node in nodes)
            {
                node.SetExpandedRecursive(status);
            }
        }

        /// <summary>
        /// 递归展开或折叠当前节点及其子节点
        /// </summary>
        public static void SetExpandedRecursive<T>(this T node, bool status) where T : class, ITreeViewModel<T>
        {
            node.IsExpanded = status;

            foreach (var child in node.Children)
            {
                child.SetExpandedRecursive(status);
            }
        }

        /// <summary>
        /// 向上递归展开所有父节点
        /// </summary>
        public static void ExpandParents<T>(this T node) where T : class, ITreeViewModel<T>
        {
            var parent = node.Parent;
            while (parent != null)
            {
                parent.IsExpanded = true;
                parent = parent.Parent;
            }
        }

        /// <summary>
        /// 初始化整棵树的 Parent 关系
        /// </summary>
        public static void InitChildrenParent<T>(this ObservableCollection<T> nodes) where T : class, ITreeViewModel<T>
        {
            if (nodes == null)
                return;

            foreach (var node in nodes)
            {
                node.Parent = null;
                InitChildrenParentInternal(node);
            }
        }

        private static void InitChildrenParentInternal<T>(T node) where T : class, ITreeViewModel<T>
        {
            foreach (var child in node.Children)
            {
                child.Parent = node;
                InitChildrenParentInternal(child);
            }
        }

        /// <summary>
        /// 根据 Name 查找节点（深度优先，返回第一个）
        /// </summary>
        public static T? FindByName<T>(this IEnumerable<T> roots, string name, StringComparison comparison = StringComparison.OrdinalIgnoreCase) where T : class, ITreeViewModel<T>
        {
            foreach (var node in roots)
            {
                var found = FindByNameInternal(node, name, comparison);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static T? FindByNameInternal<T>(T node, string name, StringComparison comparison) where T : class, ITreeViewModel<T>
        {
            if (!string.IsNullOrWhiteSpace(node.Name) &&
                node.Name.Equals(name, comparison))
            {
                return node;
            }

            foreach (var child in node.Children)
            {
                var found = FindByNameInternal(child, name, comparison);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// 获取整棵树中所有节点的 Name
        /// </summary>
        public static List<string> GetAllNames<T>(this IEnumerable<T> roots, bool needBase = true) where T : class, ITreeViewModel<T>
        {
            var result = new List<string>();

            foreach (var node in roots)
            {
                CollectNameInternal(node, result, needBase);
            }

            return result;
        }
        private static void CollectNameInternal<T>(T node, List<string> result, bool needBase) where T : class, ITreeViewModel<T>
        {
            bool isLeaf =
                node.Children == null ||
                node.Children.Count == 0;

            // 🔑 关键判断
            if (!string.IsNullOrWhiteSpace(node.Name))
            {
                if (needBase || !isLeaf)
                {
                    result.Add(node.Name);
                }
            }

            // 叶子节点没有子项，直接返回
            if (isLeaf)
                return;

            foreach (var child in node.Children)
            {
                CollectNameInternal(child, result, needBase);
            }
        }

        /// <summary>
        /// 从树中移除指定节点（自动处理 Parent / Children）
        /// </summary>
        public static bool RemoveNode<T>(this ObservableCollection<T> roots, T target) where T : class, ITreeViewModel<T>
        {
            if (roots == null || target == null)
                return false;

            // 有父级：直接从父级移除
            if (target.Parent != null)
            {
                var removed = target.Parent.Children.Remove(target);
                if (removed)
                {
                    target.Parent.UpdateSpecialData();
                }
                return removed;
            }

            // 根节点
            return roots.Remove(target);
        }

        /// <summary>
        /// 获取第一个被选中的节点（深度优先）
        /// </summary>
        public static T? GetFirstSelectItem<T>(ObservableCollection<T> list) where T : class, ITreeViewModel<T>
        {
            foreach (var node in list)
            {
                if (node.IsSelected)
                    return node;

                if (node.Children?.Count > 0)
                {
                    var child = GetFirstSelectItem(node.Children);
                    if (child != null)
                        return child;
                }
            }
            return null;
        }

        /// <summary>
        /// 查询设备是否唯一
        /// </summary>
        /// <param name="device">设备</param>
        /// <returns>true 唯一，false 已存在</returns>
        public static bool QueryDeviceUnique(this ObservableCollection<ProjectTreeViewModel> ProjectNode, ProjectTreeViewModel device)
        {
            if (device?.DaqDetails == null)
                return true;


            foreach (var item in ProjectNode)
            {
                if (!CheckNodeUnique(item, device))
                    return false;
            }

            return true;
        }
        /// <summary>
        /// 检查当前节点及子节点中是否存在与目标设备 GUID 相同的节点。
        /// </summary>
        /// <param name="current">当前遍历的节点</param>
        /// <param name="target">目标设备节点</param>
        /// <returns>true 表示唯一，false 表示存在重复</returns>
        private static bool CheckNodeUnique(ProjectTreeViewModel current, ProjectTreeViewModel target)
        {
            // 跳过自己（编辑场景）
            if (ReferenceEquals(current, target))
                return true;

            // 只检查设备节点
            if (current.NodeType == ProjectNodeType.Device)
            {
                if (!string.IsNullOrEmpty(current.DaqDetails?.Guid) &&
                    current.DaqDetails.Guid == target.DaqDetails.Guid)
                {
                    return false;
                }
            }

            // 递归检查子节点
            if (current.Children != null)
            {
                foreach (var child in current.Children)
                {
                    if (!CheckNodeUnique(child, target))
                        return false;
                }
            }

            return true;
        }


        /// <summary>
        /// 通过采集设备查询对应的项
        /// </summary>
        /// <param name="projectNodes">项集合</param>
        /// <param name="daqDetails">采集设备</param>
        /// <returns>对应采集设备的项</returns>
        public static ProjectTreeViewModel? FindByDaqGuid(this ObservableCollection<ProjectTreeViewModel> projectNodes, PluginConfigModel daqDetails)
        {
            if (projectNodes == null || daqDetails == null)
                return null;

            foreach (var node in projectNodes)
            {
                var found = FindByDaqGuidInternal(node, daqDetails.Guid);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static ProjectTreeViewModel? FindByDaqGuidInternal(ProjectTreeViewModel node, string guid)
        {
            // 命中当前节点
            if (node.DaqDetails != null && node.DaqDetails.Guid == guid)
                return node;

            // 没有子节点，直接返回
            if (node.Children == null || node.Children.Count == 0)
                return null;

            // 深度优先
            foreach (var child in node.Children)
            {
                var found = FindByDaqGuidInternal(child, guid);
                if (found != null)
                    return found;
            }

            return null;
        }


        /// <summary>
        /// 异步保存项目配置到 JSON 文件<br/>
        /// 包含重试机制（最多5次，间隔500ms），用于处理文件被占用的情况
        /// </summary>
        /// <param name="data">待保存的项目树数据</param>
        /// <param name="path">配置文件保存路径</param>
        public static async Task SaveConfigAsync(ObservableCollection<ProjectTreeViewModel> data, string path)
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
                    await WriteToFileAsync(path, data.ToJson(true));
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
        /// 异步写入字符串内容到文件<br/>
        /// 使用 UTF-8 编码，覆盖写入模式
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="data">待写入的字符串内容</param>
        private static async Task WriteToFileAsync(string path, string data)
        {
            await using var writer = new StreamWriter(path, false, Encoding.UTF8);
            await writer.WriteAsync(data);
        }

        /// <summary>
        /// 从 JSON 文件加载配置对象<br/>
        /// 文件不存在时返回默认值
        /// </summary>
        /// <typeparam name="T">反序列化目标类型</typeparam>
        /// <param name="filePath">配置文件路径</param>
        /// <returns>反序列化后的对象，文件不存在或反序列化失败时返回 default</returns>
        public static T? GetConfig<T>(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return default;
            }
            string json = FileHandler.FileToString(filePath);
            return json.ToJsonEntity<T>();
        }




        /// <summary>
        /// 创建项目详情界面实例及其 ViewModel<br/>
        /// 用于双击树节点时动态创建设备详情页
        /// </summary>
        /// <param name="bossProject">项目树根集合</param>
        /// <param name="project">当前选中的项目节点</param>
        /// <returns>界面实例和 ViewModel 的元组</returns>
        public static (ProjectDetails view, ProjectDetailsModel model) CreateDetails(this ObservableCollection<ProjectTreeViewModel> bossProject, ProjectTreeViewModel project)
        {
            ProjectDetails p = new ProjectDetails();
            ProjectDetailsModel pModel = p.DataContext.GetSource<ProjectDetailsModel>();
            pModel.SetValue(project, bossProject);
            return (p, pModel);
        }

        /// <summary>
        /// 回灌项目详情树节点的全局数据（地址、MQ插件等）
        /// </summary>
        /// <param name="nodes">详情树节点集合</param>
        public static void RebindGlobals(this IEnumerable<ProjectDetailsTreeViewModel> nodes)
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
        public static void RebindGlobals(this ObservableCollection<ProjectTreeViewModel> trees)
        {
            foreach (var node in trees)
            {
                RebindProjectNode(node);
            }
        }

        /// <summary>
        /// 回灌单个项目节点的全局数据（采集设备、地址、MQ 等），并递归处理子节点。
        /// </summary>
        /// <param name="node">待回灌的项目树节点</param>
        public static void RebindProjectNode(this ProjectTreeViewModel node)
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
        public static void RebindDetailNode(this ProjectDetailsTreeViewModel node)
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
        public static ObservableCollection<ProjectTreeViewModel> GetAllProject()
        {
            GlobalConfigModel.ProjectDict = ProjectHandler.GetConfig<ObservableCollection<ProjectTreeViewModel>>(GlobalConfigModel.UI_ProjectConfigPath)?.GetSource<ObservableCollection<ProjectTreeViewModel>>() ?? new();
            GlobalConfigModel.ProjectDict.InitChildrenParent();
            GlobalConfigModel.ProjectDict.RebindGlobals();
            return GlobalConfigModel.ProjectDict;
        }



        /// <summary>
        /// 从树集合中获取所有 DaqDetails 不为空的节点
        /// </summary>
        /// <param name="source">根节点集合</param>
        /// <returns>符合条件的节点列表</returns>
        public static List<ProjectTreeViewModel> GetAllDeviceNodes(this ObservableCollection<ProjectTreeViewModel> source)
        {
            var result = new List<ProjectTreeViewModel>();

            if (source == null || source.Count == 0)
                return result;

            foreach (var node in source)
            {
                CollectDeviceNode(node, result);
            }

            return result;
        }

        /// <summary>
        /// 递归收集 DaqDetails 不为空的节点
        /// </summary>
        /// <param name="node">当前节点</param>
        /// <param name="result">结果集合</param>
        private static void CollectDeviceNode(ProjectTreeViewModel node, List<ProjectTreeViewModel> result)
        {
            if (node == null)
                return;

            // 当前节点是设备节点
            if (node.DaqDetails != null)
            {
                result.Add(node);
            }

            // 递归子节点
            if (node.Children == null || node.Children.Count == 0)
                return;

            foreach (var child in node.Children)
            {
                CollectDeviceNode(child, result);
            }
        }

        /// <summary>
        /// 获取当前节点的完整层级路径
        /// 例如：Root > Group > Device
        /// </summary>
        /// <param name="model">当前节点</param>
        /// <param name="separator">分隔符，默认 " > "</param>
        /// <returns>层级路径字符串</returns>
        public static string GetHierarchyPath(this ProjectTreeViewModel model, string separator = " > ")
        {
            if (model == null)
                return string.Empty;

            var names = new Stack<string>();
            var current = model;

            // 一路向上找 Parent
            while (current != null)
            {
                if (!string.IsNullOrWhiteSpace(current.Name))
                {
                    names.Push(current.Name);
                }
                current = current.Parent;
            }

            return string.Join(separator, names);
        }


        /// <summary>
        /// 将 ProjectDetailsTreeViewModel 树转换为
        /// AddressModel -> List<PluginConfigModel> 的并发字典
        /// </summary>
        public static ConcurrentDictionary<AddressModel, List<PluginConfigModel>> ToAddressMqDictionary(this IEnumerable<ProjectDetailsTreeViewModel> roots)
        {
            var dict = new ConcurrentDictionary<AddressModel, List<PluginConfigModel>>();

            if (roots == null)
                return dict;

            foreach (var root in roots)
            {
                Traverse(root, null, dict);
            }

            return dict;
        }


        /// <summary>
        /// 递归遍历节点
        /// </summary>
        private static void Traverse(ProjectDetailsTreeViewModel node, AddressModel? currentAddress, ConcurrentDictionary<AddressModel, List<PluginConfigModel>> dict)
        {
            if (node == null)
                return;

            // 当前是 Address 节点
            if (node.NodeType == ProjectDetailsNodeType.Address &&
                node.AddressDetails != null)
            {
                currentAddress = node.AddressDetails;
            }

            // 当前是 Mq 节点
            if (node.NodeType == ProjectDetailsNodeType.Mq &&
                currentAddress != null &&
                node.MqDetails != null)
            {
                // 获取或创建 List
                var list = dict.GetOrAdd(currentAddress, _ => new List<PluginConfigModel>());

                // ⚠️ List 本身不是线程安全的，必须加锁
                lock (list)
                {
                    list.Add(node.MqDetails);
                }
            }

            // 递归子节点
            if (node.Children == null || node.Children.Count == 0)
                return;

            foreach (var child in node.Children)
            {
                Traverse(child, currentAddress, dict);
            }
        }




        /// <summary>
        /// 使用 ToString() 匹配节点，
        /// 只更新 IsSoftStart，并返回源集合中的对象
        /// </summary>
        /// <param name="source">树根集合</param>
        /// <param name="newModel">新的节点数据</param>
        /// <returns>源集合中的 ProjectTreeViewModel，未找到返回 null</returns>
        public static ProjectTreeViewModel? UpdateIsSoftStartByToString(this ObservableCollection<ProjectTreeViewModel> source, ProjectTreeViewModel newModel)
        {
            if (source == null || newModel == null)
                return null;

            foreach (var item in source)
            {
                // 当前节点命中
                if (item.ToString() == newModel.ToString())
                {
                    item.IsSoftStart = newModel.IsSoftStart;
                    return item; // ← 关键：返回源对象
                }

                // 递归子节点
                if (item.Children != null && item.Children.Count > 0)
                {
                    var result = item.Children.UpdateIsSoftStartByToString(newModel);
                    if (result != null)
                        return result;
                }
            }

            return null;
        }


    }
}
