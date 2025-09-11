using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Serilog;

namespace DriftOS.App
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsVM _vm;

        public SettingsWindow()
        {
            InitializeComponent();
            _vm = new SettingsVM(App.Settings.Sensitivity, App.Settings.Deadzone);
            _vm.PropertyChanged += (_, __) =>
            {
                // Live apply so you can feel changes instantly
                App.Settings.Sensitivity = _vm.Sensitivity;
                App.Settings.Deadzone = _vm.Deadzone;
            };
            DataContext = _vm;
        }

        private void OnRestoreDefaults(object sender, RoutedEventArgs e)
        {
            _vm.Sensitivity = 1.00;
            _vm.Deadzone = 0.12; // a sensible default; tweak if your model uses a different default
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            try
            {
                // Persist to disk via your existing store
                App.SettingsStore.Save(App.Settings);
                Log.Information("Settings saved. Sens={Sens} DZ={DZ}", App.Settings.Sensitivity, App.Settings.Deadzone);
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
        private double _sensitivity;
        private double _deadzone;

        public double Sensitivity
        {
            get => _sensitivity;
            set { if (Math.Abs(_sensitivity - value) > 0.0001) { _sensitivity = value; OnPropertyChanged(); } }
        }

        public double Deadzone
        {
            get => _deadzone;
            set { if (Math.Abs(_deadzone - value) > 0.0001) { _deadzone = value; OnPropertyChanged(); } }
        }

        public SettingsVM(double sensitivity, double deadzone)
        {
            _sensitivity = sensitivity;
            _deadzone = deadzone;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
