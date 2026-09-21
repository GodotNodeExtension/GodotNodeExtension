namespace GodotNodeExtension.Tests.GodotChart.Support;

using System;
using System.Collections.Generic;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// The 20 mark configurations used by the smoke, snapshot and allocation tests.
/// Data, encode field names and optional colour channel are chosen so every mark draws something.
/// </summary>
public static class MarkCases
{
    /// <summary>One mark under test.</summary>
    /// <param name="Name">Mark name used in test messages and snapshot tables.</param>
    /// <param name="Create">Factory for a fresh mark instance.</param>
    /// <param name="Data">Rows the case draws.</param>
    /// <param name="XField">Field the X channel is bound to.</param>
    /// <param name="YField">Field the Y channel is bound to.</param>
    /// <param name="ColorField">Field the Color channel is bound to, when the mark uses one.</param>
    /// <param name="PerRowSkipContract">
    /// True for the marks whose per-row skip contract ("one broken row is skipped, the remaining rows
    /// keep drawing") is checked row by row in <c>MarkDataSafetyTest</c>. Declared here so the test can
    /// derive its list from the table instead of holding a second, silently shrinking copy.
    /// </param>
    /// <param name="DirtyFields">
    /// Fields a data-safety test may break for this mark, including the ones the value-only dirty path
    /// never reaches (a category field, the Color channel, the interval endpoints). Null means "only
    /// <see cref="YField"/> carries the value".
    /// </param>
    public sealed record MarkCase(
        string Name,
        Func<Mark> Create,
        List<DataRow> Data,
        string XField,
        string YField,
        string? ColorField = null,
        bool PerRowSkipContract = false,
        IReadOnlyList<DirtyField>? DirtyFields = null);

    /// <summary>
    /// One field of a <see cref="MarkCase"/> a data-safety test may break, and what breaking it means.
    /// </summary>
    /// <param name="Name">Field to break.</param>
    /// <param name="Numeric">
    /// True when the mark reads the field as a number (so "broken" means NaN/±Infinity); false when it
    /// reads it as a category (so "broken" means the value is missing/non-textual).
    /// </param>
    /// <param name="GuardsNonFinite">
    /// True when the mark is known to keep its geometry finite for a broken value of this field.
    /// <c>false</c> records a measured gap in the mark (Component/** is out of scope for the test
    /// suite), so the data-safety case still fails on a throw but does not pretend the geometry
    /// stays finite.
    /// </param>
    public sealed record DirtyField(string Name, bool Numeric, bool GuardsNonFinite = true);

    private static DataRow D(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields)
            row.Set(field, value);
        return row;
    }

    public static List<DataRow> Simple() =>
    [
        D(("cat", "A"), ("value", 10.0)),
        D(("cat", "B"), ("value", 20.0)),
        D(("cat", "C"), ("value", 15.0)),
    ];

    /// <summary>Milestones: an event per row, on a numeric axis with a lane and a label.</summary>
    public static List<DataRow> Events() =>
    [
        D(("day", 3.0),  ("lane", "Release"), ("label", "v1.0")),
        D(("day", 9.0),  ("lane", "Release"), ("label", "v1.1")),
        D(("day", 6.0),  ("lane", "Docs"),    ("label", "guide")),
        D(("day", 12.0), ("lane", "Infra"),   ("label", "CI")),
    ];

    public static List<DataRow> Points() =>
    [
        D(("x", 1.0), ("y", 5.0), ("size", 3.0)),
        D(("x", 2.0), ("y", 9.0), ("size", 6.0)),
        D(("x", 3.0), ("y", 7.0), ("size", 4.0)),
    ];

    public static List<DataRow> Series() =>
    [
        D(("cat", "A"), ("value", 10.0), ("series", "S1")),
        D(("cat", "B"), ("value", 20.0), ("series", "S1")),
        D(("cat", "C"), ("value", 15.0), ("series", "S1")),
        D(("cat", "A"), ("value", 8.0), ("series", "S2")),
        D(("cat", "B"), ("value", 14.0), ("series", "S2")),
        D(("cat", "C"), ("value", 18.0), ("series", "S2")),
    ];

    public static List<DataRow> Distribution()
    {
        var rows = new List<DataRow>();
        var values = new[] { 3.0, 5.0, 7.0, 9.0, 11.0 };
        foreach (var cat in new[] { "A", "B" })
            foreach (var v in values)
                rows.Add(D(("cat", cat), ("value", v)));
        return rows;
    }

    public static List<DataRow> Relations() =>
    [
        D(("source", "A"), ("target", "B"), ("value", 5.0)),
        D(("source", "B"), ("target", "C"), ("value", 3.0)),
        D(("source", "A"), ("target", "C"), ("value", 2.0)),
    ];

    public static List<DataRow> Hierarchy() =>
    [
        D(("label", "root"), ("parent", ""), ("value", 0.0)),
        D(("label", "child1"), ("parent", "root"), ("value", 6.0)),
        D(("label", "child2"), ("parent", "root"), ("value", 4.0)),
    ];

    public static List<DataRow> Intervals() =>
    [
        D(("cat", "A"), ("value", 1.0), ("start", 0.0), ("end", 3.0)),
        D(("cat", "B"), ("value", 2.0), ("start", 1.0), ("end", 4.0)),
    ];

    public static List<DataRow> Bands() =>
    [
        D(("cat", "A"), ("value", 12.0), ("lower", 8.0)),
        D(("cat", "B"), ("value", 22.0), ("lower", 16.0)),
    ];

    public static List<DataRow> Boxes() =>
    [
        D(("cat", "A"), ("min", 1.0), ("q1", 3.0), ("median", 5.0), ("q3", 7.0), ("max", 9.0)),
        D(("cat", "B"), ("min", 2.0), ("q1", 4.0), ("median", 6.0), ("q3", 8.0), ("max", 11.0)),
    ];

    public static List<DataRow> Candles() =>
    [
        D(("cat", "A"), ("open", 10.0), ("high", 15.0), ("low", 8.0), ("close", 12.0)),
        D(("cat", "B"), ("open", 12.0), ("high", 14.0), ("low", 9.0), ("close", 10.0)),
    ];

    public static List<DataRow> Matrix() =>
    [
        D(("x", "A"), ("y", "1"), ("value", 1.0)),
        D(("x", "B"), ("y", "1"), ("value", 2.0)),
        D(("x", "A"), ("y", "2"), ("value", 3.0)),
        D(("x", "B"), ("y", "2"), ("value", 4.0)),
    ];

    /// <summary>Many-point variants used by the allocation tests (same mark, bigger data set).</summary>
    public static List<DataRow> ManyBars(int count)
    {
        var rows = new List<DataRow>(count);
        for (int i = 0; i < count; i++)
            rows.Add(D(("cat", $"C{i}"), ("value", 10.0 + (i % 17))));
        return rows;
    }

    public static List<DataRow> ManyPoints(int count)
    {
        var rows = new List<DataRow>(count);
        for (int i = 0; i < count; i++)
            rows.Add(D(("x", (double)i), ("y", 5.0 + (i % 11)), ("size", 3.0 + (i % 4))));
        return rows;
    }

    public static List<DataRow> ManyCells(int size)
    {
        var rows = new List<DataRow>(size * size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                rows.Add(D(("x", $"c{x}"), ("y", $"r{y}"), ("value", (double)(x + y + 1))));
        return rows;
    }

    /// <summary>
    /// Every mark case. The marks whose "one broken row is skipped, the others keep drawing" contract
    /// is checked row by row opt in with <see cref="MarkCase.PerRowSkipContract"/>; the marks whose
    /// dirty-value fields are not just the value field declare them in <see cref="MarkCase.DirtyFields"/>.
    /// </summary>
    public static readonly MarkCase[] All =
    {
        new("IntervalMark",    () => new IntervalMark(),    Simple(),       "cat", "value",
            PerRowSkipContract: true),
        new("LineMark",        () => new LineMark(),        Simple(),       "cat", "value",
            PerRowSkipContract: true),
        new("PointMark",       () => new PointMark(),       Points(),       "x",   "y",
            PerRowSkipContract: true),
        new("PieMark",         () => new PieMark(),         Simple(),       "cat", "value"),
        new("RadarMark",       () => new RadarMark(),       Series(),       "cat", "value", "series"),
        new("ViolinMark",      () => new ViolinMark(),      Distribution(), "cat", "value",
            PerRowSkipContract: true),
        new("WaffleMark",      () => new WaffleMark(),      Simple(),       "cat", "value"),
        new("FunnelMark",      () => new FunnelMark(),      Simple(),       "cat", "value"),
        new("GaugeMark",       () => new GaugeMark(),       Simple(),       "cat", "value",
            PerRowSkipContract: true),
        new("SankeyMark",      () => new SankeyMark(),      Relations(),    "cat", "value"),
        new("ChordMark",       () => new ChordMark(),       Relations(),    "cat", "value"),
        new("SunburstMark",    () => new SunburstMark(),    Hierarchy(),    "label", "value"),
        new("TreemapMark",     () => new TreemapMark(),     Simple(),       "cat", "value"),
        // The bars span start..end, not the value channel, so those two fields are what has to be able
        // to go dirty. "GuardsNonFinite: false" records a measured gap: the mark maps a NaN endpoint
        // straight into the bar rect (TimelineMark.BarGeometry has no finiteness guard).
        new("TimelineMark",    () => new TimelineMark(),    Intervals(),    "cat", "value",
            DirtyFields:
            [
                new("start", Numeric: true, GuardsNonFinite: false),
                new("end",   Numeric: true, GuardsNonFinite: false),
            ]),
        new("LollipopMark",    () => new LollipopMark(),    Simple(),       "cat", "value",
            PerRowSkipContract: true),
        new("RangeAreaMark",   () => new RangeAreaMark(),   Bands(),        "cat", "value"),
        new("BoxMark",         () => new BoxMark(),         Boxes(),        "cat", "median",
            PerRowSkipContract: true),
        new("CandlestickMark", () => new CandlestickMark(), Candles(),      "cat", "close",
            PerRowSkipContract: true),
        // A heatmap reads two category fields plus the colour VALUE, which the value-only dirty path
        // ("Y") never reaches: Y here is the row category, not the number on the ramp.
        new("HeatmapMark",     () => new HeatmapMark(),     Matrix(),       "x",   "y", "value",
            DirtyFields:
            [
                new("x",     Numeric: false),
                new("y",     Numeric: false),
                new("value", Numeric: true),
            ]),
        // A milestone places events by the numeric X field and lanes by the category field; both the
        // value (Y = "lane") and the position ("day") can go dirty.
        new("MilestoneMark",   () => new MilestoneMark(),   Events(),       "day", "lane",
            DirtyFields:
            [
                new("day",  Numeric: true),
                new("lane", Numeric: false),
            ]),
        // An annotation mark: what it draws are the levels (and the band), not the rows, so the data only
        // has to give the scales something to fit. The levels sit inside that fitted range - a level outside
        // the visible window is skipped by design (SectionMark.TryMap), and the case would draw nothing.
        new("SectionMark",     () => new SectionMark { Levels = [12d, 18d] }, Simple(), "cat", "value"),
        // A geographic mark draws the geometry it was handed, not the rows: the rows are joined to the
        // features by the value of their category field (the three lettered areas below), and the value
        // column shades them. "value" therefore feeds the colour channel, not a position.
        // The bubble layer places the rows themselves: the position channel carries the coordinate, the
        // size channel the magnitude, and a row with a broken coordinate is skipped without taking the rest
        // of the layer down.
        new("GeoBubbleMark",   () => new GeoBubbleMark(),   Cities(),       "lon", "lat", "value",
            PerRowSkipContract: true,
            DirtyFields:
            [
                new("lon",   Numeric: true),
                new("lat",   Numeric: true),
                new("value", Numeric: true),
            ]),
        new("GeoAreaMark",     () => new GeoAreaMark { Features = LetteredAreas() },
            Simple(), "cat", "value", "value",
            DirtyFields:
            [
                new("cat",   Numeric: false),
                new("value", Numeric: true),
            ]),
    };

    /// <summary>Three cities as longitude/latitude pairs with a magnitude - the bubble layer's table.</summary>
    public static List<DataRow> Cities() =>
    [
        D(("lon", 8.5), ("lat", 47.4), ("value", 30.0)),
        D(("lon", 2.3), ("lat", 48.9), ("value", 60.0)),
        D(("lon", 13.4), ("lat", 52.5), ("value", 45.0)),
    ];

    /// <summary>
    /// Three lettered square regions in the frame's own units, sized so they are visible in the default
    /// (whole world) view: the categories of <see cref="Simple"/> are what joins the table to them.
    /// </summary>
    public static List<GeoFeature> LetteredAreas()
    {
        var builder = new GeoGeometryBuilder();
        return
        [
            builder.Polygon((0, 0), (40, 0), (40, 40), (0, 40)).Feature("A", "A"),
            builder.Polygon((60, 0), (100, 0), (100, 40), (60, 40)).Feature("B", "B"),
            builder.Polygon((0, 60), (40, 60), (40, 100), (0, 100)).Feature("C", "C"),
        ];
    }

    /// <summary>Build a chart around one case, using the shared fake canvas.</summary>
    public static Chart Build(FakeCanvas2D canvas, MarkCase c, List<DataRow>? data = null,
                              float width = 400f, float height = 300f)
    {
        var chart = new Chart(canvas) { Width = width, Height = height };
        chart.Data(data ?? c.Data);
        chart.Mark(c.Create());
        chart.Encode(Channel.X, c.XField);
        chart.Encode(Channel.Y, c.YField);
        if (c.ColorField != null)
            chart.Encode(Channel.Color, c.ColorField);
        return chart;
    }
}
