using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WuziqiAi.Models;

namespace WuziqiAi.Converters;

public class StateToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CellState state)
        {
            return state switch
            {
                CellState.Black => Brushes.Black,
                CellState.White => Brushes.White,
                _ => Brushes.Transparent
            };
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class StateToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CellState state)
        {
            return state == CellState.Empty ? 0.0 : 1.0;
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class LastMoveToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
