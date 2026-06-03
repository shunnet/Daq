<h1 align="center">🔌 Daq</h1>

<p align="center">
  <img width="120" height="120" src="https://api.shunnet.top/pic/nuget.png" alt="Snet Logo"/>
</p>

<p align="center">
  <b>开源 · 免费 · 插件化 · 工业物联网数据采集工具</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-blue?logo=dotnet"/>
  <img src="https://img.shields.io/badge/platform-Windows-success?logo=windows"/>
  <img src="https://img.shields.io/badge/license-MIT-green"/>
  <img src="https://img.shields.io/badge/Core-cross--platform-lightgrey"/>
  <img src="https://img.shields.io/github/stars/shunnet/Daq?style=social"/>
</p>

<p align="center">
  🏭 基于插件架构的工业物联网（IIoT）数据采集与传输工具<br/>
  内置 Sqlite 数据库 · OPC UA 服务端 · MQTT Broker · 开箱即用
</p>

<p align="center">
  <a href="https://shunnet.top"><b>🌐 官方网站</b></a> ·
  <a href="https://github.com/shunnet/Daq"><b>📦 GitHub</b></a> ·
  <a href="https://Shunnet.top/YJybu"><b>🎬 演示视频</b></a>
</p>

## ✨ 项目介绍

Snet.Iot.Daq 是依托 **Shunnet.top 工业通信库** 开发的插件化数采工具，专为工业设备数据采集场景设计。

### 🏗️ 项目架构

```
┌─────────────────────────────────┐
│   Snet.Iot.Daq (WPF 桌面应用)    │  ← UI 层：MVVM + Material Design
├─────────────────────────────────┤
│   Snet.Iot.Daq.Core (类库)       │  ← 核心层：业务逻辑 / 数据模型 / 服务
├─────────────────────────────────┤
│   Shunnet.top 工业通信库          │  ← 底层：插件框架 / 通信协议 / 工具
└─────────────────────────────────┘
```

> 💡 Core 层不依赖 WPF，可被 Avalonia 等跨平台框架复用（已有适配器预留）

### 🛠️ 技术栈

| 分类 | 技术 / 库 |
|------|-----------|
| 🖼️ **UI 框架** | WPF + MVVM + Material Design |
| 🔌 **插件引擎** | .NET `AssemblyLoadContext`（可回收上下文） |
| 📡 **工业协议** | OPC UA Server、MQTT Broker（Shunnet.top 通信库） |
| 💾 **数据存储** | SQLite（sqlite-net） |
| 📊 **图表可视化** | ScottPlot |
| 🖥️ **系统监控** | LibreHardwareMonitor |
| 🔧 **依赖注入** | Microsoft.Extensions.DependencyInjection |
| 🌐 **多语言** | 资源文件本地化（中 / 英） |

### 📋 功能矩阵

| 模块 | 说明 |
|------|------|
| 🔌 **插件热插拔** | 运行时加载 / 卸载采集或传输插件，无需重启 |
| 📡 **OPC UA 服务端** | 内置 OPC UA Server，支持认证、证书、地址空间管理、持久订阅 |
| 📨 **MQTT Broker** | 内置 MQTT 消息代理，支持客户端管理、认证、最大连接数控制 |
| 📊 **实时图表** | 基于 ScottPlot 的多曲线实时图表，支持皮肤切换、历史数据 |
| 🖥️ **系统监控** | CPU / GPU / RAM 实时仪表盘，基于 LibreHardwareMonitor |
| 🎯 **字节级解析** | 可视化的字节/位/编码/数据格式配置器，支持任意协议解析 |
| 📦 **NuGet 插件市场** | 在线浏览、下载、一键安装 Shunnet 生态插件 |
| 🌐 **多语言国际化** | 中英文双语界面，通过资源文件统一管理 |
| 🌓 **主题切换** | 暗色 / 亮色主题一键切换，图表跟随变色 |
| 🔢 **自动组包** | 离散地址智能合并批量读取，降低通信开销 |
| ⚡ **设备软启动** | 软件启动时自动开始采集，无需手动干预 |
| 🔔 **系统托盘** | 最小化到托盘后台运行，单实例强制保护 |
| ❄️ **雪花动画** | 首页雪花粒子特效，主题跟随变色 |

### 🎯 适用场景

- 🏭 工业自动化数据采集
- 🔧 PLC / 设备监控系统
- 🌐 IoT 边缘采集网关
- 📡 OPC UA / MQTT 数据中转
- 🔬 自定义协议设备接入



## 🚀 核心特性

### 🏗️ 架构设计
- ✅ 完全开源免费（MIT License）
- ✅ 插件化架构，支持无限扩展
- ✅ **插件热插拔**：运行时加载 / 卸载，无需重启应用

### 📡 工业协议
- ✅ 内置 **OPC UA 服务端** + **MQTT Broker**，开箱即用
- ✅ 支持多设备并发采集
- ✅ 可视化的字节级协议解析器

### 📊 数据与可视化
- ✅ 内置 SQLite 轻量级数据库
- ✅ 多曲线实时图表（ScottPlot）
- ✅ 实时系统监控（CPU / GPU / RAM）

### 🔌 生态扩展
- ✅ NuGet 在线插件市场，一键下载安装

### 🎨 用户体验
- ✅ 中英文多语言支持
- ✅ 暗色 / 亮色主题切换
- ✅ 🔔 系统托盘最小化后台运行
- ✅ ⚡ 设备软启动，开机自动采集
- ✅ 🔢 自动组包优化，智能合并离散地址

### ⚡ 性能保障
- ✅ 极低 CPU / 内存占用
- ✅ 支持高频采集
- ✅ 支持 24/7 长期稳定运行
- ✅ 工业级稳定性

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

> 🤖 **AI 辅助开发**：推荐使用 [Snet.SKILLS](https://github.com/shunnet/SKILLS) —— 针对 SNET 架构的 AI 技能集合，可加速插件开发流程。

## 🖥️ 界面展示

<p align="center"><b>🏠 主界面</b></p>
<p align="center">
  <img src="images/home.png" width="900"/>
</p>

<p align="center"><b>📊 性能监控</b></p>
<p align="center">
  <img src="images/ps.png" width="900"/>
</p>

<p align="center"><b>📡 OPC UA 地址空间</b></p>
<p align="center">
  <img src="images/as.png" width="900"/>
</p>

<p align="center"><b>🔌 协议解析器</b></p>
<p align="center">
  <img src="images/prs.png" width="900"/>
</p>

<p align="center"><b>⚙️ 采集配置</b></p>
<p align="center">
  <img src="images/cs.png" width="900"/>
</p>

<p align="center"><b>🎨 主题定制</b></p>
<p align="center">
  <img src="images/ccs.png" width="900"/>
</p>

## 📦 安装与使用

### 📋 环境要求

| 组件 | 要求 |
|------|------|
| 🖥️ **操作系统** | Windows 10 / 11 (x64) |
| 🔧 **.NET 运行时** | .NET 10.0 Desktop Runtime |
| 🛠️ **开发工具** | Visual Studio 2022+（编译需要） |
| 💾 **磁盘空间** | ≥ 200 MB |

### 1️⃣ 克隆仓库

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

## 📖 文档与资源

| 资源 | 链接 |
|------|------|
| 🎬 **演示视频** | [点击观看](https://Shunnet.top/YJybu) |
| 🌐 **官方网站** | [shunnet.top](https://shunnet.top) |
| 📦 **NuGet 插件市场** | 应用内「插件设置」页面浏览 |

## 💬 社区与支持

| 渠道 | 说明 |
|------|------|
| 🐛 **Issues** | [GitHub Issues](https://github.com/shunnet/Daq/issues) — 反馈 Bug 或功能建议 |
| 💬 **QQ群** | [点击加群](https://qm.qq.com/q/gPjrD9wGty) — 技术交流与问答 |
| ⭐ **Star** | 如果这个项目对你有帮助，请点亮 Star 支持我们 ❤️ |

## 📈 Star History

<p align="center">
  <a href="https://star-history.com/#shunnet/Daq&Date">
    <img src="https://api.star-history.com/svg?repos=shunnet/Daq&type=Date" alt="Star History Chart" width="600"/>
  </a>
</p>

## 📜 License

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)

本项目基于 **MIT** 开源协议 —— 自由使用、修改、分发。

📄 完整条款请阅读 [LICENSE](LICENSE) 文件。

> ⚠️ 软件按「原样」提供，作者不对使用后果承担责任。
