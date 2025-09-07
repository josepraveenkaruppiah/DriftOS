namespace DriftOS.Core.Settings;

public sealed class SettingsModel
{
    public double Sensitivity { get; set; } = 1.0;        // 0.1–5.0
    public double Deadzone { get; set; } = 0.15;        // 0.00–0.30
    public string ScrollMode { get; set; } = "RightStick"; // or "Triggers"
}
