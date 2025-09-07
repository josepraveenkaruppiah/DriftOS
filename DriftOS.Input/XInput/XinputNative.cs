using System;
using System.Runtime.InteropServices;

namespace DriftOS.Input.XInput;

internal static class XInputNative
{
    // Try 1_4 first (Win 8+). If your OS is older, we can fall back later.
    [DllImport("xinput1_4.dll", ExactSpelling = true)]
    private static extern int XInputGetState(int dwUserIndex, out XINPUT_STATE pState);

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_STATE
    {
        public uint dwPacketNumber;
        public XINPUT_GAMEPAD Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XINPUT_GAMEPAD
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    public static bool TryGetState(int index, out XINPUT_STATE state)
        => XInputGetState(index, out state) == 0; // 0 == ERROR_SUCCESS
}
