namespace GodotNodeExtension.Tests.GodotChart.Support;

using System.Collections.Generic;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;

/// <summary>
/// Builds <see cref="MarkContext"/> / <see cref="RenderContext"/> instances for tests so that
/// individual marks can be rendered without a Chart, a backend or a scene tree.
/// </summary>
public static class TestContexts
{
    /// <summary>Default plot area used by mark tests: 400 x 300 at the origin.</summary>
    public static PlotArea DefaultPlot => new(0, 0, 400, 300);

    /// <summary>
    /// The map view a chart hands a geographic mark: a WGS84 view centred on Europe, wide enough that the
    /// cases' coordinates land inside the plot. Marks that are not geographic ignore it, and a case that needs
    /// its own view passes one.
    /// </summary>
    public static GeoViewport DefaultGeoViewport { get; } = CreateDefaultGeoViewport();

    private static GeoViewport CreateDefaultGeoViewport()
    {
        var viewport = new GeoViewport(GeoFrames.Wgs84());
        viewport.SetView(5.0, 48.0, 3.0);
        return viewport;
    }

    /// <summary>
    /// Create a mark context for the given mark data.
    /// <paramref name="theme"/> stays null by default so tests do not depend on theme values
    /// (marks fall back to their own defaults).
    /// </summary>
    public static MarkContext Mark(
        ICanvas2D canvas,
        List<DataRow> data,
        EncodeSet encodes,
        ScaleSet scales,
        ChartTheme? theme = null,
        PlotArea? plot = null,
        GeoViewport? geoViewport = null,
        int hoveredRowIndex = -1,
        int dataVersion = 1,
        int layoutVersion = 1,
        AnimationContext? animation = null,
        int selectedRowIndex = -1,
        string? focusedSeries = null,
        IReadOnlySet<string>? hiddenSeries = null)
        => new()
        {
            Canvas = canvas,
            Plot = plot ?? DefaultPlot,
            Mapper = new PlanarMapper { Plot = plot ?? DefaultPlot },
            GeoViewport = geoViewport ?? DefaultGeoViewport,
            Scales = scales,
            Encodes = encodes,
            Data = data,
            DataVersion = dataVersion,
            LayoutVersion = layoutVersion,
            Theme = theme,
            HoveredRowIndex = hoveredRowIndex,
            Animation = animation ?? AnimationContext.Default,
            AnimationProgress = animation?.EntryProgress ?? 1f,
            SelectedRowIndex = selectedRowIndex,
            FocusedSeries = focusedSeries,
            HiddenSeries = hiddenSeries,
        };

    /// <summary>EncodeSet mapping X/Y to the given field names.</summary>
    public static EncodeSet XyEncodes(string xField, string yField, Channel yChannel = Channel.Y)
    {
        var encodes = new EncodeSet();
        encodes.Set(Channel.X, new FieldEncode(xField));
        encodes.Set(yChannel, new FieldEncode(yField));
        return encodes;
    }

    /// <summary>
    /// Scales for category charts: an ordinal X fed with <paramref name="categories"/> and a
    /// linear Y covering [<paramref name="yMin"/>, <paramref name="yMax"/>].
    /// </summary>
    public static ScaleSet CategoryScales(
        IEnumerable<string> categories,
        double yMin = 0,
        double yMax = 100,
        Channel yChannel = Channel.Y)
    {
        var xScale = new OrdinalScale();
        xScale.Fit(categories);

        var scales = new ScaleSet();
        scales.Set(Channel.X, xScale);
        scales.Set(yChannel, new LinearScale(yMin, yMax));
        return scales;
    }

    /// <summary>Data row helper: <c>Row(("cat", "A"), ("value", 10))</c>.</summary>
    public static DataRow Row(params (string Field, object? Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields)
            row.Set(field, value!); // null is stored as null on purpose (missing value semantics)
        return row;
    }
}
