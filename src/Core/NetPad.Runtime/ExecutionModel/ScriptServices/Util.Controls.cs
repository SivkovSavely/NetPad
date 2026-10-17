using NetPad.Presentation.Controls;

namespace NetPad.ExecutionModel.ScriptServices;

public static partial class Util
{
    /// <summary>Creates a live chart from labeled data points.</summary>
    public static ChartControl Chart(IEnumerable<ChartDataPoint> data, ChartType type = ChartType.Line)
        => new(data, type);

    /// <summary>Creates a chart from a sequence of numeric values.</summary>
    public static ChartControl Chart(IEnumerable<double> values, ChartType type = ChartType.Line)
        => new(values, type);

    /// <summary>Creates a chart from an object sequence and a numeric value selector.</summary>
    public static ChartControl Chart<T>(
        IEnumerable<T> source,
        Func<T, double> valueSelector,
        ChartType type = ChartType.Line,
        Func<T, string?>? labelSelector = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(valueSelector);

        return new ChartControl(source.Select((item, index) => new ChartDataPoint(
            labelSelector?.Invoke(item) ?? index.ToString(), index, valueSelector(item))), type);
    }

    /// <summary>Creates a scatter chart from an object sequence and x/y selectors.</summary>
    public static ChartControl Chart<T>(
        IEnumerable<T> source,
        Func<T, double> xSelector,
        Func<T, double> ySelector,
        ChartType type = ChartType.Scatter,
        Func<T, string?>? labelSelector = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(xSelector);
        ArgumentNullException.ThrowIfNull(ySelector);

        return new ChartControl(source.Select((item, index) => new ChartDataPoint(
            labelSelector?.Invoke(item) ?? index.ToString(), xSelector(item), ySelector(item))), type);
    }
}
