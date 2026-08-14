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
  <img src="https://img.shields.io/badge/Web-Linux%20x64%20%2F%20ARM64-blue?logo=linux"/>
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
  English | 📖 <a href="README.md"><b>简体中�?/b></a>
</p>

## �?Introduction

**Snet.Iot.Daq** is a plugin-based data acquisition tool built on the **Snet.cn industrial communication library**, designed for industrial device data collection.

```
┌────────────────────────────────────────�?�? Snet.Iot.Daq (WPF desktop / Windows)   �? �?UI: MVVM + modern interface
├────────────────────────────────────────�?�? Snet.Iot.Daq.Web (Web app / Linux)     �? �?UI: Blazor Server, browser access
├────────────────────────────────────────�?�? Snet.Iot.Daq.Core (library)            �? �?Core: business logic / models / services
├────────────────────────────────────────�?�? Snet.cn industrial comm. lib.          �? �?Foundation: plugin framework / protocols / tools
└────────────────────────────────────────�?```

> 💡 The Core layer has no WPF dependency; the desktop edition (Windows) and the cross-platform Web edition (Linux / ARM64) share the same core business logic.

## 🚀 Feature Overview

| Module | Description |
|--------|-------------|
| 🔌 **Plugin Hot-Plug** | Load / unload DAQ / MQ plugins at runtime without restart; hot-update support |
| 📡 **OPC UA Server** | Built-in OPC UA Server with auth, certificates, address space management, persistent subscriptions |
| 📨 **MQTT Broker** | Built-in MQTT broker with client management, auth, max-connection control |
| 🌐 **WebAPI Service** | Built-in HTTP service (WAOn / WAOff), external system data ingestion |
| 📊 **Real-time Charts** | ScottPlot multi-line charts with skin switching and history |
| 🖥�?**System Monitoring** | CPU / GPU / RAM dashboards (LibreHardwareMonitor + WMI dual-channel) |
| 🎯 **Byte-Level Parser** | Visual byte / bit / encoding / data-format configurator for custom protocol parsing |
| 📦 **NuGet Plugin Market** | Browse, download and one-click install Snet ecosystem plugins |
| 🔢 **Auto Batching** | Smart merge of scattered addresses into batched reads, lower communication overhead |
| �?**Soft Start** | Starts acquisition automatically on launch |
| 🔔 **System Tray** | Minimize to tray, start/stop devices from tray context menu, single-instance guard |
| 🌐 **i18n / 🌓 Themes** | Chinese / English switching · dark / light themes, charts follow the skin |
| 🌐 **Cross-platform Web** | Blazor Server browser access, shared Core with the desktop edition, runs on Linux x64 / ARM64 |

### 🎯 Use Cases

🏭 Industrial automation data collection · 🔧 PLC / device monitoring · 🌐 IoT edge gateway · 📡 OPC UA / MQTT relay · 🔬 Custom protocol device integration

## 🔌 Plugin System

The plugin engine (collectible `AssemblyLoadContext` + stream loading) is provided by the **Snet.Core** package; this app handles plugin management and orchestration.

### 🔄 Load / Unload Flow

```
Upload ZIP plugin �?auto-extract to plugin dir �?create collectible AssemblyLoadContext
�?stream-load assemblies �?scan & instantiate IDaq / IMq �?register into IOC �?start acquisition
```

```
Stop acquisition �?dispose plugin (IAsyncDisposable) �?remove IOC registration
�?unload AssemblyLoadContext �?GC collect �?delete plugin files
```

> 🔁 **Hot Update**: uploading a plugin package with the same name runs "stop device �?update �?restore running state" automatically, no restart needed.

### 🛠�?Developing Plugins

1. Create a .NET class library and add the NuGet package `Snet.Core` (provides the plugin engine and the `IDaq` / `IMq` interfaces, located in the `Snet.Model.@interface` namespace)
2. Implement the interface methods (`OnAsync`, `OffAsync`, `ReadAsync`, `WriteAsync`, `GetStatusAsync`, etc.)
3. Pack the output directory into a **ZIP** file
4. Upload the ZIP on the app's "Plugin Settings" page

> 🤖 **AI-assisted development**: try [Snet.SKILLS](https://github.com/shunnet/SKILLS) �?an AI skills collection for the SNET architecture.

## 🌐 Cross-platform Web Edition (Snet.Iot.Daq.Web)

The **Blazor Server Web edition** of Snet.Iot.Daq, sharing all business capabilities of `Snet.Iot.Daq.Core` with the desktop app �?just open it in a browser, no client installation required.

- **Platforms**: Windows / Linux x64 / Linux ARM64 (Raspberry Pi, Phytium, Kunpeng, etc.)
- **Deployment**: Docker multi-arch images / Ubuntu systemd / bare-metal
- **Feature parity**: plugin hot-plug, OPC UA server, MQTT broker, WebAPI, auto batching, soft start, multi-user with role permissions
- **Differences**: no ScottPlot charts, system tray or GPU monitoring (desktop-only features)

### 🐳 Docker deployment (recommended, multi-arch)

```bash
git clone https://github.com/shunnet/Daq.git && cd Daq
docker compose -f Snet.Iot.Daq.Web/docker-compose.yml up -d --build
# Open http://<server-ip>:5051 in a browser
```

Multi-arch images (linux/amd64 + linux/arm64) are built automatically by GitHub Actions and pushed to `ghcr.io`.

### 🐧 Ubuntu bare-metal deployment

```bash
sudo bash Snet.Iot.Daq.Web/deploy/ubuntu-deploy.sh          # installs runtime �?publish �?systemd service
sudo bash Snet.Iot.Daq.Web/deploy/ubuntu-deploy.sh --port 8080 --data /srv/snet-daq
```

### 📦 GitHub Actions multi-platform packaging

Pushing a `v*` tag automatically produces **linux-x64 / linux-arm64 / win-x64** release packages (with the systemd deploy script) plus **ghcr.io dual-arch Docker images**, and publishes them to [GitHub Releases](https://github.com/shunnet/Daq/releases).

📄 Full deployment details (data persistence / ports / hardening) see [Snet.Iot.Daq.Web/README.DEPLOY.md](Snet.Iot.Daq.Web/README.DEPLOY.md).

## 📦 Installation & Usage

### 📋 Requirements

| Component | Requirement |
|-----------|-------------|
| 🖥�?**OS** | Windows 10 / 11 (x64) |
| 🔧 **.NET Runtime** | .NET 10.0 Desktop Runtime |
| 🛠�?**Build Tools** | Visual Studio 2022+ (to compile) |
| 💾 **Disk Space** | �?200 MB |

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

## 👥 Accounts & Permissions (Web Edition only)

> 💡 The desktop (WPF) edition is single-user with no login or permission system; the permission model below applies to the **Web edition (Snet.Iot.Daq.Web)** only.

The system uses a two-tier permission model: Administrator / Regular User.

| Role | Permissions |
|------|-------------|
| **Administrator** | Full access: plugin browsing & settings, address management, project configuration, start/stop collection, WebApi / auto-pack settings, server management, user management |
| **Regular User** | **Read-only**: can only view the Console (device status, system monitoring, run logs) �?no operation buttons at all |

**Default administrator account**: `snet` / `123456`

**Change password on first login** (enforced):

1. Sign in with `snet` / `123456`, the system redirects to the password change page
2. Enter the current password `123456`, a new password (at least 6 characters) and confirm it
3. Save, then sign in again with the new password

> ⚠️ Change the default password right after deployment; regular users are created by administrators on the "User Management" page.

## 🖥�?Screenshots

### 🖥�?Desktop edition (WPF / Windows)

<p align="center">
  <img src="images/home.png" width="900"/>
  <img src="images/pb.png" width="900"/>
  <img src="images/ps.png" width="900"/>
  <img src="images/as.png" width="900"/>
  <img src="images/prs.png" width="900"/>
  <img src="images/cs.png" width="900"/>
  <img src="images/ccs.png" width="900"/>
</p>

### 🌐 Web edition (browser)

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

## 📚 Resources & Community

| Channel | Link |
|---------|------|
| 🎬 **Demo Video** | [Watch](https://Snet.cn/YJybu) |
| 🌐 **Website** | [snet.cn](https://snet.cn) |
| 📦 **NuGet Plugin Market** | Browse in-app on the "Plugin Settings" page |
| 🐛 **Issues** | [GitHub Issues](https://github.com/shunnet/Daq/issues) �?bug reports & feature requests |
| 💬 **QQ Group** | [Join](https://qm.qq.com/q/gPjrD9wGty) �?technical community |
| �?**Star** | If this project helps you, please give it a Star ❤️ |

## 🙏 Acknowledgements

- [Snet.cn](https://snet.cn) �?Industrial communication library
- [Snet.Windows.Controls](https://github.com/shunnet/WpfMUI) �?WPF controls
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) �?Hardware monitoring
- [ScottPlot](https://scottplot.net) �?Scientific charting library
- [sqlite-net](https://github.com/praeclarum/sqlite-net) �?Lightweight database

## 📜 License

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)

This project is licensed under the **MIT** License �?free to use, modify and distribute.

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
