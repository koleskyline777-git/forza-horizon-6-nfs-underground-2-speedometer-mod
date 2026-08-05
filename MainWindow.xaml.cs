using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nfsu2ForzaHud.Hud;
using Nfsu2ForzaHud.Input;
using Nfsu2ForzaHud.Telemetry;

namespace Nfsu2ForzaHud;

public partial class MainWindow : Window
{
    private readonly UdpTelemetryListener _listener = new();
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly DispatcherTimer _demoTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly HudSettings _settings = HudSettings.Load();

    private AcHudAssets? _ac;
    private GlobalHotkeys? _hotkeys;
    private TrayIcon? _tray;
    private TelemetryFrame _frame = TelemetryFrame.Demo(0);
    private bool _hudVisible = true;
    private bool _useMph = true;
    private bool _demoMode = true;
    private bool _moveMode;
    private bool _forceExit;
    private double _demoT;
    private string _lastRpmFace = "";
    private int _lastRev = -1;
    private int _lastBoost = -1;
    private int _lastSpeed = -1;
    private string _lastGearKey = "";
    private bool _lastFlash;
    private int _prevGear = int.MinValue;
    private DateTime _gearChangeUtc = DateTime.MinValue;

    private const double LogicalSize = 800;
    private const int DefaultPort = 20777;

    public MainWindow()
    {
        InitializeComponent();
        _useMph = _settings.UseMph;
        _uiTimer.Tick += (_, _) => RefreshUi();
        _demoTimer.Tick += (_, _) =>
        {
            if (!_demoMode) return;
            _demoT += 0.016;
            _frame = TelemetryFrame.Demo(_demoT);
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var iconPath = ImageUtil.AssetPath("app.ico");
        if (System.IO.File.Exists(iconPath))
            Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));

        ApplyScaleAndPlace();
        MakeClickThrough(true);
        InitTray();

        var root = AcHudAssets.FindRoot();
        if (root == null)
        {
            TxtStatus.Text = "NFSU2HUD 3.0 assets not found — put mod in nfsu2-hud-assets\\NFSU2HUD 3.0";
            return;
        }

        _ac = new AcHudAssets(root);
        ImgBoostNos.Source = _ac.BoostNosOverlay;
        ImgBackground.Source = _ac.Background;
        ImgGearFlash.Source = _ac.GearFlashOff;
        ImgUom.Source = _ac.Uom(_useMph);

        _hotkeys = new GlobalHotkeys(this);
        _hotkeys.HotkeyPressed += OnHotkey;
        _hotkeys.Register();

        try
        {
            _listener.Start(DefaultPort);
            _ = PumpTelemetryAsync();
        }
        catch (Exception ex)
        {
            TxtStatus.Text = $"UDP bind failed on {DefaultPort}: {ex.Message}";
        }

        _uiTimer.Start();
        _demoTimer.Start();
        UpdateStatusChrome();

        // Live in the tray by default (no taskbar button).
        _tray?.ShowBalloon("NFSU2 Forza HUD", "Running in tray · F1 hide/show · F9 quit");
    }

    private void InitTray()
    {
        var iconPath = ImageUtil.AssetPath("app.ico");
        _tray = new TrayIcon(this, iconPath);
        _tray.ShowRequested += () => Dispatcher.Invoke(ShowHud);
        _tray.ExitRequested += () => Dispatcher.Invoke(QuitToTrayExit);
        _tray.ToggleMoveRequested += () => Dispatcher.Invoke(() => OnHotkey(GlobalHotkeys.HotMove));
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
            HideToTray();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        // Close / Alt+F4 → tray (unless Quit was requested).
        if (!_forceExit && !(_tray?.ExitRequestedFlag ?? false))
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        PersistPlacement();
        _settings.UseMph = _useMph;
        _settings.Save();
        _uiTimer.Stop();
        _demoTimer.Stop();
        _hotkeys?.Dispose();
        _listener.Stop();
        _tray?.Dispose();
    }

    private void HideToTray()
    {
        _hudVisible = false;
        Hide();
        _tray?.SetTooltip("NFSU2 HUD (hidden) · double-click to show");
    }

    private void ShowHud()
    {
        _hudVisible = true;
        Show();
        Activate();
        Topmost = true;
        _tray?.SetTooltip("NFSU2 Forza HUD");
        UpdateStatusChrome();
    }

    private void QuitToTrayExit()
    {
        _forceExit = true;
        Close();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_moveMode) return;
        try { DragMove(); }
        catch { /* ignore */ }
        PersistPlacement();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Needs move mode (click-through off) so the wheel reaches this window.
        if (!_moveMode) return;

        double step = e.Delta > 0 ? 0.05 : -0.05;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            step *= 2;

        double oldScale = _settings.Scale;
        double newScale = Math.Clamp(oldScale + step, 0.6, 2.5);
        if (Math.Abs(newScale - oldScale) < 0.001) return;

        // Keep window center fixed while resizing.
        double cx = Left + Width * 0.5;
        double cy = Top + Height * 0.5;
        _settings.Scale = newScale;
        ApplyScaleAndPlace(centerX: cx, centerY: cy);
        PersistPlacement();
        e.Handled = true;
    }

    private async Task PumpTelemetryAsync()
    {
        await foreach (var frame in _listener.Frames.ReadAllAsync())
        {
            await Dispatcher.InvokeAsync(() =>
            {
                _frame = frame;
                if (_demoMode && frame.IsRaceOn)
                {
                    _demoMode = false;
                    UpdateStatusChrome();
                }
            });
        }
    }

    private void OnHotkey(int id)
    {
        switch (id)
        {
            case GlobalHotkeys.HotToggleHud:
                if (_hudVisible) HideToTray();
                else ShowHud();
                break;
            case GlobalHotkeys.HotToggleUnits:
                _useMph = !_useMph;
                _settings.UseMph = _useMph;
                if (_ac != null)
                {
                    ImgUom.Source = _ac.Uom(_useMph);
                    _lastSpeed = -1;
                }
                break;
            case GlobalHotkeys.HotDemo:
                _demoMode = !_demoMode;
                UpdateStatusChrome();
                break;
            case GlobalHotkeys.HotMove:
                _moveMode = !_moveMode;
                MakeClickThrough(!_moveMode);
                Cursor = _moveMode ? Cursors.SizeAll : Cursors.Arrow;
                StatusBar.Opacity = _moveMode ? 1.0 : 0.85;
                UpdateStatusChrome();
                break;
            case GlobalHotkeys.HotExit:
                QuitToTrayExit();
                break;
        }
    }

    private void ApplyScaleAndPlace(double? centerX = null, double? centerY = null)
    {
        double scale = Math.Clamp(_settings.Scale <= 0 ? 1.45 : _settings.Scale, 0.6, 2.5);
        _settings.Scale = scale;

        double px = LogicalSize * scale;
        // Fit on work area if needed (keep aspect).
        var work = SystemParameters.WorkArea;
        double maxSide = Math.Min(work.Width, work.Height) * 0.92;
        if (px > maxSide)
        {
            scale = maxSide / LogicalSize;
            _settings.Scale = scale;
            px = LogicalSize * scale;
        }

        Width = px;
        Height = px;
        HudViewbox.Width = px;
        HudViewbox.Height = px;

        if (centerX is double cx && centerY is double cy)
        {
            Left = Clamp(cx - px * 0.5, work.Left, work.Right - px);
            Top = Clamp(cy - px * 0.5, work.Top, work.Bottom - px);
        }
        else if (!double.IsNaN(_settings.Left) && !double.IsNaN(_settings.Top))
        {
            Left = Clamp(_settings.Left, work.Left, work.Right - px);
            Top = Clamp(_settings.Top, work.Top, work.Bottom - px);
        }
        else
        {
            // Default: bottom-right like AC / Forza race HUD.
            Left = work.Right - px - 12;
            Top = work.Bottom - px - 8;
        }
    }

    private void PersistPlacement()
    {
        _settings.Left = Left;
        _settings.Top = Top;
        _settings.Save();
    }

    private void UpdateStatusChrome()
    {
        TxtHotkeys.Text = _moveMode
            ? "MOVE MODE — drag · scroll resize · Shift+scroll faster · F4 lock · F9 quit"
            : "F1 HUD  ·  F2 mph/kph  ·  F3 demo  ·  F4 move  ·  F9 quit";

        if (_demoMode)
            TxtStatus.Text = "Demo on — F3 off when FH6 is live";
        else
            TxtStatus.Text = $"FH6 · UDP :{DefaultPort} · pkts {_listener.PacketsReceived}";
    }

    private void RefreshUi()
    {
        if (!_hudVisible || _ac == null) return;

        var maxRpm = _frame.EffectiveMaxRpm;
        var (face, spin) = AcHudAssets.FaceForMaxRpm(maxRpm);
        if (face != _lastRpmFace)
        {
            ImgRpmFace.Source = _ac.RpmFace(face);
            _lastRpmFace = face;
        }

        int rev = AcHudAssets.RevFrame(_frame.CurrentEngineRpm, spin);
        if (rev != _lastRev)
        {
            ImgRev.Source = _ac.Rev(rev);
            _lastRev = rev;
        }

        int boost = AcHudAssets.BoostFrame(_frame.BoostPsi);
        if (boost != _lastBoost)
        {
            ImgBoostNeedle.Source = _ac.BoostNeedle(boost);
            _lastBoost = boost;
        }

        int speed = (int)Math.Round(Math.Max(0, _useMph ? _frame.SpeedMph : _frame.SpeedKph));
        if (speed != _lastSpeed)
        {
            ImgSpeed.Source = _ac.Speed(speed);
            _lastSpeed = speed;
        }

        if (_frame.Gear != _prevGear)
        {
            _prevGear = _frame.Gear;
            _gearChangeUtc = DateTime.UtcNow;
        }
        bool gearOrange = (DateTime.UtcNow - _gearChangeUtc).TotalSeconds < 0.25;
        string gearLabel = _frame.GearLabel;
        string gearKey = gearLabel + (gearOrange ? "O" : "W");
        if (gearKey != _lastGearKey)
        {
            ImgGear.Source = _ac.Gear(gearLabel, gearOrange);
            _lastGearKey = gearKey;
        }

        if (_lastFlash)
        {
            ImgGearFlash.Source = _ac.GearFlashOff;
            _lastFlash = false;
        }

        if (_moveMode) return;

        if (!_demoMode && _listener.PacketsReceived > 0 &&
            (DateTime.UtcNow - _listener.LastPacketUtc).TotalSeconds < 2)
        {
            TxtStatus.Text =
                $"FH6 · {_frame.CurrentEngineRpm:0}/{maxRpm:0} · face {face} · " +
                $"{speed} {(_useMph ? "MPH" : "KPH")} · pkts {_listener.PacketsReceived}";
        }
        else if (_demoMode)
        {
            TxtStatus.Text =
                $"Demo · {_frame.CurrentEngineRpm:0}/{maxRpm:0} · face {face} · " +
                $"{(_useMph ? "MPH" : "KPH")}";
        }
    }

    private void MakeClickThrough(bool enabled)
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        int style = GetWindowLong(hwnd, GwlExstyle);
        // Layered + tool window keeps it overlay-friendly.
        style |= WsExToolwindow | WsExLayered;
        if (enabled)
            style |= WsExTransparent;
        else
            style &= ~WsExTransparent;
        SetWindowLong(hwnd, GwlExstyle, style);
    }

    private static double Clamp(double v, double min, double max) =>
        Math.Max(min, Math.Min(max, v));

    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolwindow = 0x00000080;
    private const int WsExLayered = 0x00080000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
