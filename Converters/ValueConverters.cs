using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace pdf_to_Tiles.Converters
{
    public class NullToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; } = false;

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isNotNull = value != null;
            if (value is int intVal)
            {
                isNotNull = intVal > 0;
            }

            bool isVisible = Invert ? !isNotNull : isNotNull;
            return isVisible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
