/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 * 
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 * 
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Test;
using Snet.Core.handler;
using Snet.Iot.Daq.opc.core;
using Snet.Model.data;
using Snet.Utility;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Range = Opc.Ua.Range;

namespace Snet.Iot.Daq.opc.ua.service.core.ReferenceServer
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class ReferenceNodeManager : CustomNodeManager2
    {

        #region 私有写法
        private ServerConfiguration m_configuration;
        private List<BaseDataVariableState> m_staticNodes = [];
        /// <summary>
        /// 第一次的地址空间
        /// </summary>
        private FolderState folderState;
        /// <summary>
        /// 自动创建地址
        /// </summary>
        private bool AutoCreateAddress;
        /// <summary>
        /// 地址空间名称
        /// </summary>
        private string AddressSpaceName;

        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public ReferenceNodeManager(IServerInternal server, ApplicationConfiguration configuration, bool AutoCreateAddress, string AddressSpaceName)
            : base(server, configuration, Namespaces.ReferenceServer)
        {
            SystemContext.NodeIdFactory = this;

            // 获取节点管理器的配置。
            m_configuration = configuration.ServerConfiguration;

            // 如果不存在配置，请使用适当的默认值
            if (m_configuration == null)
            {
                m_configuration = new ServerConfiguration();
            }
            this.AutoCreateAddress = AutoCreateAddress;
            this.AddressSpaceName = AddressSpaceName;
        }

        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.  
        /// </remarks>
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                if (!externalReferences.TryGetValue(
                   ObjectIds.ObjectsFolder,
                   out IList<IReference> references))
                {
                    externalReferences[ObjectIds.ObjectsFolder] = references = [];
                }

                folderState = CreateFolder(null, AddressSpaceName, AddressSpaceName);
                folderState.Description = "公共地址空间";
                folderState.AddReference(ReferenceTypes.Organizes, true, ObjectIds.ObjectsFolder);
                references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, folderState.NodeId));
                folderState.EventNotifier = EventNotifiers.SubscribeToEvents;

                base.AddRootNotifier(this.folderState);

                List<BaseDataVariableState> variables = new List<BaseDataVariableState>();

                try
                {
                    if (AutoCreateAddress)
                    {
                        ResetRandomGenerator(1);
                        string Json = string.Empty;
                        if (!File.Exists(m_configuration.NodeManagerSaveFile))   //如果不存在 则生成固定
                        {
                            //创建默认的配置
                            string[] datatype = { "Boolean", "Byte", "ByteString", "DateTime", "Double", "Duration", "Float", "Guid", "Int16", "Int32", "Int64", "Integer", "LocaleId", "LocalizedText", "NodeId", "Number", "QualifiedName", "SByte", "String", "UInt16", "UInt32", "UInt64", "UInteger", "UtcTime", "XmlElement" };
                            //静态
                            NodeBody Devices_Static = new NodeBody() { Name = "静态常量", Description = "测试节点", CreateTime = DateTime.Now.ToString() };
                            //节点
                            List<NodeBody> nodes_Static = new List<NodeBody>();
                            //循环添加节点
                            foreach (var item in datatype)
                            {
                                nodes_Static.Add(new NodeBody() { Name = $"{item}_Static", DataType = item, Description = "静态常量", CreateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff") });
                            }
                            //数据赋值
                            Devices_Static.Nodes = nodes_Static;

                            //动态
                            NodeBody Devices_Dynamic = new NodeBody() { Name = "动态常量", Description = "测试节点", CreateTime = DateTime.Now.ToString() };
                            //节点
                            List<NodeBody> nodes_Dynamic = new List<NodeBody>();
                            //循环添加节点
                            foreach (var item in datatype)
                            {
                                nodes_Dynamic.Add(new NodeBody() { Name = $"{item}_Dynamic", DataType = item, Description = "动态常量", CreateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff"), Dynamic = true });
                            }
                            //数据赋值
                            Devices_Dynamic.Nodes = nodes_Dynamic;

                            //大量数据测试
                            NodeBody many = new NodeBody() { Name = "大量数据", Description = "测试节点", CreateTime = DateTime.Now.ToString() };
                            //节点
                            List<NodeBody> nodes_Many = new List<NodeBody>();
                            for (int i = 0; i < 1000; i++)
                            {
                                foreach (var item in datatype)
                                {
                                    nodes_Many.Add(new NodeBody() { Name = $"Many{i + 1}_{item}", DataType = item, Description = "常量集合", CreateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff") });
                                }
                            }
                            //数据赋值
                            many.Nodes = nodes_Many;

                            //创建车间
                            NodeBody nodeStructuralBody = new NodeBody() { Name = "测试数据", CreateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.ffffff"), Description = $"{AddressSpaceName} 测试节点" };
                            nodeStructuralBody.Nodes = new List<NodeBody>();
                            nodeStructuralBody.Nodes.Add(Devices_Static);
                            nodeStructuralBody.Nodes.Add(Devices_Dynamic);
                            nodeStructuralBody.Nodes.Add(many);
                            //设置json序列化反序列化格式
                            JsonSerializerSettings JsonSetting = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };
                            //当有空数据时不做解析，且进行缩进处理
                            Json = JsonConvert.SerializeObject(nodeStructuralBody, Newtonsoft.Json.Formatting.None, JsonSetting);
                            //写入文件
                            FileHandler.StringToFile(m_configuration.NodeManagerSaveFile, Json);
                        }
                        Json = FileHandler.FileToString(m_configuration.NodeManagerSaveFile);  //读取文件
                        NodeBody body = Json.ToJsonEntity<NodeBody>() ?? new NodeBody();  //JSON反序列化
                        FolderState folder = CreateFolder(body, null);
                        AddPredefinedNode(SystemContext, folder);
                        m_simulationTimer = new Timer(DoSimulation, null, 1000, 1000);
                        references.Add(new NodeStateReference(ReferenceTypes.Organizes, false, folderState.NodeId));
                    }
                }
                catch (Exception e)
                {
                    m_logger.Log(LogLevel.Error, e, "Error creating the ReferenceNodeManager address space.");
                }

                AddPredefinedNode(SystemContext, folderState);

                // 重置随机生成器并生成边界值
                ResetRandomGenerator(100, 1);
                m_simulationTimer = new Timer(DoSimulation, null, 1000, 1000);

            }
        }

        /// <summary>
        /// 创建文件夹
        /// </summary>
        /// <param name="forlderName">文件夹名称</param>
        /// <param name="fs">文件夹状态</param>
        /// <returns>返回文件夹状态</returns>
        public FolderState CreateFolder(string forlderName, FolderState? fs = null, string? des = null)
        {
            if (fs == null)
            {
                fs = folderState;
            }
            FolderState scalarFolder = CreateFolder(fs, forlderName, des);
            AddPredefinedNode(SystemContext, scalarFolder);
            return scalarFolder;
        }

        /// <summary>
        /// 创建节点
        /// </summary>
        public void CreateNode(NodeBody node, FolderState folder)
        {
            if (node.Dynamic)  //创建动态
            {
                //通过反射找到对应的属性并获取它的值
                CreateDynamicVariable(folder, node.Name, node.Description, (NodeId)typeof(DataTypeIds).GetField(node.DataType).GetValue(new object()), ValueRanks.Scalar, node.AccessLevel);
            }
            else  //创建静态
            {
                //通过反射找到对应的属性并获取它的值
                CreateVariable(folder, node.Name, node.Description, (NodeId)typeof(DataTypeIds).GetField(node.DataType).GetValue(new object()), ValueRanks.Scalar, accessLevel: node.AccessLevel);
            }
        }

        /// <summary>
        /// 创建一个动态变量
        /// </summary>
        public BaseDataVariableState CreateDynamicVariable(NodeState parent, string name, string des, BuiltInType dataType, int valueRank, byte accessLevel = 3)
        {
            return CreateDynamicVariable(parent, name, des, (uint)dataType, valueRank, accessLevel);
        }

        /// <summary>
        /// 创建一个动态变量
        /// </summary>
        public BaseDataVariableState CreateDynamicVariable(NodeState parent, string name, string des, NodeId dataType, int valueRank, byte accessLevel = 3)
        {
            BaseDataVariableState variable = CreateVariable(parent, name, des, dataType, valueRank, accessLevel: accessLevel);
            m_dynamicNodes.Add(variable);
            return variable;
        }

        /// <summary>
        /// 创建一个新变量
        /// </summary>
        public BaseDataVariableState CreateVariable(NodeState parent, string name, string des, BuiltInType dataType, int valueRank, bool ini = true, object? value = null, byte accessLevel = 3)
        {
            return CreateVariable(parent, name, des, (uint)dataType, valueRank, ini, value, accessLevel);
        }

        /// <summary>
        /// 创建一个新变量
        /// </summary>
        public BaseDataVariableState CreateVariable(NodeState parent, string name, string des, NodeId dataType, int valueRank, bool ini = true, object? value = null, byte accessLevel = 3)
        {
            BaseDataVariableState variable = new BaseDataVariableState(parent);

            string newName = $"{parent.NodeId.Identifier}.{name}";

            variable.SymbolicName = name;
            variable.ReferenceTypeId = ReferenceTypes.Organizes;
            variable.TypeDefinitionId = VariableTypeIds.BaseDataVariableType;
            variable.NodeId = new NodeId(newName, NamespaceIndex);
            variable.BrowseName = new QualifiedName(name, NamespaceIndex);
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description;
            variable.UserWriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description;
            variable.DataType = dataType;
            variable.ValueRank = valueRank;
            variable.AccessLevel = accessLevel;
            variable.UserAccessLevel = accessLevel;
            variable.Historizing = false;
            variable.Value = ini ? GetNewValue(variable) : null;
            variable.StatusCode = ini ? StatusCodes.Good : StatusCodes.Bad;
            variable.Description = des;
            if (value != null)
            {
                variable.Value = value;
                variable.StatusCode = StatusCodes.Good;
            }
            variable.Timestamp = DateTime.UtcNow;

            if (valueRank == ValueRanks.OneDimension)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0 });
            }
            else if (valueRank == ValueRanks.TwoDimensions)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>(new List<uint> { 0, 0 });
            }

            if (parent != null)
            {
                parent.AddChild(variable);
            }

            m_staticNodes.Add(variable);

            return variable;
        }

        /// <summary>
        /// Creates a new folder.
        /// </summary>
        private FolderState CreateFolder(NodeState parent, string name, string des)
        {
            FolderState folder = new FolderState(parent);

            folder.SymbolicName = name;
            folder.ReferenceTypeId = ReferenceTypes.Organizes;
            folder.TypeDefinitionId = ObjectTypeIds.FolderType;


            if (parent != null)
            {
                string newName = $"{parent.NodeId.Identifier}.{name}";
                folder.NodeId = new NodeId(newName, NamespaceIndex);
            }
            else
            {
                folder.NodeId = new NodeId(name, NamespaceIndex);
            }
            folder.BrowseName = new QualifiedName(name, NamespaceIndex);
            folder.DisplayName = new LocalizedText("en", name);
            folder.WriteMask = AttributeWriteMask.None;
            folder.UserWriteMask = AttributeWriteMask.None;
            folder.EventNotifier = EventNotifiers.None;
            folder.Description = des;

            if (parent != null)
            {
                parent.AddChild(folder);
            }

            return folder;
        }

        /// <summary>
        /// 通过结构体创建地址
        /// </summary>
        /// <param name="node">结构体</param>
        /// <param name="fs">文件夹</param>
        /// <returns></returns>
        public OperateResult StructuralBodyCreateAddress(NodeBody node, FolderState? fs = null)
        {
            try
            {
                FolderState folder = CreateFolder(node, fs);
                if (folder != null)
                {
                    AddPredefinedNode(SystemContext, folder);
                    return new OperateResult(true, "通过结构体创建地址成功", new Random().Next(1, 50), folder);
                }
                return new OperateResult(false, "通过结构体创建地址失败，未正常返回“FolderState”", new Random().Next(1, 50), folder);
            }
            catch (Exception ex)
            {
                return new OperateResult(false, $"通过结构体创建地址异常：{ex.Message}", new Random().Next(1, 50));
            }
        }


        /// <summary>
        /// 创建地址
        /// </summary>
        public FolderState CreateFolder(NodeBody node, FolderState? folder = null)
        {
            if (folder == null)
            {
                folder = folderState;
            }
            if (node.Nodes != null && node.Nodes.Count > 0)
            {
                FolderState scalarFolder = CreateFolder(folder, node.Name, node.Description);
                foreach (var item in node.Nodes)
                {
                    CreateFolder(item, scalarFolder);
                }
                AddPredefinedNode(SystemContext, scalarFolder);
                return scalarFolder;
            }
            else
            {
                CreateNode(node, folder);
                return folder;
            }
        }

        /// <summary>
        /// 移除文件夹与文件夹下的节点地址
        /// </summary>
        /// <param name="folderNameArray">文件夹名称集合</param>
        /// <returns>统一出参</returns>
        public OperateResult RemoveFolder(List<NodeId> folderNameArray)
        {
            try
            {
                List<string> FailMessage = new List<string>();
                foreach (var item in folderNameArray)
                {
                    bool state = DeleteNode(SystemContext, item);
                    if (!state)
                    {
                        FailMessage.Add($"{item} 移除失败");
                    }
                    else
                    {
                        List<NodeId> nodeIds = GetFolderAddress(folderNameArray);
                        if (nodeIds.Count > 0)
                        {
                            foreach (var nodeid in nodeIds)
                            {
                                if (!RemoveNodeId(nodeid, null))
                                {
                                    FailMessage.Add($"{nodeid.ToString()} 移除失败");
                                }
                            }
                        }
                    }
                }
                return new OperateResult(true, "移除成功", new Random().Next(1, 50));
            }
            catch (Exception ex)
            {
                return new OperateResult(false, $"移除异常：{ex.Message}", new Random().Next(1, 50));
            }
        }


        /// <summary>
        /// 创建地址
        /// </summary>
        /// <param name="addressArray">地址集合</param>
        /// <param name="fs">文件夹状态</param>
        public OperateResult CreateAddress(List<AddressBody> addressArray, FolderState? fs = null)
        {
            try
            {
                if (fs == null)
                {
                    fs = folderState;
                }
                List<string> FailMessage = new List<string>();
                for (int i = 0; i < addressArray.Count; i++)
                {
                    //判断地址是否存在
                    string name = $"{fs.NodeId}.{addressArray[i].AddressName}";
                    BaseDataVariableState? bdvs = null;
                    if (addressArray[i].Dynamic)
                    {
                        bdvs = m_dynamicNodes.FirstOrDefault(c => c.NodeId.ToString() == name);
                        if (bdvs == null)
                        {
                            CreateDynamicVariable(fs, addressArray[i].AddressName, addressArray[i].Description, addressArray[i].DataType, ValueRanks.Scalar, addressArray[i].AccessLevel);
                        }
                        else
                        {
                            FailMessage.Add($"{addressArray[i].AddressName} 已存在");
                        }
                    }
                    else
                    {
                        bdvs = m_staticNodes.FirstOrDefault(c => c.NodeId.ToString() == name);
                        if (bdvs == null)
                        {
                            CreateVariable(fs, addressArray[i].AddressName, addressArray[i].Description, addressArray[i].DataType, ValueRanks.Scalar, false, addressArray[i].DefaultValue, addressArray[i].AccessLevel);
                        }
                        else
                        {
                            FailMessage.Add($"{addressArray[i].AddressName} 已存在");
                        }
                    }
                }
                AddPredefinedNode(SystemContext, fs);
                if (FailMessage.Count > 0)
                {
                    return new OperateResult(false, FailMessage.ToJson(true), new Random().Next(1, 50), addressArray);
                }
                return new OperateResult(true, "地址创建成功", new Random().Next(1, 50), addressArray);
            }
            catch (Exception ex)
            {
                return new OperateResult(false, $"地址创建异常：{ex.Message}", new Random().Next(1, 50));
            }
        }

        /// <summary>
        /// 读取点
        /// </summary>
        /// <param name="address">地址集合</param>
        /// <returns>统一出参</returns>
        public OperateResult ReadAddress(Address address)
        {
            try
            {
                List<string> FailMessage = new List<string>();
                //先组织值
                List<ReadValueId> readValueIds = new List<ReadValueId>();
                List<DataValue> dataValues = new List<DataValue>();
                List<ServiceResult> serviceResults = new List<ServiceResult>();
                foreach (var item in address.AddressArray)
                {
                    NodeId? nodeId = GetNodeId(item.AddressName, null);
                    if (nodeId != null)
                    {
                        readValueIds.Add(new ReadValueId() { NodeId = nodeId, AttributeId = Attributes.Value });
                        dataValues.Add(new DataValue());
                        serviceResults.Add(new ServiceResult(new StatusCode()));
                    }
                    else
                    {
                        FailMessage.Add($"{item.AddressName} 读取失败，不存在此地址");
                    }
                }
                Read(SystemContext.OperationContext, 10000, readValueIds, dataValues, serviceResults);
                //键值集合
                //节点数据
                ConcurrentDictionary<string, AddressValue> param = new ConcurrentDictionary<string, AddressValue>();
                for (int i = 0; i < dataValues.Count(); i++)
                {
                    if (!StatusCode.IsGood(serviceResults[i].StatusCode))
                    {
                        //说明写入失败
                        FailMessage.Add($"{readValueIds[i].NodeId.ToString()} 读取失败");
                    }
                    else
                    {
                        //数据处理
                        AddressValue? addressValue = AddressHandler.ExecuteDispose(address.AddressArray[i], dataValues[i].Value, string.IsNullOrEmpty(dataValues[i].Value?.ToString()) ? "失败:" + dataValues[i].StatusCode.ToString() : "成功");

                        //数据添加
                        param.AddOrUpdate(address.AddressArray[i].AddressName, addressValue, (k, v) => addressValue);
                    }
                }
                if (FailMessage.Count > 0)
                {
                    return new OperateResult(false, FailMessage.ToJson(true), new Random().Next(1, 50), param);
                }
                return new OperateResult(true, "读取成功", new Random().Next(1, 50), param);
            }
            catch (Exception ex)
            {
                return new OperateResult(false, $"读取异常：{ex.Message}", new Random().Next(1, 50));
            }
        }

        /// <summary>
        /// 写入点
        /// </summary>
        /// <typeparam name="V">类型</typeparam>
        /// <param name="Values">键值集合</param>
        /// <returns>统一出参</returns>
        public OperateResult WriteAddress<V>(ConcurrentDictionary<string, V> values)
        {
            if (values == null || values.Count == 0)
                return new OperateResult(false, "写入集合为空", Random.Shared.Next(1, 50));

            try
            {
                int capacity = values.Count;

                // 预设容量，避免扩容
                List<string> failMessages = new(capacity);
                List<WriteValue> writeValues = new(capacity);
                List<ServiceResult> serviceResults = new(capacity);

                foreach (var item in values)
                {
                    NodeId? nodeId = GetNodeId(item.Key, null);
                    if (nodeId == null)
                    {
                        failMessages.Add($"{item.Key} 写入失败，不存在此地址");
                        continue;
                    }

                    writeValues.Add(new WriteValue
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        Value = new DataValue
                        {
                            WrappedValue = new Variant(item.Value)
                        }
                    });

                    // 直接使用默认构造，不创建 StatusCode 对象
                    serviceResults.Add(new ServiceResult(new StatusCode()));
                }

                // 没有可写数据，直接返回
                if (writeValues.Count == 0)
                {
                    return new OperateResult(false,
                        failMessages.Count > 0 ? failMessages.ToJson(true) : "无可写节点",
                        Random.Shared.Next(1, 50),
                        failMessages);
                }

                // 批量写入
                Write(SystemContext.OperationContext, writeValues, serviceResults);

                // 用 Count 属性，不用 LINQ Count()
                for (int i = 0; i < serviceResults.Count; i++)
                {
                    if (!StatusCode.IsGood(serviceResults[i].StatusCode))
                    {
                        failMessages.Add(
                            $"{writeValues[i].NodeId} 写入失败，状态码：{serviceResults[i].StatusCode.Code}");
                    }
                }

                if (failMessages.Count > 0)
                {
                    return new OperateResult(false,
                        failMessages.ToJson(true),
                        Random.Shared.Next(1, 50),
                        failMessages);
                }

                return new OperateResult(true, "写入成功", Random.Shared.Next(1, 50));
            }
            catch (Exception ex)
            {
                return new OperateResult(false,
                    $"写入异常：{ex.Message}",
                    Random.Shared.Next(1, 50));
            }
        }


        /// <summary>
        /// 移除地址
        /// </summary>
        /// <param name="addressArray">地址集合</param>
        public OperateResult RemoveAddress(List<AddressBody> addressArray)
        {
            try
            {
                List<string> FailMessage = new List<string>();
                for (int i = 0; i < addressArray.Count; i++)
                {
                    NodeId? nodeId = GetNodeId(addressArray[i].AddressName, addressArray[i].Dynamic);
                    if (nodeId != null)
                    {
                        bool state = DeleteNode(SystemContext, nodeId);
                        if (!state)
                        {
                            FailMessage.Add($"{addressArray[i].AddressName} 移除失败");
                        }
                        else
                        {
                            state = RemoveNodeId(nodeId);
                            if (!state)
                            {
                                FailMessage.Add($"{addressArray[i].AddressName} 移除失败");
                            }
                        }
                    }
                    else
                    {
                        FailMessage.Add($"{addressArray[i].AddressName} 移除失败，不存在此地址");
                    }
                }
                if (FailMessage.Count() > 0)
                {
                    return new OperateResult(false, FailMessage.ToJson(true), new Random().Next(1, 50));
                }
                return new OperateResult(true, "移除成功", new Random().Next(1, 50));
            }
            catch (Exception ex)
            {
                return new OperateResult(false, $"移除异常：{ex.Message}", new Random().Next(1, 50));
            }
        }

        /// <summary>
        /// 获取nodeid
        /// </summary>
        /// <param name="addressName">地址全称</param>
        /// <param name="dynamic">是不是动态的</param>
        /// <returns></returns>
        public NodeId? GetNodeId(string addressName, bool? dynamic = false)
        {
            if (dynamic == true)
            {
                //是动态的
                if (m_dynamicNodes.Count > 0)
                {
                    return m_dynamicNodes.FirstOrDefault(c => c.NodeId.ToString() == addressName)?.NodeId ?? null;
                }
            }
            else if (dynamic == false)
            {
                //不是动态的
                if (m_staticNodes.Count > 0)
                {
                    return m_staticNodes.FirstOrDefault(c => c.NodeId.ToString() == addressName)?.NodeId ?? null;
                }
            }
            else
            {
                if (m_staticNodes.Count > 0)
                {
                    return m_staticNodes.FirstOrDefault(c => c.NodeId.ToString() == addressName)?.NodeId ?? null;
                }
                if (m_dynamicNodes.Count > 0)
                {
                    return m_dynamicNodes.FirstOrDefault(c => c.NodeId.ToString() == addressName)?.NodeId ?? null;
                }
            }
            return null;
        }
        /// <summary>
        /// 移除nodeid
        /// </summary>
        /// <param name="nodeId">nodeid</param>
        /// <param name="dynamic">是不是动态的，null就是动态静态都删一遍</param>
        /// <returns></returns>
        public bool RemoveNodeId(NodeId nodeId, bool? dynamic = false)
        {
            if (dynamic == true)
            {
                lock (m_dynamicNodes)
                {
                    //是动态的
                    if (m_dynamicNodes.Count > 0)
                    {
                        BaseDataVariableState? baseData = m_staticNodes.FirstOrDefault(c => c.NodeId == nodeId);
                        if (baseData != null)
                        {
                            return m_dynamicNodes.Remove(baseData);
                        }
                    }
                }
            }
            else if (dynamic == false)
            {
                lock (m_staticNodes)
                {
                    //不是动态的
                    if (m_staticNodes.Count > 0)
                    {
                        BaseDataVariableState? baseData = m_staticNodes.FirstOrDefault(c => c.NodeId == nodeId);
                        if (baseData != null)
                        {
                            return m_staticNodes.Remove(baseData);
                        }
                    }
                }
            }
            else
            {
                lock (m_staticNodes)
                {
                    if (m_staticNodes.Count > 0)
                    {
                        BaseDataVariableState? baseData = m_staticNodes.FirstOrDefault(c => c.NodeId == nodeId);
                        if (baseData != null)
                        {
                            return m_staticNodes.Remove(baseData);
                        }
                    }
                }
                lock (m_dynamicNodes)
                {
                    if (m_dynamicNodes.Count > 0)
                    {
                        BaseDataVariableState? baseData = m_staticNodes.FirstOrDefault(c => c.NodeId == nodeId);
                        if (baseData != null)
                        {
                            return m_dynamicNodes.Remove(baseData);
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 获取文件夹内的地址
        /// </summary>
        /// <returns>返回nodeid集合</returns>
        public List<NodeId> GetFolderAddress(List<NodeId> folderNameArray)
        {
            List<NodeId> nodeIds = new List<NodeId>();
            foreach (NodeId folderName in folderNameArray)
            {
                if (m_staticNodes.Count > 0)
                {
                    List<BaseDataVariableState> baseDatas = m_staticNodes.Where(s => s.NodeId.ToString().Contains(folderName.ToString())).ToList();
                    foreach (var item in baseDatas)
                    {
                        nodeIds.Add(item.NodeId);
                    }
                }
                if (m_dynamicNodes.Count > 0)
                {
                    List<BaseDataVariableState> baseDatas = m_dynamicNodes.Where(s => s.NodeId.ToString().Contains(folderName.ToString())).ToList();
                    foreach (var item in baseDatas)
                    {
                        nodeIds.Add(item.NodeId);
                    }
                }
            }
            return nodeIds;
        }

        /// <summary>
        /// 获取地址集合
        /// </summary>
        /// <returns></returns>
        public List<string> GetAddressArray()
        {
            List<string> strings = new List<string>();
            foreach (var item in m_staticNodes)
            {
                strings.Add(item.NodeId.ToString());
            }
            foreach (var item in m_dynamicNodes)
            {
                strings.Add(item.NodeId.ToString());
            }
            return strings;
        }

        #endregion 私有写法







        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public ReferenceNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            bool useSamplingGroups = false)
            : base(
                  server,
                  configuration,
                  useSamplingGroups,
                  server.Telemetry.CreateLogger<ReferenceNodeManager>(),
                  Namespaces.ReferenceServer)
        {
            SystemContext.NodeIdFactory = this;

            // use suitable defaults if no configuration exists.
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // TBD

                Utils.SilentDispose(m_simulationTimer);
                m_simulationTimer = null;
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            if (node is BaseInstanceState instance &&
                instance.Parent != null &&
                instance.Parent.NodeId.Identifier is string id)
            {
                return new NodeId(
                    id + "_" + instance.SymbolicName,
                    instance.Parent.NodeId.NamespaceIndex);
            }

            return node.NodeId;
        }

        private static bool IsAnalogType(BuiltInType builtInType)
        {
            switch (builtInType)
            {
                case BuiltInType.Byte:
                case BuiltInType.UInt16:
                case BuiltInType.UInt32:
                case BuiltInType.UInt64:
                case BuiltInType.SByte:
                case BuiltInType.Int16:
                case BuiltInType.Int32:
                case BuiltInType.Int64:
                case BuiltInType.Float:
                case BuiltInType.Double:
                    return true;
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    return false;
                default:
                    Debug.Fail($"Unexpected BuiltInType {builtInType}");
                    return false;
            }
        }

        private static Range GetAnalogRange(BuiltInType builtInType)
        {
            switch (builtInType)
            {
                case BuiltInType.UInt16:
                    return new Range(ushort.MaxValue, ushort.MinValue);
                case BuiltInType.UInt32:
                    return new Range(uint.MaxValue, uint.MinValue);
                case BuiltInType.UInt64:
                    return new Range(ulong.MaxValue, ulong.MinValue);
                case BuiltInType.SByte:
                    return new Range(sbyte.MaxValue, sbyte.MinValue);
                case BuiltInType.Int16:
                    return new Range(short.MaxValue, short.MinValue);
                case BuiltInType.Int32:
                    return new Range(int.MaxValue, int.MinValue);
                case BuiltInType.Int64:
                    return new Range(long.MaxValue, long.MinValue);
                case BuiltInType.Float:
                    return new Range(float.MaxValue, float.MinValue);
                case BuiltInType.Double:
                    return new Range(double.MaxValue, double.MinValue);
                case BuiltInType.Byte:
                    return new Range(byte.MaxValue, byte.MinValue);
                case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                    return new Range(sbyte.MaxValue, sbyte.MinValue);
                default:
                    Debug.Fail($"Unexpected BuiltInType {builtInType}");
                    return new Range(sbyte.MaxValue, sbyte.MinValue);
            }
        }
        private ServiceResult OnWriteInterval(
            ISystemContext context,
            NodeState node,
            ref object value)
        {
            try
            {
                m_simulationInterval = (ushort)value;

                if (m_simulationEnabled)
                {
                    m_simulationTimer.Change(100, m_simulationInterval);
                }

                return ServiceResult.Good;
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Error writing Interval variable.");
                return ServiceResult.Create(e, StatusCodes.Bad, "Error writing Interval variable.");
            }
        }

        private ServiceResult OnWriteEnabled(
            ISystemContext context,
            NodeState node,
            ref object value)
        {
            try
            {
                m_simulationEnabled = (bool)value;

                if (m_simulationEnabled)
                {
                    m_simulationTimer.Change(100, m_simulationInterval);
                }
                else
                {
                    m_simulationTimer.Change(100, 0);
                }

                return ServiceResult.Good;
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Error writing Enabled variable.");
                return ServiceResult.Create(e, StatusCodes.Bad, "Error writing Enabled variable.");
            }
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private BaseDataVariableState CreateMeshVariable(
            NodeState parent,
            string path,
            string name,
            params NodeState[] peers)
        {
            BaseDataVariableState variable = CreateVariable(
                parent,
                path,
                name,
                BuiltInType.Double,
                ValueRanks.Scalar);

            if (peers != null)
            {
                foreach (NodeState peer in peers)
                {
                    peer.AddReference(ReferenceTypes.HasCause, false, variable.NodeId);
                    variable.AddReference(ReferenceTypes.HasCause, true, peer.NodeId);
                    peer.AddReference(ReferenceTypes.HasEffect, true, variable.NodeId);
                    variable.AddReference(ReferenceTypes.HasEffect, false, peer.NodeId);
                }
            }

            return variable;
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private DataItemState CreateDataItemVariable(
            NodeState parent,
            string path,
            string name,
            BuiltInType dataType,
            int valueRank)
        {
            var variable = new DataItemState(parent);
            variable.ValuePrecision = new PropertyState<double>(variable);
            variable.Definition = new PropertyState<string>(variable);

            variable.Create(SystemContext, null, variable.BrowseName, null, true);

            variable.SymbolicName = name;
            variable.ReferenceTypeId = ReferenceTypes.Organizes;
            variable.NodeId = new NodeId(path, NamespaceIndex);
            variable.BrowseName = new QualifiedName(path, NamespaceIndex);
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.None;
            variable.UserWriteMask = AttributeWriteMask.None;
            variable.DataType = (uint)dataType;
            variable.ValueRank = valueRank;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.Value = TypeInfo.GetDefaultValue((uint)dataType, valueRank, Server.TypeTree);
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;

            if (valueRank == ValueRanks.OneDimension)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>([0]);
            }
            else if (valueRank == ValueRanks.TwoDimensions)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>([0, 0]);
            }

            variable.ValuePrecision.Value = 2;
            variable.ValuePrecision.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.ValuePrecision.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Definition.Value = string.Empty;
            variable.Definition.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Definition.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            parent?.AddChild(variable);

            return variable;
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private AnalogItemState CreateAnalogItemVariable(
            NodeState parent,
            string path,
            string name,
            BuiltInType dataType,
            int valueRank)
        {
            return CreateAnalogItemVariable(parent, path, name, dataType, valueRank, null);
        }

        private AnalogItemState CreateAnalogItemVariable(
            NodeState parent,
            string path,
            string name,
            BuiltInType dataType,
            int valueRank,
            object initialValues)
        {
            return CreateAnalogItemVariable(
                parent,
                path,
                name,
                dataType,
                valueRank,
                initialValues,
                null);
        }

        private AnalogItemState CreateAnalogItemVariable(
            NodeState parent,
            string path,
            string name,
            BuiltInType dataType,
            int valueRank,
            object initialValues,
            Range customRange)
        {
            return CreateAnalogItemVariable(
                parent,
                path,
                name,
                (uint)dataType,
                valueRank,
                initialValues,
                customRange);
        }

        private AnalogItemState CreateAnalogItemVariable(
            NodeState parent,
            string path,
            string name,
            NodeId dataType,
            int valueRank,
            object initialValues,
            Range customRange)
        {
            var variable = new AnalogItemState(parent)
            {
                BrowseName = new QualifiedName(path, NamespaceIndex)
            };
            variable.EngineeringUnits = new PropertyState<EUInformation>(variable);
            variable.InstrumentRange = new PropertyState<Range>(variable);

            variable.Create(
                SystemContext,
                new NodeId(path, NamespaceIndex),
                variable.BrowseName,
                null,
                true);

            variable.NodeId = new NodeId(path, NamespaceIndex);
            variable.SymbolicName = name;
            variable.DisplayName = new LocalizedText("en", name);
            variable.WriteMask = AttributeWriteMask.None;
            variable.UserWriteMask = AttributeWriteMask.None;
            variable.ReferenceTypeId = ReferenceTypes.Organizes;
            variable.DataType = dataType;
            variable.ValueRank = valueRank;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;

            if (valueRank == ValueRanks.OneDimension)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>([0]);
            }
            else if (valueRank == ValueRanks.TwoDimensions)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>([0, 0]);
            }

            BuiltInType builtInType = TypeInfo.GetBuiltInType(dataType, Server.TypeTree);

            // Simulate a mV Voltmeter
            Range newRange = GetAnalogRange(builtInType);
            // Using anything but 120,-10 fails a few tests
            newRange.High = Math.Min(newRange.High, 120);
            newRange.Low = Math.Max(newRange.Low, -10);
            variable.InstrumentRange.Value = newRange;

            variable.EURange.Value = customRange ?? new Range(100, 0);

            variable.Value = initialValues ??
                TypeInfo.GetDefaultValue(dataType, valueRank, Server.TypeTree);

            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;
            // The latest UNECE version (Rev 11, published in 2015) is available here:
            // http://www.opcfoundation.org/UA/EngineeringUnits/UNECE/rec20_latest_08052015.zip
            variable.EngineeringUnits.Value = new EUInformation(
                "mV",
                "millivolt",
                "http://www.opcfoundation.org/UA/units/un/cefact")
            {
                // The mapping of the UNECE codes to OPC UA(EUInformation.unitId) is available here:
                // http://www.opcfoundation.org/UA/EngineeringUnits/UNECE/UNECE_to_OPCUA.csv
                UnitId = 12890 // "2Z"
            };
            variable.OnWriteValue = OnWriteAnalog;
            variable.EURange.OnWriteValue = OnWriteAnalogRange;
            variable.EURange.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.EURange.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.EngineeringUnits.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.EngineeringUnits.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.InstrumentRange.OnWriteValue = OnWriteAnalogRange;
            variable.InstrumentRange.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.InstrumentRange.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            parent?.AddChild(variable);

            return variable;
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private TwoStateDiscreteState CreateTwoStateDiscreteItemVariable(
            NodeState parent,
            string path,
            string name,
            string trueState,
            string falseState)
        {
            var variable = new TwoStateDiscreteState(parent)
            {
                NodeId = new NodeId(path, NamespaceIndex),
                BrowseName = new QualifiedName(path, NamespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None
            };

            variable.Create(SystemContext, null, variable.BrowseName, null, true);

            variable.SymbolicName = name;
            variable.ReferenceTypeId = ReferenceTypes.Organizes;
            variable.DataType = DataTypeIds.Boolean;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.Value = (bool)GetNewValue(variable);
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;

            variable.TrueState.Value = trueState;
            variable.TrueState.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.TrueState.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            variable.FalseState.Value = falseState;
            variable.FalseState.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.FalseState.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            parent?.AddChild(variable);

            return variable;
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private MultiStateDiscreteState CreateMultiStateDiscreteItemVariable(
            NodeState parent,
            string path,
            string name,
            params string[] values)
        {
            var variable = new MultiStateDiscreteState(parent)
            {
                NodeId = new NodeId(path, NamespaceIndex),
                BrowseName = new QualifiedName(path, NamespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None
            };

            variable.Create(SystemContext, null, variable.BrowseName, null, true);

            variable.SymbolicName = name;
            variable.ReferenceTypeId = ReferenceTypes.Organizes;
            variable.DataType = DataTypeIds.UInt32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.Value = (uint)0;
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;
            variable.OnWriteValue = OnWriteDiscrete;

            var strings = new LocalizedText[values.Length];

            for (int ii = 0; ii < strings.Length; ii++)
            {
                strings[ii] = values[ii];
            }

            variable.EnumStrings.Value = strings;
            variable.EnumStrings.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.EnumStrings.UserAccessLevel = AccessLevels.CurrentReadOrWrite;

            parent?.AddChild(variable);

            return variable;
        }

        /// <summary>
        /// Creates a new UInt32 variable.
        /// </summary>
        private MultiStateValueDiscreteState CreateMultiStateValueDiscreteItemVariable(
            NodeState parent,
            string path,
            string name,
            params string[] enumNames)
        {
            return CreateMultiStateValueDiscreteItemVariable(parent, path, name, null, enumNames);
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private MultiStateValueDiscreteState CreateMultiStateValueDiscreteItemVariable(
            NodeState parent,
            string path,
            string name,
            NodeId nodeId,
            params string[] enumNames)
        {
            var variable = new MultiStateValueDiscreteState(parent)
            {
                NodeId = new NodeId(path, NamespaceIndex),
                BrowseName = new QualifiedName(path, NamespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None
            };

            variable.Create(SystemContext, null, variable.BrowseName, null, true);

            variable.SymbolicName = name;
            variable.ReferenceTypeId = ReferenceTypes.Organizes;
            variable.DataType = nodeId ?? DataTypeIds.UInt32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Historizing = false;
            variable.Value = (uint)0;
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;
            variable.OnWriteValue = OnWriteValueDiscrete;

            // there are two enumerations for this type:
            // EnumStrings = the string representations for enumerated values
            // ValueAsText = the actual enumerated value

            // set the enumerated strings
            var strings = new LocalizedText[enumNames.Length];
            for (int ii = 0; ii < strings.Length; ii++)
            {
                strings[ii] = enumNames[ii];
            }

            // set the enumerated values
            var values = new EnumValueType[enumNames.Length];
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = new EnumValueType
                {
                    Value = ii,
                    Description = strings[ii],
                    DisplayName = strings[ii]
                };
            }
            variable.EnumValues.Value = values;
            variable.EnumValues.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.EnumValues.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.ValueAsText.Value = variable.EnumValues.Value[0].DisplayName;

            parent?.AddChild(variable);

            return variable;
        }

        private ServiceResult OnWriteDiscrete(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            var variable = node as MultiStateDiscreteState;

            // verify data type.
            var typeInfo = TypeInfo.IsInstanceOfDataType(
                value,
                variable.DataType,
                variable.ValueRank,
                context.NamespaceUris,
                context.TypeTable);

            if (typeInfo == null || typeInfo == TypeInfo.Unknown)
            {
                return StatusCodes.BadTypeMismatch;
            }

            if (indexRange != NumericRange.Empty)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);

            if (number >= variable.EnumStrings.Value.Length || number < 0)
            {
                return StatusCodes.BadOutOfRange;
            }

            return ServiceResult.Good;
        }

        private ServiceResult OnWriteValueDiscrete(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            var typeInfo = TypeInfo.Construct(value);

            if (node is not MultiStateValueDiscreteState variable ||
                typeInfo == null ||
                typeInfo == TypeInfo.Unknown ||
                !TypeInfo.IsNumericType(typeInfo.BuiltInType))
            {
                return StatusCodes.BadTypeMismatch;
            }

            if (indexRange != NumericRange.Empty)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            int number = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            if (number >= variable.EnumValues.Value.Length || number < 0)
            {
                return StatusCodes.BadOutOfRange;
            }

            if (!node.SetChildValue(
                context,
                BrowseNames.ValueAsText,
                variable.EnumValues.Value[number].DisplayName,
                true
            ))
            {
                return StatusCodes.BadOutOfRange;
            }

            node.ClearChangeMasks(context, true);

            return ServiceResult.Good;
        }

        private ServiceResult OnWriteAnalog(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            var variable = node as AnalogItemState;

            // verify data type.
            var typeInfo = TypeInfo.IsInstanceOfDataType(
                value,
                variable.DataType,
                variable.ValueRank,
                context.NamespaceUris,
                context.TypeTable);

            if (typeInfo == null || typeInfo == TypeInfo.Unknown)
            {
                return StatusCodes.BadTypeMismatch;
            }

            // check index range.
            if (variable.ValueRank >= 0)
            {
                if (indexRange != NumericRange.Empty)
                {
                    object target = variable.Value;
                    ServiceResult result = indexRange.UpdateRange(ref target, value);

                    if (ServiceResult.IsBad(result))
                    {
                        return result;
                    }

                    value = target;
                }
            }
            // check instrument range.
            else
            {
                if (indexRange != NumericRange.Empty)
                {
                    return StatusCodes.BadIndexRangeInvalid;
                }

                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);

                if (variable.InstrumentRange != null &&
                    (number < variable.InstrumentRange.Value.Low ||
                        number > variable.InstrumentRange.Value.High))
                {
                    return StatusCodes.BadOutOfRange;
                }
            }

            return ServiceResult.Good;
        }

        private ServiceResult OnWriteAnalogRange(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            var typeInfo = TypeInfo.Construct(value);

            if (node is not PropertyState<Range> variable ||
                value is not ExtensionObject extensionObject ||
                typeInfo == null ||
                typeInfo == TypeInfo.Unknown)
            {
                return StatusCodes.BadTypeMismatch;
            }
            if (extensionObject.Body is not Range newRange ||
                variable.Parent is not AnalogItemState parent)
            {
                return StatusCodes.BadTypeMismatch;
            }

            if (indexRange != NumericRange.Empty)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            var parentTypeInfo = TypeInfo.Construct(parent.Value);
            Range parentRange = GetAnalogRange(parentTypeInfo.BuiltInType);
            if (parentRange.High < newRange.High || parentRange.Low > newRange.Low)
            {
                return StatusCodes.BadOutOfRange;
            }

            value = newRange;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private BaseDataVariableState CreateVariable(
            NodeState parent,
            string path,
            string name,
            BuiltInType dataType,
            int valueRank)
        {
            return CreateVariable(parent, path, name, (uint)dataType, valueRank);
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private BaseDataVariableState CreateVariable(
            NodeState parent,
            string path,
            string name,
            NodeId dataType,
            int valueRank)
        {
            var variable = new BaseDataVariableState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypes.Organizes,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType,
                NodeId = new NodeId(path, NamespaceIndex),
                BrowseName = new QualifiedName(path, NamespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description,
                UserWriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description,
                DataType = dataType,
                ValueRank = valueRank,
                AccessLevel = AccessLevels.CurrentReadOrWrite,
                UserAccessLevel = AccessLevels.CurrentReadOrWrite,
                Historizing = false
            };
            variable.Value = GetNewValue(variable);
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTime.UtcNow;

            if (valueRank == ValueRanks.OneDimension)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>([0]);
            }
            else if (valueRank == ValueRanks.TwoDimensions)
            {
                variable.ArrayDimensions = new ReadOnlyList<uint>([0, 0]);
            }

            parent?.AddChild(variable);

            return variable;
        }

        private BaseDataVariableState[] CreateVariables(
            NodeState parent,
            string path,
            string name,
            BuiltInType dataType,
            int valueRank,
            ushort numVariables)
        {
            return CreateVariables(parent, path, name, (uint)dataType, valueRank, numVariables);
        }

        private BaseDataVariableState[] CreateVariables(
            NodeState parent,
            string path,
            string name,
            NodeId dataType,
            int valueRank,
            ushort numVariables)
        {
            // first, create a new Parent folder for this data-type
            FolderState newParentFolder = CreateFolder(parent, path, name);

            var itemsCreated = new List<BaseDataVariableState>();
            // now to create the remaining NUMBERED items
            for (uint i = 0; i < numVariables; i++)
            {
                string newName = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}_{1}",
                    name,
                    i.ToString("00", CultureInfo.InvariantCulture));
                string newPath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}_{1}",
                    path,
                    newName);
                itemsCreated.Add(
                    CreateVariable(newParentFolder, newPath, newName, dataType, valueRank));
            }
            return [.. itemsCreated];
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private BaseDataVariableState CreateDynamicVariable(
            NodeState parent,
            string path,
            string name,
            BuiltInType dataType,
            int valueRank)
        {
            return CreateDynamicVariable(parent, path, name, (uint)dataType, valueRank);
        }

        /// <summary>
        /// Creates a new variable.
        /// </summary>
        private BaseDataVariableState CreateDynamicVariable(
            NodeState parent,
            string path,
            string name,
            NodeId dataType,
            int valueRank)
        {
            BaseDataVariableState variable = CreateVariable(
                parent,
                path,
                name,
                dataType,
                valueRank);
            m_dynamicNodes.Add(variable);
            return variable;
        }

        private BaseDataVariableState[] CreateDynamicVariables(
            NodeState parent,
            string path,
            string name,
            BuiltInType dataType,
            int valueRank,
            uint numVariables)
        {
            return CreateDynamicVariables(
                parent,
                path,
                name,
                (uint)dataType,
                valueRank,
                numVariables);
        }

        private BaseDataVariableState[] CreateDynamicVariables(
            NodeState parent,
            string path,
            string name,
            NodeId dataType,
            int valueRank,
            uint numVariables)
        {
            // first, create a new Parent folder for this data-type
            FolderState newParentFolder = CreateFolder(parent, path, name);

            var itemsCreated = new List<BaseDataVariableState>();
            // now to create the remaining NUMBERED items
            for (uint i = 0; i < numVariables; i++)
            {
                string newName = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}_{1}",
                    name,
                    i.ToString("00", CultureInfo.InvariantCulture));
                string newPath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}_{1}",
                    path,
                    newName);
                itemsCreated.Add(
                    CreateDynamicVariable(newParentFolder, newPath, newName, dataType, valueRank));
            } //for i
            return [.. itemsCreated];
        }

        /// <summary>
        /// Creates a new view.
        /// </summary>
        private ViewState CreateView(
            NodeState parent,
            IDictionary<NodeId, IList<IReference>> externalReferences,
            string path,
            string name)
        {
            var type = new ViewState
            {
                SymbolicName = name,
                NodeId = new NodeId(path, NamespaceIndex),
                BrowseName = new QualifiedName(name, NamespaceIndex)
            };
            type.DisplayName = type.BrowseName.Name;
            type.WriteMask = AttributeWriteMask.None;
            type.UserWriteMask = AttributeWriteMask.None;
            type.ContainsNoLoops = true;

            if (!externalReferences.TryGetValue(
                ObjectIds.ViewsFolder,
                out IList<IReference> references))
            {
                externalReferences[ObjectIds.ViewsFolder] = references = [];
            }

            type.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ViewsFolder);
            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, type.NodeId));

            if (parent != null)
            {
                parent.AddReference(ReferenceTypes.Organizes, false, type.NodeId);
                type.AddReference(ReferenceTypes.Organizes, true, parent.NodeId);
            }

            AddPredefinedNode(SystemContext, type);
            return type;
        }

        /// <summary>
        /// Creates a new method.
        /// </summary>
        private MethodState CreateMethod(NodeState parent, string path, string name)
        {
            var method = new MethodState(parent)
            {
                SymbolicName = name,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                NodeId = new NodeId(path, NamespaceIndex),
                BrowseName = new QualifiedName(path, NamespaceIndex),
                DisplayName = new LocalizedText("en", name),
                WriteMask = AttributeWriteMask.None,
                UserWriteMask = AttributeWriteMask.None,
                Executable = true,
                UserExecutable = true
            };

            parent?.AddChild(method);

            return method;
        }

        private ServiceResult OnVoidCall(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            return ServiceResult.Good;
        }

        private ServiceResult OnAddCall(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            // all arguments must be provided.
            if (inputArguments.Count < 2)
            {
                return StatusCodes.BadArgumentsMissing;
            }

            try
            {
                float floatValue = (float)inputArguments[0];
                uint uintValue = (uint)inputArguments[1];

                // set output parameter
                outputArguments[0] = floatValue + uintValue;
                return ServiceResult.Good;
            }
            catch
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
        }

        private ServiceResult OnMultiplyCall(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            // all arguments must be provided.
            if (inputArguments.Count < 2)
            {
                return StatusCodes.BadArgumentsMissing;
            }

            try
            {
                short op1 = (short)inputArguments[0];
                ushort op2 = (ushort)inputArguments[1];

                // set output parameter
                outputArguments[0] = op1 * op2;
                return ServiceResult.Good;
            }
            catch
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
        }

        private ServiceResult OnDivideCall(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            // all arguments must be provided.
            if (inputArguments.Count < 2)
            {
                return StatusCodes.BadArgumentsMissing;
            }

            try
            {
                int op1 = (int)inputArguments[0];
                ushort op2 = (ushort)inputArguments[1];

                // set output parameter
                outputArguments[0] = op1 / (float)op2;
                return ServiceResult.Good;
            }
            catch
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
        }

        private ServiceResult OnSubstractCall(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            // all arguments must be provided.
            if (inputArguments.Count < 2)
            {
                return StatusCodes.BadArgumentsMissing;
            }

            try
            {
                short op1 = (short)inputArguments[0];
                byte op2 = (byte)inputArguments[1];

                // set output parameter
                outputArguments[0] = (short)(op1 - op2);
                return ServiceResult.Good;
            }
            catch
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
        }

        private ServiceResult OnHelloCall(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            // all arguments must be provided.
            if (inputArguments.Count < 1)
            {
                return StatusCodes.BadArgumentsMissing;
            }

            try
            {
                string op1 = (string)inputArguments[0];

                // set output parameter
                outputArguments[0] = "hello " + op1;
                return ServiceResult.Good;
            }
            catch
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
        }

        private ServiceResult OnInputCall(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            // all arguments must be provided.
            if (inputArguments.Count < 1)
            {
                return StatusCodes.BadArgumentsMissing;
            }

            return ServiceResult.Good;
        }

        private ServiceResult OnOutputCall(
            ISystemContext context,
            MethodState method,
            IList<object> inputArguments,
            IList<object> outputArguments)
        {
            // all arguments must be provided.
            try
            {
                // set output parameter
                outputArguments[0] = "Output";
                return ServiceResult.Good;
            }
            catch
            {
                return new ServiceResult(StatusCodes.BadInvalidArgument);
            }
        }

        private void ResetRandomGenerator(int seed, int boundaryValueFrequency = 0)
        {
            m_randomSource = new RandomSource(seed);
            m_generator = new DataGenerator(m_randomSource, Server.Telemetry)
            {
                BoundaryValueFrequency = boundaryValueFrequency
            };
        }

        private object GetNewValue(BaseVariableState variable)
        {
            object value = null;
            for (int retryCount = 0; value == null && retryCount < 10; retryCount++)
            {
                value = m_generator.GetRandom(
                    variable.DataType,
                    variable.ValueRank,
                    [10],
                    Server.TypeTree);
                // skip Variant Null
                if (value is Variant variant && variant.Value == null)
                {
                    value = null;
                }
            }

            return value;
        }

        private void DoSimulation(object state)
        {
            if (!m_simulationEnabled)
            {
                return;
            }
            int running = Interlocked.Increment(ref m_simulationsRunning);
            try
            {
                if (running > 0)
                {
                    LogLevel logLevel = running > 1 ?
                        running > 4 ? LogLevel.Warning : LogLevel.Information :
                        LogLevel.Debug;
                    m_logger.Log(logLevel,
                        "Simulation timer fired while {Count} simulations are already queued to run.",
                        running);
                }
                lock (Lock)
                {
                    DateTime timeStamp = DateTime.UtcNow;
                    foreach (BaseDataVariableState variable in m_dynamicNodes)
                    {
                        variable.Value = GetNewValue(variable);
                        variable.Timestamp = timeStamp;
                        variable.ClearChangeMasks(SystemContext, false);
                    }
                }
            }
            catch (Exception e)
            {
                m_logger.LogError(e, "Unexpected error doing simulation #{Count}.", running);
            }
            finally
            {
                Interlocked.Decrement(ref m_simulationsRunning);
            }
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                // TBD
            }
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        protected override NodeHandle GetManagerHandle(
            ServerSystemContext context,
            NodeId nodeId,
            IDictionary<NodeId, NodeState> cache)
        {
            lock (Lock)
            {
                // quickly exclude nodes that are not in the namespace.
                if (!IsNodeIdInNamespace(nodeId))
                {
                    return null;
                }

                if (!PredefinedNodes.TryGetValue(nodeId, out NodeState node))
                {
                    return null;
                }

                return new NodeHandle
                {
                    NodeId = nodeId,
                    Node = node,
                    Validated = true
                };
            }
        }

        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        protected override NodeState ValidateNode(
            ServerSystemContext context,
            NodeHandle handle,
            IDictionary<NodeId, NodeState> cache)
        {
            // not valid if no root.
            if (handle == null)
            {
                return null;
            }

            // check if previously validated.
            if (handle.Validated)
            {
                return handle.Node;
            }

            // TBD

            return null;
        }

        private RandomSource m_randomSource;
        private DataGenerator m_generator;
        private Timer m_simulationTimer;
        private ushort m_simulationInterval = 1000;
        private bool m_simulationEnabled = true;
        private int m_simulationsRunning;
        private readonly List<BaseDataVariableState> m_dynamicNodes = [];

        private static readonly bool[] s_booleanArray
            = [true, false, true, false, true, false, true, false, true];

        private static readonly double[] s_doubleArray =
        [
            9.00001d,
            9.0002d,
            9.003d,
            9.04d,
            9.5d,
            9.06d,
            9.007d,
            9.008d,
            9.0009d
        ];

        private static readonly float[] s_singleArray
            = [0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 1.1f, 2.2f, 3.3f, 4.4f, 5.5f];

        private static readonly int[] s_int32Array = [10, 11, 12, 13, 14, 15, 16, 17, 18, 19];

        private static readonly string[] s_stringArray
            = ["en", "fr", "de", "en", "fr", "de", "en", "fr", "de", "en"];

        private static readonly string[] s_stringArray0 =
        [
            "a00",
            "b10",
            "c20",
            "d30",
            "e40",
            "f50",
            "g60",
            "h70",
            "i80",
            "j90"
        ];

        private static readonly string[] s_stringArray1 = ["open", "closed", "jammed"];
        private static readonly string[] s_stringArray2 = ["red", "green", "blue", "cyan"];
        private static readonly string[] s_stringArray3 = ["lolo", "lo", "normal", "hi", "hihi"];
        private static readonly string[] s_stringArray4 = ["left", "right", "center"];
        private static readonly string[] s_stringArray5 = ["circle", "cross", "triangle"];
        private static readonly string[] s_stringArray6 = ["open", "closed", "jammed"];
        private static readonly string[] s_stringArray7 = ["red", "green", "blue", "cyan"];
        private static readonly string[] s_stringArray8 = ["lolo", "lo", "normal", "hi", "hihi"];
        private static readonly string[] s_stringArray9 = ["left", "right", "center"];
    }

    public static class VariableExtensions
    {
        public static BaseDataVariableState MinimumSamplingInterval(
            this BaseDataVariableState variable,
            int minimumSamplingInterval)
        {
            variable.MinimumSamplingInterval = minimumSamplingInterval;
            return variable;
        }
    }
}
