using System;
using System.Threading;
using System.Threading.Tasks;

namespace DriftOS.Input.XInput;

public sealed class XInputPoller : IDisposable
{
    private readonly int _index;
    private readonly int _hz;
    private CancellationTokenSource? _cts;

    // Normalized left-stick (-1..+1). Buttons are raw bitfield.
    public event Action<float, float, ushort>? OnState;

    public XInputPoller(int index = 0, int hz = 120)
    {
        _index = index;
        _hz = Math.Clamp(hz, 30, 240);
    }

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(async () =>
        {
            var period = TimeSpan.FromMilliseconds(1000.0 / _hz);
            while (!token.IsCancellationRequested)
            {
                if (XInputNative.TryGetState(_index, out var st))
                {
                    float lx = Math.Clamp(st.Gamepad.sThumbLX / 32767f, -1f, 1f);
                    float ly = Math.Clamp(st.Gamepad.sThumbLY / 32767f, -1f, 1f);
                    OnState?.Invoke(lx, ly, st.Gamepad.wButtons);
                }
                try { await Task.Delay(period, token); }
                catch { /* canceled */ }
            }
        }, token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public void Dispose() => Stop();
}
