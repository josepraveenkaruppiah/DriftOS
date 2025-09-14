using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using DriftOS.Core.Settings;
using Serilog;

namespace DriftOS.App
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsVM _vm;

        public SettingsWindow()
        {
            InitializeComponent();

            // Build VM from current app settings
            _vm = new SettingsVM();
            DataContext = _vm;
        }

        // Save button (XAML: Click="OnSave")
        private void OnSave(object sender, RoutedEventArgs e)
        {
            try
            {
                // Write VM -> model
                _vm.ApplyToModel(App.Settings);

                // Persist
                App.SettingsStore.Save(App.Settings);

                // Side effects
                try { AutoStart.Apply(App.Settings.AutoStart); } catch { /* best-effort */ }

                Log.Information("Settings saved");
                System.Windows.MessageBox.Show($"Saved to: {JsonSettingsStore.ConfigPath}", "Settings",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save settings");
                System.Windows.MessageBox.Show(ex.ToString(), "Save failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Close/Cancel (XAML: Click="OnClose")
        private void OnClose(object sender, RoutedEventArgs e) => Close();

        // Restore Defaults (XAML: Click="OnRestoreDefaults")
        private void OnRestoreDefaults(object sender, RoutedEventArgs e)
        {
            // Use model's constructor defaults so we stay in sync with codebase
            var def = new SettingsModel();

            // Core motion
            _vm.PointerSpeed = def.PointerSpeed;
            _vm.ScrollSpeedV = def.ScrollSpeedV;
            _vm.ScrollSpeedH = def.ScrollSpeedH;

            _vm.InvertScrollV = def.InvertScrollV;
            _vm.InvertScrollH = def.InvertScrollH;

            _vm.Deadzone = def.Deadzone;
            _vm.PointerAlpha = def.PointerAlpha;
            _vm.ScrollAlpha = def.ScrollAlpha;
            _vm.ScrollGamma = def.ScrollGamma;

            // Game-aware
            _vm.PauseInFullscreenApps = def.PauseInFullscreenApps;
            _vm.BlockedProcesses = def.BlockedProcesses ?? "";

            // Startup
            _vm.AutoStart = def.AutoStart;

            System.Windows.MessageBox.Show("Defaults restored (not saved yet). Click Save to persist.",
                "Defaults", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ---------------- VM ----------------
        public class SettingsVM : INotifyPropertyChanged
        {
            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

            private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
            {
                if (Equals(field, value)) return false;
                field = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
                return true;
            }

            public SettingsVM()
            {
                var s = App.Settings;

                _pointerSpeed = s.PointerSpeed;
                _scrollSpeedV = s.ScrollSpeedV;
                _scrollSpeedH = s.ScrollSpeedH;

                _invertScrollV = s.InvertScrollV;
                _invertScrollH = s.InvertScrollH;

                _deadzone = s.Deadzone;
                _pointerAlpha = s.PointerAlpha;
                _scrollAlpha = s.ScrollAlpha;
                _scrollGamma = s.ScrollGamma;

                _pauseInFullscreenApps = s.PauseInFullscreenApps;
                _blockedProcesses = s.BlockedProcesses ?? "";

                _autoStart = s.AutoStart;
            }

            // ----- Properties bound in XAML -----

            // Core motion
            private double _pointerSpeed;
            public double PointerSpeed
            {
                get => _pointerSpeed;
                set { if (Set(ref _pointerSpeed, value)) { OnPropertyChanged(nameof(DisplayScrollSpeedV)); OnPropertyChanged(nameof(DisplayScrollSpeedH)); } }
            }

            private double _scrollSpeedV;
            public double ScrollSpeedV
            {
                get => _scrollSpeedV;
                set { if (Set(ref _scrollSpeedV, value)) OnPropertyChanged(nameof(DisplayScrollSpeedV)); }
            }

            private double _scrollSpeedH;
            public double ScrollSpeedH
            {
                get => _scrollSpeedH;
                set { if (Set(ref _scrollSpeedH, value)) OnPropertyChanged(nameof(DisplayScrollSpeedH)); }
            }

            private bool _invertScrollV;
            public bool InvertScrollV
            {
                get => _invertScrollV;
                set => Set(ref _invertScrollV, value);
            }

            private bool _invertScrollH;
            public bool InvertScrollH
            {
                get => _invertScrollH;
                set => Set(ref _invertScrollH, value);
            }

            private double _deadzone;
            public double Deadzone
            {
                get => _deadzone;
                set => Set(ref _deadzone, value);
            }

            private double _pointerAlpha;
            public double PointerAlpha
            {
                get => _pointerAlpha;
                set => Set(ref _pointerAlpha, value);
            }

            private double _scrollAlpha;
            public double ScrollAlpha
            {
                get => _scrollAlpha;
                set => Set(ref _scrollAlpha, value);
            }

            private double _scrollGamma;
            public double ScrollGamma
            {
                get => _scrollGamma;
                set => Set(ref _scrollGamma, value);
            }

            // Game-aware auto-pause
            private bool _pauseInFullscreenApps;
            public bool PauseInFullscreenApps
            {
                get => _pauseInFullscreenApps;
                set => Set(ref _pauseInFullscreenApps, value);
            }

            private string _blockedProcesses = "";
            public string BlockedProcesses
            {
                get => _blockedProcesses;
                set => Set(ref _blockedProcesses, value ?? "");
            }

            // Startup
            private bool _autoStart;
            public bool AutoStart
            {
                get => _autoStart;
                set => Set(ref _autoStart, value);
            }

            // Readouts used by XAML labels
            public string DisplayScrollSpeedV => FormatSigned(ScrollSpeedV);
            public string DisplayScrollSpeedH => FormatSigned(ScrollSpeedH);

            private static string FormatSigned(double v)
            {
                return (v >= 0 ? "+" : "") + v.ToString("0.00") + "x";
            }

            // Push VM -> SettingsModel
            public void ApplyToModel(SettingsModel target)
            {
                target.PointerSpeed = PointerSpeed;
                target.ScrollSpeedV = ScrollSpeedV;
                target.ScrollSpeedH = ScrollSpeedH;

                target.InvertScrollV = InvertScrollV;
                target.InvertScrollH = InvertScrollH;

                target.Deadzone = Deadzone;
                target.PointerAlpha = PointerAlpha;
                target.ScrollAlpha = ScrollAlpha;
                target.ScrollGamma = ScrollGamma;

                target.PauseInFullscreenApps = PauseInFullscreenApps;
                target.BlockedProcesses = BlockedProcesses ?? "";

                target.AutoStart = AutoStart;
            }
        }
    }
}
