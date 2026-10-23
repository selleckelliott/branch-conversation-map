using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Branch.Desktop;

public record HostWindow(IntPtr Handle, string Title)
{
    public override string ToString() => Title;
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    public delegate bool EnumWindowProc(IntPtr handle, IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr handle);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr handle);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr handle, out Rect rect);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr handle, StringBuilder title, int count);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr handle, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? className, string title);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);

    public static List<HostWindow> Hosts()
    {
        var found = new List<HostWindow>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle)) return true;
            GetWindowThreadProcessId(handle, out uint id);
            try
            {
                string process = Process.GetProcessById((int)id).ProcessName;
                if (!process.Equals("Codex", StringComparison.OrdinalIgnoreCase) && !process.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase)) return true;
                var title = new StringBuilder(512); GetWindowText(handle, title, title.Capacity);
                if (title.Length > 0) found.Add(new HostWindow(handle, title.ToString()));
            }
            catch (ArgumentException) { }
            catch (System.ComponentModel.Win32Exception) { }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
