using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SigXor;

/// <summary>
/// Manages one voice-input capsule overlay per connected screen.
/// </summary>
public sealed class VoiceInputOverlayController : IDisposable
{
    private readonly List<VoiceInputOverlay> _overlays = [];
    private bool _disposed;

    public void ShowRecording(IReadOnlyList<Screen> screens)
    {
        Sync(screens);
        foreach (var overlay in _overlays)
            overlay.ShowRecording();
    }

    public void ShowProcessing(IReadOnlyList<Screen> screens)
    {
        Sync(screens);
        foreach (var overlay in _overlays)
            overlay.ShowProcessing();
    }

    public void HideOverlay()
    {
        foreach (var overlay in _overlays)
            overlay.HideOverlay();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var overlay in _overlays)
            overlay.Close();
        _overlays.Clear();
    }

    private void Sync(IReadOnlyList<Screen> screens)
    {
        if (_disposed)
            return;

        var targets = screens.Count > 0
            ? screens.ToArray()
            : [];

        // Drop overlays that no longer match a live screen.
        for (var i = _overlays.Count - 1; i >= 0; i--)
        {
            var overlay = _overlays[i];
            if (targets.Any(s => SameScreen(s, overlay.TargetScreen)))
                continue;

            overlay.Close();
            _overlays.RemoveAt(i);
        }

        // Ensure one overlay per screen.
        foreach (var screen in targets)
        {
            var existing = _overlays.FirstOrDefault(o => SameScreen(o.TargetScreen, screen));
            if (existing != null)
            {
                existing.SetTargetScreen(screen);
                continue;
            }

            var overlay = new VoiceInputOverlay();
            overlay.SetTargetScreen(screen);
            _overlays.Add(overlay);
        }
    }

    private static bool SameScreen(Screen? a, Screen? b)
    {
        if (a == null || b == null)
            return false;

        return a.Bounds == b.Bounds && a.Scaling == b.Scaling;
    }
}

public partial class VoiceInputOverlay : Window
{
    private readonly Border[] _bars;
    private readonly DispatcherTimer _waveTimer;
    private readonly Random _random = new();
    private bool _isVisible;
    private Screen? _targetScreen;

    public Screen? TargetScreen => _targetScreen;

    public VoiceInputOverlay()
    {
        InitializeComponent();

        _bars = [Bar1, Bar2, Bar3, Bar4];
        _waveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(90)
        };
        _waveTimer.Tick += OnWaveTick;

        Opened += (_, _) => Reposition();
    }

    public void SetTargetScreen(Screen screen)
    {
        _targetScreen = screen;
        if (_isVisible)
            Reposition();
    }

    public void ShowRecording()
    {
        StatusText.Text = "SigXor";
        ShowOverlay();
        _waveTimer.Start();
    }

    public void ShowProcessing()
    {
        StatusText.Text = "识别中";
        ShowOverlay();
        _waveTimer.Start();
    }

    public void HideOverlay()
    {
        _waveTimer.Stop();
        if (!_isVisible)
            return;

        _isVisible = false;
        Hide();
    }

    private void ShowOverlay()
    {
        if (!_isVisible)
        {
            _isVisible = true;
            Show();
        }

        Reposition();
        Topmost = true;
    }

    private void Reposition()
    {
        var screen = _targetScreen ?? Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
        if (screen == null)
            return;

        var area = screen.WorkingArea;
        UpdateLayout();

        var scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
        var widthPx = (int)((Width > 0 ? Width : 160) * scale);
        var heightPx = (int)((Height > 0 ? Height : 44) * scale);
        var bottomMarginPx = (int)(28 * scale);

        Position = new PixelPoint(
            area.X + (area.Width - widthPx) / 2,
            area.Y + area.Height - heightPx - bottomMarginPx);
    }

    private void OnWaveTick(object? sender, EventArgs e)
    {
        for (var i = 0; i < _bars.Length; i++)
            _bars[i].Height = _random.Next(5, 19);
    }

    protected override void OnClosed(EventArgs e)
    {
        _waveTimer.Stop();
        base.OnClosed(e);
    }
}
