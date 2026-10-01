using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@enum;
using Snet.Iot.Daq.Core.@interface;
using Snet.Utility;
using SQLite;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text;

namespace Snet.Iot.Daq.Core.handler
{

    /// <summary>
    /// 项目树形结构处理器，提供树节点的选中、展开、查找、移除、配置保存与加载等操作。
    /// </summary>
    public static class ProjectHandlerCore
    {
        /// <summary>按真实项目结构检查地址引用，供两端删除前使用；不读取文件，也不匹配无关文本。</summary>
        /// <param name="nodes">项目根节点；调用方负责与项目编辑互斥。</param>
        /// <param name="guid">地址的完整唯一标识，空标识不视为引用。</param>
        /// <returns>任意详情节点引用该地址时返回 true；循环和共享节点仅遍历一次。</returns>
        public static bool IsAddressReferenced(IEnumerable<IProjectTreeViewModel> nodes, string guid)
        {
            if (string.IsNullOrWhiteSpace(guid)) return false;
            var projects = new Stack<IProjectTreeViewModel>(nodes);
            var details = new Stack<IProjectDetailsTreeViewModel>();
            var visitedProjects = new HashSet<IProjectTreeViewModel>(ReferenceEqualityComparer.Instance);
            var visitedDetails = new HashSet<IProjectDetailsTreeViewModel>(ReferenceEqualityComparer.Instance);
            while (projects.TryPop(out var project))
            {
                if (!visitedProjects.Add(project)) continue;
                if (project.Children is not null)
                    foreach (var child in project.Children) projects.Push(child);
                if (project.Details is not null)
                    foreach (var detail in project.Details) details.Push(detail);
            }
            while (details.TryPop(out var detail))
            {
                if (!visitedDetails.Add(detail)) continue;
                if (string.Equals(detail.AddressDetails?.Guid, guid, StringComparison.Ordinal)) return true;
                if (detail.Children is not null)
                    foreach (var child in detail.Children) details.Push(child);
            }
            return false;
        }

        /// <summary>取得项目结构中引用的地址快照，供两端导入回灌；循环及共享节点只访问一次。</summary>
        /// <param name="nodes">项目根集合；调用方负责遍历期间的结构同步。</param>
        /// <returns>真实节点引用的地址列表，不解析描述文本，也不更改节点状态。</returns>
        public static List<IAddressModel> GetReferencedAddresses(IEnumerable<IProjectTreeViewModel> nodes)
        {
            var result = new List<IAddressModel>();
            var projects = new Stack<IProjectTreeViewModel>(nodes);
            var details = new Stack<IProjectDetailsTreeViewModel>();
            var visitedProjects = new HashSet<IProjectTreeViewModel>(ReferenceEqualityComparer.Instance);
            var visitedDetails = new HashSet<IProjectDetailsTreeViewModel>(ReferenceEqualityComparer.Instance);
            while (projects.TryPop(out var project))
            {
                if (!visitedProjects.Add(project)) continue;
                foreach (var child in project.Children) projects.Push(child);
                foreach (var detail in project.Details) details.Push(detail);
            }
            while (details.TryPop(out var detail))
            {
                if (!visitedDetails.Add(detail)) continue;
                if (detail.AddressDetails is not null) result.Add(detail.AddressDetails);
                foreach (var child in detail.Children) details.Push(child);
            }
            return result;
        }

        /// <summary>统计设备详情树中的全部地址节点，包含尚未配置转发插件的点位。</summary>
        /// <param name="nodes">设备详情根节点；为空时返回零。调用方负责结构遍历与修改的同步。</param>
        /// <returns>地址节点数量，不修改选择、展开或父子关系。</returns>
        public static int CountAddressNodes(IEnumerable<IProjectDetailsTreeViewModel>? nodes)
        {
            if (nodes is null) return 0;
            var count = 0;
            var pending = new Stack<IProjectDetailsTreeViewModel>(nodes);
            var visited = new HashSet<IProjectDetailsTreeViewModel>(ReferenceEqualityComparer.Instance);
            while (pending.TryPop(out var node))
            {
                if (!visited.Add(node)) continue;
                if (node.NodeType == ProjectDetailsNodeType.Address) count++;
                if (node.Children is not null)
                    foreach (var child in node.Children) pending.Push(child);
            }
            return count;
        }

        #region IProjectDetailsTreeViewModel
        /// <summary>
        /// 确保整棵树中只有一个节点被选中
        /// （selectedNode 为 null 时全部取消选中）
        /// </summary>
        public static void EnsureSingleSelection(this ObservableCollection<IProjectDetailsTreeViewModel> nodes, IProjectDetailsTreeViewModel? selectedNode = default)
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
        public static void IsExpandedAll(this ObservableCollection<IProjectDetailsTreeViewModel> nodes, bool status = true)
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
        public static void SetExpandedRecursive(this IProjectDetailsTreeViewModel node, bool status)
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
        public static void ExpandParents(this IProjectDetailsTreeViewModel node)
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
        public static void InitChildrenParent(this ObservableCollection<IProjectDetailsTreeViewModel> nodes)
        {
            if (nodes == null)
                return;

            foreach (var node in nodes)
            {
                node.Parent = null;
                InitChildrenParentInternal(node);
            }
        }

        private static void InitChildrenParentInternal(IProjectDetailsTreeViewModel node)
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
        public static IProjectDetailsTreeViewModel? FindByName(this IEnumerable<IProjectDetailsTreeViewModel> roots, string name, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            foreach (var node in roots)
            {
                var found = FindByNameInternal(node, name, comparison);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static IProjectDetailsTreeViewModel? FindByNameInternal(IProjectDetailsTreeViewModel node, string name, StringComparison comparison)
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
        public static List<string> GetAllNames(this IEnumerable<IProjectDetailsTreeViewModel> roots, bool needBase = true)
        {
            var result = new List<string>();

            foreach (var node in roots)
            {
                CollectNameInternal(node, result, needBase);
            }

            return result;
        }
        private static void CollectNameInternal(IProjectDetailsTreeViewModel node, List<string> result, bool needBase)
        {
            bool isLeaf =
                node.Children == null ||
                node.Children.Count == 0;

            // 关键判断
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
        public static bool RemoveNode(this ObservableCollection<IProjectDetailsTreeViewModel> roots, IProjectDetailsTreeViewModel target)
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
        public static IProjectDetailsTreeViewModel? GetFirstSelectItem(ObservableCollection<IProjectDetailsTreeViewModel> list)
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
        #endregion

        #region IProjectTreeViewModel
        /// <summary>
        /// 确保整棵树中只有一个节点被选中
        /// （selectedNode 为 null 时全部取消选中）
        /// </summary>
        public static void EnsureSingleSelection(this ObservableCollection<IProjectTreeViewModel> nodes, IProjectTreeViewModel? selectedNode = default)
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
        public static void IsExpandedAll(this ObservableCollection<IProjectTreeViewModel> nodes, bool status = true)
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
        public static void SetExpandedRecursive(this IProjectTreeViewModel node, bool status)
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
        public static void ExpandParents(this IProjectTreeViewModel node)
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
        public static void InitChildrenParent(this ObservableCollection<IProjectTreeViewModel> nodes)
        {
            if (nodes == null)
                return;

            foreach (var node in nodes)
            {
                node.Parent = null;
                InitChildrenParentInternal(node);
            }
        }

        private static void InitChildrenParentInternal(IProjectTreeViewModel node)
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
        public static IProjectTreeViewModel? FindByName(this IEnumerable<IProjectTreeViewModel> roots, string name, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            foreach (var node in roots)
            {
                var found = FindByNameInternal(node, name, comparison);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static IProjectTreeViewModel? FindByNameInternal(IProjectTreeViewModel node, string name, StringComparison comparison)
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
        public static List<string> GetAllNames(this IEnumerable<IProjectTreeViewModel> roots, bool needBase = true)
        {
            var result = new List<string>();

            foreach (var node in roots)
            {
                CollectNameInternal(node, result, needBase);
            }

            return result;
        }
        private static void CollectNameInternal(IProjectTreeViewModel node, List<string> result, bool needBase)
        {
            bool isLeaf =
                node.Children == null ||
                node.Children.Count == 0;

            // 关键判断
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
        public static bool RemoveNode(this ObservableCollection<IProjectTreeViewModel> roots, IProjectTreeViewModel target)
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
        public static IProjectTreeViewModel? GetFirstSelectItem(ObservableCollection<IProjectTreeViewModel> list)
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
        #endregion

        /// <summary>
        /// 查询设备是否唯一
        /// </summary>
        /// <param name="device">设备</param>
        /// <returns>true 唯一，false 已存在</returns>
        public static bool QueryDeviceUnique(this ObservableCollection<IProjectTreeViewModel> ProjectNode, IProjectTreeViewModel device)
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
        private static bool CheckNodeUnique(IProjectTreeViewModel current, IProjectTreeViewModel target)
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
        public static IProjectTreeViewModel? FindByDaqGuid(this ObservableCollection<IProjectTreeViewModel> projectNodes, PluginConfigModel daqDetails)
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

        private static IProjectTreeViewModel? FindByDaqGuidInternal(IProjectTreeViewModel node, string guid)
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
        /// 异步写入字符串内容到文件<br/>
        /// 使用 UTF-8 编码，覆盖写入模式
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="data">待写入的字符串内容</param>
        public static async Task WriteToFileAsync(string path, string data)
        {
            var destination = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, data, Encoding.UTF8);
                File.Move(temporary, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
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
        /// 从树集合中获取所有 DaqDetails 不为空的节点
        /// </summary>
        /// <param name="source">根节点集合</param>
        /// <returns>符合条件的节点列表</returns>
        public static List<IProjectTreeViewModel> GetAllDeviceNodes(this ObservableCollection<IProjectTreeViewModel> source)
        {
            var result = new List<IProjectTreeViewModel>();

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
        private static void CollectDeviceNode(IProjectTreeViewModel node, List<IProjectTreeViewModel> result)
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
        public static string GetHierarchyPath(this IProjectTreeViewModel model, string separator = " > ")
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
        /// AddressModel -> List<PluginConfigModel> 的并发字典；未绑定 MQ 的地址保留空列表，仍参与采集和 UA 转发。
        /// </summary>
        public static ConcurrentDictionary<IAddressModel, List<PluginConfigModel>> ToAddressMqDictionary(this IEnumerable<IProjectDetailsTreeViewModel> roots)
        {
            var dict = new ConcurrentDictionary<IAddressModel, List<PluginConfigModel>>();

            if (roots == null)
                return dict;

            var pending = new Stack<(IProjectDetailsTreeViewModel Node, IAddressModel? Address)>();
            var visited = new HashSet<IProjectDetailsTreeViewModel>(ReferenceEqualityComparer.Instance);
            foreach (var root in roots) pending.Push((root, null));
            while (pending.TryPop(out var item))
            {
                var node = item.Node;
                if (!visited.Add(node)) continue;
                var address = item.Address;
                if (node.NodeType == ProjectDetailsNodeType.Address && node.AddressDetails is not null)
                {
                    address = node.AddressDetails;
                    dict.TryAdd(address, new List<PluginConfigModel>());
                }
                if (node.NodeType == ProjectDetailsNodeType.Mq && address is not null && node.MqDetails is not null)
                {
                    var list = dict.GetOrAdd(address, static _ => new List<PluginConfigModel>());
                    if (!list.Any(plugin => plugin.Guid == node.MqDetails.Guid)) list.Add(node.MqDetails);
                }
                if (node.Children is not null)
                    foreach (var child in node.Children) pending.Push((child, address));
            }
            return dict;
        }

        /// <summary>
        /// 使用 ToString() 匹配节点，只更新 IsSoftStart，并返回源集合中的对象。<br/>
        /// <b>注意：</b>匹配依赖 <see cref="IProjectTreeViewModel.ToString()"/> 的唯一性，
        /// 若多个节点 ToString() 相同则仅更新第一个命中节点。
        /// 如需严格唯一性匹配，改用基于 Guid/引用的方法。
        /// </summary>
        /// <param name="source">树根集合</param>
        /// <param name="newModel">新的节点数据</param>
        /// <returns>源集合中的 ProjectTreeViewModel，未找到返回 null</returns>
        public static IProjectTreeViewModel? UpdateIsSoftStartByToString(this ObservableCollection<IProjectTreeViewModel> source, IProjectTreeViewModel newModel)
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


        /// <summary>
        /// 带重试机制的异步文件写入。<br/>
        /// 遇到文件被占用（<see cref="IOException"/>）时最多重试 <paramref name="maxRetries"/> 次，
        /// 每次间隔 <paramref name="delayMilliseconds"/> 毫秒；其他异常直接返回失败。
        /// </summary>
        /// <param name="path">目标文件路径</param>
        /// <param name="data">写入内容</param>
        /// <param name="maxRetries">最大重试次数，默认 5</param>
        /// <param name="delayMilliseconds">重试间隔（毫秒），默认 500</param>
        /// <param name="onRetry">可选的重试回调，参数为（当前次数, 异常信息）</param>
        /// <param name="onError">可选的错误回调，参数为异常信息</param>
        /// <returns>true 表示写入成功；false 表示超过重试次数或发生非 IO 异常</returns>
        public static async Task<bool> WriteToFileWithRetryAsync(
            string path,
            string data,
            int maxRetries = 5,
            int delayMilliseconds = 500,
            Action<int, string>? onRetry = null,
            Action<string>? onError = null)
        {
            int retries = 0;

            while (retries < maxRetries)
            {
                try
                {
                    await WriteToFileAsync(path, data);
                    return true;
                }
                catch (IOException ex)
                {
                    retries++;
                    onRetry?.Invoke(retries, ex.Message);
                    if (retries < maxRetries)
                        await Task.Delay(delayMilliseconds);
                }
                catch (Exception ex)
                {
                    onError?.Invoke(ex.Message);
                    return false;
                }
            }

            onError?.Invoke($"文件写入失败，已重试 {maxRetries} 次，文件可能被长时间占用：{path}");
            return false;
        }

        /// <summary>
        /// 批量插入（防重复），基于指定字段查重。<br/>
        /// 先读取数据库中所有已有记录建立 Key 集合，再在事务内插入不重复的项。
        /// </summary>
        /// <typeparam name="T">实体类型（需有无参构造）</typeparam>
        /// <typeparam name="TKey">查重字段类型</typeparam>
        /// <param name="db">SQLite 连接</param>
        /// <param name="dbLock">数据库访问锁（<see cref="SQLiteConnection"/> 非线程安全，由调用方提供）</param>
        /// <param name="items">待插入的数据集合</param>
        /// <param name="onInserted">提交成功并释放数据库锁后逐项执行的可选回调；回滚不调用。回调异常不会撤销已提交事务。</param>
        /// <param name="keySelectors">查重字段选择器，支持多个</param>
        /// <returns>插入结果统计（成功数、重复数、失败数）</returns>
        public static BatchInsertResult InsertUnique<T, TKey>(
            SQLite.SQLiteConnection db,
            object dbLock,
            IEnumerable<T> items,
            Action<T>? onInserted = null,
            params Func<T, TKey>[] keySelectors)
                where T : class, new()
        {
            var result = new BatchInsertResult();
            // 外部通知只能在事务提交且数据库锁释放后执行，回滚时不得暴露未提交实体。
            var inserted = onInserted is null ? null : new List<T>();

            lock (dbLock)
            {
                // 1. 读取已有数据，建立各字段的 Key 集合
                var existingKeys = keySelectors
                    .Select(_ => new HashSet<TKey>())
                    .ToArray();

                foreach (var existing in db.Table<T>())
                {
                    for (int i = 0; i < keySelectors.Length; i++)
                    {
                        var key = keySelectors[i](existing);
                        if (key != null)
                            existingKeys[i].Add(key);
                    }
                }

                // 2. 在事务内插入不重复的项
                db.RunInTransaction(() =>
                {
                    foreach (var item in items)
                    {
                        // 泛型工具仍支持其他实体；地址额外遵循两端一致的业务校验。
                        if (item is IAddressModel address && !AddressStore.IsValid(address))
                        {
                            result.Failed++;
                            continue;
                        }
                        bool isDuplicate = false;

                        for (int i = 0; i < keySelectors.Length; i++)
                        {
                            var key = keySelectors[i](item);
                            if (key != null && existingKeys[i].Contains(key))
                            {
                                isDuplicate = true;
                                break;
                            }
                        }

                        if (isDuplicate)
                        {
                            result.Duplicate++;
                            continue;
                        }

                        if (db.Insert(item) > 0)
                        {
                            result.Success++;
                            inserted?.Add(item);

                            for (int i = 0; i < keySelectors.Length; i++)
                            {
                                var key = keySelectors[i](item);
                                if (key != null)
                                    existingKeys[i].Add(key);
                            }
                        }
                        else
                        {
                            result.Failed++;
                        }
                    }
                });
            }

            if (onInserted is not null)
                foreach (var item in inserted!) onInserted(item);
            return result;
        }
    }
}
