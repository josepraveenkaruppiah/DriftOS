namespace DriftOS.Core.Settings
{
    public sealed class SettingsModel
    {
        // Legacy (kept for backward compat / migration)
        public double Sensitivity { get; set; } = 1.0;

        // Core
        public double Deadzone { get; set; } = 0.12;
        public bool AutoStart { get; set; } = false;


        // New — v2
        public double PointerSpeed { get; set; } = 1.0; // scales cursor pixels/sec
        public double ScrollSpeedV { get; set; } = 1.0; // notches/sec @ full deflection
        public double ScrollSpeedH { get; set; } = 1.0;

        public bool InvertScrollV { get; set; } = false;
        public bool InvertScrollH { get; set; } = false;

        // Smoothing / shaping
        public double PointerAlpha { get; set; } = 0.35; // 0..1 (EMA)
        public double ScrollAlpha { get; set; } = 0.50; // 0..1
        public double ScrollGamma { get; set; } = 1.60; // 1..2.5 (shallower → stronger mid-stick)
    }
}
