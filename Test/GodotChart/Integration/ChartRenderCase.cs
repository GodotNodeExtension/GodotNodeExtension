namespace GodotNodeExtension.Tests.GodotChart;

using System.Collections.Generic;
using GodotNodeExtension.Component.GodotChart;

/// <summary>
/// One minimal, valid data set per <see cref="ChartKind"/>: the field names a kind actually reads
/// (the conventions <see cref="ChartView.ApplyEncodes"/> and the marks document - <c>min/q1/median/q3/max</c>,
/// <c>open/high/low/close</c>, <c>lower</c>, <c>start/end</c>, <c>parent</c>, <c>source/target</c>,
/// <c>label</c>, <c>lane</c>), with as many rows as the kind needs to draw something meaningful.
/// <para>
/// The integration suite renders every case through the real backend, so this is deliberately separate
/// from <c>MarkCases</c> (which feeds the fake canvas): the rows here go through the whole
/// <see cref="ChartView"/> pipeline - kind → mark, channel binding, scale inference, layout, legend.
/// </para>
/// </summary>
public sealed record ChartRenderCase(
    ChartKind Kind,
    string XField,
    string YField,
    string ColorField,
    IReadOnlyList<DataRow> Rows)
{
    /// <summary>Name used in test messages and in the PNG file name.</summary>
    public string Label => Kind.ToString();

    /// <summary>Build a row from field/value pairs.</summary>
    public static DataRow Row(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields)
            row.Set(field, value);
        return row;
    }

    /// <summary>Every chart kind, with a minimal data set that exercises it.</summary>
    public static IReadOnlyList<ChartRenderCase> All { get; } = new[]
    {
        new ChartRenderCase(ChartKind.Bar, "category", "value", "", new[]
        {
            Row(("category", "A"), ("value", 10.0)),
            Row(("category", "B"), ("value", 20.0)),
            Row(("category", "C"), ("value", 15.0)),
        }),
        new ChartRenderCase(ChartKind.Line, "category", "value", "", new[]
        {
            Row(("category", "A"), ("value", 10.0)),
            Row(("category", "B"), ("value", 24.0)),
            Row(("category", "C"), ("value", 8.0)),
            Row(("category", "D"), ("value", 18.0)),
        }),
        new ChartRenderCase(ChartKind.Area, "category", "value", "", new[]
        {
            Row(("category", "A"), ("value", 10.0)),
            Row(("category", "B"), ("value", 24.0)),
            Row(("category", "C"), ("value", 8.0)),
            Row(("category", "D"), ("value", 18.0)),
        }),
        new ChartRenderCase(ChartKind.Scatter, "label", "value", "", new[]
        {
            Row(("label", "A"), ("value", 12.0)),
            Row(("label", "B"), ("value", 28.0)),
            Row(("label", "C"), ("value", 19.0)),
            Row(("label", "D"), ("value", 33.0)),
        }),
        new ChartRenderCase(ChartKind.RangeArea, "day", "value", "", new[]
        {
            Row(("day", "Mon"), ("value", 28.0), ("lower", 18.0)),
            Row(("day", "Tue"), ("value", 30.0), ("lower", 20.0)),
            Row(("day", "Wed"), ("value", 26.0), ("lower", 17.0)),
            Row(("day", "Thu"), ("value", 32.0), ("lower", 22.0)),
        }),
        new ChartRenderCase(ChartKind.Pie, "category", "value", "", new[]
        {
            Row(("category", "Desktop"), ("value", 52.0)),
            Row(("category", "Mobile"), ("value", 34.0)),
            Row(("category", "Tablet"), ("value", 14.0)),
        }),
        new ChartRenderCase(ChartKind.Donut, "category", "value", "", new[]
        {
            Row(("category", "Direct"), ("value", 38.0)),
            Row(("category", "Search"), ("value", 44.0)),
            Row(("category", "Social"), ("value", 18.0)),
        }),
        new ChartRenderCase(ChartKind.Radar, "category", "value", "series", new[]
        {
            Row(("category", "Speed"), ("value", 70.0), ("series", "v1")),
            Row(("category", "Power"), ("value", 55.0), ("series", "v1")),
            Row(("category", "Range"), ("value", 82.0), ("series", "v1")),
            Row(("category", "Agility"), ("value", 48.0), ("series", "v1")),
            Row(("category", "Speed"), ("value", 60.0), ("series", "v2")),
            Row(("category", "Power"), ("value", 75.0), ("series", "v2")),
            Row(("category", "Range"), ("value", 40.0), ("series", "v2")),
            Row(("category", "Agility"), ("value", 68.0), ("series", "v2")),
        }),
        // A violin is a distribution: several samples per category.
        new ChartRenderCase(ChartKind.Violin, "weapon", "damage", "", new[]
        {
            Row(("weapon", "Sword"), ("damage", 22.4)),
            Row(("weapon", "Sword"), ("damage", 31.7)),
            Row(("weapon", "Sword"), ("damage", 45.2)),
            Row(("weapon", "Sword"), ("damage", 28.9)),
            Row(("weapon", "Sword"), ("damage", 39.1)),
            Row(("weapon", "Sword"), ("damage", 24.8)),
            Row(("weapon", "Axe"), ("damage", 44.1)),
            Row(("weapon", "Axe"), ("damage", 58.3)),
            Row(("weapon", "Axe"), ("damage", 62.7)),
            Row(("weapon", "Axe"), ("damage", 51.9)),
            Row(("weapon", "Axe"), ("damage", 47.5)),
            Row(("weapon", "Axe"), ("damage", 55.8)),
        }),
        new ChartRenderCase(ChartKind.Box, "stat", "", "", new[]
        {
            Row(("stat", "STR"), ("min", 20.0), ("q1", 40.0), ("median", 55.0), ("q3", 70.0), ("max", 95.0)),
            Row(("stat", "AGI"), ("min", 15.0), ("q1", 35.0), ("median", 50.0), ("q3", 65.0), ("max", 85.0)),
            Row(("stat", "INT"), ("min", 30.0), ("q1", 50.0), ("median", 65.0), ("q3", 80.0), ("max", 100.0)),
        }),
        new ChartRenderCase(ChartKind.Candlestick, "day", "", "", new[]
        {
            Row(("day", "D1"), ("open", 100.5), ("high", 102.3), ("low", 97.8), ("close", 99.4)),
            Row(("day", "D2"), ("open", 99.4), ("high", 101.7), ("low", 95.2), ("close", 97.1)),
            Row(("day", "D3"), ("open", 97.1), ("high", 100.9), ("low", 96.4), ("close", 100.2)),
            Row(("day", "D4"), ("open", 100.2), ("high", 103.5), ("low", 99.1), ("close", 101.8)),
            Row(("day", "D5"), ("open", 101.8), ("high", 104.6), ("low", 98.7), ("close", 99.9)),
        }),
        // X = column, Y = row, Color = cell value (the heatmap convention).
        new ChartRenderCase(ChartKind.Heatmap, "hour", "day", "load", new[]
        {
            Row(("hour", "00"), ("day", "Mon"), ("load", 12.0)),
            Row(("hour", "06"), ("day", "Mon"), ("load", 48.0)),
            Row(("hour", "12"), ("day", "Mon"), ("load", 77.0)),
            Row(("hour", "00"), ("day", "Tue"), ("load", 19.0)),
            Row(("hour", "06"), ("day", "Tue"), ("load", 55.0)),
            Row(("hour", "12"), ("day", "Tue"), ("load", 91.0)),
        }),
        new ChartRenderCase(ChartKind.Treemap, "folder", "value", "", new[]
        {
            Row(("folder", "root"), ("parent", ""), ("value", 0.0)),
            Row(("folder", "Users"), ("parent", "root"), ("value", 0.0)),
            Row(("folder", "alice"), ("parent", "Users"), ("value", 25.0)),
            Row(("folder", "bob"), ("parent", "Users"), ("value", 20.0)),
            Row(("folder", "shared"), ("parent", "Users"), ("value", 15.0)),
            Row(("folder", "Windows"), ("parent", "root"), ("value", 0.0)),
            Row(("folder", "System32"), ("parent", "Windows"), ("value", 30.0)),
            Row(("folder", "Fonts"), ("parent", "Windows"), ("value", 15.0)),
        }),
        new ChartRenderCase(ChartKind.Sunburst, "label", "value", "", new[]
        {
            Row(("label", "total"), ("parent", ""), ("value", 0.0)),
            Row(("label", "frontend"), ("parent", "total"), ("value", 40.0)),
            Row(("label", "backend"), ("parent", "total"), ("value", 35.0)),
            Row(("label", "art"), ("parent", "total"), ("value", 25.0)),
        }),
        new ChartRenderCase(ChartKind.Sankey, "source", "value", "", new[]
        {
            Row(("source", "Search"), ("target", "Landing"), ("value", 40.0)),
            Row(("source", "Social"), ("target", "Landing"), ("value", 25.0)),
            Row(("source", "Landing"), ("target", "Signup"), ("value", 38.0)),
            Row(("source", "Landing"), ("target", "Bounce"), ("value", 27.0)),
        }),
        new ChartRenderCase(ChartKind.Chord, "source", "value", "", new[]
        {
            Row(("source", "Design"), ("target", "Client"), ("value", 18.0)),
            Row(("source", "Client"), ("target", "Backend"), ("value", 12.0)),
            Row(("source", "Backend"), ("target", "Design"), ("value", 9.0)),
            Row(("source", "Design"), ("target", "Backend"), ("value", 6.0)),
        }),
        // Only the first row is drawn (the mark is a single-value indicator).
        new ChartRenderCase(ChartKind.Gauge, "label", "value", "", new[]
        {
            Row(("label", "progress"), ("value", 65.0)),
        }),
        new ChartRenderCase(ChartKind.Funnel, "stage", "count", "", new[]
        {
            Row(("stage", "Visitors"), ("count", 10000.0)),
            Row(("stage", "Sign-ups"), ("count", 6200.0)),
            Row(("stage", "Trial"), ("count", 3800.0)),
            Row(("stage", "Paid"), ("count", 1500.0)),
        }),
        new ChartRenderCase(ChartKind.Waffle, "service", "value", "service", new[]
        {
            Row(("service", "API"), ("value", 60.0)),
            Row(("service", "Web"), ("value", 30.0)),
            Row(("service", "Jobs"), ("value", 10.0)),
        }),
        // Timeline is horizontal: the category runs down Y and start/end span X.
        new ChartRenderCase(ChartKind.Timeline, "buff", "", "effect", new[]
        {
            Row(("buff", "Shield"), ("start", 0.0), ("end", 5.0), ("effect", "Buff")),
            Row(("buff", "Haste"), ("start", 2.0), ("end", 8.0), ("effect", "Buff")),
            Row(("buff", "Poison"), ("start", 3.0), ("end", 7.0), ("effect", "Debuff")),
            Row(("buff", "Regen"), ("start", 6.0), ("end", 12.0), ("effect", "Buff")),
        }),
        new ChartRenderCase(ChartKind.Lollipop, "team", "value", "", new[]
        {
            Row(("team", "Red"), ("value", 78.0)),
            Row(("team", "Blue"), ("value", 64.0)),
            Row(("team", "Green"), ("value", 51.0)),
            Row(("team", "Gold"), ("value", 88.0)),
        }),
        // Events on a numeric axis, one lane per stream, the text read from the label field.
        new ChartRenderCase(ChartKind.Milestone, "week", "lane", "lane", new[]
        {
            Row(("week", 2.0), ("lane", "Release"), ("label", "v0.9")),
            Row(("week", 7.0), ("lane", "Release"), ("label", "v1.0")),
            Row(("week", 11.0), ("lane", "Release"), ("label", "v1.1")),
            Row(("week", 4.0), ("lane", "Docs"), ("label", "guide")),
            Row(("week", 9.0), ("lane", "Docs"), ("label", "api ref")),
            Row(("week", 3.0), ("lane", "Infra"), ("label", "CI")),
        }),
        // Geographic areas: the rows are the regions (one grid cell each, named by the category field) and
        // the value column shades them, so this case needs no map file to be a map.
        new ChartRenderCase(ChartKind.GeoArea, "zone", "value", "value", new[]
        {
            Row(("zone", "North"), ("value", 42.0)),
            Row(("zone", "East"), ("value", 18.0)),
            Row(("zone", "South"), ("value", 63.0)),
            Row(("zone", "West"), ("value", 27.0)),
            Row(("zone", "Centre"), ("value", 35.0)),
        }),
    };
}
