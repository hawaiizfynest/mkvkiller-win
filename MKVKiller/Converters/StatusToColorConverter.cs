using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using MKVKiller.Models;

namespace MKVKiller.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is JobStatus s)
        {
            return s switch
            {
                JobStatus.Running => new SolidColorBrush(Color.FromRgb(43, 61, 110)),
                JobStatus.Done => new SolidColorBrush(Color.FromRgb(30, 74, 58)),
                JobStatus.Error => new SolidColorBrush(Color.FromRgb(94, 43, 43)),
                JobStatus.Interrupted => new SolidColorBrush(Color.FromRgb(94, 74, 43)),
                _ => new SolidColorBrush(Color.FromRgb(58, 58, 58))
            };
        }
        return Brushes.Gray;
    }
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public class StatusToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is JobStatus s)
        {
            return s switch
            {
                JobStatus.Running => new SolidColorBrush(Color.FromRgb(156, 181, 255)),
                JobStatus.Done => new SolidColorBrush(Color.FromRgb(156, 229, 193)),
                JobStatus.Error => new SolidColorBrush(Color.FromRgb(255, 161, 161)),
                JobStatus.Interrupted => new SolidColorBrush(Color.FromRgb(255, 217, 161)),
                _ => new SolidColorBrush(Color.FromRgb(204, 204, 204))
            };
        }
        return Brushes.Gray;
    }
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}
