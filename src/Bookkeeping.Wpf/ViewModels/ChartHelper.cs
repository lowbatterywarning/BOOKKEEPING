using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;

namespace Bookkeeping.Wpf.ViewModels;

/// <summary>
/// Shared helper for building the revenue-vs-expenses bar chart used across
/// dashboard tiles and the program detail window.
/// </summary>
internal static class ChartHelper
{
    /// <summary>
    /// Creates a PlotModel with two vertical bars (green Revenue, red Expenses).
    /// </summary>
    /// <param name="revenue">Revenue amount.</param>
    /// <param name="expenses">Expenses amount.</param>
    /// <param name="title">Optional chart title (null for compact tile view).</param>
    /// <param name="barWidth">Width of each bar in pixels.</param>
    /// <param name="categoryFontSize">Font size for the category axis labels.</param>
    /// <param name="valueAxisFormat">Format string for the value axis (e.g. "#,##0" or "C0").</param>
    /// <param name="plotMargins">Custom plot margins (uses compact defaults if null).</param>
    public static PlotModel CreateRevenueExpenseChart(
        decimal revenue,
        decimal expenses,
        string? title = null,
        double barWidth = 40,
        double categoryFontSize = 11,
        string valueAxisFormat = "#,##0",
        OxyThickness? plotMargins = null)
    {
        var model = new PlotModel
        {
            PlotAreaBorderThickness = new OxyThickness(0),
            PlotMargins = plotMargins ?? new OxyThickness(20, 5, 10, 25),
            TextColor = OxyColor.FromRgb(0x33, 0x33, 0x33),
        };

        if (!string.IsNullOrEmpty(title))
        {
            model.Title = title;
            model.TitleFontSize = 14;
            model.TitleFontWeight = 600;
            model.TitleColor = OxyColor.FromRgb(0x2c, 0x3e, 0x50);
        }

        // Category axis (labels under bars)
        model.Axes.Add(new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            Key = "Category",
            ItemsSource = new[] { "Revenue", "Expenses" },
            TicklineColor = OxyColors.Transparent,
            AxislineColor = OxyColor.FromRgb(0xDD, 0xDD, 0xDD),
            FontSize = categoryFontSize,
        });

        // Value axis (left)
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = 0,
            MinimumPadding = 0.05,
            MaximumPadding = 0.15,
            AxislineColor = OxyColors.Transparent,
            TicklineColor = OxyColor.FromRgb(0xDD, 0xDD, 0xDD),
            MajorGridlineStyle = LineStyle.Dash,
            MajorGridlineColor = OxyColor.FromRgb(0xEE, 0xEE, 0xEE),
            MinorGridlineStyle = LineStyle.None,
            FontSize = 10,
            StringFormat = valueAxisFormat,
        });

        // Revenue bar (green)
        model.Series.Add(new LinearBarSeries
        {
            Points = { new DataPoint(0, Convert.ToDouble(revenue)) },
            FillColor = OxyColor.FromRgb(0x27, 0xAE, 0x60),
            StrokeColor = OxyColor.FromRgb(0x1E, 0x8E, 0x4F),
            StrokeThickness = 1,
            BarWidth = barWidth,
        });

        // Expenses bar (red)
        model.Series.Add(new LinearBarSeries
        {
            Points = { new DataPoint(1, Convert.ToDouble(expenses)) },
            FillColor = OxyColor.FromRgb(0xC0, 0x39, 0x2B),
            StrokeColor = OxyColor.FromRgb(0xA0, 0x2D, 0x22),
            StrokeThickness = 1,
            BarWidth = barWidth,
        });

        return model;
    }

    /// <summary>
    /// Creates a per-program donation chart with one green bar per program.
    /// Used for sponsor tiles and sponsor detail windows.
    /// </summary>
    /// <param name="programDonations">List of (program name, donation amount) tuples.</param>
    /// <param name="title">Optional chart title.</param>
    /// <param name="barWidth">Width of each bar.</param>
    /// <param name="compactLabels">If true, truncate program names to 3 chars for tile display.</param>
    /// <param name="plotMargins">Custom plot margins.</param>
    public static PlotModel CreatePerProgramDonationChart(
        List<(string ProgramName, decimal Amount)> programDonations,
        string? title = null,
        double barWidth = 40,
        bool compactLabels = false,
        OxyThickness? plotMargins = null)
    {
        var model = new PlotModel
        {
            PlotAreaBorderThickness = new OxyThickness(0),
            PlotMargins = plotMargins ?? new OxyThickness(20, 5, 10, 25),
            TextColor = OxyColor.FromRgb(0x33, 0x33, 0x33),
        };

        if (!string.IsNullOrEmpty(title))
        {
            model.Title = title;
            model.TitleFontSize = 14;
            model.TitleFontWeight = 600;
            model.TitleColor = OxyColor.FromRgb(0x2c, 0x3e, 0x50);
        }

        // Truncate program names for compact tile display
        var labels = compactLabels
            ? programDonations
                .Select(p => p.ProgramName.Length > 3
                    ? p.ProgramName[..3].ToUpperInvariant()
                    : p.ProgramName.ToUpperInvariant())
                .ToArray()
            : programDonations
                .Select(p => p.ProgramName)
                .ToArray();

        // If no programs, show a placeholder
        if (labels.Length == 0)
        {
            labels = new[] { "—" };
            programDonations = new List<(string, decimal)> { ("—", 0) };
        }

        model.Axes.Add(new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            Key = "Category",
            ItemsSource = labels,
            TicklineColor = OxyColors.Transparent,
            AxislineColor = OxyColor.FromRgb(0xDD, 0xDD, 0xDD),
            FontSize = 10,
            Angle = 0,
        });

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Minimum = 0,
            MinimumPadding = 0.05,
            MaximumPadding = 0.15,
            AxislineColor = OxyColors.Transparent,
            TicklineColor = OxyColor.FromRgb(0xDD, 0xDD, 0xDD),
            MajorGridlineStyle = LineStyle.Dash,
            MajorGridlineColor = OxyColor.FromRgb(0xEE, 0xEE, 0xEE),
            MinorGridlineStyle = LineStyle.None,
            FontSize = 10,
            StringFormat = "#,##0",
        });

        var series = new LinearBarSeries
        {
            FillColor = OxyColor.FromRgb(0x27, 0xAE, 0x60),
            StrokeColor = OxyColor.FromRgb(0x1E, 0x8E, 0x4F),
            StrokeThickness = 1,
            BarWidth = barWidth,
        };

        for (int i = 0; i < programDonations.Count; i++)
        {
            series.Points.Add(new DataPoint(i, Convert.ToDouble(programDonations[i].Amount)));
        }

        model.Series.Add(series);

        return model;
    }
}
