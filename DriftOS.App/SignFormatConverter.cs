using System;
using System.Globalization;
using System.Windows.Data;

namespace DriftOS.App
{
    // Combines (invert flag, magnitude) -> "signed string with 2 decimals"
    public sealed class SignFormatConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = values.Length > 0 && values[0] is bool b && b;
            double mag = values.Length > 1 && values[1] is double d ? d : 0.0;
            double signed = invert ? -mag : mag;
            return signed.ToString("F2", culture);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => new object[] { System.Windows.Data.Binding.DoNothing, System.Windows.Data.Binding.DoNothing };
    }
}
