<h1 align="center">🔌 Snet.Iot.Daq</h1>

<p align="center">
  <img width="120" height="120" src="https://api.snet.cn/pic/nuget.png" alt="Snet Logo"/>
</p>

<p align="center">
  <b>开源 · 免费 · 插件化 · 工业物联网数据采集工具</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-blue?logo=dotnet"/>
  <img src="https://img.shields.io/badge/platform-Windows-success?logo=windows"/>
  <img src="https://img.shields.io/badge/Web-Linux%20x64%20%2F%20ARM64-blue?logo=linux"/>
  <img src="https://img.shields.io/badge/license-MIT-green"/>
  <img src="https://img.shields.io/badge/Core-cross--platform-lightgrey"/>
  <img src="https://img.shields.io/github/stars/shunnet/Daq?style=social"/>
</p>

<p align="center">
  <a href="https://snet.cn"><b>🌐 官方网站</b></a> ·
  <a href="https://github.com/shunnet/Daq"><b>📦 GitHub</b></a> ·
  <a href="https://github.com/shunnet/Daq/releases"><b>📥 下载</b></a> ·
  <a href="https://github.com/shunnet/Debug"><b>🔧 调试工具 Debug</b></a> ·
  <a href="https://Snet.cn/YJybu"><b>🎬 演示视频</b></a>
</p>

<p align="center">
  📖 <a href="README.en.md"><b>English</b></a> | 简体中文
</p>

## ✨ 项目简介

**Snet.Iot.Daq** 是依托 **Snet.cn 工业通信库** 开发的插件化数采工具，专为工业设备数据采集场景设计。

```
┌────────────────────────────────────────┐
│  Snet.Iot.Daq (WPF 桌面应用 / Windows)  │  ← UI 层：MVVM + 现代化界面
├────────────────────────────────────────┤
│  Snet.Iot.Daq.Web (Web 应用 / Linux)    │  ← UI 层：Blazor Server，浏览器访问
├────────────────────────────────────────┤
│  Snet.Iot.Daq.Core (类库)               │  ← 核心层：业务逻辑 / 数据模型 / 服务
├────────────────────────────────────────┤
│  Snet.cn 工业通信库                  │  ← 底层：插件框架 / 通信协议 / 工具
└────────────────────────────────────────┘
```

> 💡 Core 层不依赖 WPF，桌面版（Windows）与 Web 跨平台版（Linux / ARM64）共用同一核心业务逻辑

## 🚀 功能总览

| 模块 | 说明 |
|------|------|
| 🔌 **插件热插拔** | 运行时加载 / 卸载 DAQ / MQ 插件，无需重启；同名插件支持热更新 |
| 📡 **OPC UA 服务端** | 内置 OPC UA Server，支持认证、证书、地址空间管理、持久订阅 |
| 📨 **MQTT Broker** | 内置 MQTT 消息代理，支持客户端管理、认证、最大连接数控制 |
| 🌐 **WebAPI 服务** | 内置 HTTP 服务（WAOn / WAOff 启停），支持外部系统数据接入 |
| 📊 **实时图表** | ScottPlot 多曲线实时图表，皮肤切换、历史数据 |
| 🖥️ **系统监控** | CPU / GPU / RAM 实时仪表盘（LibreHardwareMonitor + WMI 双通道） |
| 🎯 **字节级解析** | 可视化字节 / 位 / 编码 / 数据格式配置器，自定义协议解析 |
| 📦 **NuGet 插件市场** | 在线浏览、下载、一键安装 Snet 生态插件 |
| 🔢 **自动组包** | 离散地址智能合并批量读取，降低通信开销 |
| ⚡ **设备软启动** | 软件启动时自动开始采集，无需手动干预 |
| 🔔 **系统托盘** | 最小化后台运行，托盘右键直接启停设备，单实例保护 |
| 🌐 **多语言 / 🌓 主题** | 中英文切换 · 暗 / 亮主题，图表跟随变色 |
| 🌐 **Web 跨平台版** | Blazor Server 浏览器访问，与桌面版共享 Core，支持 Linux x64 / ARM64 部署 |

### 🎯 适用场景

🏭 工业自动化数据采集 · 🔧 PLC / 设备监控系统 · 🌐 IoT 边缘采集网关 · 📡 OPC UA / MQTT 数据中转 · 🔬 自定义协议设备接入

## 🔌 插件系统

插件引擎（可回收 `AssemblyLoadContext` + 流式加载）由 **Snet.Core** 包提供，本应用负责插件管理与调度。

### 🔄 加载 / 卸载流程

```
上传 ZIP 插件包 → 自动解压到插件目录 → 创建可回收 AssemblyLoadContext
→ 流式加载程序集 → 扫描并实例化 IDaq / IMq 接口 → 注册到 IOC 容器 → 开始采集
```

```
停止采集 → 释放插件实例（IAsyncDisposable）→ 移除 IOC 注册
→ 卸载 AssemblyLoadContext → GC 回收 → 删除插件文件
```

> 🔁 **热更新**：上传同名插件包时，自动执行「停止设备 → 更新 → 恢复运行状态」，全程无需重启。

### 🛠️ 插件开发

1. 新建 .NET 类库项目，添加 NuGet 包：`Snet.Core`（提供插件引擎与 `IDaq` / `IMq` 接口，接口位于 `Snet.Model.@interface` 命名空间）
2. 实现接口方法（`OnAsync`、`OffAsync`、`ReadAsync`、`WriteAsync`、`GetStatusAsync` 等）
3. 编译后将输出目录打包为 **ZIP** 文件
4. 在程序「插件设置」页面上传 ZIP 即可自动加载

> 🤖 **AI 辅助开发**：推荐使用 [Snet.SKILLS](https://github.com/shunnet/SKILLS) —— 针对 SNET 架构的 AI 技能集合，加速插件开发。

## 🌐 WEB 跨平台版本（Snet.Iot.Daq.Web）

**Blazor Server Web 版**，与桌面版共用 `Snet.Iot.Daq.Core` 全部业务能力，浏览器访问即可使用，无需安装客户端。

- **平台支持**：Windows / Linux x64 / Linux ARM64（树莓派、飞腾、鲲鹏等）
- **界面**：青蓝玻璃拟态 UI，暗 / 亮双主题，中英文切换，手机 / 平板自适应布局
- **功能对齐**：插件热插拔与热更新、OPC UA 服务端、MQTT Broker、WebAPI、自动组包、设备软启动
- **多用户与安全**：管理员 / 普通用户两级权限（普通用户只读），登录失败锁定，操作日志审计
- **差异项**：无桌面版专属的 ScottPlot 图表、系统托盘与 GPU 监控

### 🐳 Docker 部署（推荐）

```bash
git clone https://github.com/shunnet/Daq.git && cd Daq
docker compose -f Snet.Iot.Daq.Web/docker-compose.yml up -d --build
# 浏览器访问 http://<服务器IP>:5051
```

多架构镜像（linux/amd64 + linux/arm64）由 GitHub Actions 自动构建并推送到 `ghcr.io`。

### 🐧 Ubuntu 裸机部署

```bash
sudo bash Snet.Iot.Daq.Web/deploy/ubuntu-deploy.sh          # 自动装运行时 → 发布 → systemd 服务
sudo bash Snet.Iot.Daq.Web/deploy/ubuntu-deploy.sh --port 8080 --data /srv/snet-daq
```

### 📦 GitHub Actions 多平台打包

推送 `v*` tag 自动触发：产出 **linux-x64 / linux-arm64 / win-x64** 发布包（附 systemd 部署脚本）与 **ghcr.io 双架构 Docker 镜像**，并发布到 [GitHub Releases](https://github.com/shunnet/Daq/releases)。

📄 完整部署细节（数据持久化 / 端口 / 安全加固）见 [Snet.Iot.Daq.Web/README.DEPLOY.md](Snet.Iot.Daq.Web/README.DEPLOY.md)。

## 📦 安装与使用（桌面版）

### 📋 环境要求

| 组件 | 要求 |
|------|------|
| 🖥️ **操作系统** | Windows 10 / 11 (x64) |
| 🔧 **.NET 运行时** | .NET 10.0 Desktop Runtime |
| 🛠️ **开发工具** | Visual Studio 2022+（编译需要） |
| 💾 **磁盘空间** | ≥ 200 MB |

### 📥 1️⃣ 克隆仓库

```bash
git clone https://github.com/shunnet/Daq.git
cd Daq
```

### 🔨 2️⃣ 编译项目

使用 **Visual Studio 2022** 或更高版本打开 `Snet.Iot.Daq.sln`，选择 Debug 或 Release 构建。

### ▶️ 3️⃣ 运行程序

构建完成后运行输出目录中的 `Snet.Iot.Daq.exe`。

> 💡 **无需编译？** 前往 [GitHub Releases](https://github.com/shunnet/Daq/releases) 下载预编译 ZIP 包，解压即用。

## 👥 账号与权限（仅 WEB 跨平台版）

> 💡 桌面版（WPF）为单机使用，无登录与权限体系；以下权限模型仅适用于 **WEB 跨平台版（Snet.Iot.Daq.Web）**。

系统采用管理员 / 普通用户两级权限：

| 角色 | 权限 |
|------|------|
| **管理员** | 全部功能：插件浏览与设置、地址管理、项目配置、采集启停、WebApi / 组包设置、服务端管理、用户管理 |
| **普通用户** | **只读访问**：仅可查看控制台（设备状态、系统监控、运行日志），无任何操作按钮 |

**默认管理员账号**：`snet` / `123456`

**首次登录修改密码**（系统强制）：

1. 使用 `snet` / `123456` 登录，系统自动进入修改密码页面
2. 输入当前密码 `123456`、新密码（至少 6 位）并确认
3. 保存后使用新密码重新登录

> ⚠️ 生产部署后请立即修改默认密码；普通用户由管理员在「用户管理」页面创建。

## 🖥️ 界面展示

### 🖥️ 桌面版（WPF / Windows）

<p align="center">
  <img src="images/home.png" width="900"/>
  <img src="images/pb.png" width="900"/>
  <img src="images/ps.png" width="900"/>
  <img src="images/as.png" width="900"/>
  <img src="images/prs.png" width="900"/>
  <img src="images/cs.png" width="900"/>
  <img src="images/ccs.png" width="900"/>
</p>

### 🌐 Web 跨平台版（浏览器访问）

<p align="center">
  <img src="images/w1.png" width="900"/>
  <img src="images/w2.png" width="900"/>
  <img src="images/w3.png" width="900"/>
  <img src="images/w4.png" width="900"/>
  <img src="images/w5.png" width="900"/>
  <img src="images/w6.png" width="900"/>
  <img src="images/w7.png" width="900"/>
  <img src="images/w8.png" width="900"/>
</p>

## 📚 资源与社区

| 渠道 | 链接 |
|------|------|
| 🎬 **演示视频** | [点击观看](https://Snet.cn/YJybu) |
| 🌐 **官方网站** | [snet.cn](https://snet.cn) |
| 📦 **NuGet 插件市场** | 应用内「插件设置」页面浏览 |
| 🐛 **Issues** | [GitHub Issues](https://github.com/shunnet/Daq/issues) — 反馈 Bug 或功能建议 |
| 💬 **QQ 群** | [点击加群](https://qm.qq.com/q/gPjrD9wGty) — 技术交流与问答 |
| ⭐ **Star** | 如果这个项目对你有帮助，请点亮 Star 支持我们 ❤️ |

## 🙏 致谢

- [Snet.cn](https://snet.cn) — 工业通信库
- [Snet.Windows.Controls](https://github.com/shunnet/WpfMUI) — WPF 控件库
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) — 硬件监控
- [ScottPlot](https://scottplot.net) — 科学图表库
- [sqlite-net](https://github.com/praeclarum/sqlite-net) — 轻量级数据库

## 📜 License

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)

本项目基于 **MIT** 开源协议 —— 自由使用、修改、分发。

📄 完整条款请阅读 [LICENSE](LICENSE) 文件。

> ⚠️ 软件按「原样」提供，作者不对使用后果承担责任。

## 📈 Star History

<a href="https://www.star-history.com/?repos=shunnet%2FDaq&type=date&legend=bottom-right">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=shunnet/Daq&type=date&theme=dark&legend=bottom-right&sealed_token=3Tft7-uvG5B6fob8INAvqe9armRuIkXlOveR3cnY2kXGwaJMMtYWOyq45srnSCO-Dq6_0dPyepq-b8O_4fMB87CqYCIZdawTTa_JHyS2oahHiDr0o_2NTA"/>
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=shunnet/Daq&type=date&legend=bottom-right&sealed_token=3Tft7-uvG5B6fob8INAvqe9armRuIkXlOveR3cnY2kXGwaJMMtYWOyq45srnSCO-Dq6_0dPyepq-b8O_4fMB87CqYCIZdawTTa_JHyS2oahHiDr0o_2NTA"/>
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=shunnet/Daq&type=date&legend=bottom-right&sealed_token=3Tft7-uvG5B6fob8INAvqe9armRuIkXlOveR3cnY2kXGwaJMMtYWOyq45srnSCO-Dq6_0dPyepq-b8O_4fMB87CqYCIZdawTTa_JHyS2oahHiDr0o_2NTA"/>
 </picture>
</a>
