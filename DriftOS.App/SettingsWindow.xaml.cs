using System;
using System.ComponentModel;
using System.Globalization;
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
            var s = App.Settings;

            // Migrate legacy → v2 if needed
            if (s.PointerSpeed <= 0) s.PointerSpeed = s.Sensitivity > 0 ? s.Sensitivity : 1.0;
            if (s.ScrollSpeedV <= 0) s.ScrollSpeedV = s.PointerSpeed;
            if (s.ScrollSpeedH <= 0) s.ScrollSpeedH = s.PointerSpeed;
            if (s.PointerAlpha <= 0) s.PointerAlpha = 0.35;
            if (s.ScrollAlpha <= 0) s.ScrollAlpha = 0.50;
            if (s.ScrollGamma <= 0) s.ScrollGamma = 1.60;

            _vm = new SettingsVM(s.PointerSpeed, s.ScrollSpeedV, s.ScrollSpeedH, s.InvertScrollV, s.InvertScrollH,
                                 s.Deadzone, s.PointerAlpha, s.ScrollAlpha, s.ScrollGamma);
            _vm.PropertyChanged += (_, __) =>
            {
                // LIVE apply to app settings
                s.PointerSpeed = _vm.PointerSpeed;
                s.ScrollSpeedV = _vm.ScrollSpeedV;
                s.ScrollSpeedH = _vm.ScrollSpeedH;
                s.InvertScrollV = _vm.InvertScrollV;
                s.InvertScrollH = _vm.InvertScrollH;
                s.Deadzone = _vm.Deadzone;
                s.PointerAlpha = _vm.PointerAlpha;
                s.ScrollAlpha = _vm.ScrollAlpha;
                s.ScrollGamma = _vm.ScrollGamma;
            };
            DataContext = _vm;
        }

        private void OnRestoreDefaults(object sender, RoutedEventArgs e)
        {
            _vm.PointerSpeed = 1.00;
            _vm.ScrollSpeedV = 1.00;
            _vm.ScrollSpeedH = 1.00;
            _vm.InvertScrollV = false;
            _vm.InvertScrollH = false;
            _vm.Deadzone = 0.12;
            _vm.PointerAlpha = 0.35;
            _vm.ScrollAlpha = 0.50;
            _vm.ScrollGamma = 1.60;
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            try
            {
                App.SettingsStore.Save(App.Settings);
                AutoStart.Apply(App.Settings.AutoStart);
                var path = JsonSettingsStore.ConfigPath;
                Log.Information("Settings saved to {Path}", path);
                System.Windows.MessageBox.Show($"Saved to:\n{path}", "DriftOS",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to save settings:\n{ex.Message}", "DriftOS",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();
    }

    internal sealed class SettingsVM : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private double _pointerSpeed, _scrollV, _scrollH, _deadzone, _pAlpha, _sAlpha, _sGamma;
        private bool _invV, _invH;

        public double PointerSpeed
        {
            get => _pointerSpeed;
            set { if (Math.Abs(_pointerSpeed - value) > 0.0001) { _pointerSpeed = value; OnPropertyChanged(); } }
        }

        public double ScrollSpeedV
        {
            get => _scrollV;
            set
            {
                if (Math.Abs(_scrollV - value) > 0.0001)
                {
                    _scrollV = value; OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayScrollSpeedV));
                }
            }
        }

        public double ScrollSpeedH
        {
            get => _scrollH;
            set
            {
                if (Math.Abs(_scrollH - value) > 0.0001)
                {
                    _scrollH = value; OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayScrollSpeedH));
                }
            }
        }

        public bool InvertScrollV
        {
            get => _invV;
            set { if (_invV != value) { _invV = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayScrollSpeedV)); } }
        }

        public bool InvertScrollH
        {
            get => _invH;
            set { if (_invH != value) { _invH = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayScrollSpeedH)); } }
        }

        public double Deadzone
        {
            get => _deadzone;
            set { if (Math.Abs(_deadzone - value) > 0.0001) { _deadzone = value; OnPropertyChanged(); } }
        }

        public double PointerAlpha
        {
            get => _pAlpha;
            set { if (Math.Abs(_pAlpha - value) > 0.0001) { _pAlpha = value; OnPropertyChanged(); } }
        }

        public double ScrollAlpha
        {
            get => _sAlpha;
            set { if (Math.Abs(_sAlpha - value) > 0.0001) { _sAlpha = value; OnPropertyChanged(); } }
        }

        public double ScrollGamma
        {
            get => _sGamma;
            set { if (Math.Abs(_sGamma - value) > 0.0001) { _sGamma = value; OnPropertyChanged(); } }
        }

        // ---- Computed display strings (bullet-proof, culture aware) ----
        private static readonly CultureInfo Culture = CultureInfo.CurrentCulture;
        public string DisplayScrollSpeedV => (InvertScrollV ? -ScrollSpeedV : ScrollSpeedV).ToString("F2", Culture);
        public string DisplayScrollSpeedH => (InvertScrollH ? -ScrollSpeedH : ScrollSpeedH).ToString("F2", Culture);

        public SettingsVM(double pointerSpeed, double scrollV, double scrollH, bool invV, bool invH,
                          double deadzone, double pAlpha, double sAlpha, double sGamma)
        {
            _pointerSpeed = pointerSpeed;
            _scrollV = scrollV;
            _scrollH = scrollH;
            _invV = invV; _invH = invH;
            _deadzone = deadzone;
            _pAlpha = pAlpha; _sAlpha = sAlpha; _sGamma = sGamma;
        }
    }
}
