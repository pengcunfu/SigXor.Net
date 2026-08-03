using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace SigXor;

public partial class VoiceInputOverlay : Window
{
    private readonly Border[] _bars;
    private readonly DispatcherTimer _waveTimer;
    private readonly Random _random = new();
    private bool _isVisible;

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
        var screen = Screens.Primary ?? Screens.All[0];
        var area = screen.WorkingArea;
        UpdateLayout();

        var width = Width > 0 ? Width : 160;
        var height = Height > 0 ? Height : 44;

        Position = new PixelPoint(
            area.X + (int)((area.Width - width) / 2),
            area.Y + (int)(area.Height - height - 28));
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
