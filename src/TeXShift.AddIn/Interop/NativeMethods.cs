using System;
using System.Runtime.InteropServices;

namespace TeXShift.AddIn.Interop
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("UIAutomationCore.dll")]
        internal static extern int UiaDisconnectAllProviders();
    }
}
