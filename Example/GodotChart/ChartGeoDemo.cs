using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// Geographic page: one map file read at runtime and drawn twice - a choropleth (a value per region) and the
/// same geometry as the outline-only base map a data layer is laid on top of.
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
    }

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
            var plot = new PlotArea(
                chart.PaddingLeft,
                chart.PaddingTop,
                Mathf.Max(1f, chart.Width - chart.PaddingLeft - chart.PaddingRight),
                Mathf.Max(1f, chart.Height - chart.PaddingTop - chart.PaddingBottom));
            chart.FitGeoBounds(_bounds.MinX, _bounds.MinY, _bounds.MaxX, _bounds.MaxY, plot, 0.06f);
            if (!shade) return;
            // A value per region reads as a ramp, not as one category colour per shade: the colour scale is
            // the host's call, which is what makes "area" and "choropleth" one mark in this library.
            chart.Scale(Channel.Color, new SequentialColorScale(0.0, 100.0));
        };
    }

    /// <summary>The issues the reader recorded, for the page's own diagnostics (empty for this file).</summary>
    public IReadOnlyList<string> Issues => _issues;
}
