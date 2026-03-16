using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Snet.Iot.Daq.data;
using Snet.Iot.Daq.@enum;
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
    /// 插件参数处理类
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
        /// 第一次加载，初始化插件
        /// </summary>
        /// <param name="path">插件路径</param>
        /// <param name="iName">接口名称</param>
        /// <returns>插件信息集合</returns>
        public static List<(PluginDetailsModel Model, object? Param)> InitPlugin(string path, string iName)
        {
            //结果
            List<(PluginDetailsModel Model, object? Param)> result = new();

            //数据
            ConcurrentDictionary<(string path, string className), Type> copy = new ConcurrentDictionary<(string path, string className), Type>();

            //库
            string[] libs = Directory.GetFiles(path, DllWatcherFormat, SearchOption.AllDirectories);
            //循环文件，添加程序集
            foreach (var lib in libs)
            {
                try
                {
                    //加载程序集
                    Assembly assembly = Assembly.LoadFrom(lib);
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
                        copy.AddOrUpdate((path, type.Name), type, (k, v) => type);
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
                    //类名
                    string className = item.Value.Name;
                    //命名空间
                    string @namespace = $"{item.Value.Namespace}.{item.Value.Name}";
                    //名称
                    string name = item.Value.Name;
                    //版本号
                    AssemblyName assemblyName = item.Value.Assembly.GetName();
                    string version = assemblyName.Version.ToString().Replace(".0", string.Empty);

                    if (iName.Contains(PluginType.Mq.ToString()))
                    {
                        if (!icoMq.TryGetValue(className, out IMq? mq))
                        {
                            mq = Activator.CreateInstance(item.Value) as IMq;
                            if (mq != null)
                            {
                                icoMq.TryAdd(className, mq);
                            }
                        }

                        //格式
                        string configFormat = $"{item.Value.Namespace}.{item.Value.Name}" + ".{0}." + PluginType.Mq.ToString() + ".Config.json";

                        //参数
                        object? param = mq.GetParam(true).ResultData;

                        //添加结果
                        result.Add((new(name, @namespace, configFormat, version), param));
                    }
                    else if (iName.Contains(PluginType.Daq.ToString()))
                    {
                        if (!icoDaq.TryGetValue(className, out IDaq? daq))
                        {
                            daq = Activator.CreateInstance(item.Value) as IDaq;
                            if (daq != null)
                            {
                                icoDaq.TryAdd(className, daq);
                            }
                        }

                        //格式
                        string configFormat = $"{item.Value.Namespace}.{item.Value.Name}" + ".{0}." + PluginType.Daq.ToString() + ".Config.json";

                        //参数
                        object? param = daq.GetParam(true).ResultData;

                        //添加结果
                        result.Add((new(name, @namespace, configFormat, version), param));
                    }

                    //更新容器
                    iocType.AddOrUpdate(className, item.Value, (k, v) => item.Value);
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
        /// 获取插件参数对象
        /// </summary>
        /// <param name="path">插件路径</param>
        /// <param name="iName">接口名称</param>
        /// <param name="className">类名</param>
        /// <returns>返回对应采集的入参对象</returns>
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
        /// 通过从本地获取的参数转换成对应的参数对象
        /// </summary>
        /// <param name="className">类名</param>
        /// <param name="content">参数</param>
        /// <returns>对应的对象</returns>
        public static object? ConvertPluginJsonParam(string className, string content)
        {
            if (iocType.TryGetValue(className, out Type? type))
            {
                JObject? jsonObject = Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(content);
                object[]? objs = [jsonObject];
                //获取构造函数信息
                ConstructorInfo? constructorInfo = type?.GetConstructors().Where(c => c.GetParameters().Length > 0).FirstOrDefault();
                //返回数据
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

                    object? converted = Convert.ChangeType(token.ToObject(prop.PropertyType), prop.PropertyType);
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
        /// 状态验证
        /// </summary>
        /// <param name="iName">接口名称</param>
        /// <param name="className">类名</param>
        /// <param name="data">实例化的数据</param>
        /// <returns>返回是否能正常打开</returns>
        public static async Task<OperateResult> StatusVerifyAsync(string iName, string className, string data)
        {
            OperateResult result = OperateResult.CreateFailureResult("Verify Failure");
            if (iocType.TryGetValue(className, out Type? type))
            {
                object? obj = ConvertPluginJsonParam(className, data);
                if (iName.Contains(PluginType.Mq.ToString()))
                {
                    IMq mq = Activator.CreateInstance(type, [obj]) as IMq;
                    result = await mq.OnAsync();
                    await mq.DisposeAsync();
                }
                else if (iName.Contains(PluginType.Daq.ToString()))
                {
                    IDaq daq = Activator.CreateInstance(type, [obj]) as IDaq;
                    result = await daq.OnAsync();
                    await daq.DisposeAsync();
                }
            }
            return result;
        }

        /// <summary>
        /// 保存插件界面你配置
        /// </summary>
        /// <param name="data">json 数据</param>
        /// <param name="path">保存的路径</param>
        public static void SavePluginUIConfig(ObservableCollection<PluginConfigModel> data, string path)
        {
            FileHandler.StringToFile(path, data.ToJson(true));
        }

        /// <summary>
        /// 保存插件界面你配置
        /// </summary>
        /// <param name="data">json 数据</param>
        /// <param name="path">保存的路径</param>
        public static void SavePluginUIConfig(ObservableCollection<PluginListModel> data, string path)
        {
            FileHandler.StringToFile(path, data.ToJson(true));
        }

        /// <summary>
        /// 获取插件界面配置
        /// </summary>
        /// <typeparam name="T">对象</typeparam>
        /// <param name="path">文件路径</param>
        /// <returns>返回指定对象</returns>
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
        /// 从插件文件名称得到对象SN码
        /// </summary>
        /// <param name="fileName">文件名称</param>
        /// <returns></returns>
        public static string PluginFileNameToSN(string fileName, PluginType type)
        {
            string newData = fileName.Replace($".{type.ToString()}.Config.json", string.Empty);
            return newData.Split('.').LastOrDefault() ?? string.Empty;
        }

        /// <summary>
        /// 创建一个新的对象
        /// </summary>
        /// <param name="plugin">插件配置</param>
        /// <returns>对象</returns>
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
        /// 生产
        /// </summary>
        /// <param name="plugin">采集插件配置</param>
        /// <param name="topic">主题</param>
        /// <param name="content">内容</param>
        /// <returns>生产结果</returns>
        public static async Task<OperateResult> TestProduceAsync(this PluginConfigModel plugin, string topic, string content)
        {
            IMq? mqNew = await plugin.CreateNewObjetcAsync<IMq>();
            OperateResult operateResult = await mqNew.GetStatusAsync();
            if (!operateResult.Status)
            {
                operateResult = await mqNew.OnAsync();
            }
            if (operateResult.Status)
            {
                //读取数据
                operateResult = await mqNew.ProduceAsync(topic, content);
                //释放掉这个连接
                await mqNew.OffAsync();
                await mqNew.DisposeAsync();
                return operateResult;
            }
            return operateResult;
        }


        /// <summary>
        /// 读取地址
        /// </summary>
        /// <param name="plugin">采集插件配置</param>
        /// <param name="model">地址的模型</param>
        /// <returns>读取结果</returns>
        public static async Task<OperateResult> TestReadAddressAsync(this AddressModel model, PluginConfigModel plugin)
        {
            IDaq? daqNew = await plugin.CreateNewObjetcAsync<IDaq>();
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
                    //读取数据
                    operateResult = await daqNew.ReadAsync(new Address() { AddressArray = [address] });
                    //释放掉这个连接
                    await daqNew.OffAsync();
                    await daqNew.DisposeAsync();
                    return operateResult;
                }
                operateResult = OperateResult.CreateFailureResult("地址转换失败");
            }
            return operateResult;
        }

        /// <summary>
        /// 写入地址
        /// </summary>
        /// <param name="plugin">采集插件配置</param>
        /// <param name="model">地址的模型</param>
        /// <param name="write">写入的数据</param>
        /// <returns></returns>
        public static async Task<OperateResult> TestWriteAddressAsync(this AddressModel model, PluginConfigModel plugin, WriteModel write)
        {
            IDaq? daqNew = await plugin.CreateNewObjetcAsync<IDaq>();
            OperateResult operateResult = await daqNew.GetStatusAsync();
            if (!operateResult.Status)
            {
                operateResult = await daqNew.OnAsync();
            }
            if (operateResult.Status)
            {
                //组织写入数据
                ConcurrentDictionary<string, WriteModel> keys = new();
                keys[model.Address] = write;
                //写入数据
                operateResult = await daqNew.WriteAsync(keys);
                //释放掉这个连接
                await daqNew.OffAsync();
                await daqNew.DisposeAsync();
            }
            return operateResult;
        }

        /// <summary>
        /// 传输数据
        /// </summary>
        /// <param name="plugin">传输采集信息</param>
        /// <param name="data">数据</param>
        /// <returns>操作结果</returns>
        public static async Task<OperateResult> TestTransmitDataAsync(this AddressModel address, PluginConfigModel plugin, AddressValue data)
        {
            IMq? mqNew = await plugin.CreateNewObjetcAsync<IMq>();
            OperateResult operateResult = await mqNew.GetStatusAsync();
            if (!operateResult.Status)
            {
                operateResult = await mqNew.OnAsync();
            }
            if (operateResult.Status)
            {
                //转换数据
                string content = address.SimplifyValue ? data.GetSimplify().ToJson(true) : data.ToJson(true);
                //读取数据
                operateResult = await mqNew.ProduceAsync(address.Topic, content);
                //释放掉这个连接
                await mqNew.OffAsync();
                await mqNew.DisposeAsync();
                return operateResult;
            }
            return operateResult;
        }

        /// <summary>
        /// 获取所有插件
        /// </summary>
        /// <param name="obj">全局静态对象</param>
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
        /// 添加插件到统一集合
        /// </summary>
        /// <param name="plugin">插件对象</param>
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
        /// 地址集合转换
        /// </summary>
        /// <param name="models">集合</param>
        /// <returns>转换后的数据</returns>
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
        /// 地址集合转换
        /// </summary>
        /// <param name="models">集合</param>
        /// <returns>转换后的数据</returns>
        public static Address AddressConvert(this AddressModel models)
        {
            Address address = new();
            address.AddressArray = new() { models.Convert() };
            return address;
        }
    }
}
