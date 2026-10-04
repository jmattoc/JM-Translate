using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace JmTranslate.App;

internal static class NativeKeys
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint ModAlt = 0x1, ModControl = 0x2, ModNoRepeat = 0x4000;
    public const int WmHotkey = 0x0312;

    /// <summary>true si la tecla está pulsada ahora mismo, aunque otra aplicación tenga el foco.</summary>
    public static bool IsDown(int vKey) => (GetAsyncKeyState(vKey) & 0x8000) != 0;

    public static bool Register(IntPtr hwnd, int id, uint modifiers, uint vk) => RegisterHotKey(hwnd, id, modifiers | ModNoRepeat, vk);
    public static void Unregister(IntPtr hwnd, int id) => UnregisterHotKey(hwnd, id);
}

/// <summary>Atajos globales (funcionan con Teams, Zoom o Meet en primer plano).</summary>
internal sealed class HotkeyManager : IDisposable
{
    private readonly IntPtr _hwnd;
    private readonly HwndSource _source;
    private readonly List<int> _ids = new();

    public event Action<int>? Pressed;

    public HotkeyManager(Window window)
    {
        _hwnd = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_hwnd) ?? throw new InvalidOperationException("No hay ventana para los atajos.");
        _source.AddHook(Hook);
    }

    /// <returns>false si otra aplicación ya usa esa combinación.</returns>
    public bool Register(int id, uint modifiers, uint vk)
    {
        if (!NativeKeys.Register(_hwnd, id, modifiers, vk)) return false;
        _ids.Add(id);
        return true;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeKeys.WmHotkey) return IntPtr.Zero;
        Pressed?.Invoke(wParam.ToInt32());
        handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _ids) NativeKeys.Unregister(_hwnd, id);
        _source.RemoveHook(Hook);
    }
}
