# <img src="https://api.shunnet.top/pic/nuget.png" height="28"> KMSim  

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)  
[![Repo](https://img.shields.io/badge/Repository-shunnet/KMSim-blue)](https://github.com/shunnet/KMSim)  

> 🚀 **Windows 平台可编程键鼠模拟器**  
> 高效、灵活的自动化控制解决方案，适用于测试、批量操作及脚本化控制。


## 📌 项目简介

KMSim 是一款 **Windows 下键鼠模拟器**，支持多种自动化操作场景。  
无论是自动化测试、重复性操作，还是复杂脚本化控制，KMSim 都能帮你轻松实现。


## ⚙️ 核心功能

- ⌨️ **键盘模拟**：  
  支持单键、组合键、延迟输入等操作。

- 🖱️ **鼠标控制**：  
  支持鼠标移动、点击、双击、滚轮滚动等操作。

- 📜 **脚本化控制**：  
  通过自定义脚本，实现复杂自动化流程。

- 🎨 **高度可定制**：  
  丰富配置选项，可根据不同场景自由调整。


## 🚀 安装与使用

### 1️⃣ 克隆仓库

```bash
git clone https://github.com/shunnet/KMSim.git
cd KMSim
```

### 2️⃣ 编译项目

使用 **Visual Studio** 打开 `Snet.Windows.KMSim.slnx`，选择合适的构建配置（Debug 或 Release），然后构建项目。

### 3️⃣ 运行程序

构建完成后，在输出目录中找到 `Snet.Windows.KMSim.exe`，双击运行即可启动。

### 4️⃣ 测试脚本

```bash
CopyContentAsync = 记事本
LWinAsync
DelayAsync = 1000
PasteAsync
DelayAsync = 1000
EnterAsync
DelayAsync = 1000
CopyContentAsync = 欢迎使用 Shunnet.top 键鼠模拟器 
PasteAsync

While = true
PasteAsync
OemCommaAsync
DelayAsync = 1000
EnterAsync
```


## 🙏 致谢  

- 🌐 [Shunnet.top](https://shunnet.top)  
- 🔥 [WpfMUI](https://github.com/shunnet/WpfMUI)  


## 📜 许可证  

![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)  

本项目基于 **MIT** 开源。  
请阅读 [LICENSE](LICENSE) 获取完整条款。  
⚠️ 软件按 “原样” 提供，作者不对使用后果承担责任。  


## 🌍 查阅  

👉 [点击跳转](https://Shunnet.top/72VRn)  