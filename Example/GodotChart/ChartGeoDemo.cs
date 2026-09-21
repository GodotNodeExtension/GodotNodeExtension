using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// Geographic page: one map file read at runtime and drawn three ways - a choropleth (a value per region), the
/// same geometry as an outline-only base map, and the two-stage picture that design is for: that base map with
/// a bubble layer on top of it.
/// <para>
/// The file is <c>Example/GodotChart/Assets/geo-regions.geojson</c>, read with
/// <see cref="GeoJsonReader.ParseFile(string, ICollection{string})"/> and never fetched from the network.
/// The provinces carry only a <c>name</c>; the values are a small table in this script, and the mark joins the
/// two by that name - which is the whole point of <see cref="GeoDataJoiner"/>: the map says where the regions
/// are, the table says what they are worth.
/// </para>
/// <para>
/// Both cells are the <b>same</b> mark over the <b>same</b> features. The difference is one switch:
/// <see cref="GeoAreaMark.Shade"/> is true for the choropleth (each region filled with its row's colour) and
/// false for the base map (outlines only), which is how the two-stage picture of a geographic chart is built.
/// A colour scale is installed by hand - <see cref="SequentialColorScale"/> - because a value should read as a
/// ramp rather than as a category per shade.
/// </para>
/// </summary>
public partial class ChartGeoDemo : Control
{
    /// <summary>Regions shaded by their value.</summary>
    [Export] public ChartView Choropleth { get; set; } = null!;

    /// <summary>The same regions as an outline-only base map.</summary>
    [Export] public ChartView BaseMap { get; set; } = null!;

    /// <summary>Base map and bubble layer in one chart - the two-stage picture.</summary>
    [Export] public ChartView TwoStage { get; set; } = null!;

    /// <summary>The map file this page reads.</summary>
    private const string MapPath = "res://Example/GodotChart/Assets/geo-regions.geojson";

    /// <summary>
    /// Field the values are read with, and the join key on the map side (a province's <c>name</c> property).
    /// It is <c>category</c>, the field <see cref="ChartView.SetValues"/> writes.
    /// </summary>
    private const string JoinField = "category";

    private IReadOnlyList<GeoFeature> _regions = [];
    private GeoBounds _bounds;
    private readonly List<string> _issues = [];

    /// <summary>
    /// Read the map, put the values in the same order, and hand both cells their geometry. The viewport is
    /// framed here rather than in the scene because the map's extent is only known once the file is read - and
    /// the plot rectangle to fit it into is only known once the chart exists, which is exactly what
    /// <see cref="ChartView.ConfigureChart"/> is for.
    /// </summary>
    public override void _Ready()
    {
        _regions = GeoJsonReader.ParseFile(MapPath, _issues);
        if (_regions.Count == 0 || GeoMath.BoundsOf(_regions) is not { } bounds)
        {
            GD.PushWarning($"[ChartGeoDemo] no regions read from {MapPath}.");
            return;
        }
        _bounds = bounds;

        var values = RegionValues();
        Configure(Choropleth, values, shade: true);
        Configure(BaseMap, values, shade: false);
        ConfigureTwoStage(TwoStage);
    }

    /// <summary>
    /// The two-stage picture: the base map is the <b>view's own</b> mark (outlines, no colour channel bound), so
    /// the bubble layer added afterwards lands on top of it - the order the marks are added in is the order they
    /// are painted, and a chart draws no Cartesian axes for either of them.
    /// </summary>
    /// <remarks>
    /// The two layers read different tables: the regions come from the map file's names, the bubbles from the
    /// city table below. That is what a mark's own <see cref="Mark.Data"/> and its own encodes are for - one
    /// chart, two data sets, one coordinate frame.
    /// </remarks>
    private void ConfigureTwoStage(ChartView view)
    {
        view.SetValues(RegionValues()); // what a bubble layout would fall back on; the bubbles bring their own
        view.ConfigureMark(mark =>
        {
            if (mark is not GeoAreaMark area) return;
            area.Features = _regions;
            area.RowField = JoinField;
            area.Shade = false;         // the base map of this cell
        });
        view.ConfigureChart = chart =>
        {
            chart.SetGeoFrame(GeoFrames.Wgs84());
            chart.FitGeoBounds(_bounds.MinX, _bounds.MinY, _bounds.MaxX, _bounds.MaxY, EstimatedPlot(chart), 0.06f);

            var bubbles = new GeoBubbleMark { Data = CityRows(), MinRadius = 5f, MaxRadius = 20f };
            bubbles.Encode(Channel.X, "lon")
                   .Encode(Channel.Y, "lat")
                   .Encode(Channel.Size, "value")
                   .Encode(Channel.Color, "value");
            chart.Mark(bubbles);

            // The flow layer reads a third table: two coordinates per row, the value as the curve's width and
            // colour. Three layers, three tables, one frame.
            var flows = new GeoFlowMark { Data = RouteRows(), MinWidth = 1.5f, MaxWidth = 7f };
            flows.Encode(Channel.Size, "value").Encode(Channel.Color, "value");
            chart.Mark(flows);
        };
    }

    /// <summary>A handful of cities inside the mapped landmass, with the value that sizes their bubble.</summary>
    private static List<DataRow> CityRows()
    {
        var rows = new List<DataRow>
        {
            Row("Harbourwatch", 12.4, 47.2, 62.0),
            Row("Silverport", 8.1, 52.6, 88.0),
            Row("Oldkeep", 22.7, 44.9, 41.0),
            Row("Farrow", 30.5, 49.8, 73.0),
            Row("Valesend", 16.2, 55.1, 35.0),
            Row("Southgate", 26.9, 39.4, 54.0),
        };
        return rows;

        static DataRow Row(string city, double lon, double lat, double value)
            => new DataRow(4).Set("city", city).Set("lon", lon).Set("lat", lat).Set("value", value);
    }

    /// <summary>Routes between the cities above, as the flow layer's table.</summary>
    private static List<DataRow> RouteRows()
    {
        var rows = new List<DataRow>
        {
            Route("Harbourwatch", "Silverport", 64.0),
            Route("Silverport", "Valesend", 38.0),
            Route("Oldkeep", "Farrow", 52.0),
            Route("Farrow", "Southgate", 41.0),
            Route("Valesend", "Oldkeep", 27.0),
            Route("Southgate", "Harbourwatch", 33.0),
        };
        return rows;

        static DataRow Route(string from, string to, double value)
            => new DataRow(5)
                .Set("route", $"{from}-{to}")
                .Set("start_lon", Coordinate(from).Lon).Set("start_lat", Coordinate(from).Lat)
                .Set("end_lon", Coordinate(to).Lon).Set("end_lat", Coordinate(to).Lat)
                .Set("value", value);

        static (double Lon, double Lat) Coordinate(string city)
            => CityRows().Find(row => row.Get<string>("city") == city) is { } found
                ? (found.Get<double>("lon"), found.Get<double>("lat"))
                : (0.0, 0.0);
    }

    /// <summary>The plot rectangle a chart of this size lays out, near enough to frame a map from a page.</summary>
    private static PlotArea EstimatedPlot(Chart chart) => new(
        chart.PaddingLeft,
        chart.PaddingTop,
        Mathf.Max(1f, chart.Width - chart.PaddingLeft - chart.PaddingRight),
        Mathf.Max(1f, chart.Height - chart.PaddingTop - chart.PaddingBottom));

    /// <summary>The table the map is joined to: one value per province of the file.</summary>
    private static List<(string Category, double Value)> RegionValues() =>
    [
        ("Northmark", 62.0),
        ("Silverdale", 38.0),
        ("Eastreach", 74.0),
        ("Vale", 21.0),
        ("Midlands", 55.0),
        ("Suncoast", 88.0),
        ("Highfell", 47.0),
        ("Westport", 30.0),
    ];

    /// <summary>Wire one cell: rows, geometry, the frame and the view.</summary>
    private void Configure(ChartView view, List<(string Category, double Value)> values, bool shade)
    {
        view.SetValues(values);
        view.ConfigureMark(mark =>
        {
            if (mark is not GeoAreaMark area) return;
            area.Features = _regions;   // the geometry the rows are joined to
            area.RowField = JoinField;
            area.Shade = shade;
        });
        view.ConfigureChart = chart =>
        {
            // The grid the view builds when a geographic mark has no geometry of its own is not what this page
            // draws: the frame comes from the map file's coordinates.
            chart.SetGeoFrame(GeoFrames.Wgs84());
            chart.FitGeoBounds(_bounds.MinX, _bounds.MinY, _bounds.MaxX, _bounds.MaxY,
                               EstimatedPlot(chart), 0.06f);
            if (!shade) return;
            // A value per region reads as a ramp, not as one category colour per shade: the colour scale is
            // the host's call, which is what makes "area" and "choropleth" one mark in this library.
            chart.Scale(Channel.Color, new SequentialColorScale(0.0, 100.0));
        };
    }

    /// <summary>The issues the reader recorded, for the page's own diagnostics (empty for this file).</summary>
    public IReadOnlyList<string> Issues => _issues;
}
