using System.Globalization;
using System.Windows.Data;

namespace RRDA.RepImp
{
    [ValueConversion(typeof(long), typeof(string))]
    public class FileSizeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || value is not long bytes) return "0 B";

            string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
            int counter = 0;
            double number = (double)bytes;

            while (number >= 1024 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }

            return string.Format("{0:0.##} {1}", number, suffixes[counter]);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
