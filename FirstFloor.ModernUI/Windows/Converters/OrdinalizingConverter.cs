using System;
using System.Globalization;
using System.Windows.Data;
using FirstFloor.ModernUI.Helpers;
using FirstFloor.ModernUI.Serialization;

namespace FirstFloor.ModernUI.Windows.Converters {
    [ValueConversion(typeof(int), typeof(string))]
    public class OrdinalizingConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            return value.As<int>().ToOrdinal(parameter as string, culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }

    [ValueConversion(typeof(int), typeof(string))]
    public class OrdinalizingShortConverter : IValueConverter {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
            return value.As<int>().ToOrdinalShort(parameter as string, culture);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
            throw new NotSupportedException();
        }
    }
}