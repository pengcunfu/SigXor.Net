using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace SigXor;

public partial class MainWindow : Window
{
    private enum RecordingTrigger { None, KeyboardHold, KeyboardToggle }

    private IKeyboardHookService? _keyboardHook;
    private AudioCapture? _audioCapture;
    private SpeechRecognizer? _speechRecognizer;
    private TextSimulator? _textSimulator;
    private VoiceInputOverlay? _voiceOverlay;
    private TrayIconManager? _trayIcon;
    private bool _serviceRunning;
    private bool _isStartingService;
    private bool _isExiting;
    private bool _isLoadingSettings;
    private bool _isRecording;
    private bool _isShortcutDown;
    private bool _altHoldTriggeredThisPress;
    private bool _keyboardToggleActive;
    private bool _isCapturing;
    private bool _ocrBusy;
    private bool _wasMainWindowVisible;
    private RecordingTrigger _activeTrigger = RecordingTrigger.None;
    private readonly DispatcherTimer _statusTimer;
    private readonly Config _config;
    private CancellationTokenSource? _downloadCts;
    private WriteableBitmap? _capturedFullScreen;
    private WriteableBitmap? _capturedRegion;
    private PixelRect _virtualBounds;
    private RegionCaptureOverlay? _captureOverlay;
    private RegionPreviewWindow? _previewWindow;
    private ScreenshotToolbar? _screenshotToolbar;
    private OcrEngine? _ocrEngine;

    public MainWindow()
    {
        _config = Config.Instance;
        SpeechModelManager.EnsureDefaultVisibility();
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version != null
            ? $"版本 {version.Major}.{version.Minor}.{version.Build}"
            : "版本 1.0.0";
        SpeechModelManager.ModelsChanged += OnModelsChanged;
        LoadUserSettings();
        InitializeServices();

        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(0.1)
        };
        _statusTimer.Tick += UpdateStatus;
        _statusTimer.Start();

        Opened += OnWindowOpened;
        Closing += OnClosing;
    }

    public void PrepareSilentStartup()
    {
        Hide();
        ShowInTaskbar = false;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        Opened -= OnWindowOpened;
        DisableMinMaxButtonsForWindow();
        _ = StartService();
    }

    private void DisableMinMaxButtonsForWindow()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var handle = TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
            return;

        const int gwlStyle = -16;
        const nint wsMinimizeBox = 0x00020000;
        const nint wsMaximizeBox = 0x00010000;

        var style = GetWindowLongPtr(handle, gwlStyle);
        style &= ~wsMinimizeBox;
        style &= ~wsMaximizeBox;
        SetWindowLongPtr(handle, gwlStyle, style);

        const uint swpNomove = 0x0002;
        const uint swpNosize = 0x0001;
        const uint swpNozorder = 0x0004;
        const uint swpFrameChanged = 0x0020;
        SetWindowPos(handle, nint.Zero, 0, 0, 0, 0,
            swpNomove | swpNosize | swpNozorder | swpFrameChanged);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private void ShowMainWindow()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        _isExiting = true;
        StopService();
        _trayIcon?.Dispose();
        _trayIcon = null;

        if (Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private void UpdateServiceButtonState()
    {
        StartServiceButton.IsEnabled = !_serviceRunning && !_isStartingService;
        StopServiceButton.IsEnabled = _serviceRunning;
        _trayIcon?.SetServiceRunning(_serviceRunning);
    }

    private async void StartServiceButton_Click(object? sender, RoutedEventArgs e) => await StartService();

    private void StopServiceButton_Click(object? sender, RoutedEventArgs e) => StopService();

    private void OnModelsChanged() =>
        Dispatcher.UIThread.Post(() =>
        {
            RefreshEngineComboBox();
            UpdateModelActionButtons();
        });

    private void LoadUserSettings()
    {
        if (_config != null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _isLoadingSettings = true;
                try
                {
                    RefreshEngineComboBox();
                    SelectComboBoxByTag(LanguageComboBox, _config.RecognitionLanguage);

                    ShowNotificationsCheckBox.IsChecked = _config.ShowNotifications;
                    UseClipboardCheckBox.IsChecked = _config.UseClipboard;
                    ScreenshotShortcutCheckBox.IsChecked = _config.EnableScreenshotShortcut;
                    SilentStartCheckBox.IsChecked = _config.SilentStart;
                    MinimizeToTrayCheckBox.IsChecked = _config.MinimizeToTray;

                    var autoStart = _config.AutoStartWithWindows;
                    if (autoStart != StartupHelper.IsEnabled())
                    {
                        try { StartupHelper.SetEnabled(autoStart, _config.SilentStart); }
                        catch { AutoStartCheckBox.IsChecked = StartupHelper.IsEnabled(); }
                    }

                    AutoStartCheckBox.IsChecked = StartupHelper.IsEnabled();
                    UpdateServiceButtonState();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"加载设置失败: {ex.Message}");
                }
                finally
                {
                    _isLoadingSettings = false;
                }
            });
        }
    }

    private void InitializeServices()
    {
        try
        {
            _keyboardHook = PlatformServices.CreateKeyboardHook();
            _keyboardHook.HoldThresholdMs = (int)(_config.AltHoldThreshold * 1000);
            _keyboardHook.ScreenshotEnabled = _config.EnableScreenshotShortcut;
            _keyboardHook.ShortcutPressed += OnShortcutPressed;
            _keyboardHook.ShortcutReleased += OnShortcutReleased;
            _keyboardHook.ShortcutHoldDetected += OnShortcutHoldDetected;
            _keyboardHook.ScreenshotShortcutPressed += OnScreenshotShortcutPressed;
            _keyboardHook.EscapePressed += OnEscapePressed;

            _audioCapture = new AudioCapture();
            _audioCapture.StatusChanged += OnAudioStatusChanged;

            _speechRecognizer = new SpeechRecognizer();
            _speechRecognizer.StatusChanged += OnRecognitionStatusChanged;
            _speechRecognizer.Error += OnSpeechError;

            _textSimulator = new TextSimulator(_config.TypingDelay);
            _textSimulator.SetOwnerWindow(this);
            _voiceOverlay = new VoiceInputOverlay();

            _trayIcon = new TrayIconManager();
            _trayIcon.ShowWindowRequested += (_, _) => ShowMainWindow();
            _trayIcon.ExitRequested += (_, _) => ExitApplication();

            // 全局钩子随程序启动常驻：截屏快捷键不依赖语音服务是否运行
            _keyboardHook.Start();

            RecognitionStatusText.Text = _keyboardHook.IsSupported
                ? "已初始化"
                : "已初始化（快捷键仅 Windows 可用）";
        }
        catch (Exception ex)
        {
            _ = DialogHelper.ShowErrorAsync(this, $"初始化服务失败: {ex.Message}");
        }
    }

    private async Task StartService()
    {
        if (_serviceRunning || _isStartingService)
            return;

        _isStartingService = true;
        UpdateServiceButtonState();
        RecognitionStatusText.Text = "正在启动服务...";

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                await DialogHelper.ShowWarningAsync(this,
                    "当前平台下全局快捷键与音频录制功能受限。\nWindows 上可获得完整体验。",
                    "平台提示");
            }

            var engineTag = _config.RecognitionEngine;
            var model = SpeechModelManager.GetModel(engineTag);
            var engineName = model?.DisplayName ?? engineTag;

            if (!SpeechModelManager.IsInstalled(engineTag))
            {
                await DialogHelper.ShowWarningAsync(this,
                    $"当前选择的「{engineName}」尚未下载。\n\n请点击识别状态旁的「下载」按钮下载模型后再启动服务。",
                    "模型未就绪");
                RecognitionStatusText.Text = "模型未下载";
                UpdateModelActionButtons();
                return;
            }

            RecognitionStatusText.Text = $"正在初始化 {engineName}...";
            if (_speechRecognizer != null && !_speechRecognizer.IsInitialized)
            {
                var initSuccess = await _speechRecognizer.InitializeAsync();
                if (!initSuccess)
                {
                    await DialogHelper.ShowErrorAsync(this,
                        $"{engineName} 初始化失败。\n\n" +
                        "请尝试：\n" +
                        "1. 删除 models 目录中的模型文件后重新下载\n" +
                        "2. 切换到其他已下载的识别引擎");
                    RecognitionStatusText.Text = "初始化失败";
                    return;
                }
            }

            if (_keyboardHook?.IsSupported == true)
            {
                _keyboardHook.Start();
                await Task.Delay(100);
            }

            _serviceRunning = true;
            ShowNotification("服务已启动", "使用右 Alt 键进行语音输入");
            RecognitionStatusText.Text = $"{_speechRecognizer?.EngineName ?? engineName} 就绪";
        }
        catch (Exception ex)
        {
            RecognitionStatusText.Text = "启动失败";
            await DialogHelper.ShowErrorAsync(this, $"启动服务失败: {ex.Message}");
        }
        finally
        {
            _isStartingService = false;
            UpdateServiceButtonState();
        }
    }

    private void StopService()
    {
        try
        {
            _audioCapture?.StopRecording();

            _isRecording = false;
            _isShortcutDown = false;
            _altHoldTriggeredThisPress = false;
            _keyboardToggleActive = false;
            _activeTrigger = RecordingTrigger.None;
            _voiceOverlay?.HideOverlay();

            _serviceRunning = false;
            UpdateServiceButtonState();

            ShowNotification("服务已停止", "语音输入功能已关闭");
        }
        catch (Exception ex)
        {
            _ = DialogHelper.ShowErrorAsync(this, $"停止服务失败: {ex.Message}");
        }
    }

    private void RunOnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private void OnShortcutPressed(object? sender, EventArgs e)
    {
        if (!_serviceRunning)
            return;

        _textSimulator?.CaptureTargetWindow();
        RunOnUiThread(() =>
        {
            _isShortcutDown = true;
            _altHoldTriggeredThisPress = false;
            ShortcutStatusText.Text = _keyboardToggleActive ? "录音中" : "按下";
        });
    }

    private void OnShortcutHoldDetected(object? sender, EventArgs e)
    {
        if (!_serviceRunning)
            return;

        RunOnUiThread(() =>
        {
            if (!_isShortcutDown || _isRecording)
                return;

            _altHoldTriggeredThisPress = true;
            ShortcutStatusText.Text = "长按录音";
            StartRecording(RecordingTrigger.KeyboardHold);
        });
    }

    private void OnShortcutReleased(object? sender, EventArgs e)
    {
        if (!_serviceRunning)
            return;

        RunOnUiThread(() =>
        {
            _isShortcutDown = false;

            if (_altHoldTriggeredThisPress)
            {
                ShortcutStatusText.Text = "释放";
                if (_isRecording && _activeTrigger == RecordingTrigger.KeyboardHold)
                    StopRecording();
                return;
            }

            if (_keyboardToggleActive)
            {
                ShortcutStatusText.Text = "结束";
                StopRecording();
            }
            else if (!_isRecording)
            {
                ShortcutStatusText.Text = "录音中(再按结束)";
                StartRecording(RecordingTrigger.KeyboardToggle);
                _keyboardToggleActive = true;
            }
        });
    }

    private void OnScreenshotShortcutPressed(object? sender, EventArgs e)
    {
        try
        {
            Dispatcher.UIThread.Post(StartRegionCapture);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"截屏调度失败: {ex.Message}");
        }
    }

    /// <summary>进入区域截屏流程：隐藏主窗口 → 抓取整屏 → 显示选区覆盖层。</summary>
    private async void StartRegionCapture()
    {
        if (_isCapturing || _isExiting)
            return;

        if (!ScreenshotHelper.IsSupported)
        {
            ShowNotification("截屏失败", "当前平台不支持截屏");
            return;
        }

        _isCapturing = true;
        if (_keyboardHook != null)
            _keyboardHook.EscapeCaptureEnabled = true;
        try
        {
            _wasMainWindowVisible = IsVisible;
            if (IsVisible)
            {
                Hide();
                ShowInTaskbar = false;
            }

            // 等待主窗口真正从屏幕上消失，避免把自家窗口截进图里
            if (_wasMainWindowVisible)
                await Task.Delay(150);

            var full = ScreenshotHelper.CaptureFullScreen();
            if (full == null)
            {
                ShowNotification("截屏失败", "无法获取屏幕图像");
                FinishRegionCapture(restoreWindow: true);
                return;
            }

            if (_isCapturing)
            {
                _capturedFullScreen = full;
                _virtualBounds = GetVirtualScreenBounds();

                var overlay = new RegionCaptureOverlay(full, _virtualBounds, RenderScaling);
                overlay.SelectionConfirmed += OnRegionConfirmed;
                overlay.SelectionCancelled += OnRegionCancelled;
                _captureOverlay = overlay;
                overlay.Show();
            }
            else
            {
                full.Dispose();
            }
        }
        catch (Exception ex)
        {
            _captureOverlay?.Close();
            _captureOverlay = null;
            _capturedFullScreen?.Dispose();
            _capturedFullScreen = null;
            _isCapturing = false;
            if (_keyboardHook != null)
                _keyboardHook.EscapeCaptureEnabled = false;
            RestoreMainWindowAfterCapture();
            ShowNotification("截屏失败", ex.Message);
        }
    }

    /// <summary>截屏流程期间按下 ESC：退出截屏（关闭预览/工具条，不复制）。</summary>
    private void OnEscapePressed(object? sender, EventArgs e)
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_isCapturing)
                    FinishRegionCapture(restoreWindow: true);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ESC 退出截屏调度失败: {ex.Message}");
        }
    }

    private void OnRegionConfirmed(object? sender, PixelRect rect)
    {
        try
        {
            if (_capturedFullScreen == null)
            {
                FinishRegionCapture(restoreWindow: true);
                return;
            }

            var region = ScreenshotHelper.CropBitmap(_capturedFullScreen, rect);
            _capturedFullScreen.Dispose();
            _capturedFullScreen = null;

            if (region == null)
            {
                ShowNotification("截屏失败", "无法裁剪选区");
                FinishRegionCapture(restoreWindow: true);
                return;
            }

            _capturedRegion?.Dispose();
            _capturedRegion = region;

            _captureOverlay?.Close();
            _captureOverlay = null;

            var screenRect = new PixelRect(
                _virtualBounds.X + rect.X,
                _virtualBounds.Y + rect.Y,
                rect.Width,
                rect.Height);

            _previewWindow?.Close();
            _previewWindow = new RegionPreviewWindow();
            _previewWindow.DragPositionChanged += OnPreviewPositionChanged;
            _previewWindow.ShowAt(region, screenRect, RenderScaling);

            _screenshotToolbar?.Close();
            _screenshotToolbar = new ScreenshotToolbar();
            _screenshotToolbar.OcrRequested += OnToolbarOcrRequested;
            _screenshotToolbar.CopyRequested += OnToolbarCopyRequested;
            _screenshotToolbar.SaveRequested += OnToolbarSaveRequested;
            _screenshotToolbar.CloseRequested += OnToolbarCloseRequested;
            _screenshotToolbar.ShowAt(screenRect, Screens.All.ToArray());
            ForceWindowTopmost(_screenshotToolbar);
        }
        catch (Exception ex)
        {
            ShowNotification("截屏失败", ex.Message);
            FinishRegionCapture(restoreWindow: true);
        }
    }

    private void OnRegionCancelled(object? sender, EventArgs e)
    {
        _captureOverlay?.Close();
        _captureOverlay = null;
        FinishRegionCapture(restoreWindow: true);
    }

    private async void OnToolbarOcrRequested(object? sender, EventArgs e)
    {
        var bitmap = _capturedRegion;
        if (bitmap == null || _ocrBusy)
            return;

        string? toastMessage = null;
        _ocrBusy = true;
        _screenshotToolbar?.SetBusy("准备中...");
        try
        {
            _ocrEngine ??= new OcrEngine();
            var text = await _ocrEngine.RecognizeAsync(bitmap,
                msg => Dispatcher.UIThread.Post(() => _screenshotToolbar?.SetBusy(msg)));

            if (string.IsNullOrWhiteSpace(text))
            {
                toastMessage = "未识别到文字";
                ShowNotification("OCR", "未识别到文字");
            }
            else
            {
                if (OperatingSystem.IsWindows())
                    WindowsClipboardHelper.SetText(text);
                else
                    await CopyTextToClipboardAsync(text);

                Dispatcher.UIThread.Post(() => LastRecognizedText.Text = text);
                toastMessage = "识别完成，结果已复制到剪贴板";
                ShowNotification("OCR 识别完成", TruncateText(text, 60));
            }
        }
        catch (Exception ex)
        {
            toastMessage = "OCR 识别失败";
            ShowNotification("OCR 识别失败", ex.Message);
        }
        finally
        {
            _ocrBusy = false;
            _screenshotToolbar?.SetIdle();
        }

        if (toastMessage != null)
            _screenshotToolbar?.ShowToast(toastMessage);
    }

    private void OnToolbarCopyRequested(object? sender, EventArgs e)
    {
        var bitmap = _capturedRegion;
        if (bitmap == null)
            return;

        if (ScreenshotHelper.CopyBitmapToClipboard(bitmap))
        {
            _screenshotToolbar?.ShowToast("已复制到剪贴板");
            ShowNotification("已复制", "选区图片已复制到剪贴板");
        }
        else
        {
            _screenshotToolbar?.ShowToast("复制失败");
            ShowNotification("复制失败", "无法写入剪贴板");
        }
    }

    private async void OnToolbarSaveRequested(object? sender, EventArgs e)
    {
        var bitmap = _capturedRegion;
        if (bitmap == null)
            return;

        try
        {
            var screenshotDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SigXor",
                "Screenshots");
            Directory.CreateDirectory(screenshotDir);

            var topLevel = _screenshotToolbar as TopLevel ?? this;
            IStorageFolder? startFolder = null;
            try
            {
                startFolder = await topLevel.StorageProvider
                    .TryGetFolderFromPathAsync(screenshotDir);
            }
            catch
            {
                // 起始目录不可用时忽略，使用系统默认位置
            }

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(
                new FilePickerSaveOptions
                {
                    Title = "保存截图",
                    SuggestedFileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                    DefaultExtension = "png",
                    FileTypeChoices =
                    [
                        new FilePickerFileType("PNG 图片") { Patterns = ["*.png"] }
                    ],
                    SuggestedStartLocation = startFolder
                });

            if (file == null)
                return;

            await using (var stream = await file.OpenWriteAsync())
            {
                bitmap.Save(stream);
            }

            _screenshotToolbar?.ShowToast($"已保存: {Path.GetFileName(file.Path.LocalPath)}");
            ShowNotification("已保存", file.Path.LocalPath);
        }
        catch (Exception ex)
        {
            _screenshotToolbar?.ShowToast("保存失败");
            ShowNotification("保存失败", ex.Message);
        }
    }

    private void OnToolbarCloseRequested(object? sender, EventArgs e)
    {
        if (_ocrBusy)
            return;

        var copied = false;
        if (_capturedRegion != null)
            copied = ScreenshotHelper.CopyBitmapToClipboard(_capturedRegion);

        FinishRegionCapture(restoreWindow: true);
        ShowNotification(
            copied ? "已复制" : "复制失败",
            copied ? "截图已复制到剪贴板" : "无法写入剪贴板");
    }

    /// <summary>预览窗格拖动过程中实时更新，让工具条持续跟随其下方。</summary>
    private void OnPreviewPositionChanged(object? sender, PixelPoint previewPosition)
    {
        var preview = _previewWindow;
        var toolbar = _screenshotToolbar;
        if (preview == null || toolbar == null)
            return;

        var scale = preview.RenderScaling > 0 ? preview.RenderScaling : 1.0;
        var width = (int)Math.Ceiling(preview.Width * scale);
        var height = (int)Math.Ceiling(preview.Height * scale);
        toolbar.MoveNear(new PixelRect(
            previewPosition.X,
            previewPosition.Y,
            Math.Max(1, width),
            Math.Max(1, height)));
    }

    /// <summary>把工具条提升到置顶窗口最上层，确保不被预览窗格遮挡。</summary>
    private static void ForceWindowTopmost(Window window)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var handle = window.TryGetPlatformHandle()?.Handle ?? nint.Zero;
        if (handle == nint.Zero)
            return;

        const uint swpNosize = 0x0001;
        const uint swpNomove = 0x0002;
        const uint swpNoactivate = 0x0010;
        SetWindowPos(handle, new nint(-1), 0, 0, 0, 0,
            swpNomove | swpNosize | swpNoactivate);
    }

    private void FinishRegionCapture(bool restoreWindow)
    {
        _captureOverlay?.Close();
        _captureOverlay = null;
        _previewWindow?.Close();
        _previewWindow = null;
        _screenshotToolbar?.Close();
        _screenshotToolbar = null;
        _capturedFullScreen?.Dispose();
        _capturedFullScreen = null;
        _capturedRegion?.Dispose();
        _capturedRegion = null;
        _isCapturing = false;
        if (_keyboardHook != null)
            _keyboardHook.EscapeCaptureEnabled = false;

        if (restoreWindow)
            RestoreMainWindowAfterCapture();
    }

    private void RestoreMainWindowAfterCapture()
    {
        if (_wasMainWindowVisible)
            ShowMainWindow();
    }

    private PixelRect GetVirtualScreenBounds()
    {
        PixelRect? union = null;
        foreach (var screen in Screens.All)
        {
            union = union.HasValue ? union.Value.Union(screen.Bounds) : screen.Bounds;
        }

        return union ?? Screens.Primary?.Bounds ?? new PixelRect(0, 0, 1920, 1080);
    }

    private async Task CopyTextToClipboardAsync(string text)
    {
        var clipboard = Clipboard ?? TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
            throw new InvalidOperationException("无法访问剪贴板");
        await clipboard.SetTextAsync(text);
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;
        return text.Substring(0, maxLength) + "…";
    }

    private void StartRecording(RecordingTrigger trigger)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("音频录制当前仅支持 Windows。");

            _activeTrigger = trigger;
            _isRecording = true;
            _textSimulator?.CaptureTargetWindow();
            _audioCapture?.StartRecording(_config.SampleRate, _config.Channels, _config.BitDepth);
            _voiceOverlay?.ShowRecording();
        }
        catch (Exception ex)
        {
            ShowNotification("录音启动失败", ex.Message);
            _isRecording = false;
            _activeTrigger = RecordingTrigger.None;
            _voiceOverlay?.HideOverlay();
        }
    }

    private async void StopRecording()
    {
        if (!_isRecording)
            return;

        try
        {
            _isRecording = false;
            _activeTrigger = RecordingTrigger.None;
            _keyboardToggleActive = false;
            _audioCapture?.StopRecording();
            _voiceOverlay?.ShowProcessing();

            var audioData = _audioCapture?.GetCompleteAudio();
            if (audioData != null && _speechRecognizer != null)
            {
                RecognitionStatusText.Text = "正在识别...";
                var result = await _speechRecognizer.RecognizeFromBufferAsync(audioData, _config.SampleRate);
                _voiceOverlay?.HideOverlay();
                if (!string.IsNullOrEmpty(result))
                    await OnTextRecognizedAsync(result);
            }
            else
            {
                _voiceOverlay?.HideOverlay();
            }
        }
        catch (Exception ex)
        {
            _voiceOverlay?.HideOverlay();
            ShowNotification("录音停止失败", ex.Message);
        }
    }

    private async Task OnTextRecognizedAsync(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        try
        {
            if (_config.UseClipboard || OperatingSystem.IsWindows())
                await _textSimulator!.InsertTextAsync(text);
            else
                await _textSimulator!.TypeTextAsync(text);

            Dispatcher.UIThread.Post(() => LastRecognizedText.Text = text);
            ShowNotification("文字输入完成", text);
        }
        catch (Exception ex)
        {
            ShowNotification("文字输入失败", ex.Message);
        }
    }

    private void OnAudioStatusChanged(object? sender, string status) =>
        Dispatcher.UIThread.Post(() => RecordingStatusText.Text = status);

    private void OnRecognitionStatusChanged(object? sender, string status) =>
        Dispatcher.UIThread.Post(() => RecognitionStatusText.Text = status);

    private void OnSpeechError(object? sender, Exception error) =>
        Dispatcher.UIThread.Post(() => ShowNotification("语音识别错误", error.Message));

    private void UpdateStatus(object? sender, EventArgs e)
    {
        if (!_isShortcutDown && ShortcutStatusText.Text != "等待中...")
            ShortcutStatusText.Text = "等待中...";
    }

    private static void SelectComboBoxByTag(ComboBox? comboBox, string tagValue)
    {
        if (comboBox == null)
            return;

        foreach (var item in comboBox.Items)
        {
            if (item is ComboBoxItem comboItem && comboItem.Tag?.ToString() == tagValue)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private void RefreshEngineComboBox()
    {
        if (EngineComboBox == null)
            return;

        _isLoadingSettings = true;
        try
        {
            var selectable = SpeechModelManager.GetSelectableModels().ToList();
            var savedEngine = _config.RecognitionEngine;

            EngineComboBox.Items.Clear();

            if (selectable.Count == 0)
            {
                EngineComboBox.IsEnabled = false;
                EngineComboBox.Items.Add(new ComboBoxItem
                {
                    Content = "请先下载模型",
                    Tag = "",
                    IsEnabled = false
                });
                EngineComboBox.SelectedIndex = 0;
                return;
            }

            EngineComboBox.IsEnabled = true;
            foreach (var model in selectable)
            {
                EngineComboBox.Items.Add(new ComboBoxItem
                {
                    Content = model.DisplayName,
                    Tag = model.EngineTag
                });
            }

            var target = selectable.FirstOrDefault(m =>
                m.EngineTag.Equals(savedEngine, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                target = selectable[0];
                _config.RecognitionEngine = target.EngineTag;
                _config.Save();
            }

            SelectComboBoxByTag(EngineComboBox, target.EngineTag);
            UpdateModelActionButtons();
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    private void UpdateModelActionButtons()
    {
        if (DownloadModelButton == null || OpenModelFolderButton == null)
            return;

        var engineId = _config.RecognitionEngine;
        var installed = SpeechModelManager.IsInstalled(engineId);
        var busy = _downloadCts != null;

        DownloadModelButton.IsVisible = !installed;
        OpenModelFolderButton.IsVisible = installed;
        DownloadModelButton.IsEnabled = !busy;
        OpenModelFolderButton.IsEnabled = !busy;
    }

    private async void DownloadModelButton_Click(object? sender, RoutedEventArgs e)
    {
        var engineId = _config.RecognitionEngine;
        if (SpeechModelManager.IsInstalled(engineId))
            return;

        _downloadCts = new CancellationTokenSource();
        UpdateModelActionButtons();
        RecognitionStatusText.Text = "准备下载...";

        try
        {
            var ok = await SpeechModelManager.DownloadAsync(
                engineId,
                msg => Dispatcher.UIThread.Post(() => RecognitionStatusText.Text = msg),
                _downloadCts.Token);

            if (ok)
            {
                RecognitionStatusText.Text = "模型已下载";
                RefreshEngineComboBox();
            }
            else
            {
                RecognitionStatusText.Text = "下载失败";
            }
        }
        catch (OperationCanceledException)
        {
            RecognitionStatusText.Text = "已取消下载";
        }
        catch (Exception ex)
        {
            RecognitionStatusText.Text = "下载失败";
            await DialogHelper.ShowWarningAsync(this, ex.Message, "下载失败");
        }
        finally
        {
            _downloadCts?.Dispose();
            _downloadCts = null;
            UpdateModelActionButtons();
        }
    }

    private void OpenModelFolderButton_Click(object? sender, RoutedEventArgs e)
    {
        var dir = SpeechModelManager.ModelsDirectory;
        Directory.CreateDirectory(dir);

        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        else if (OperatingSystem.IsLinux())
        {
            Process.Start("xdg-open", dir);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Process.Start("open", dir);
        }
    }

    private async void EngineComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings)
            return;

        try
        {
            if (EngineComboBox?.SelectedItem is not ComboBoxItem item
                || string.IsNullOrEmpty(item.Tag?.ToString()))
                return;

            var newEngine = item.Tag.ToString()!;
            if (_config.RecognitionEngine == newEngine)
                return;

            if (!SpeechModelManager.IsInstalled(newEngine))
            {
                await DialogHelper.ShowInfoAsync(this,
                    "该引擎尚未下载，请先下载模型。",
                    "无法切换");
                RefreshEngineComboBox();
                return;
            }

            _config.RecognitionEngine = newEngine;
            _config.Save();

            if (_serviceRunning)
                StopService();

            _speechRecognizer?.Dispose();
            _speechRecognizer = new SpeechRecognizer();
            _speechRecognizer.StatusChanged += OnRecognitionStatusChanged;
            _speechRecognizer.Error += OnSpeechError;

            RecognitionStatusText.Text = "引擎已切换，请重新启动服务";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"引擎选择错误: {ex.Message}");
        }
    }

    private void LanguageComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingSettings)
            return;

        try
        {
            if (LanguageComboBox?.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                var newLanguage = item.Tag.ToString()!;
                if (_config.RecognitionLanguage == newLanguage)
                    return;

                _config.RecognitionLanguage = newLanguage;
                _config.Save();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"语言选择错误: {ex.Message}");
        }
    }

    private void ShowNotificationsCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
            return;

        _config.ShowNotifications = ShowNotificationsCheckBox.IsChecked == true;
        _config.Save();
    }

    private void UseClipboardCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings)
            return;

        _config.UseClipboard = UseClipboardCheckBox.IsChecked == true;
        _config.Save();
    }

    private void ScreenshotShortcutCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings || ScreenshotShortcutCheckBox == null)
            return;

        _config.EnableScreenshotShortcut = ScreenshotShortcutCheckBox.IsChecked == true;
        _config.Save();
        if (_keyboardHook != null)
            _keyboardHook.ScreenshotEnabled = _config.EnableScreenshotShortcut;
    }

    private void ShowNotification(string title, string message)
    {
        if (_config.ShowNotifications)
            _trayIcon?.ShowBalloon(title, message);

        System.Diagnostics.Debug.WriteLine($"{title}: {message}");
    }

    private async void SilentStartCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings || SilentStartCheckBox == null)
            return;

        _config.SilentStart = SilentStartCheckBox.IsChecked == true;
        _config.Save();

        if (StartupHelper.IsEnabled())
        {
            try
            {
                StartupHelper.SetEnabled(true, _config.SilentStart);
            }
            catch (Exception ex)
            {
                await DialogHelper.ShowWarningAsync(this, $"更新开机自启动参数失败: {ex.Message}");
            }
        }
    }

    private void MinimizeToTrayCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings || MinimizeToTrayCheckBox == null)
            return;

        _config.MinimizeToTray = MinimizeToTrayCheckBox.IsChecked == true;
        _config.Save();
    }

    private async void AutoStartCheckBox_Changed(object? sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings || AutoStartCheckBox == null)
            return;

        try
        {
            var enabled = AutoStartCheckBox.IsChecked == true;
            StartupHelper.SetEnabled(enabled, _config.SilentStart);
            _config.AutoStartWithWindows = enabled;
            _config.Save();
        }
        catch (Exception ex)
        {
            AutoStartCheckBox.IsChecked = StartupHelper.IsEnabled();
            await DialogHelper.ShowWarningAsync(this, $"设置开机自启动失败: {ex.Message}");
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_isExiting && _config.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
            ShowNotification("SigXor", "程序已最小化到托盘");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        try
        {
            if (!_isExiting)
                StopService();

            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _downloadCts = null;
            _statusTimer?.Stop();
            _keyboardHook?.Dispose();
            _audioCapture?.Dispose();
            _speechRecognizer?.Dispose();
            _voiceOverlay?.Close();
            _voiceOverlay = null;
            _captureOverlay?.Close();
            _captureOverlay = null;
            _previewWindow?.Close();
            _previewWindow = null;
            _screenshotToolbar?.Close();
            _screenshotToolbar = null;
            _capturedFullScreen?.Dispose();
            _capturedFullScreen = null;
            _capturedRegion?.Dispose();
            _capturedRegion = null;
            _ocrEngine?.Dispose();
            _ocrEngine = null;
            _trayIcon?.Dispose();
            _trayIcon = null;
            SpeechModelManager.ModelsChanged -= OnModelsChanged;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"清理资源时出错: {ex.Message}");
        }

        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && IsVisible)
            Hide();
        base.OnKeyDown(e);
    }
}
