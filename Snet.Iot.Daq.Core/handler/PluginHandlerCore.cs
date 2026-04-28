using Snet.Core.plugin;
using Snet.Iot.Daq.Core.data;
using Snet.Iot.Daq.Core.@interface;
using Snet.Model.data;
using Snet.Model.@enum;
using Snet.Model.@interface;
using Snet.Utility;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;

namespace Snet.Iot.Daq.Core.handler
{
    /// <summary>
    /// 插件参数处理类<br/>
    /// 负责插件的加载、移除、实例化、参数转换、状态验证、配置读写等核心操作。
    /// 内部使用 ConcurrentDictionary 缓存程序集类型和实例，支持线程安全的并发访问。
    /// </summary>
    public static class PluginHandlerCore
    {
        /// <summary>
        /// 插件操作核心实例
        /// </summary>
        public static readonly PluginOperate pluginOperate = PluginOperate.Instance(typeof(PluginHandlerCore).Name);

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
            => await pluginOperate.CreateAsync<T>(plugin.Name, plugin.Param, plugin.Type);

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
        public static async Task<OperateResult> TestReadAddressAsync(this IAddressModel model, PluginConfigModel plugin)
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
        public static async Task<OperateResult> TestWriteAddressAsync(this IAddressModel model, PluginConfigModel plugin, WriteModel write)
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
        public static async Task<OperateResult> TestTransmitDataAsync(this IAddressModel address, PluginConfigModel plugin, AddressValue data)
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
        /// 将 AddressModel 列表批量转换为底层 Address 对象<br/>
        /// 预分配 AddressArray 容量以减少扩容开销
        /// </summary>
        /// <param name="models">地址模型集合</param>
        /// <returns>转换后的 Address 对象，包含所有地址详情</returns>
        public static Address AddressConvert(this List<IAddressModel> models)
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
        public static Address AddressConvert(this IAddressModel models)
        {
            Address address = new();
            address.AddressArray = new() { models.Convert() };
            return address;
        }
    }
}
