using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@enum;
using Snet.Iot.Daq.data;
using Snet.Log;
using Snet.Model.data;
using Snet.Model.@interface;
using Snet.Utility;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;

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
        /// 程序集类型IOC容器缓存
        /// </summary>
        private static readonly ConcurrentDictionary<string, Type> iocType = new();

        /// <summary>
        /// DAQ实例对象
        /// </summary>
        private static readonly ConcurrentDictionary<string, IDaq> icoDaq = new();

        /// <summary>
        /// MQ实例对象
        /// </summary>
        private static readonly ConcurrentDictionary<string, IMq> icoMq = new();

        /// <summary>
        /// 插件加载上下文：以插件类名为键，支持通过 Unload() 实现程序集热卸载
        /// </summary>
        private static readonly ConcurrentDictionary<string, PluginLoadContext> pluginContexts = new();

        /// <summary>
        /// 动态库监控格式
        /// </summary>
        private static readonly string DllWatcherFormat = "Snet.*.dll";

        /// <summary>
        /// 接口过滤器
        /// </summary>
        private static bool InterfaceFilter(Type typeObj, Object criteriaObj)
        {
            return typeObj.ToString() == criteriaObj.ToString();
        }


        /// <summary>
        /// 插件初始化加载<br/>
        /// 扫描指定路径下的所有符合命名规则的 DLL，通过反射加载实现了指定接口的类型，<br/>
        /// 创建基础实例并缓存到 IOC 容器中。
        /// </summary>
        /// <param name="path">插件 DLL 所在目录路径</param>
        /// <param name="iName">目标接口全名（如 Snet.Model.interface.IDaq）</param>
        /// <returns>插件信息和参数的元组列表</returns>
        public static List<(PluginDetailsModel Model, object? Param)> InitPlugin(string path, string iName)
        {
            //结果
            List<(PluginDetailsModel Model, object? Param)> result = new();

            //数据（同时跟踪类型和其所属的加载上下文）
            ConcurrentDictionary<(string path, string className), (Type type, PluginLoadContext context)> copy = new();

            //库
            string[] libs = [];
            try
            {
                libs = Directory.GetFiles(path, DllWatcherFormat, SearchOption.AllDirectories);
            }
            catch { }
            //循环文件，添加程序集
            foreach (var lib in libs)
            {
                try
                {
                    //使用可回收的 AssemblyLoadContext 加载程序集，支持热卸载
                    string fullPath = Path.GetFullPath(lib);
                    var context = new PluginLoadContext(fullPath);
                    Assembly assembly = context.LoadFromFileStream(fullPath);
                    //获取所有类
                    Type[] types = assembly.GetExportedTypes();
                    //过滤器
                    TypeFilter typeFilter = new TypeFilter(InterfaceFilter);
                    //集合
                    List<Type> typesArray = new List<Type>();
                    //检索类是否继承接口
                    foreach (Type type in types)
                    {
                        if (type.FindInterfaces(typeFilter, iName).Length > 0)
                        {
                            typesArray.Add(type);
                        }
                    }
                    //添加至集合
                    foreach (Type type in typesArray)
                    {
                        if (type.Name.Contains("IOu") || type.Name.Contains("DaqAbstract`2") || type.Name.Contains("MqAbstract`2"))
                        {
                            continue;
                        }
                        copy.AddOrUpdate((path, type.Name), (type, context), (k, v) => (type, context));
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.Error(ex.Message, "Plugin/LoadFail", ex);
                    continue;
                }
            }


            foreach (var item in copy)
            {
                try
                {
                    //解构类型和加载上下文
                    (Type pluginType, PluginLoadContext pluginContext) = item.Value;

                    //类名
                    string className = pluginType.Name;
                    //命名空间
                    string @namespace = $"{pluginType.Namespace}.{pluginType.Name}";
                    //名称
                    string name = pluginType.Name;
                    //版本号
                    AssemblyName assemblyName = pluginType.Assembly.GetName();
                    string version = assemblyName.Version.ToString().Replace(".0", string.Empty);

                    if (iName.Contains(PluginType.Mq.ToString()))
                    {
                        if (!icoMq.TryGetValue(className, out IMq? mq))
                        {
                            mq = Activator.CreateInstance(pluginType) as IMq;
                            if (mq != null)
                            {
                                icoMq.TryAdd(className, mq);
                            }
                        }

                        //格式
                        string configFormat = $"{pluginType.Namespace}.{pluginType.Name}" + ".{0}." + PluginType.Mq.ToString() + ".Config.json";

                        //参数
                        object? param = mq.GetParam(true).ResultData;

                        //添加结果
                        result.Add((new(name, @namespace, configFormat, version), param));
                    }
                    else if (iName.Contains(PluginType.Daq.ToString()))
                    {
                        if (!icoDaq.TryGetValue(className, out IDaq? daq))
                        {
                            daq = Activator.CreateInstance(pluginType) as IDaq;
                            if (daq != null)
                            {
                                icoDaq.TryAdd(className, daq);
                            }
                        }

                        //格式
                        string configFormat = $"{pluginType.Namespace}.{pluginType.Name}" + ".{0}." + PluginType.Daq.ToString() + ".Config.json";

                        //参数
                        object? param = daq.GetParam(true).ResultData;

                        //添加结果
                        result.Add((new(name, @namespace, configFormat, version), param));
                    }

                    //更新类型容器
                    iocType.AddOrUpdate(className, pluginType, (k, v) => pluginType);

                    //记录加载上下文（热重载时卸载旧上下文）
                    if (pluginContexts.TryGetValue(className, out var oldContext) && !ReferenceEquals(oldContext, pluginContext))
                    {
                        try { oldContext.Unload(); } catch { }
                    }
                    pluginContexts[className] = pluginContext;
                }
                catch (Exception ex)
                {
                    LogHelper.Error(ex.Message, "Plugin/LoadFail", ex);
                    continue;
                }
            }
            return result;
        }

        /// <summary>
        /// 热卸载指定插件<br/>
        /// 释放插件的缓存实例（IDaq/IMq），移除类型注册，并卸载对应的程序集加载上下文。<br/>
        /// 同一 DLL 中的所有插件类型将一并卸载，卸载后 GC 将在下一次回收时释放程序集内存。
        /// </summary>
        /// <param name="className">插件类名（IOC 容器中的键）</param>
        /// <returns>是否成功卸载</returns>
        public static async Task<bool> RemovePluginAsync(string className)
        {
            if (!pluginContexts.TryGetValue(className, out PluginLoadContext? context))
                return false;

            //查找同一加载上下文下的所有插件类（同一 DLL 可能包含多个插件类型）
            var relatedClassNames = pluginContexts
                .Where(kvp => ReferenceEquals(kvp.Value, context))
                .Select(kvp => kvp.Key)
                .ToList();

            //依次清理所有相关插件的缓存实例和类型注册
            foreach (var name in relatedClassNames)
            {
                if (icoDaq.TryRemove(name, out IDaq? daq))
                {
                    try { await daq.DisposeAsync(); } catch { }
                }
                if (icoMq.TryRemove(name, out IMq? mq))
                {
                    try { await mq.DisposeAsync(); } catch { }
                }
                iocType.TryRemove(name, out _);
                pluginContexts.TryRemove(name, out _);
            }

            //卸载程序集加载上下文
            context.Unload();
            return true;
        }

        /// <summary>
        /// 热卸载所有已加载的插件<br/>
        /// 依次释放所有缓存实例并卸载全部程序集加载上下文
        /// </summary>
        public static async Task RemoveAllPluginsAsync()
        {
            //收集所有需要卸载的上下文（去重）
            var contexts = pluginContexts.Values.Distinct().ToList();

            //释放所有 DAQ 实例
            foreach (var kvp in icoDaq)
            {
                try { await kvp.Value.DisposeAsync(); } catch { }
            }
            icoDaq.Clear();

            //释放所有 MQ 实例
            foreach (var kvp in icoMq)
            {
                try { await kvp.Value.DisposeAsync(); } catch { }
            }
            icoMq.Clear();

            //清除类型注册
            iocType.Clear();
            pluginContexts.Clear();

            //卸载所有程序集上下文
            foreach (var ctx in contexts)
            {
                try { ctx.Unload(); } catch { }
            }
        }

        /// <summary>
        /// 获取插件的默认参数对象<br/>
        /// 从 IOC 容器缓存的基础实例中获取该插件的参数模板
        /// </summary>
        /// <param name="iName">接口名称（用于区分 Daq/Mq 类型）</param>
        /// <param name="className">插件类名（IOC 容器中的键）</param>
        /// <returns>参数对象，未找到时返回 null</returns>
        public static object? GetPluginParamObject(string iName, string className)
        {
            //加载程序集
            if (iocType.TryGetValue(className, out Type? type))
            {
                if (iName.Contains(PluginType.Mq.ToString()))
                {
                    if (icoMq.TryGetValue(className, out IMq? mq))
                    {
                        return mq.GetParam(true).ResultData;
                    }
                }
                else if (iName.Contains(PluginType.Daq.ToString()))
                {
                    if (icoDaq.TryGetValue(className, out IDaq? daq))
                    {
                        return daq.GetParam(true).ResultData;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// 将本地 JSON 格式的插件参数转换为对应的强类型参数对象<br/>
        /// 1. 从 IOC 容器获取插件类型<br/>
        /// 2. 反序列化 JSON 为 JObject<br/>
        /// 3. 通过构造函数反射进行参数类型匹配和转换
        /// </summary>
        /// <param name="className">插件类名（IOC 容器中的键）</param>
        /// <param name="content">JSON 格式的参数字符串</param>
        /// <returns>转换后的参数对象，失败返回 null</returns>
        public static object? ConvertPluginJsonParam(string className, string content)
        {
            if (iocType.TryGetValue(className, out Type? type))
            {
                JObject? jsonObject = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(content);
                object[]? objs = [jsonObject];
                // 获取第一个有参构造函数（优化：直接使用带谓词的 FirstOrDefault，避免多余的 Where 枚举）
                ConstructorInfo? constructorInfo = type.GetConstructors().FirstOrDefault(c => c.GetParameters().Length > 0);
                // 返回数据
                return ParamTypeConvert(objs, constructorInfo).FirstOrDefault();
            }

            return null;
        }

        #region 参数类型转换
        /// <summary>
        /// 参数类型转换（精简优化版）
        /// </summary>
        private static object[]? ParamTypeConvert(object[] objs, object source)
        {
            if (objs == null || objs.Length == 0)
                return null;

            ParameterInfo[]? paramArray = source switch
            {
                MethodInfo m => m.GetParameters(),
                ConstructorInfo c => c.GetParameters(),
                _ => null
            };

            if (paramArray == null || paramArray.Length == 0)
                return null;

            int count = Math.Min(objs.Length, paramArray.Length);
            var list = new object[count];

            for (int i = 0; i < count; i++)
            {
                var targetType = paramArray[i].ParameterType;
                var value = objs[i];

                // null → 默认值
                if (value == null)
                {
                    list[i] = GetDefault(targetType);
                    continue;
                }

                // 方法参数（简单类型）
                if (source is MethodInfo)
                {
                    list[i] = ConvertSimple(value, targetType);
                    continue;
                }

                // 构造函数参数（复杂类型）
                if (source is ConstructorInfo)
                {
                    list[i] = ConvertObject(value, targetType);
                    continue;
                }
            }

            return list;
        }

        /// <summary>
        /// 简单类型转换
        /// </summary>
        private static object ConvertSimple(object value, Type type)
        {
            try
            {
                if (value.GetType() == type)
                    return value;

                return Convert.ChangeType(value, type);
            }
            catch
            {
                return GetDefault(type);
            }
        }

        /// <summary>
        /// 复杂类型：通过 JSON → T
        /// </summary>
        private static object ConvertObject(object value, Type type)
        {
            try
            {
                var jObj = value as JObject
                    ?? JsonConvert.DeserializeObject<JObject>(JsonConvert.SerializeObject(value));

                if (jObj == null)
                    return GetDefault(type);

                var model = Activator.CreateInstance(type);
                foreach (var prop in type.GetProperties())
                {
                    if (!prop.CanWrite) continue;

                    JToken? token = jObj[prop.Name];
                    if (token == null) continue;

                    object? converted = token.ToObject(prop.PropertyType);
                    prop.SetValue(model, converted);
                }

                return model!;
            }
            catch
            {
                return GetDefault(type);
            }
        }

        /// <summary>
        /// 获取默认值
        /// </summary>
        private static object? GetDefault(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        #endregion

        /// <summary>
        /// 验证插件连接状态<br/>
        /// 创建临时实例尝试打开连接，验证配置参数是否正确，完成后立即释放
        /// </summary>
        /// <param name="iName">接口名称（用于区分 Daq/Mq 类型）</param>
        /// <param name="className">插件类名</param>
        /// <param name="data">序列化的连接参数数据</param>
        /// <returns>操作结果，包含连接测试成功/失败状态</returns>
        public static async Task<OperateResult> StatusVerifyAsync(string iName, string className, string data)
        {
            OperateResult result = OperateResult.CreateFailureResult("Verify Failure");
            if (iocType.TryGetValue(className, out Type? type))
            {
                object? obj = ConvertPluginJsonParam(className, data);
                if (iName.Contains(PluginType.Mq.ToString()))
                {
                    if (Activator.CreateInstance(type, [obj]) is IMq mq)
                    {
                        result = await mq.OnAsync();
                        await mq.DisposeAsync();
                    }
                }
                else if (iName.Contains(PluginType.Daq.ToString()))
                {
                    if (Activator.CreateInstance(type, [obj]) is IDaq daq)
                    {
                        result = await daq.OnAsync();
                        await daq.DisposeAsync();
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// 保存插件配置列表到本地 JSON 文件
        /// </summary>
        /// <param name="data">插件配置集合</param>
        /// <param name="path">保存路径</param>
        public static void SavePluginUIConfig(ObservableCollection<PluginConfigModel> data, string path)
        {
            FileHandler.StringToFile(path, data.ToJson(true));
        }

        /// <summary>
        /// 保存插件列表配置到本地 JSON 文件
        /// </summary>
        /// <param name="data">插件列表集合</param>
        /// <param name="path">保存路径</param>
        public static void SavePluginUIConfig(ObservableCollection<PluginListModel> data, string path)
        {
            FileHandler.StringToFile(path, data.ToJson(true));
        }

        /// <summary>
        /// 从本地 JSON 文件加载插件界面配置<br/>
        /// 文件不存在时返回默认值
        /// </summary>
        /// <typeparam name="T">反序列化目标类型</typeparam>
        /// <param name="filePath">配置文件路径</param>
        /// <returns>反序列化后的对象，文件不存在或反序列化失败时返回 default</returns>
        public static T? GetPluginUIConfig<T>(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return default;
            }
            string json = FileHandler.FileToString(filePath);
            return json.ToJsonEntity<T>();
        }

        /// <summary>
        /// 从插件文件名提取设备 SN 码<br/>
        /// 例如 "Namespace.ClassName.MySN.Daq.Config.json" → "MySN"
        /// </summary>
        /// <param name="fileName">插件配置文件名</param>
        /// <param name="type">插件类型（Daq/Mq）</param>
        /// <returns>提取的 SN 码字符串</returns>
        public static string PluginFileNameToSN(string fileName, PluginType type)
        {
            string newData = fileName.Replace($".{type.ToString()}.Config.json", string.Empty);
            return newData.Split('.').LastOrDefault() ?? string.Empty;
        }

        /// <summary>
        /// 通过插件配置创建新的设备实例<br/>
        /// 从 IOC 容器中获取缓存的基础实例，调用其 CreateInstanceAsync 方法创建新连接
        /// </summary>
        /// <typeparam name="T">目标接口类型（IDaq 或 IMq）</typeparam>
        /// <param name="plugin">插件配置信息，包含类名和连接参数</param>
        /// <returns>新创建的实例，失败时返回 default</returns>
        public static async Task<T?> CreateNewObjetcAsync<T>(this PluginConfigModel plugin)
        {
            switch (plugin.Type)
            {
                case PluginType.Daq:
                    if (icoDaq.TryGetValue(plugin.Name, out IDaq? daqBase))
                    {
                        OperateResult operateResult = await daqBase.CreateInstanceAsync(plugin.Param);
                        //创建一个临时的对象
                        if (operateResult.GetDetails(out string? msg, out IDaq? daqNew))
                        {
                            return daqNew.GetSource<T>();
                        }
                    }
                    break;
                case PluginType.Mq:
                    if (icoMq.TryGetValue(plugin.Name, out IMq? mqBase))
                    {
                        OperateResult operateResult = await mqBase.CreateInstanceAsync(plugin.Param);
                        //创建一个临时的对象
                        if (operateResult.GetDetails(out string? msg, out IMq? mqNew))
                        {
                            return mqNew.GetSource<T>();
                        }
                    }
                    break;
            }
            return default(T);
        }

        /// <summary>
        /// 测试生产消息<br/>
        /// 创建临时 MQ 实例进行单次消息生产测试，完成后释放资源。
        /// 使用 try/finally 确保资源可靠释放，即使发生异常也不会泄漏连接。
        /// </summary>
        /// <param name="plugin">传输插件配置信息</param>
        /// <param name="topic">消息主题</param>
        /// <param name="content">消息内容</param>
        /// <returns>操作结果，包含生产成功/失败状态</returns>
        public static async Task<OperateResult> TestProduceAsync(this PluginConfigModel plugin, string topic, string content)
        {
            IMq? mqNew = await plugin.CreateNewObjetcAsync<IMq>();
            if (mqNew is null)
                return OperateResult.CreateFailureResult("Failed to create MQ instance");

            try
            {
                OperateResult operateResult = await mqNew.GetStatusAsync();
                if (!operateResult.Status)
                {
                    operateResult = await mqNew.OnAsync();
                }
                if (operateResult.Status)
                {
                    // 生产消息
                    operateResult = await mqNew.ProduceAsync(topic, content);
                }
                return operateResult;
            }
            finally
            {
                // 确保资源可靠释放
                await mqNew.OffAsync();
                await mqNew.DisposeAsync();
            }
        }


        /// <summary>
        /// 测试读取地址数据<br/>
        /// 创建临时 DAQ 实例进行单次地址读取测试，完成后释放资源。
        /// 使用 try/finally 确保资源可靠释放。
        /// </summary>
        /// <param name="model">地址模型，包含地址信息和数据类型</param>
        /// <param name="plugin">采集插件配置信息</param>
        /// <returns>操作结果，成功时 ResultData 包含读取到的数据</returns>
        public static async Task<OperateResult> TestReadAddressAsync(this AddressModel model, PluginConfigModel plugin)
        {
            IDaq? daqNew = await plugin.CreateNewObjetcAsync<IDaq>();
            if (daqNew is null)
                return OperateResult.CreateFailureResult("Failed to create DAQ instance");

            try
            {
                OperateResult operateResult = await daqNew.GetStatusAsync();
                if (!operateResult.Status)
                {
                    operateResult = await daqNew.OnAsync();
                }
                if (operateResult.Status)
                {
                    AddressDetails? address = model.Convert();
                    if (address is not null)
                    {
                        // 执行读取操作
                        return await daqNew.ReadAsync(new Address() { AddressArray = [address] });
                    }
                    return OperateResult.CreateFailureResult("地址转换失败");
                }
                return operateResult;
            }
            finally
            {
                // 确保资源可靠释放
                await daqNew.OffAsync();
                await daqNew.DisposeAsync();
            }
        }

        /// <summary>
        /// 测试写入地址数据<br/>
        /// 创建临时 DAQ 实例进行单次地址写入测试，完成后释放资源。
        /// 使用 try/finally 确保资源可靠释放。
        /// </summary>
        /// <param name="model">地址模型，包含目标地址信息</param>
        /// <param name="plugin">采集插件配置信息</param>
        /// <param name="write">待写入的数据模型</param>
        /// <returns>操作结果，包含写入成功/失败状态</returns>
        public static async Task<OperateResult> TestWriteAddressAsync(this AddressModel model, PluginConfigModel plugin, WriteModel write)
        {
            IDaq? daqNew = await plugin.CreateNewObjetcAsync<IDaq>();
            if (daqNew is null)
                return OperateResult.CreateFailureResult("Failed to create DAQ instance");

            try
            {
                OperateResult operateResult = await daqNew.GetStatusAsync();
                if (!operateResult.Status)
                {
                    operateResult = await daqNew.OnAsync();
                }
                if (operateResult.Status)
                {
                    // 组织写入数据：以地址字符串为 Key
                    var keys = new ConcurrentDictionary<string, WriteModel>();
                    keys[model.Address] = write;
                    // 执行写入操作
                    operateResult = await daqNew.WriteAsync(keys);
                }
                return operateResult;
            }
            finally
            {
                // 确保资源可靠释放
                await daqNew.OffAsync();
                await daqNew.DisposeAsync();
            }
        }

        /// <summary>
        /// 测试数据传输<br/>
        /// 创建临时 MQ 实例进行单次数据传输测试，完成后释放资源。
        /// 使用 try/finally 确保资源可靠释放。
        /// </summary>
        /// <param name="address">地址模型，包含主题、精简值设置等</param>
        /// <param name="plugin">传输插件配置信息</param>
        /// <param name="data">待传输的地址值数据</param>
        /// <returns>操作结果，包含传输成功/失败状态</returns>
        public static async Task<OperateResult> TestTransmitDataAsync(this AddressModel address, PluginConfigModel plugin, AddressValue data)
        {
            IMq? mqNew = await plugin.CreateNewObjetcAsync<IMq>();
            if (mqNew is null)
                return OperateResult.CreateFailureResult("Failed to create MQ instance");

            try
            {
                OperateResult operateResult = await mqNew.GetStatusAsync();
                if (!operateResult.Status)
                {
                    operateResult = await mqNew.OnAsync();
                }
                if (operateResult.Status)
                {
                    // 根据精简值设置决定序列化方式
                    string content = address.SimplifyValue ? data.GetSimplify().ToJson(true) : data.ToJson(true);
                    // 执行消息生产
                    operateResult = await mqNew.ProduceAsync(address.Topic, content);
                }
                return operateResult;
            }
            finally
            {
                // 确保资源可靠释放
                await mqNew.OffAsync();
                await mqNew.DisposeAsync();
            }
        }

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
            foreach (var item in GetPluginUIConfig<ObservableCollection<PluginConfigModel>>(GlobalConfigModel.UI_PluginConfigPath))
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

        /// <summary>
        /// 将 AddressModel 列表批量转换为底层 Address 对象<br/>
        /// 预分配 AddressArray 容量以减少扩容开销
        /// </summary>
        /// <param name="models">地址模型集合</param>
        /// <returns>转换后的 Address 对象，包含所有地址详情</returns>
        public static Address AddressConvert(this List<AddressModel> models)
        {
            Address address = new();
            address.AddressArray = new(models.Count);
            foreach (var item in models)
            {
                address.AddressArray.Add(item.Convert());
            }
            return address;
        }

        /// <summary>
        /// 将单个 AddressModel 转换为底层 Address 对象
        /// </summary>
        /// <param name="models">地址模型</param>
        /// <returns>转换后的 Address 对象，包含单个地址详情</returns>
        public static Address AddressConvert(this AddressModel models)
        {
            Address address = new();
            address.AddressArray = new() { models.Convert() };
            return address;
        }
    }
}
