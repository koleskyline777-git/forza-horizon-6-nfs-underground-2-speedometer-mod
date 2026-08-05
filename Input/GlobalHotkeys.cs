using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Nfsu2ForzaHud.Input;

public sealed class GlobalHotkeys : IDisposable
{
    public const int HotToggleHud = 1;
    public const int HotToggleUnits = 2;
    public const int HotDemo = 3;
    public const int HotMove = 4;
    public const int HotExit = 9;

    private const uint ModNone = 0;
    private const uint VkF1 = 0x70;
    private const uint VkF2 = 0x71;
    private const uint VkF3 = 0x72;
    private const uint VkF4 = 0x73;
    private const uint VkF9 = 0x78;

    private HwndSource? _source;
    private readonly Window _window;

    public event Action<int>? HotkeyPressed;

    public GlobalHotkeys(Window window) => _window = window;

    public void Register()
    {
        var helper = new WindowInteropHelper(_window);
        helper.EnsureHandle();
        _source = HwndSource.FromHwnd(helper.Handle);
        _source?.AddHook(WndProc);

        RegisterHotKey(helper.Handle, HotToggleHud, ModNone, VkF1);
        RegisterHotKey(helper.Handle, HotToggleUnits, ModNone, VkF2);
        RegisterHotKey(helper.Handle, HotDemo, ModNone, VkF3);
        RegisterHotKey(helper.Handle, HotMove, ModNone, VkF4);
        RegisterHotKey(helper.Handle, HotExit, ModNone, VkF9);
    }

    public void Dispose()
    {
        if (_source is null) return;
        var hwnd = _source.Handle;
        UnregisterHotKey(hwnd, HotToggleHud);
        UnregisterHotKey(hwnd, HotToggleUnits);
        UnregisterHotKey(hwnd, HotDemo);
        UnregisterHotKey(hwnd, HotMove);
        UnregisterHotKey(hwnd, HotExit);
        _source.RemoveHook(WndProc);
        _source = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WmHotkey = 0x0312;
        if (msg == WmHotkey)
        {
            HotkeyPressed?.Invoke(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
