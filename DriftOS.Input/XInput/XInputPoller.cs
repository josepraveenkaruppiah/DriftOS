using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace DriftOS.Input.XInput
{
    public sealed class XInputPoller : IDisposable
    {
        public event Action<double, double, ushort>? OnState;                                   // (lx, ly, buttons)
        public event Action<double, double, double, double, ushort>? OnStateEx;                 // (lx, ly, rx, ry, buttons)

        private readonly int _periodMs;
        private Thread? _thread;
        private volatile bool _running;

        public XInputPoller(int hz = 120)
        {
            _periodMs = Math.Max(1, (int)Math.Round(1000.0 / Math.Max(1, hz)));
        }

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(PollLoop) { IsBackground = true, Name = "XInputPoller" };
            _thread.Start();
        }

        public void Dispose()
        {
            _running = false;
            try { _thread?.Join(250); } catch { /* ignore */ }
        }

        private static double Norm(short v)
        {
            // map [-32768..32767] to [-1..1]
            double n = v / 32767.0;
            if (n < -1) n = -1;
            if (n > 1) n = 1;
            return n;
        }

        private void PollLoop()
        {
            while (_running)
            {
                XINPUT_STATE state;
                uint rc = XInputGetState(0, out state);
                if (rc == 0)
                {
                    var gp = state.Gamepad;
                    double lx = Norm(gp.sThumbLX);
                    double ly = Norm(gp.sThumbLY);
                    double rx = Norm(gp.sThumbRX);
                    double ry = Norm(gp.sThumbRY);
                    ushort buttons = gp.wButtons;

                    OnState?.Invoke(lx, ly, buttons);
                    OnStateEx?.Invoke(lx, ly, rx, ry, buttons);
                }
                Thread.Sleep(_periodMs);
            }
        }

        #region P/Invoke
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState", ExactSpelling = true)]
        private static extern uint XInputGetState(uint dwUserIndex, out XINPUT_STATE pState);

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }
        #endregion
    }
}
