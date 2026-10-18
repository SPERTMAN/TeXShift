using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TeXShift.Tests.E2E
{
    internal static class OneNoteEditorInput
    {
        private const uint WmKeyDown = 0x0100;
        private const uint WmKeyUp = 0x0101;
        private const int VkShift = 0x10;
        private const int VkLShift = 0xA0;
        private const byte KeyDownState = 0x80;
        private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint code, uint mapType);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint attachThreadId, uint attachToThreadId, bool attach);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState(byte[] keyState);

        [DllImport("user32.dll")]
        private static extern bool SetKeyboardState(byte[] keyState);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        /// <summary>
        /// Posts to the document canvas. NavigateTo updates the selection but can leave native
        /// keyboard focus on the Ribbon, so hwndFocus is not a reliable input target.
        /// </summary>
        public static void PressKey(IntPtr mainWindow, int virtualKey)
        {
            IntPtr target = IntPtr.Zero;
            EnumChildWindows(mainWindow, (child, unused) =>
            {
                var className = new StringBuilder(256);
                GetClassName(child, className, className.Capacity);
                if (className.ToString() != "OneNote::DocumentCanvas" || !IsWindowVisible(child)) return true;
                target = child;
                return false;
            }, IntPtr.Zero);
            if (target == IntPtr.Zero) throw new InvalidOperationException("OneNote page editor was not found.");

            // lParam: repeat count 1 and scan code; key-up also sets the previous-state and transition bits.
            int keyData = 1 | ((int)MapVirtualKey((uint)virtualKey, 0) << 16);
            if (!PostMessage(target, WmKeyDown, (IntPtr)virtualKey, (IntPtr)keyData) ||
                !PostMessage(target, WmKeyUp, (IntPtr)virtualKey, (IntPtr)(keyData | unchecked((int)0xC0000000))))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        /// <summary>
        /// Runs <paramref name="action"/> with Shift down for OneNote. Posted keys carry no modifier state,
        /// so Shift is set in the keyboard state shared with OneNote's input thread.
        /// </summary>
        public static void HoldShift(IntPtr mainWindow, Action action)
        {
            uint oneNoteThread = GetWindowThreadProcessId(mainWindow, out _);
            uint currentThread = GetCurrentThreadId();
            if (!AttachThreadInput(currentThread, oneNoteThread, true))
            {
                throw new InvalidOperationException("Could not share keyboard state with OneNote.");
            }

            try
            {
                var state = new byte[256];
                GetKeyboardState(state);
                byte shift = state[VkShift];
                byte leftShift = state[VkLShift];
                state[VkShift] = KeyDownState;
                state[VkLShift] = KeyDownState;
                SetKeyboardState(state);

                try
                {
                    action();
                }
                finally
                {
                    state[VkShift] = shift;
                    state[VkLShift] = leftShift;
                    SetKeyboardState(state);
                }
            }
            finally
            {
                AttachThreadInput(currentThread, oneNoteThread, false);
            }
        }

    }
}
