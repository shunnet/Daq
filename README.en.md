<h1 align="center">🔌 Snet.Iot.Daq</h1>

<p align="center">
  <img width="120" height="120" src="https://api.snet.cn/pic/nuget.png" alt="Snet Logo"/>
</p>

<p align="center">
  <b>Open source · Free · Plugin-based · Industrial IoT data acquisition tool</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-10.0-blue?logo=dotnet"/>
  <img src="https://img.shields.io/badge/platform-Windows-success?logo=windows"/>
  <img src="https://img.shields.io/badge/license-MIT-green"/>
  <img src="https://img.shields.io/badge/Core-cross--platform-lightgrey"/>
  <img src="https://img.shields.io/github/stars/shunnet/Daq?style=social"/>
</p>

<p align="center">
  <a href="https://snet.cn"><b>🌐 Website</b></a> ·
  <a href="https://github.com/shunnet/Daq"><b>📦 GitHub</b></a> ·
  <a href="https://github.com/shunnet/Daq/releases"><b>📥 Download</b></a> ·
  <a href="https://github.com/shunnet/Debug"><b>🔧 Debug Tool</b></a> ·
  <a href="https://Snet.cn/YJybu"><b>🎬 Demo</b></a>
</p>

<p align="center">
  English | 📖 <a href="README.md"><b>简体中文</b></a>
</p>

## ✨ Introduction

**Snet.Iot.Daq** is a plugin-based data acquisition tool built on the **Snet.cn industrial communication library**, designed for industrial device data collection.

```
┌─────────────────────────────────┐
│   Snet.Iot.Daq (WPF desktop app) │  ← UI: MVVM + modern interface
├─────────────────────────────────┤
│   Snet.Iot.Daq.Core (library)    │  ← Core: business logic / models / services
├─────────────────────────────────┤
│   Snet.cn industrial comm. lib.  │  ← Foundation: plugin framework / protocols / tools
└─────────────────────────────────┘
```

> 💡 The Core layer has no WPF dependency, so it can be reused by cross-platform frameworks such as Avalonia (adapters reserved).

## 🚀 Feature Overview

| Module | Description |
|--------|-------------|
| 🔌 **Plugin Hot-Plug** | Load / unload DAQ / MQ plugins at runtime without restart; hot-update support |
| 📡 **OPC UA Server** | Built-in OPC UA Server with auth, certificates, address space management, persistent subscriptions |
| 📨 **MQTT Broker** | Built-in MQTT broker with client management, auth, max-connection control |
| 🌐 **WebAPI Service** | Built-in HTTP service (WAOn / WAOff), external system data ingestion |
| 📊 **Real-time Charts** | ScottPlot multi-line charts with skin switching and history |
| 🖥️ **System Monitoring** | CPU / GPU / RAM dashboards (LibreHardwareMonitor + WMI dual-channel) |
| 🎯 **Byte-Level Parser** | Visual byte / bit / encoding / data-format configurator for custom protocol parsing |
| 📦 **NuGet Plugin Market** | Browse, download and one-click install Snet ecosystem plugins |
| 🔢 **Auto Batching** | Smart merge of scattered addresses into batched reads, lower communication overhead |
| ⚡ **Soft Start** | Starts acquisition automatically on launch |
| 🔔 **System Tray** | Minimize to tray, start/stop devices from tray context menu, single-instance guard |
| 🌐 **i18n / 🌓 Themes** | Chinese / English switching · dark / light themes, charts follow the skin |

### 🎯 Use Cases

🏭 Industrial automation data collection · 🔧 PLC / device monitoring · 🌐 IoT edge gateway · 📡 OPC UA / MQTT relay · 🔬 Custom protocol device integration

## 🔌 Plugin System

The plugin engine (collectible `AssemblyLoadContext` + stream loading) is provided by the **Snet.Core** package; this app handles plugin management and orchestration.

### 🔄 Load / Unload Flow

```
Upload ZIP plugin → auto-extract to plugin dir → create collectible AssemblyLoadContext
→ stream-load assemblies → scan & instantiate IDaq / IMq → register into IOC → start acquisition
```

```
Stop acquisition → dispose plugin (IAsyncDisposable) → remove IOC registration
→ unload AssemblyLoadContext → GC collect → delete plugin files
```

> 🔁 **Hot Update**: uploading a plugin package with the same name runs "stop device → update → restore running state" automatically, no restart needed.

### 🛠️ Developing Plugins

1. Create a .NET class library and add the NuGet package `Snet.Core` (provides the plugin engine and the `IDaq` / `IMq` interfaces, located in the `Snet.Model.@interface` namespace)
2. Implement the interface methods (`OnAsync`, `OffAsync`, `ReadAsync`, `WriteAsync`, `GetStatusAsync`, etc.)
3. Pack the output directory into a **ZIP** file
4. Upload the ZIP on the app's "Plugin Settings" page

> 🤖 **AI-assisted development**: try [Snet.SKILLS](https://github.com/shunnet/SKILLS) — an AI skills collection for the SNET architecture.

## 📦 Installation & Usage

### 📋 Requirements

| Component | Requirement |
|-----------|-------------|
| 🖥️ **OS** | Windows 10 / 11 (x64) |
| 🔧 **.NET Runtime** | .NET 10.0 Desktop Runtime |
| 🛠️ **Build Tools** | Visual Studio 2022+ (to compile) |
| 💾 **Disk Space** | ≥ 200 MB |

### 📥 1️⃣ Clone

```bash
git clone https://github.com/shunnet/Daq.git
cd Daq
```

### 🔨 2️⃣ Build

Open `Snet.Iot.Daq.sln` with **Visual Studio 2022** or later, then build Debug or Release.

### ▶️ 3️⃣ Run

Launch `Snet.Iot.Daq.exe` from the output directory.

> 💡 **No build required?** Download the pre-built ZIP from [GitHub Releases](https://github.com/shunnet/Daq/releases) and run it directly.

## 🖥️ Screenshots

<p align="center">
  <img src="images/home.png" width="900"/>
  <img src="images/pb.png" width="900"/>
  <img src="images/ps.png" width="900"/>
  <img src="images/as.png" width="900"/>
  <img src="images/prs.png" width="900"/>
  <img src="images/cs.png" width="900"/>
  <img src="images/ccs.png" width="900"/>
</p>

## 📚 Resources & Community

| Channel | Link |
|---------|------|
| 🎬 **Demo Video** | [Watch](https://Snet.cn/YJybu) |
| 🌐 **Website** | [snet.cn](https://snet.cn) |
| 📦 **NuGet Plugin Market** | Browse in-app on the "Plugin Settings" page |
| 🐛 **Issues** | [GitHub Issues](https://github.com/shunnet/Daq/issues) — bug reports & feature requests |
| 💬 **QQ Group** | [Join](https://qm.qq.com/q/gPjrD9wGty) — technical community |
| ⭐ **Star** | If this project helps you, please give it a Star ❤️ |

## 🙏 Acknowledgements

- [Snet.cn](https://snet.cn) — Industrial communication library
- [Snet.Windows.Controls](https://github.com/shunnet/WpfMUI) — WPF controls
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) — Hardware monitoring
- [ScottPlot](https://scottplot.net) — Scientific charting library
- [sqlite-net](https://github.com/praeclarum/sqlite-net) — Lightweight database

## 📜 License

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)

This project is licensed under the **MIT** License — free to use, modify and distribute.

📄 See the [LICENSE](LICENSE) file for the full terms.

> ⚠️ The software is provided "as is", without warranty of any kind.

## 📈 Star History

<a href="https://www.star-history.com/?repos=shunnet%2FDaq&type=date&legend=bottom-right">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=shunnet/Daq&type=date&theme=dark&legend=bottom-right&sealed_token=3Tft7-uvG5B6fob8INAvqe9armRuIkXlOveR3cnY2kXGwaJMMtYWOyq45srnSCO-Dq6_0dPyepq-b8O_4fMB87CqYCIZdawTTa_JHyS2oahHiDr0o_2NTA"/>
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=shunnet/Daq&type=date&legend=bottom-right&sealed_token=3Tft7-uvG5B6fob8INAvqe9armRuIkXlOveR3cnY2kXGwaJMMtYWOyq45srnSCO-Dq6_0dPyepq-b8O_4fMB87CqYCIZdawTTa_JHyS2oahHiDr0o_2NTA"/>
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=shunnet/Daq&type=date&legend=bottom-right&sealed_token=3Tft7-uvG5B6fob8INAvqe9armRuIkXlOveR3cnY2kXGwaJMMtYWOyq45srnSCO-Dq6_0dPyepq-b8O_4fMB87CqYCIZdawTTa_JHyS2oahHiDr0o_2NTA"/>
 </picture>
</a>
