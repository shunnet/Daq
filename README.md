<h1 align="center">Daq</h1>

<p align="center">
  <img width="120" height="120" src="https://api.shunnet.top/pic/nuget.png" alt="Snet Logo"/>
</p>

<p align="center">
  <b>开源 · 免费 · 插件化 · 工业物联网数据采集工具</b>
</p>

<p align="center">

  <img src="https://img.shields.io/badge/.NET-10.0-blue"/>
  <img src="https://img.shields.io/badge/platform-Windows-success"/>
  <img src="https://img.shields.io/badge/license-MIT-green"/>
  <img src="https://img.shields.io/github/stars/shunnet/Daq?style=social"/>

</p>

<p align="center">
  基于插件架构的工业物联网（IIoT）数据采集与传输工具，内置 Sqlite 数据库，实现开箱即用的数据采集解决方案。
</p>

<p align="center">
  <a href="https://shunnet.top"><b>🌐 官方网站</b></a> ·
  <a href="https://github.com/shunnet/Daq"><b>📦 GitHub</b></a> ·
  <a href="https://Shunnet.top/YJybu"><b>🎬 演示视频</b></a>
</p>

## ✨ 项目介绍

Snet.Iot.Daq 是依托 **Shunnet.top 工业通信库** 开发的插件化数采工具，专为工业设备数据采集场景设计。

支持：

- 插件热插拔（运行时加载 / 卸载插件，无需重启）
- 多设备并发采集
- 字节级协议解析
- 插件扩展架构（DAQ 数据采集 / MQ 消息队列）
- 点位映射与存储
- 高性能稳定运行

适用于：

- 工业自动化
- PLC 数据采集
- 设备监控系统
- IoT 边缘采集网关
- 自定义协议设备

运行环境：

- 工业现场
- 边缘计算设备
- Windows 服务器



## 🚀 核心特性

- ✔ 完全开源免费（MIT License）
- ✔ 插件化架构，支持无限扩展
- ✔ **插件热插拔**：运行时上传 / 卸载插件，无需重启应用
- ✔ 内置 Sqlite 轻量级数据库
- ✔ 支持多设备并发采集
- ✔ 支持字节级协议解析
- ✔ 支持自定义点位映射
- ✔ 高性能低资源占用
- ✔ 开箱即用
- ✔ 支持长期稳定运行

## 🔌 插件热插拔

本项目基于 .NET `AssemblyLoadContext` 实现了完整的 **插件热插拔** 机制，支持在应用运行期间动态加载和卸载插件，无需重启进程。

### 工作流程

```
上传 ZIP 插件包 → 自动解压到插件目录 → 创建可回收 AssemblyLoadContext
→ 流式加载程序集 → 扫描并实例化 IDaq / IMq 接口 → 注册到 IOC 容器 → 开始采集
```

卸载流程：

```
停止采集 → 释放插件实例（IAsyncDisposable）→ 移除 IOC 注册
→ 卸载 AssemblyLoadContext → GC 回收 → 删除插件文件
```

### 技术亮点

| 特性 | 说明 |
| --- | --- |
| 可回收程序集上下文 | 使用 `AssemblyLoadContext(isCollectible: true)`，卸载后可被 GC 回收 |
| 流式加载（无文件锁） | 通过 `MemoryStream` + `LoadFromStream` 加载 DLL，避免文件被锁定，卸载后即可删除 |
| 类型一致性保证 | 共享接口程序集（如 `IDaq`、`IMq`）始终从默认上下文加载，确保 `as` 类型转换正确 |
| 并发安全 | 使用 `ConcurrentDictionary` 管理插件实例与上下文，支持多插件并发操作 |
| 双插件类型 | 同时支持 **DAQ**（数据采集类）和 **MQ**（消息队列类）两种插件 |
| 调试支持 | 加载时自动附带 PDB 符号文件，方便调试插件代码 |

### 插件开发

1. 新建 .NET 类库项目，引用 `IDaq` 或 `IMq` 接口
2. 实现接口方法（`On`、`Off`、`GetStatus` 等）
3. 编译后将输出目录打包为 **ZIP** 文件
4. 在程序「插件设置」页面上传 ZIP 即可自动加载

## ⚡ 性能特点

- ✔ 极低 CPU 占用
- ✔ 极低内存占用
- ✔ 支持高频采集
- ✔ 支持 24/7 长期运行
- ✔ 工业级稳定性


## 🖥️ 界面

<p align="center">
  <img src="images/home.png" width="900"/>
</p>

<p align="center">
  <img src="images/ps.png" width="900"/>
</p>

<p align="center">
  <img src="images/as.png" width="900"/>
</p>

<p align="center">
  <img src="images/prs.png" width="900"/>
</p>

<p align="center">
  <img src="images/cs.png" width="900"/>
</p>

<p align="center">
  <img src="images/ccs.png" width="900"/>
</p>

## 📦 安装与使用

### 1️⃣克隆仓库

``` bash
git clone https://github.com/shunnet/Daq.git
cd Daq
```

### 2️⃣ 编译项目

使用 **Visual Studio 2022** 或更高版本打开：

`Snet.Iot.Daq.sln`

选择 Debug 或 Release 构建。

### 3️⃣ 运行程序

构建完成后，在输出目录中找到 `Snet.Iot.Daq.exe`，双击运行即可启动。


## 🙏 致谢

- [Shunnet.top](https://shunnet.top)
- [WpfMUI](https://github.com/shunnet/WpfMUI)
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
- [scottplot.net](https://scottplot.net)
- [sqlite-net](https://github.com/praeclarum/sqlite-net)


## 🎬 查阅

👉 [点击跳转](https://Shunnet.top/YJybu)  


## 📜 License

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)  

本项目基于 **MIT** 开源。  
请阅读 [LICENSE](LICENSE) 获取完整条款。  
⚠️ 软件按 “原样” 提供，作者不对使用后果承担责任。  


