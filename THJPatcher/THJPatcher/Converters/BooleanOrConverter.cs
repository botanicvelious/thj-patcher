using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace THJPatcher.Converters
{
    /// <summary>
    /// Converts multiple boolean values using OR logic
    /// </summary>
    public class BooleanOrConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return values.OfType<bool>().Any(b => b);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 