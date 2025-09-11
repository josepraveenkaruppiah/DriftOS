using System;
using System.Runtime.InteropServices;

namespace DriftOS.Core.IO
{
    public sealed class SendInputMouseOutput : IMouseOutput
    {
        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_HWHEEL = 0x01000;
        private const int WHEEL_DELTA = 120;

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion U;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private static void SendMouse(uint flags, int dx = 0, int dy = 0, int data = 0)
        {
            var input = new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dx = dx,
                        dy = dy,
                        mouseData = unchecked((uint)data),
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        public void Move(int dx, int dy) => SendMouse(MOUSEEVENTF_MOVE, dx, dy);
        public void LeftDown() => SendMouse(MOUSEEVENTF_LEFTDOWN);
        public void LeftUp() => SendMouse(MOUSEEVENTF_LEFTUP);
        public void RightDown() => SendMouse(MOUSEEVENTF_RIGHTDOWN);
        public void RightUp() => SendMouse(MOUSEEVENTF_RIGHTUP);

        public void Scroll(int wheelDelta)
        {
            // wheelDelta is typically n*120
            SendMouse(MOUSEEVENTF_WHEEL, data: wheelDelta);
        }

        public void HScroll(int wheelDelta)
        {
            // positive = right, negative = left
            SendMouse(MOUSEEVENTF_HWHEEL, data: wheelDelta);
        }
    }
}
