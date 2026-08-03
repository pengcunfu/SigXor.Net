# SigXor

一个支持右 Alt 快捷键激活语音输入的跨平台桌面应用程序，基于 **Avalonia UI**，使用阿里 **SenseVoice** 模型进行多语言语音识别。

## 功能特点

- **全局快捷键**：右 Alt 短按切换/长按录音（Windows 完整支持）
- **实时语音录制**：使用系统麦克风进行实时语音录制
- **AI 语音识别**：SenseVoice（中英日韩粤）本地离线识别
- **自动输入**：将识别的文字自动输入到当前焦点位置
- **系统托盘**：最小化到托盘、开机自启动
- **可配置**：识别引擎、语言、输入方式等
- **离线运行**：模型下载后可离线使用

## 系统要求

| 平台 | 支持情况 |
|------|----------|
| Windows 10/11 | 完整功能（快捷键、录音、输入） |
| Linux | UI、模型管理、剪贴板输入（需 xdotool） |
| macOS | UI、模型管理、剪贴板输入（需辅助功能权限） |

- .NET 10.0 Runtime（或 .NET SDK 用于开发）
- 麦克风设备（Windows 录音）
- 首次运行需要网络连接（下载模型）

## 快速开始

### 构建与运行

```bash
# 还原依赖并构建
dotnet build -c Release

# 运行
dotnet run -c Release
```

### 单文件发布

```bash
# Windows
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Linux
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

# macOS (Apple Silicon)
dotnet publish -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true
```

## 使用方法

1. **启动程序**并点击「开始服务」
2. **首次使用**等待 SenseVoice 模型自动下载（约 230MB）
3. **语音输入**（Windows）：
   - 短按右 Alt：开始/结束长录音
   - 按住右 Alt 后松手：短句录音
4. 在「模型管理」中下载/删除识别模型

## 配置选项

配置自动保存到 `%APPDATA%\SigXor\config.json`（Windows）或对应平台的用户目录。

- 识别引擎 / 语言
- 键盘模拟 / 剪贴板粘贴
- 开机自启动 / 静默启动
- 关闭时最小化到托盘

## 技术架构

### 核心模块

- **KeyboardHook**：Windows 全局键盘钩子（其他平台为占位实现）
- **AudioCapture**：NAudio 音频捕获（Windows）
- **SpeechRecognition**：SenseVoice（sherpa-onnx）引擎
- **TextSimulator**：跨平台文本输入（Windows SendInput / Linux xdotool / macOS osascript）
- **TrayIconManager**：Avalonia 原生系统托盘

### 技术栈

- **UI 框架**：.NET 10.0 + Avalonia 11.3
- **音频**：NAudio 2.3.0
- **语音识别**：SenseVoice（org.k2fsa.sherpa.onnx 1.13.2）

## 项目结构

```
├── App.axaml / Program.cs     # Avalonia 应用入口
├── MainWindow.axaml           # 主窗口
├── ModelManagementWindow.axaml
├── VoiceInputOverlay.axaml    # 录音浮层
├── Services/                  # 平台抽象
├── KeyboardHook.cs            # Windows 快捷键
├── TextSimulator.cs           # 跨平台文本输入
└── StartupHelper.cs           # 跨平台开机自启
```

## 平台说明

### Linux 额外依赖

```bash
sudo apt install xdotool   # 用于模拟键盘输入
```

### macOS 权限

在「系统设置 → 隐私与安全性 → 辅助功能」中允许本程序，以便模拟键盘输入。

## 故障排除

```bash
dotnet clean
dotnet restore
dotnet build -c Release
```

- **模型路径**：`models/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17/`（SenseVoice）

## 开发环境

- .NET 10.0 SDK
- Visual Studio 2022+ / Rider / VS Code
- 支持 Windows、Linux、macOS 开发

## 许可证

MIT License
