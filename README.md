# <img src="https://api.shunnet.top/pic/nuget.png" height="32"> Snet - 工业协议与数据采集框架

> 🚀 **统一 · 高效 · 灵活 · 可扩展**  
> 面向工业数据采集、传输、转发、消息中间件的全栈式解决方案


## ✨ 框架特色

1. ✅ 所有协议公共函数 **支持同步 / 异步**  
2. ✅ 协议 **读写参数统一**，入参出参统一  
3. ✅ **支持多点转发 / 多点解析**  
4. ✅ **快速对接与配置**，可即插即用  
5. ✅ **支持虚拟点位**（模拟测试更便捷）  
6. ✅ 采集协议全面支持 **订阅模式**  
7. ✅ **采集与转发协议接口统一**  
8. ✅ 事件结果统一，并支持 **异步事件**  
9. ✅ 所有采集协议均支持 **WebAPI 控制与数据获取**  
10. ✅ **快捷二次开发**，仅需继承接口与抽象类即可  
11. ✅ 拥有详细的**日志记录**，**高度自定义**日志输出路径与记录到数据库
12. ✅ 全局支持**中英文**实时切换
13. ✅ 支持**反射**，**脚本解析**，**进程缓存**，**共享缓存**，**抽象扩展**
14. ✅ 支持**JSON**/**XML**/**Protobuf**...，一系列**序列化**与**反序列化**


## 🧩 核心组件  

- **[ Snet.Log ]** 日志系统  
  - `Verbose` 详细信息  
  - `Debug` 调试  
  - `Info` 信息  
  - `Warning` 警告  
  - `Error` 错误  
  - `Fatal` 致命错误  

- **[ Snet.Unility ]** 公共方法集合  
  - 字节、枚举、文件、字符串、验证、比对、转换、反射、Json、Xml、Ftp、System ...  

- **[ Snet.Model ]**  
  - 特性、数据结构、枚举、接口  

- **[ Snet.Core ]**  
  - 抽象、扩展、处理、反射、队列、中间件、脚本、订阅、虚拟地址、TCP、UDP、HTTP、WS、串口、WebApi  

- **[ Snet.Driver ]**  
  - 底层驱动通信库  


## 📡 采集协议

支持 20+ 种工业通信协议，覆盖 **PLC / 工控 / 电力 / 机器人 / DB / 标准通信** 等：  

```
三菱 / 西门子 / Modbus / 汇川 / 欧姆龙 / LSis / 基恩士 / 松下 / 罗克韦尔 / 倍福  
通用电器 / 安川 / 山武 / 永宏 / 丰炜 / 富士 / 信捷 / 麦格米特 / 横河 / 丰田 / 台达 / 维控  
电力通讯规约 / OPC (UA、DA、DAHttp) / DB (SqlServer、MySql、Oracle、SQLite)  
TEP (Tcp扩展插件) / Sim (模拟库) / 英威腾 / 西蒙 / 发那科 / 自由协议 / 图尔克 / 理化
```

接口定义：

```csharp
/// <summary>
/// 数采接口
/// </summary>
public interface IDaq : IOn, IOff, IRead, IWrite, ISubscribe, IGetStatus, IEvent, IGetParam, ICreateInstance, ILog, IWA, IGetObject, ILanguage, IDisposable, IAsyncDisposable { }
```


## 📬 消息中间件协议

支持常见的高性能消息中间件：  

- Kafka [AdminClient、Producer、Consumer]  
- Mqtt [Client (Publish/Subscribe)、Service、WSService]  
- RabbitMQ [Publish、Subscribe]  
- Netty [Client (Publish/Subscribe)、Service]  
- NetMQ [Publish、Subscribe]  

接口定义：

```csharp
/// <summary>
/// 消息中间件接口
/// </summary>
public interface IMq : IOn, IOff, IProducer, IConsumer, IGetStatus, IEvent, IGetParam, ICreateInstance, ILog, ILanguage, IDisposable, IAsyncDisposable { }
```


## 🖥️ 协议服务端 (数据模拟)

- Mqtt 服务端  
- Mqtt WebSocket 服务端  
- OpcUa 服务端  
- Socket 服务端  
- WebSocket 服务端  


## ⚡ 实例创建方式

支持 **无参、有参、单例、接口化实例** 等多种方式，快速上手：  

```csharp
//实例创建的几种方式
//以OPCUA 采集协议为例
using Snet.Model.@interface;
using Snet.Opc.ua.client;


OpcUaClientOperate? operate = null;
IDaq? daq = null;


//无参实例
operate = new OpcUaClientOperate();
//无参实例调函数创建实例，与无参实例配合使用
operate = new OpcUaClientOperate().CreateInstance(new OpcUaClientData.Basics()).GetRData<OpcUaClientOperate>();
//有参实例
operate = new OpcUaClientOperate(new OpcUaClientData.Basics());
//有参单例
operate = OpcUaClientOperate.Instance(new OpcUaClientData.Basics());
//接口 - 无参实例
daq = new OpcUaClientOperate();
//接口 - 无参实例调函数创建实例，与无参实例配合使用
daq = new OpcUaClientOperate().CreateInstance(new OpcUaClientData.Basics()).GetRData<OpcUaClientOperate>();
//接口 - 有参实例
daq = new OpcUaClientOperate(new OpcUaClientData.Basics());
//接口 - 有参单例
daq = OpcUaClientOperate.Instance(new OpcUaClientData.Basics());


using (operate)
{
    //使用完直接释放
}


using (daq)
{
    //使用完直接释放
}
```


## 📥 采集应用示例

通过 **NuGet** 安装协议包，快速实现采集：  

```csharp
//采集协议
//以OPCUA 采集协议为例
using System.Collections.Concurrent;
using Snet.Core.script;
using Snet.Log;
using Snet.Model.data;
using Snet.Model.@enum;
using Snet.Opc.ua.client;
using Snet.Utility;

using (OpcUaClientOperate operate = new OpcUaClientOperate(new OpcUaClientData.Basics
{
    ServerUrl = "opc.tcp://127.0.0.1:6688",
    UserName = "user",
    Password = "password",
}))
{
    //点位地址
    Address address = new Address();
    address.SN = Guid.NewGuid().ToString();
    address.CreationTime = DateTime.Now.ToLocalTime();
    address.AddressArray = new List<AddressDetails>
    {
         new AddressDetails()                                        //地址详情参数介绍
        {
            SN=$"",                                                  //可以理解成唯一标识符（可以存机台号、组名、车间、厂）
            AddressAnotherName="",                                   //地址别名
            AddressDataType=DataType.String,                         //数据类型
            AddressDescribe="",                                      //地址描述
            AddressExtendParam=new object(),                         //扩展数据
            AddressName="",                                          //实际地址[ 不能为空 ]
            AddressPropertyName="",                                  //属性名称
            AddressType=AddressType.Reality,                         //地址类型
            IsEnable=true,                                           //是否启用
            AddressMqParam=new AddressMq                      		 //消息队列生产
            {
                ISns = new List<string> { "ISN1", "ISN2" },          //实例SN
                Topic = $"topic",                                    //主题
                ContentFormat="Value:{0}"                            //内容格式
            },
            AddressParseParam = new AddressParse                  //反射解析
            {
                ReflectionParam = new object[]                    //反射解析的参数
                {
                    new ReflectionData.Basics                     //反射解析基础数据
                    {
                                                                  //反射解析的基础数据
                    },
                    "SN"                                          //反射解析的SN
                }
             },
         }
    };

    #region 打开
    OperateResult result = operate.On();
    LogHelper.Info(result.ToJson(true));  //转成JSON.JSON格式化
    #endregion 打开

    #region 读取
    //读取
    result = operate.Read(address);
    LogHelper.Info(result.ToJson(true));   //转成JSON.JSON格式化
    #endregion 读取

    #region 订阅
	//事件信息结果
	operate.OnInfoEvent += delegate (object? sender, EventInfoResult e)
	{
		LogHelper.Info(e.ToJson(true));    //转成JSON.JSON格式化
	};
	//事件数据结果
    operate.OnDataEvent += delegate (object? sender, EventDataResult e)
	{
		LogHelper.Info(e.ToJson(true));    //转成JSON.JSON格式化

		//得到精简版数据 速度很快 <=2ms
		IEnumerable<AddressValueSimplify>? simplifies = e.GetSource<ConcurrentDictionary<string, AddressValue>>()?.GetSimplifyArray();
	};
    result = operate.Subscribe(address);
    LogHelper.Info(result.ToJson(true));   //转成JSON.JSON格式化
    #endregion 订阅

    #region 写入
    ConcurrentDictionary<string, object> value = new ConcurrentDictionary<string, object>
    {
        ["地址"] = "string 值",
        ["地址"] = (float)1.1f,
        ["地址"] = (double)2.2d,
        ["地址"] = (int)3,
        ["地址"] = true
    };
    result = operate.Write(value);
    LogHelper.Info(result.ToJson(true));   //转成JSON.JSON格式化
    #endregion 写入

    #region 关闭
    result = operate.Off();
    LogHelper.Info(result.ToJson(true));   //转成JSON.JSON格式化
    #endregion 关闭

    #region 获取状态
    result = operate.GetStatus();
    LogHelper.Info(result.ToJson(true));   //转成JSON.JSON格式化
    #endregion 获取状态

    #region 获取参数
    result = operate.GetParam();
    LogHelper.Info(result.GetRData<ParamStructure>().ToJson(true));   //转成JSON.JSON格式化
    #endregion 获取参数
}
```


## 🔄 MQ应用示例

```csharp
//MQ协议
//以MQTT为例
using Snet.Log;
using Snet.Model.data;
using Snet.Mqtt.client;
using Snet.Utility;

using (MqttClientOperate operate = new MqttClientOperate(new MqttClientData.Basics
{
    Ip = "127.0.0.1",
    Port = 11819,
    UserName = "user",
    Password = "password"
}))
{
    #region 打开
    OperateResult result = operate.On();
    LogHelper.Info(result.ToJson(true));   //转成JSON.JSON格式化
    #endregion 打开

    #region 生产
    result = operate.Produce("主题", "内容");
    LogHelper.Info(result.ToJson(true));   //转成JSON.JSON格式化
    #endregion 生产

    #region 消费
    //事件信息结果
	operate.OnInfoEvent += delegate (object? sender, EventInfoResult e)
	{
		LogHelper.Info(e.ToJson(true));    //转成JSON.JSON格式化
	};
	//事件数据结果
    operate.OnDataEvent += delegate (object? sender, EventDataResult e)
	{
		LogHelper.Info(e.ToJson(true));    //转成JSON.JSON格式化

		//根据传进的RRT 转发响应类型来判断
		 switch (basics.RT)
		 {
			 case ResponseType.Bytes:  //字节数据

				 byte[] bytes = e.GetRData<byte[]>();

				 break;
			 case ResponseType.Content:  //字符串数据

				 string str = e.GetRData<string>();

				 break;
			 case ResponseType.ContentWithTopic: //带主题与内容的包体数据

			     ResponseModel rm = e.RData.ToJsonEntity<ResponseModel>();

				 break;
		 }
	};
    result = operate.Consume("主题");
    LogHelper.Info(result.ToJson(true));   //转成JSON.JSON格式化
    #endregion 消费

    #region 关闭
    result = operate.Off();
    LogHelper.Info(result.ToJson(true));   //转成JSON.JSON格式化
    #endregion 关闭

    #region 获取状态
    result = operate.GetStatus();
    LogHelper.Info(result.ToJson(true));   //转成JSON.JSON格式化
    #endregion 获取状态

    #region 获取参数
    result = operate.GetParam();
    LogHelper.Info(result.GetRData<ParamStructure>().ToJson(true));   //转成JSON.JSON格式化
    #endregion 获取参数
}
```


## ⚙️ 常用操作类名

```csharp
SerialOperate       // 串口通信
HttpClientOperate   // HTTP 客户端
HttpServiceOperate  // HTTP 服务端
TcpClientOperate    // TCP 客户端
TcpServiceOperate   // TCP 服务端
UdpOperate          // UDP 通信
WsClientOperate     // WebSocket 客户端
WsServiceOperate    // WebSocket 服务端
ProcessCacheOperate // 内存缓存
ShareCacheOperate   // 共享缓存
ReflectionOperate   // 反射操作
ChannelOperate 		// 通道操作
```


## 📦 快速开始

1. 打开 **NuGet** 搜索并安装对应协议包  
2. 引入命名空间 `using Snet.XXX`  
3. 按照上方示例编写采集/转发代码  


## 🏗️ 架构总结

Snet 框架通过 **统一接口设计** 与 **模块化协议支持**，实现了：

- 数据采集  
- 协议转发  
- 消息中间件交互  
- 模拟服务端测试  
- 高效日志与缓存  

从 **设备 → 协议 → 中间件 → 应用层** 全链路打通，支持快速构建工业物联网系统。  


## 📜 许可证  

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)  

本项目基于 **MIT** 开源。  
请阅读 [LICENSE](LICENSE) 获取完整条款。  
⚠️ 软件按 “原样” 提供，作者不对使用后果承担责任。  


## 🌍 查阅  

👉 [点击跳转](https://Shunnet.top/rRbY7)  
