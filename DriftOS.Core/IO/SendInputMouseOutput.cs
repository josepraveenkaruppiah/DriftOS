using System;
using System.Runtime.InteropServices;

namespace DriftOS.Core.IO;

public sealed class SendInputMouseOutput : IMouseOutput
{
    private const uint INPUT_MOUSE = 0;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
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

    private static void Send(uint flags, int dx = 0, int dy = 0)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mi = new MOUSEINPUT
            {
                dx = dx,
                dy = dy,
                dwFlags = flags
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void Move(int dx, int dy) => Send(MOUSEEVENTF_MOVE, dx, dy);
    public void LeftDown() => Send(MOUSEEVENTF_LEFTDOWN);
    public void LeftUp() => Send(MOUSEEVENTF_LEFTUP);
    public void RightDown() => Send(MOUSEEVENTF_RIGHTDOWN);
    public void RightUp() => Send(MOUSEEVENTF_RIGHTUP);
}
