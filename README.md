# SigXor

一个支持右 Alt 快捷键激活语音输入的跨平台桌面应用程序，基于 **Avalonia UI**，使用阿里 **SenseVoice** 模型进行多语言语音识别。

## 功能特点

- **全局快捷键**：右 Alt 短按切换/长按录音（Windows 完整支持）
- **区域截屏**：Fn + \` 拖动选择任意区域，弹出工具条（OCR 识别 / 复制 / 保存）
- **OCR 文字识别**：PaddleOCR PP-OCRv5 本地离线识别（RapidOcrNet），中英文混排
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

**区域截屏**（Windows）：按 Fn + \` 进入选区模式，拖动鼠标框选任意区域，松手后自动弹出工具条：

- 截图会固定在选区位置显示，方便查看和核对
- **OCR 识别**：本地识别选区文字（中英文），结果复制到剪贴板并托盘提示
- **复制图片**：把选区图片复制到剪贴板
- **保存**：保存 PNG 到 `%APPDATA%\SigXor\Screenshots`
- **完成**：关闭工具条

首次使用 OCR 时自动下载中文识别模型（约 16MB，存放在 `models/ocr/v5`），之后完全离线。可在「设置」中关闭该快捷键。

> 说明：Windows 键盘驱动不会把笔记本的 Fn 键暴露给系统，因此程序监听的是 \` 键本身（无 Shift/Ctrl/Alt/Win 组合时触发）。按下 Fn + \` 即可截屏；单独按 \` 也会触发，并会拦截该按键避免误输入。

## 配置选项

配置自动保存到 `%APPDATA%\SigXor\config.json`（Windows）或对应平台的用户目录。

- 识别引擎 / 语言
- 键盘模拟 / 剪贴板粘贴
- 启用 / 关闭 Fn + \` 区域截屏快捷键
- 开机自启动 / 静默启动
- 关闭时最小化到托盘

## 技术架构

### 核心模块

- **KeyboardHook**：Windows 全局键盘钩子（其他平台为占位实现）
- **ScreenshotHelper**：Win32 整屏/区域截屏（GDI + Avalonia 位图，含剪贴板写入）
- **RegionCaptureOverlay**：区域选区覆盖层（多显示器虚拟桌面）
- **RegionPreviewWindow**：选区截图预览窗口（固定在选区位置）
- **ScreenshotToolbar**：截屏工具条（OCR / 复制 / 保存 / 完成）
- **OcrEngine / OcrModelManager**：RapidOcrNet 本地 OCR 与模型下载
- **AudioCapture**：NAudio 音频捕获（Windows）
- **SpeechRecognition**：SenseVoice（sherpa-onnx）引擎
- **TextSimulator**：跨平台文本输入（Windows SendInput / Linux xdotool / macOS osascript）
- **TrayIconManager**：Avalonia 原生系统托盘

### 技术栈

- **UI 框架**：.NET 10.0 + Avalonia 11.3
- **音频**：NAudio 2.3.0
- **语音识别**：SenseVoice（org.k2fsa.sherpa.onnx 1.13.2）
- **OCR 识别**：RapidOcrNet 3.0.0（PaddleOCR PP-OCRv5 ONNX，Apache-2.0）

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

- **模型路径**：`models/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-int8-2024-07-17/`（SenseVoice）；`models/ocr/v5/`（OCR）

## 开发环境

- .NET 10.0 SDK
- Visual Studio 2022+ / Rider / VS Code
- 支持 Windows、Linux、macOS 开发

## 许可证

MIT License
