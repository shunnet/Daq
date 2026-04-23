using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@enum;
using Snet.Iot.Daq.Core.@interface;
using Snet.Utility;
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
        /// AddressModel -> List<PluginConfigModel> 的并发字典
        /// </summary>
        public static ConcurrentDictionary<IAddressModel, List<PluginConfigModel>> ToAddressMqDictionary(this IEnumerable<IProjectDetailsTreeViewModel> roots)
        {
            var dict = new ConcurrentDictionary<IAddressModel, List<PluginConfigModel>>();

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
        private static void Traverse(IProjectDetailsTreeViewModel node, IAddressModel? currentAddress, ConcurrentDictionary<IAddressModel, List<PluginConfigModel>> dict)
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


    }
}
