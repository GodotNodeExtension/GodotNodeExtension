namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="ChartView"/>, the one-node chart: choosing a
/// <see cref="ChartKind"/> and feeding rows is enough - the node builds the mark, maps the channels,
/// configures the axes and the legend, handles hover with a tooltip, sizes the surface to itself and
/// redraws when a setting changes.
/// <para>
/// The node hosts a <see cref="Canvas2DControl"/> internally; these tests inject a
/// <see cref="FakeCanvas2D"/> through <see cref="ChartView.CanvasFactory"/> so the whole pipeline can
/// be verified without a GPU, and pump one frame the way the engine would.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class ChartViewTest
{
    /// <summary>Create a view with an injected canvas, sized and added to the tree.</summary>
    private static ChartView AddView(FakeCanvas2D fake, Vector2 size)
    {
        var view = new ChartView { Size = size, CanvasFactory = (_, _) => fake };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(view);
        return view;
    }

    /// <summary>Run one frame the way the engine does: the view rebuilds, its surface then draws.</summary>
    /// <summary>
    /// Did the chart paint that background? The surface itself is cleared transparent, so the theme's
    /// background is what the background renderer fills with.
    /// </summary>
    private static bool PaintedBackground(FakeCanvas2D fake, Color background)
        => fake.FillColors.Any(c => c.ToHtml() == background.ToHtml());

    /// <summary>Floating point comparison: chart geometry is never bit-exact.</summary>
    private static bool Approx(float a, float b, float eps = 0.5f) => Math.Abs(a - b) <= eps;

    private static void PumpFrame(ChartView view, int frames = 1, float delta = 0.016f)
    {
        for (int i = 0; i < frames; i++)
        {
            view._Process(delta);
            foreach (var child in view.GetChildren(includeInternal: true))
                if (child is Canvas2DControl surface)
                    surface._Process(delta);
        }
    }

    private static void Release(ChartView view)
    {
        // Tolerant on purpose: a failing assertion must not turn into a cascade of secondary errors.
        try
        {
            view.GetParent()?.RemoveChild(view);
        }
        finally
        {
            view.Free();
        }
    }

    private static DataRow Row(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields)
            row.Set(field, value);
        return row;
    }

    private static List<DataRow> Bars() =>
    [
        Row(("category", "A"), ("value", 10.0)),
        Row(("category", "B"), ("value", 20.0)),
        Row(("category", "C"), ("value", 15.0)),
    ];

    private static List<DataRow> Series() =>
    [
        Row(("category", "A"), ("value", 10.0), ("series", "S1")),
        Row(("category", "B"), ("value", 20.0), ("series", "S1")),
        Row(("category", "A"), ("value", 5.0), ("series", "S2")),
        Row(("category", "B"), ("value", 12.0), ("series", "S2")),
    ];

    // ── Data: typed rows ────────────────────────────────────────────────────

    [TestCase]
    public void ExportedRowsKeepTheirValueTypes()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(200, 150));
        view.Rows =
        [
            new() { ["category"] = "Jan", ["value"] = 1200, ["ratio"] = 0.5f, ["ok"] = true },
            new() { ["category"] = "Feb", ["value"] = 1800, ["ratio"] = 0.25f, ["ok"] = false },
        ];

        PumpFrame(view);

        AssertThat(view.DataRows.Count).IsEqual(2);
        AssertThat(view.DataRows[0].Get<long>("value")).IsEqual(1200L);      // int stays integer
        AssertThat(view.DataRows[0].Get<float>("ratio")).IsEqual(0.5f);      // float stays float
        AssertThat(view.DataRows[0].Get<string>("category")).IsEqual("Jan"); // text stays text
        AssertThat(view.DataRows[0].Get<bool>("ok")).IsTrue();               // bool stays bool
        AssertThat(view.DataRows[1].Get<bool>("ok")).IsFalse();
        // One fill for the theme's background plus one per bar: two rows really became two bars.
        AssertThat(fake.FillCount).IsEqual(3);

        Release(view);
    }

    [TestCase]
    public void SetDataReplacesTheRowsAndClearsTheExportedArray()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(200, 150));
        view.Rows =
        [
            new() { ["category"] = "old", ["value"] = 1 },
        ];

        view.SetData(Bars());
        PumpFrame(view);

        // The inspector array must not contradict the model.
        AssertThat(view.Rows.Count).IsEqual(0);
        AssertThat(view.DataRows.Count).IsEqual(3);

        Release(view);
    }

    [TestCase]
    public void AddRowRoundTripsTheValuesIntoTypedVariants()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(200, 150));
        view.AddRow(Row(("category", "A"), ("value", 7), ("flag", true)));
        view.AddRow(Row(("category", "B"), ("value", 9.5), ("flag", false)));
        PumpFrame(view);

        AssertThat(view.Rows.Count).IsEqual(2);
        AssertThat(view.Rows[0]["value"].VariantType).IsEqual(Variant.Type.Int);
        AssertThat(view.Rows[1]["value"].VariantType).IsEqual(Variant.Type.Float);
        AssertThat(view.Rows[0]["flag"].AsBool()).IsTrue();
        AssertThat(view.Rows[1]["flag"].AsBool()).IsFalse();

        Release(view);
    }

    [TestCase]
    public void CsvIsAvailableForCodeButNotThePrimaryPath()
    {
        var rows = ChartView.ParseCsv("category,value,series\n# a comment\nA,10,North\nB,20.5,South\n");

        AssertThat(rows.Count).IsEqual(2);
        AssertThat(rows[0].Get<string>("category")).IsEqual("A");
        AssertThat(rows[0].Get<double>("value")).IsEqual(10.0);
        AssertThat(rows[1].Get<double>("value")).IsEqual(20.5);
        AssertThat(rows[1].Get<string>("series")).IsEqual("South");
    }

    [TestCase]
    public void SetCsvFillsTheRows()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(200, 150));
        view.SetCsv("category,value\nA,10\nB,20\n");

        PumpFrame(view);

        AssertThat(view.DataRows.Count).IsEqual(2);
        // The background fill plus one bar per CSV row.
        AssertThat(fake.FillCount).IsEqual(3);

        Release(view);
    }

    // ── Kind → mark ─────────────────────────────────────────────────────────

    /// <summary>
    /// The mark every <see cref="ChartKind"/> builds in the view. The test below compares this table with the
    /// enum as a set, so a kind added to the library cannot stay untested - the list used to name 10 of the 22
    /// kinds, and the other 12 were only exercised by the integration suites.
    /// </summary>
    private static readonly (ChartKind Kind, Type Mark)[] ExpectedMarks =
    [
        (ChartKind.Bar, typeof(IntervalMark)),
        (ChartKind.Line, typeof(LineMark)),
        (ChartKind.Area, typeof(LineMark)),
        (ChartKind.Scatter, typeof(PointMark)),
        (ChartKind.RangeArea, typeof(RangeAreaMark)),
        (ChartKind.Pie, typeof(PieMark)),
        (ChartKind.Donut, typeof(PieMark)),
        (ChartKind.Radar, typeof(RadarMark)),
        (ChartKind.Violin, typeof(ViolinMark)),
        (ChartKind.Box, typeof(BoxMark)),
        (ChartKind.Candlestick, typeof(CandlestickMark)),
        (ChartKind.Heatmap, typeof(HeatmapMark)),
        (ChartKind.Treemap, typeof(TreemapMark)),
        (ChartKind.Sunburst, typeof(SunburstMark)),
        (ChartKind.Sankey, typeof(SankeyMark)),
        (ChartKind.Chord, typeof(ChordMark)),
        (ChartKind.Gauge, typeof(GaugeMark)),
        (ChartKind.Funnel, typeof(FunnelMark)),
        (ChartKind.Waffle, typeof(WaffleMark)),
        (ChartKind.Timeline, typeof(TimelineMark)),
        (ChartKind.Lollipop, typeof(LollipopMark)),
        (ChartKind.Milestone, typeof(MilestoneMark)),
        (ChartKind.GeoArea, typeof(GeoAreaMark)),
        (ChartKind.GeoBubble, typeof(GeoBubbleMark)),
    ];

    [TestCase]
    public void KindBuildsTheMatchingMark()
    {
        Asserts.RequireSceneTree();

        var kinds = Enum.GetValues<ChartKind>();
        AssertThat(ExpectedMarks.Length).IsEqual(kinds.Length);
        foreach (var kind in kinds)
        {
            AssertThat(ExpectedMarks.Any(c => c.Kind == kind)).IsTrue();
            AssertThat(ExpectedMarks.Count(c => c.Kind == kind)).IsEqual(1);
        }

        foreach (var (kind, markType) in ExpectedMarks)
        {
            var fake = new FakeCanvas2D();
            var view = AddView(fake, new Vector2(200, 150));
            view.Kind = kind;
            view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });

            PumpFrame(view);

            AssertThat(view.Chart is not null).IsTrue();
            AssertThat(view.Chart!.Marks[0].GetType()).IsEqual(markType);
            AssertThat(view.Kind).IsEqual(kind);

            Release(view);
        }
    }

    [TestCase]
    public void KindStylingMatchesTheChartType()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(200, 150));

        view.Kind = ChartKind.Donut;
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });
        PumpFrame(view);
        AssertThat(((PieMark)view.Chart!.Marks[0]).InnerRadius > 0f).IsTrue();

        view.Kind = ChartKind.Area;
        PumpFrame(view);
        AssertThat(((LineMark)view.Chart!.Marks[0]).ShowArea).IsTrue();

        Release(view);
    }

    [TestCase]
    public void KindDefaultsToBarAndIsNeverGuessed()
    {
        Asserts.RequireSceneTree();

        // Rows that look like a candlestick must not change the kind: the node draws what it is told.
        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(120, 100));
        AssertThat(view.Kind).IsEqual(ChartKind.Bar);

        view.SetData(Row(("category", "A"), ("open", 1.0), ("high", 2.0), ("low", 0.5), ("close", 1.5)));
        PumpFrame(view);

        AssertThat(view.Kind).IsEqual(ChartKind.Bar);
        AssertThat(view.Chart!.Marks[0]).IsInstanceOf<IntervalMark>();

        Release(view);
    }

    // ── Channels ────────────────────────────────────────────────────────────

    [TestCase]
    public void ShapeFieldReachesTheShapeChannel()
    {
        Asserts.RequireSceneTree();

        DataRow[] rows =
        {
            Row(("cat", "A"), ("value", 1.0), ("kind", "a")),
            Row(("cat", "B"), ("value", 2.0), ("kind", "b")),
        };

        // Without the field both points are discs...
        var plain = new FakeCanvas2D();
        var plainView = AddView(plain, new Vector2(220, 160));
        plainView.Kind = ChartKind.Scatter;
        plainView.XField = "cat";      // the default X field name is "category"
        plainView.SetData(rows);
        PumpFrame(plainView);
        AssertThat(plain.Circles.Count).IsEqual(2);

        // ...with it the second one becomes a square (the second symbol of the vocabulary).
        var shaped = new FakeCanvas2D();
        var shapedView = AddView(shaped, new Vector2(220, 160));
        shapedView.Kind = ChartKind.Scatter;
        shapedView.XField = "cat";
        shapedView.ShapeField = "kind";
        shapedView.SetData(rows);
        PumpFrame(shapedView);

        AssertThat(shaped.Circles.Count).IsEqual(1);
        AssertThat(shaped.Rects.Count).IsEqual(2);   // the chart background plus the square point

        Release(plainView);
        Release(shapedView);
    }

    [TestCase]
    public void ExplicitChannelFieldsReachTheEncodes()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        // Roomier than the chart's content minimum: the engine clamps a Control's size to the minimum the node
        // reports, so a smaller Size here would be raised and the rects below would not be 220x160.
        var view = AddView(fake, new Vector2(320, 200));
        view.XField = "month";
        view.YField = "revenue";
        view.SetData(new[]
        {
            Row(("month", "Jan"), ("revenue", 1200.0)),
            Row(("month", "Feb"), ("revenue", 1800.0)),
        });

        PumpFrame(view);

        // Every category draws its own bar: the two field names really reached the X/Y encodes, so the
        // two rows became two elements side by side - and the taller value makes the taller bar.
        // ("Something was drawn" would also pass for a single bar, or for a chart that only painted
        // its background.)
        var background = ChartTheme.Dark().BackgroundColor;
        AssertThat(PaintedBackground(fake, background)).IsTrue();

        // The background is the node-sized rect; every other rect is a bar.
        var bars = fake.Rects.Where(r => !Approx(r.W, 320f) || !Approx(r.H, 200f)).ToList();
        AssertThat(bars.Count).IsEqual(2);
        AssertThat(bars[0].X).IsNotEqual(bars[1].X);
        AssertThat(bars[1].H > bars[0].H).IsTrue();   // revenue 1800 > 1200
        AssertThat(fake.FillColors.Count(c => c.ToHtml() != background.ToHtml())).IsEqual(2);
        AssertThat(fake.NonFiniteCoordinateCount).IsEqual(0);

        Release(view);
    }

    [TestCase]
    public void TheColorChannelSplitsTheRowsIntoSeparateSeries()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 160));
        view.ColorField = "series";
        view.SetData(Series());

        PumpFrame(view);

        AssertThat(fake.FillColors.Select(c => c.ToHtml()).Distinct().Count() > 1).IsTrue();

        Release(view);
    }

    [TestCase]
    public void EveryPieSlicePicksItsOwnPaletteColor()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 200));
        var palette = new[] { Colors.Red, Colors.Green, Colors.Blue };
        view.CustomTheme = new ChartTheme { Palette = palette };
        view.Kind = ChartKind.Pie;
        view.Legend = LegendPosition.None;   // the legend swatches would repeat the slice colours
        view.SetValues(new[] { ("Desktop", 52.0), ("Mobile", 34.0), ("Tablet", 14.0) });

        PumpFrame(view);

        // One row is one slice and the category names it, so the category also picks the colour (the G2
        // default) - the rows do not have to carry a series field for the pie to be readable.
        var sliceFills = fake.FillColors.Where(c => palette.Any(p => p.ToHtml() == c.ToHtml())).ToList();

        AssertThat(sliceFills.Count).IsEqual(3);
        AssertThat(sliceFills.Select(c => c.ToHtml()).Distinct().Count()).IsEqual(3);

        Release(view);
    }

    [TestCase]
    public void ThePieLegendListsTheCategories()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 200));
        view.Kind = ChartKind.Donut;
        view.SetValues(new[] { ("Desktop", 52.0), ("Mobile", 34.0), ("Tablet", 14.0) });

        PumpFrame(view);

        // Colouring by the category brings the legend with it: the channel that carries the colours is
        // the one that lists them.
        var texts = fake.TextDraws.Select(d => d.Text).ToList();

        AssertThat(texts.Contains("Desktop")).IsTrue();
        AssertThat(texts.Contains("Mobile")).IsTrue();
        AssertThat(texts.Contains("Tablet")).IsTrue();

        Release(view);
    }

    [TestCase]
    public void AnExplicitColorFieldStillWinsOverTheCategory()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 200));
        var palette = new[] { Colors.Red, Colors.Green, Colors.Blue };
        view.CustomTheme = new ChartTheme { Palette = palette };
        view.Kind = ChartKind.Pie;
        view.ColorField = "region";
        view.SetData(new[]
        {
            Row(("category", "Desktop"), ("value", 52.0), ("region", "EU")),
            Row(("category", "Mobile"), ("value", 34.0), ("region", "EU")),
            Row(("category", "Tablet"), ("value", 14.0), ("region", "US")),
        });

        PumpFrame(view);

        // The explicit field (two regions) decides the colours, not the three categories.
        var sliceFills = fake.FillColors.Where(c => palette.Any(p => p.ToHtml() == c.ToHtml())).ToList();

        AssertThat(sliceFills.Select(c => c.ToHtml()).Distinct().Count()).IsEqual(2);

        var texts = fake.TextDraws.Select(d => d.Text).ToList();
        AssertThat(texts.Contains("EU")).IsTrue();
        AssertThat(texts.Contains("US")).IsTrue();

        Release(view);
    }

    [TestCase]
    public void ColoursInTheDataPaintTheElements()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 200));
        view.Kind = ChartKind.Pie;
        view.ColorField = "color";
        view.Legend = LegendPosition.None;
        view.SetData(new[]
        {
            Row(("category", "A"), ("value", 10.0), ("color", Colors.Red)),
            Row(("category", "B"), ("value", 20.0), ("color", "#0000ff")),
        });

        PumpFrame(view);

        // A colour channel whose values are colours uses them as they are - no palette substitution.
        var fills = fake.FillColors.Select(c => c.ToHtml()).ToList();

        AssertThat(fills.Contains(Colors.Red.ToHtml())).IsTrue();
        AssertThat(fills.Contains(Colors.Blue.ToHtml())).IsTrue();

        Release(view);
    }

    [TestCase]
    public void AConstantColourFieldPaintsTheWholeView()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 200));
        view.Kind = ChartKind.Pie;
        view.ColorField = "constant:#ff8800";   // a colour field can also be a constant, not a field name
        view.Legend = LegendPosition.None;
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });

        PumpFrame(view);

        // The chart fills its own background too, so only the slices are left to check.
        var sliceFills = fake.FillColors
            .Where(c => c.ToHtml() != ChartTheme.Dark().BackgroundColor.ToHtml())
            .Select(c => c.ToHtml())
            .Distinct()
            .ToList();

        AssertThat(sliceFills.Count).IsEqual(1);
        AssertThat(sliceFills[0]).IsEqual(new Color("#ff8800").ToHtml());

        Release(view);
    }

    [TestCase]
    public void ColorMappingIdentityKeepsTheDataColoursOnAMixedColumn()
    {
        Asserts.RequireSceneTree();

        // The column mixes colours and a plain category: with the automatic mapping it counts as
        // categorical (no value is a colour), with Identity the colours win and only the leftover value
        // falls back to the palette.
        var rows = new[]
        {
            Row(("category", "A"), ("value", 10.0), ("color", Colors.Red)),
            Row(("category", "B"), ("value", 20.0), ("color", Colors.Blue)),
            Row(("category", "C"), ("value", 30.0), ("color", "plain")),
        };

        (ChartView View, FakeCanvas2D Canvas) Build(ColorMappingKind mapping)
        {
            var fake = new FakeCanvas2D();
            var view = AddView(fake, new Vector2(240, 180));
            view.Kind = ChartKind.Bar;
            view.ColorField = "color";
            view.ColorMapping = mapping;
            view.SetData(rows);
            PumpFrame(view);
            return (view, fake);
        }

        var automatic = Build(ColorMappingKind.Auto);
        AssertThat(automatic.Canvas.FillColors.Any(c => c.ToHtml() == Colors.Red.ToHtml())).IsFalse();
        Release(automatic.View);

        var identity = Build(ColorMappingKind.Identity);
        var fills = identity.Canvas.FillColors.Select(c => c.ToHtml()).ToList();

        AssertThat(fills.Contains(Colors.Red.ToHtml())).IsTrue();
        AssertThat(fills.Contains(Colors.Blue.ToHtml())).IsTrue();
        Release(identity.View);
    }

    [TestCase]
    public void ColorMappingCategoryForcesThePalette()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 180));
        view.Kind = ChartKind.Bar;
        view.ColorField = "color";
        view.ColorMapping = ColorMappingKind.Category;
        view.SetData(new[]
        {
            Row(("category", "A"), ("value", 10.0), ("color", "#ff0000")),
            Row(("category", "B"), ("value", 20.0), ("color", "#0000ff")),
        });

        PumpFrame(view);

        var palette = ChartTheme.Dark().Palette;
        var fills = fake.FillColors.Select(c => c.ToHtml()).ToList();

        AssertThat(fills.Contains(palette[0].ToHtml())).IsTrue();
        AssertThat(fills.Contains(palette[1].ToHtml())).IsTrue();
        AssertThat(fills.Contains(new Color("#ff0000").ToHtml())).IsFalse();
        // Categorical means a legend too - the keys are the field values.
        AssertThat(fake.TextDraws.Any(d => d.Text == "#ff0000")).IsTrue();

        Release(view);
    }

    [TestCase]
    public void ColorMappingSequentialUsesTheThemeGradient()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 180));
        view.Kind = ChartKind.Bar;
        view.CustomTheme = new ChartTheme { SequentialGradient = new[] { Colors.Black, Colors.White } };
        view.ColorField = "value";
        view.ColorMapping = ColorMappingKind.Sequential;
        view.SetValues(new[] { ("A", 10.0), ("B", 30.0) });

        PumpFrame(view);

        var fills = fake.FillColors.Select(c => c.ToHtml()).ToList();

        AssertThat(fills.Contains(Colors.Black.ToHtml())).IsTrue();
        AssertThat(fills.Contains(Colors.White.ToHtml())).IsTrue();

        Release(view);
    }

    [TestCase]
    public void YAxisRangePinsTheValueAxis()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 200));
        view.Kind = ChartKind.Scatter;
        view.YAxisRange = new Vector2(0, 200);
        view.SetData(new[]
        {
            Row(("category", "A"), ("value", 50.0)),
            Row(("category", "B"), ("value", 100.0)),
        });

        PumpFrame(view);

        var plot = view.Chart!.CurrentPlotArea!.Value;

        // Pinned 0…200: 50 sits three quarters down and 100 half way - not the 0…100 fit of the data.
        AssertThat(Approx(fake.Circles[0].Cy, plot.Y + plot.Height * 0.75f)).IsTrue();
        AssertThat(Approx(fake.Circles[1].Cy, plot.Y + plot.Height * 0.5f)).IsTrue();

        Release(view);
    }

    [TestCase]
    public void AxisRangeLeavesACategoryAxisAlone()
    {
        Asserts.RequireSceneTree();

        string Positions(Vector2 range)
        {
            var fake = new FakeCanvas2D();
            var view = AddView(fake, new Vector2(240, 200));
            view.Kind = ChartKind.Scatter;
            view.XAxisRange = range;
            view.SetData(new[]
            {
                Row(("category", "A"), ("value", 1.0)),
                Row(("category", "B"), ("value", 2.0)),
            });
            PumpFrame(view);
            var positions = string.Join(",", fake.Circles.Select(c => c.Cx));
            Release(view);
            return positions;
        }

        // A category axis has no linear scale to pin, so the range is a no-op instead of a broken axis.
        AssertThat(Positions(new Vector2(0, 10))).IsEqual(Positions(Vector2.Zero));
    }

    [TestCase]
    public void SizeRangeSetsTheRadiusBounds()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 220));
        view.Kind = ChartKind.Scatter;
        view.SizeField = "weight";
        view.SizeRange = new Vector2(4f, 30f);
        view.SetData(new[]
        {
            // 10 and 30 are the ends of the fitted domain, so the radii are the configured bounds.
            Row(("category", "A"), ("value", 1.0), ("weight", 10.0)),
            Row(("category", "B"), ("value", 2.0), ("weight", 30.0)),
        });

        PumpFrame(view, 3);

        // The column maps onto the configured pixel range instead of the theme's 3…23.
        AssertThat(Approx(fake.Circles[0].Radius, 4f)).IsTrue();
        AssertThat(Approx(fake.Circles[1].Radius, 30f)).IsTrue();

        Release(view);
    }

    [TestCase]
    public void OpacityRangeKeepsTheFaintestElementVisible()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 220));
        view.Kind = ChartKind.Bar;
        view.OpacityField = "weight";
        view.OpacityRange = new Vector2(0.4f, 1f);
        view.SetData(new[]
        {
            Row(("category", "A"), ("value", 10.0), ("weight", 5.0)),
            Row(("category", "B"), ("value", 20.0), ("weight", 15.0)),
        });

        PumpFrame(view, 3);

        // 5…15 maps onto 0.4…1 instead of 0…1, so nothing fades away.
        AssertThat(fake.FillOpacities.All(o => o >= 0.4f - 1e-3f)).IsTrue();
        AssertThat(fake.FillOpacities.Max() > 0.9f).IsTrue();

        Release(view);
    }

    [TestCase]
    public void ShapeSymbolsReplacesTheDefaultVocabulary()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 220));
        view.Kind = ChartKind.Scatter;
        view.ShapeField = "kind";
        view.ShapeSymbols = [ShapeKind.Square, ShapeKind.Triangle];
        view.SetData(new[]
        {
            Row(("category", "A"), ("value", 1.0), ("kind", "boxed")),
            Row(("category", "B"), ("value", 2.0), ("kind", "pointy")),
        });

        PumpFrame(view, 3);

        // The default vocabulary starts with Circle, so without ShapeSymbols the first category would be
        // a disc (ShapeGeometry.Build: Circle -> path.Circle, Square -> path.Rect). The replaced
        // vocabulary must show up in the geometry itself: no circle symbol anywhere, one square symbol
        // of the point diameter - and still one fill per category, so the triangle is drawn too.
        float diameter = 2f * new PointMark().DefaultRadius;
        var squares = fake.Rects.Where(r => Approx(r.W, diameter) && Approx(r.H, diameter)).ToList();
        var background = ChartTheme.Dark().BackgroundColor;

        AssertThat(fake.Circles.Count).IsEqual(0);
        AssertThat(squares.Count).IsEqual(1);
        AssertThat(fake.FillColors.Count(c => c.ToHtml() != background.ToHtml())).IsEqual(2);

        Release(view);
    }

    [TestCase]
    public void ConfigureChartSeesTheBuiltChartAndCanOverrideAScale()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 180));
        Chart? configured = null;
        view.ConfigureChart = chart =>
        {
            configured = chart;
            // A hand-built scale is exactly what the exports cannot express.
            chart.Scale(Channel.Color, new ColorScale { Palette = new[] { Colors.Red } });
        };
        view.ColorField = "category";
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });

        PumpFrame(view);

        AssertThat(ReferenceEquals(configured, view.Chart)).IsTrue();
        // The chart fills its own background too, so only the bars are left to check.
        var fills = fake.FillColors
            .Where(c => c.ToHtml() != ChartTheme.Dark().BackgroundColor.ToHtml())
            .Select(c => c.ToHtml())
            .Distinct()
            .ToList();

        AssertThat(fills.Count).IsEqual(1);
        AssertThat(fills[0]).IsEqual(Colors.Red.ToHtml());

        Release(view);
    }

    [TestCase]
    public void AColourValueInTheInspectorRowsKeepsItsType()
    {
        Asserts.RequireSceneTree();

        // The scene path: a Color variant in Rows must not be flattened to text on the way in, or the
        // colour channel would read a category string and paint a palette colour instead.
        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 200));
        view.Kind = ChartKind.Pie;
        view.ColorField = "color";
        view.Legend = LegendPosition.None;
        view.Rows =
        [
            new() { { "category", "A" }, { "value", 10.0 }, { "color", Colors.Red } },
        ];

        PumpFrame(view);

        AssertThat(fake.FillColors.Any(c => c.ToHtml() == Colors.Red.ToHtml())).IsTrue();

        Release(view);
    }

    [TestCase]
    public void TheSizeChannelScalesTheDots()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 160));
        view.Kind = ChartKind.Scatter;
        view.SizeField = "size";
        view.SetData(new[]
        {
            Row(("category", "A"), ("value", 10.0), ("size", 1.0)),
            Row(("category", "B"), ("value", 20.0), ("size", 9.0)),
            Row(("category", "C"), ("value", 15.0), ("size", 5.0)),
        });

        PumpFrame(view);

        // One dot per row: three rows, three circles (nothing else in this chart draws a circle - the
        // background is a rect and the grid is drawn with lines).
        AssertThat(fake.Circles.Count).IsEqual(3);
        var radii = fake.Circles.Select(c => c.Radius).ToList();
        AssertThat(radii[1] > radii[0]).IsTrue();   // the bigger size draws the bigger dot

        Release(view);
    }

    [TestCase]
    public void TheOpacityChannelReachesTheFill()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(220, 160));
        view.OpacityField = "alpha";
        view.SetData(new[]
        {
            Row(("category", "A"), ("value", 10.0), ("alpha", 0.2)),
            Row(("category", "B"), ("value", 20.0), ("alpha", 1.0)),
            Row(("category", "C"), ("value", 15.0), ("alpha", 0.6)),
        });

        PumpFrame(view);

        AssertThat(fake.FillOpacities.Distinct().Count() > 1).IsTrue();

        Release(view);
    }

    [TestCase]
    public void AxisTitlesAndUnitsReachTheCanvas()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(280, 180));
        view.XAxisTitle = "Month";
        view.YAxisTitle = "Revenue";
        view.YAxisUnit = "USD";
        view.SetValues(new[] { ("Jan", 1200.0), ("Feb", 1800.0) });

        PumpFrame(view);

        // Titles are drawn on the canvas; the unit shows up in the axis tooltip (the label the axis
        // zone reports when the pointer is over it).
        AssertThat(fake.Texts.Contains("Month")).IsTrue();
        AssertThat(fake.Texts.Contains("Revenue")).IsTrue();

        // Titles are drawn on the canvas; a unit is reported by the axis zone when the pointer is over
        // it - the X unit would show on the X axis, this one belongs to the Y axis.
        var plot = view.Chart!.CurrentPlotArea!.Value;
        var xAxisHit = view.Chart.HitTest(new Vector2(plot.X + plot.Width * 0.5f, plot.Y + plot.Height + 8f));
        AssertThat(xAxisHit is not null).IsTrue();
        AssertThat(xAxisHit!.Label!.Contains("Month")).IsTrue();

        var yAxisHit = view.Chart.HitTest(new Vector2(plot.X - 8f, plot.Y + plot.Height * 0.5f));
        AssertThat(yAxisHit is not null).IsTrue();
        AssertThat(yAxisHit!.Label!.Contains("USD")).IsTrue();

        Release(view);
    }

    [TestCase]
    public void TheLegendListsTheSeries()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(280, 180));
        view.ColorField = "series";
        view.Legend = LegendPosition.Bottom;
        view.SetData(Series());

        PumpFrame(view);

        AssertThat(fake.Texts.Contains("S1")).IsTrue();
        AssertThat(fake.Texts.Contains("S2")).IsTrue();

        Release(view);
    }

    [TestCase]
    public void LegendNoneDrawsNoLegend()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(280, 180));
        view.ColorField = "series";
        view.Legend = LegendPosition.None;
        view.SetData(Series());

        PumpFrame(view);

        AssertThat(fake.Texts.Contains("S1")).IsFalse();

        Release(view);
    }

    // ── Hover + tooltip ─────────────────────────────────────────────────────

    [TestCase]
    public void HoveringShowsTheTooltipAndTheCrosshair()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 200));
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0), ("C", 15.0) });
        PumpFrame(view);

        int strokesBefore = fake.StrokeCount;
        var plot = view.Chart!.CurrentPlotArea!.Value;

        // Aim at the middle bar's body, like the engine would.
        view._GuiInput(new InputEventMouseMotion
        {
            Position = new Vector2(plot.X + plot.Width * 0.5f, plot.Y + plot.Height - 12f),
        });
        PumpFrame(view, 3);

        AssertThat(view.Tooltip.IsVisible).IsTrue();
        AssertThat(view.Chart!.CurrentHoveredRowIndex >= 0).IsTrue();
        AssertThat(fake.StrokeCount > strokesBefore).IsTrue();   // the crosshair drew

        Release(view);
    }

    [TestCase]
    public void LeavingTheNodeHidesTheTooltipAgain()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 200));
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });
        PumpFrame(view);

        var plot = view.Chart!.CurrentPlotArea!.Value;
        view._GuiInput(new InputEventMouseMotion
        {
            // With two bars the plot centre sits on the gap between them: aim at the first bar.
            Position = new Vector2(plot.X + plot.Width * 0.25f, plot.Y + plot.Height - 12f),
        });
        PumpFrame(view, 3);
        AssertThat(view.Tooltip.IsVisible).IsTrue();

        view._Notification((int)Control.NotificationMouseExit);
        PumpFrame(view, 20);   // the fade out is time based

        AssertThat(view.Tooltip.IsVisible).IsFalse();
        AssertThat(view.Chart!.CurrentHoveredRowIndex).IsEqual(-1);

        Release(view);
    }

    [TestCase]
    public void AChartBackgroundOverrideReachesTheSurface()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 200));
        // The node has no background export of its own, so a host overrides the chart instead: a
        // transparent background means the background renderer paints nothing (the surface is always
        // cleared transparent), and the chart really is see-through.
        view.ConfigureChart = chart => chart.BackgroundColor = new Color(0f, 0f, 0f, 0f);
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });
        PumpFrame(view);

        AssertThat(view.Surface!.BackgroundColor.A).IsEqual(0f);
        AssertThat(PaintedBackground(fake, ChartTheme.Dark().BackgroundColor)).IsFalse();

        // Without an override the theme's background is painted again.
        view.ConfigureChart = null;
        view.Refresh();
        PumpFrame(view);

        AssertThat(PaintedBackground(fake, ChartTheme.Dark().BackgroundColor)).IsTrue();

        Release(view);
    }

    [TestCase]
    public void AnInPlaceSymbolListEditIsNoticed()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 200));
        view.Kind = ChartKind.Scatter;
        view.ShapeField = "shape";
        view.ShapeSymbols = [ShapeKind.Circle];
        view.SetData(new[] { new DataRow(2).Set("category", "A").Set("value", 10.0).Set("shape", "one") });
        PumpFrame(view);

        // The inspector edits an exported array in place, which never reaches the setter: the editor-only
        // signature check is what notices it (and it has to rebuild, not just repaint).
        var before = view.Chart;
        view.ShapeSymbols.Add(ShapeKind.Square);
        view.CheckVariantRows();
        PumpFrame(view);

        AssertThat(ReferenceEquals(before, view.Chart)).IsFalse();

        Release(view);
    }

    /// <summary>Records the wheel events that reach it, standing in for the ScrollContainer a host puts around a chart.</summary>
    private sealed partial class WheelRecordingParent : Control
    {
        /// <summary>Number of wheel-down events that arrived from the child chart.</summary>
        public int WheelEvents { get; private set; }

        /// <inheritdoc />
        public override void _GuiInput(InputEvent @event)
        {
            if (@event is not InputEventMouseButton { Pressed: true } button) return;
            if (button.ButtonIndex is not (MouseButton.WheelDown or MouseButton.WheelUp)) return;

            WheelEvents++;
            AcceptEvent();   // the panel takes the wheel: exactly what a ScrollContainer does
        }
    }

    [TestCase]
    public void TheWheelBubblesFromTheChartToItsParent()
    {
        Asserts.RequireSceneTree();

        var tree = (SceneTree)Engine.GetMainLoop();
        var originalRootSize = tree.Root.Size;
        tree.Root.Size = new Vector2I(320, 240);   // a headless root can be tiny; hit testing needs the point inside

        var parent = new WheelRecordingParent { Position = Vector2.Zero, Size = new Vector2(200f, 120f) };
        tree.Root.AddChild(parent);

        var fake = new FakeCanvas2D();
        var view = new ChartView
        {
            CanvasFactory = (_, _) => fake,
            Position = Vector2.Zero,
            Size = new Vector2(200f, 120f),
        };
        parent.AddChild(view);
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });
        PumpFrame(view, 2);

        var wheel = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.WheelDown,
            Pressed = true,
            Position = view.GlobalPosition + new Vector2(20f, 20f),
            GlobalPosition = view.GlobalPosition + new Vector2(20f, 20f),
        };
        tree.Root.PushInput(wheel);

        // The chart accepts motion and clicks but not the wheel, so the parent - the host's scroll panel -
        // receives it. With MouseFilter Stop (or an accepted wheel) this stays 0.
        AssertThat(parent.WheelEvents).IsEqual(1);

        // The parent owns the chart here: freeing it frees the view too, so nothing may touch `view` after
        // this point (a freed Godot object throws on access).
        parent.GetParent()?.RemoveChild(parent);
        parent.Free();
        tree.Root.Size = originalRootSize;   // the root viewport is shared state
    }

    [TestCase]
    public void SetDataKeepsTheExportedArraySignatureInStep()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 200));
        view.SetData(new[] { new DataRow(2).Set("category", "A").Set("value", 10.0) });
        PumpFrame(view);

        // What the editor runs every frame. With the signature out of step this reloaded the model from the
        // exported array the node had just emptied, so the rows vanished.
        view.CheckVariantRows();
        PumpFrame(view);
        AssertThat(view.DataRows.Count).IsEqual(1);

        // AddRow mirrors into the exported array, so the two stay in step and the check stays quiet.
        view.AddRow(new DataRow(2).Set("category", "B").Set("value", 20.0));
        view.CheckVariantRows();
        PumpFrame(view);
        AssertThat(view.DataRows.Count).IsEqual(2);
        AssertThat(view.Rows.Count).IsEqual(1);   // SetData had emptied the exported array

        // Replacing an exported row - what the editor inspector does - is picked up again, and the exported
        // array is the source of truth for that edit.
        view.Rows[0] = new Godot.Collections.Dictionary
        {
            { "category", "Z" },
            { "value", 99.0 },
        };
        view.CheckVariantRows();
        PumpFrame(view);
        AssertThat(view.DataRows.Count).IsEqual(1);
        // The row really carries the new value: an inspector edit must reach the typed model.
        object? replaced = view.DataRows[0].Get("value");
        AssertThat(replaced is not null).IsTrue();
        AssertThat((double)replaced!).IsEqual(99.0);

        Release(view);
    }

    [TestCase]
    public void AssigningAnEqualThemeArrayDoesNotNotify()
    {
        var theme = ChartTheme.Dark().Clone();
        int notifications = 0;
        theme.Changed += () => notifications++;

        // Same content, fresh instance: the editor assigns arrays wholesale, so this must stay silent.
        theme.Palette = (Color[])theme.Palette.Clone();
        AssertThat(notifications).IsEqual(0);

        // Different content does notify.
        theme.Palette = new[] { Colors.Red, Colors.Blue };
        AssertThat(notifications).IsEqual(1);
    }

    [TestCase]
    public void LeavingTheTreeDropsTheChart()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 200));
        view.SetValues(new[] { ("A", 10.0) });
        PumpFrame(view);
        AssertThat(view.Chart is not null).IsTrue();

        // The surface disposes its canvas on the way out, so the chart built on it must not survive.
        view.GetParent().RemoveChild(view);
        AssertThat(view.Chart is null).IsTrue();

        view.Free();
    }

    [TestCase]
    public void HoveringAChartRaisesTheOnHoverEvent()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 200));
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0), ("C", 15.0) });

        int events = 0;
        int lastRow = -2;
        view.ConfigureChart = chart => chart.OnHover += (_, e) =>
        {
            events++;
            lastRow = e.RowIndex;
        };
        PumpFrame(view);

        var plot = view.Chart!.CurrentPlotArea!.Value;
        view._GuiInput(new InputEventMouseMotion
        {
            Position = new Vector2(plot.X + plot.Width * 0.5f, plot.Y + plot.Height - 12f),
        });
        PumpFrame(view, 2);

        // The handler is mounted per rebuild, and the node must not pre-set the hovered row itself - that is
        // what used to swallow this event.
        AssertThat(events).IsEqual(1);
        AssertThat(lastRow).IsEqual(1);

        Release(view);
    }

    /// <summary>
    /// Leaving the chart reports "nothing is hovered any more" through <c>OnHover</c> with row -1. The exit
    /// handler used to set the hovered row before asking for the change, so the event was swallowed and a
    /// host that clears its own highlight on <c>RowIndex == -1</c> never got the call.
    /// </summary>
    [TestCase]
    public void LeavingTheChartRaisesTheOnHoverEventWithNoRow()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 200));
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0), ("C", 15.0) });

        var rows = new List<int>();
        view.ConfigureChart = chart => chart.OnHover += (_, e) => rows.Add(e.RowIndex);
        PumpFrame(view);

        var plot = view.Chart!.CurrentPlotArea!.Value;
        view._GuiInput(new InputEventMouseMotion
        {
            Position = new Vector2(plot.X + plot.Width * 0.5f, plot.Y + plot.Height - 12f),
        });
        PumpFrame(view, 2);
        AssertThat(rows.Count).IsEqual(1);
        AssertThat(rows[0]).IsEqual(1);   // the middle bar

        view._Notification((int)Control.NotificationMouseExit);
        PumpFrame(view);

        AssertThat(rows.Count).IsEqual(2);
        AssertThat(rows[1]).IsEqual(-1);
        AssertThat(view.Chart.CurrentHoveredRowIndex).IsEqual(-1);
        AssertThat(view.Chart.CurrentSelectedRowIndex).IsEqual(-1);

        Release(view);
    }


    [TestCase]
    public void AThemePropertyAssignmentNotifiesTheChart()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 200));
        var theme = ChartTheme.Dark().Clone();
        view.CustomTheme = theme;
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });
        PumpFrame(view);

        AssertThat(PaintedBackground(fake, theme.BackgroundColor)).IsTrue();

        // Editing the resource - in the inspector or from code - has to reach the chart without the host
        // calling EmitChanged(): the theme's own setters raise Resource.Changed.
        theme.BackgroundColor = new Color(1f, 0f, 0f);
        PumpFrame(view);

        AssertThat(PaintedBackground(fake, new Color(1f, 0f, 0f))).IsTrue();

        Release(view);
    }

    [TestCase]
    public void TheWheelScrollsPastTheChartInsteadOfSelecting()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 200));
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0), ("C", 15.0) });
        PumpFrame(view);

        // Pass lets the events the chart does not accept (the wheel) reach the host's ScrollContainer.
        AssertThat(view.MouseFilter).IsEqual(Control.MouseFilterEnum.Pass);

        var plot = view.Chart!.CurrentPlotArea!.Value;
        view._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.WheelDown,
            Pressed = true,
            Position = new Vector2(plot.X + plot.Width * 0.5f, plot.Y + plot.Height - 12f),
        });
        PumpFrame(view);

        // The wheel is not a click: nothing was selected and no hover row was settled on.
        AssertThat(view.Chart!.CurrentSelectedRowIndex).IsEqual(-1);
        AssertThat(view.Chart.CurrentHoveredRowIndex).IsEqual(-1);

        // A real press still selects, so the pointer handling itself is untouched.
        view._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = new Vector2(plot.X + plot.Width * 0.5f, plot.Y + plot.Height - 12f),
        });
        PumpFrame(view);

        AssertThat(view.Chart.CurrentSelectedRowIndex).IsEqual(1);   // the middle bar

        Release(view);
    }

    [TestCase]
    public void TheTooltipCanBeTurnedOff()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 200));
        view.ShowTooltip = false;
        view.ShowCrosshair = false;
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });
        PumpFrame(view);

        var plot = view.Chart!.CurrentPlotArea!.Value;
        view._GuiInput(new InputEventMouseMotion
        {
            Position = new Vector2(plot.X + plot.Width * 0.5f, plot.Y + plot.Height - 12f),
        });
        PumpFrame(view, 3);

        AssertThat(view.Tooltip.IsVisible).IsFalse();
        // No tooltip content and no crosshair colour reached the canvas.
        AssertThat(fake.Texts.Any(t => t.Contains(':'))).IsFalse();
        AssertThat(fake.StrokeColors.Any(c => c.A is > 0f and < 1f)).IsFalse();

        Release(view);
    }

    [TestCase]
    public void TheThemeMasterSwitchTurnsTheTooltipOff()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 200));
        var theme = ChartTheme.Dark().Clone();
        theme.EnableTooltip = false;          // the theme-level master switch
        view.CustomTheme = theme;
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0), ("C", 15.0) });
        PumpFrame(view);

        var plot = view.Chart!.CurrentPlotArea!.Value;
        var middleBar = new Vector2(plot.X + plot.Width * 0.5f, plot.Y + plot.Height - 12f);
        view._GuiInput(new InputEventMouseMotion { Position = middleBar });
        PumpFrame(view, 3);

        // The node still asks for the tooltip - the theme is what keeps the bubble away.
        AssertThat(view.ShowTooltip).IsTrue();
        AssertThat(view.Tooltip.IsVisible).IsFalse();
        AssertThat(fake.Texts.Any(t => t.Contains(':'))).IsFalse();

        // Switching the theme back on re-enables it without touching the node.
        theme.EnableTooltip = true;
        PumpFrame(view);
        view._GuiInput(new InputEventMouseMotion { Position = middleBar });
        PumpFrame(view, 3);

        AssertThat(view.Tooltip.IsVisible).IsTrue();

        Release(view);
    }

    // ── Presentation ────────────────────────────────────────────────────────

    [TestCase]
    public void TheSurfaceCoversTheWholeNode()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(200, 150));
        view.SetValues(new[] { ("A", 10.0) });
        PumpFrame(view);

        // The internal surface must occupy the node rect: a zero-size rect presents nothing even
        // though the canvas itself renders.
        AssertThat(view.Surface is not null).IsTrue();
        AssertThat(view.Surface!.Size.IsEqualApprox(view.Size)).IsTrue();

        Release(view);
    }

    [TestCase]
    public void TheSurfaceFollowsTheNodeSize()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        // Above the content minimum on purpose: the engine clamps a Control's size to what the node reports
        // through _GetMinimumSize, so a smaller Size would come back raised.
        var view = AddView(fake, new Vector2(320, 240));
        view.SetValues(new[] { ("A", 10.0) });
        PumpFrame(view);
        AssertThat(view.Chart!.Width).IsEqual(320f);
        AssertThat(view.Chart.Height).IsEqual(240f);

        view.Size = new Vector2(420, 300);
        PumpFrame(view);

        AssertThat(view.Chart!.Width).IsEqual(420f);
        AssertThat(view.Chart.Height).IsEqual(300f);

        Release(view);
    }

    // ── Custom theme resource ───────────────────────────────────────────────

    /// <summary>A theme with values that cannot be confused with the built-in palettes.</summary>
    private static ChartTheme DistinctTheme() => new()
    {
        BackgroundColor = new Color(0.11f, 0.22f, 0.33f),
        GridColor = new Color(0.44f, 0.55f, 0.66f, 0.9f),
        AxisColor = new Color(0.77f, 0.11f, 0.22f),
    };

    [TestCase]
    public void ACustomThemeResourceReachesTheChart()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(160, 120));
        var theme = DistinctTheme();

        view.CustomTheme = theme;
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });
        PumpFrame(view);

        // The background and the grid come from the assigned resource, not from the built-in palette.
        AssertThat(theme.BackgroundColor.ToHtml()).IsNotEqual(ChartTheme.Dark().BackgroundColor.ToHtml());
        AssertThat(PaintedBackground(fake, theme.BackgroundColor)).IsTrue();
        // The grid renderer draws its lines with the theme's grid colour (DrawLine records it).
        AssertThat(fake.Lines.Any(l => l.Color.ToHtml() == theme.GridColor.ToHtml())).IsTrue();

        // Dropping the resource falls back to the built-in palette.
        view.CustomTheme = null;
        PumpFrame(view);
        AssertThat(PaintedBackground(fake, ChartTheme.Dark().BackgroundColor)).IsTrue();

        Release(view);
    }

    [TestCase]
    public void ACustomThemeWinsOverTheThemeKind()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(160, 120));
        var theme = DistinctTheme();

        view.ThemeKind = ChartThemeKind.Light;
        view.CustomTheme = theme;
        view.SetValues(new[] { ("A", 10.0) });
        PumpFrame(view);

        // ThemeKind only picks the fallback palette: an assigned resource takes precedence.
        AssertThat(PaintedBackground(fake, theme.BackgroundColor)).IsTrue();
        AssertThat(PaintedBackground(fake, ChartTheme.Light().BackgroundColor)).IsFalse();

        Release(view);
    }

    [TestCase]
    public void TheAssignedThemeResourceIsNotMutated()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(160, 120));
        var theme = DistinctTheme();
        theme.FontFamily = "SomeFamily";
        string background = theme.BackgroundColor.ToHtml();

        view.CustomTheme = theme;
        view.SetValues(new[] { ("A", 10.0) });
        PumpFrame(view);

        // The node renders with the assigned values - from its own copy, so the file the user assigned
        // (usually a .tres shared by several nodes) is never rewritten by rendering.
        AssertThat(PaintedBackground(fake, theme.BackgroundColor)).IsTrue();
        AssertThat(theme.FontFamily).IsEqual("SomeFamily");
        AssertThat(theme.BackgroundColor.ToHtml()).IsEqual(background);
        AssertThat(fake.DrewAnything).IsTrue();

        Release(view);
    }

    [TestCase]
    public void EditingTheThemeResourceUpdatesTheChart()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(160, 120));
        var theme = DistinctTheme();

        view.CustomTheme = theme;
        view.SetValues(new[] { ("A", 10.0) });
        PumpFrame(view);
        AssertThat(PaintedBackground(fake, theme.BackgroundColor)).IsTrue();

        // Editing the .tres in the Inspector keeps the same instance, so the node has to follow the
        // resource's own changed signal - a reference comparison cannot see the edit.
        theme.BackgroundColor = new Color(0.25f, 0.5f, 0.75f);
        theme.EmitChanged();
        PumpFrame(view);

        AssertThat(PaintedBackground(fake, theme.BackgroundColor)).IsTrue();

        Release(view);
    }

    [TestCase]
    public void ChartThemeIsAToolScript()
    {
        // Editor-only regression gate: a non-tool C# resource only gets a placeholder script instance in
        // the editor, so a theme created or loaded there is a bare Resource and assigning it throws in
        // the generated property setter (see Doc/GodotChart/customization.md).
        var script = ResourceLoader.Load<Script>("res://Component/GodotChart/ChartTheme.cs");

        AssertThat(script).IsNotNull();
        AssertThat(script!.IsTool()).IsTrue();
    }

    [TestCase]
    public void AThemeResourceSurvivesASaveAndLoadRoundTrip()
    {
        Asserts.RequireSceneTree();

        // The whole point of the feature is editing a .tres in the editor, so the resource has to
        // survive a real save/load cycle (scratch files live in this component's own tmp/ directory,
        // which is git-ignored - see AGENTS.md §2).
        string path = "res://tmp/GodotChart/chartview_custom_theme.tres";
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://tmp/GodotChart"));
        var theme = DistinctTheme();

        var saved = ResourceSaver.Save(theme, path);
        AssertThat(saved).IsEqual(Error.Ok);

        var loaded = ResourceLoader.Load<ChartTheme>(path);
        AssertThat(loaded is not null).IsTrue();
        AssertThat(loaded!.BackgroundColor.ToHtml()).IsEqual(theme.BackgroundColor.ToHtml());
        AssertThat(loaded.GridColor.ToHtml()).IsEqual(theme.GridColor.ToHtml());
        AssertThat(loaded.AxisColor.ToHtml()).IsEqual(theme.AxisColor.ToHtml());

        // ... and the loaded resource drives the chart.
        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(160, 120));
        view.CustomTheme = loaded;
        view.SetValues(new[] { ("A", 10.0) });
        PumpFrame(view);
        AssertThat(PaintedBackground(fake, theme.BackgroundColor)).IsTrue();

        Release(view);
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
    }

    [TestCase]
    public void TheBackgroundFollowsThePalette()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(120, 100));
        view.SetValues(new[] { ("A", 10.0) });
        PumpFrame(view);

        AssertThat(PaintedBackground(fake, ChartTheme.Dark().BackgroundColor)).IsTrue();

        // The background is a theme concern only: the node has no colour export of its own to
        // override it with, so switching the palette is the way to change it.
        view.ThemeKind = ChartThemeKind.Light;
        PumpFrame(view);
        AssertThat(PaintedBackground(fake, ChartTheme.Light().BackgroundColor)).IsTrue();

        Release(view);
    }

    [TestCase]
    public void ChangingASettingRedrawsExactlyOncePerFrame()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(200, 150));
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });

        PumpFrame(view);
        int fills = fake.FillCount;
        AssertThat(fills > 0).IsTrue();

        PumpFrame(view, 2);
        AssertThat(fake.FillCount).IsEqual(fills);   // an idle frame must not redraw

        view.SetValues(new[] { ("A", 5.0), ("B", 7.0), ("C", 9.0) });
        PumpFrame(view);
        AssertThat(fake.FillCount > fills).IsTrue();

        Release(view);
    }

    [TestCase]
    public void ConfigureMarkReachesTheBuiltMark()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(220, 160));
        view.Kind = ChartKind.Box;
        view.ConfigureMark(mark =>
        {
            if (mark is not BoxMark box) return;
            box.MinField = "lo";
            box.Q1Field = "lo1";
            box.MedianField = "mid";
            box.Q3Field = "hi1";
            box.MaxField = "hi";
        });
        view.SetData(new[]
        {
            Row(("category", "A"), ("lo", 1.0), ("lo1", 3.0), ("mid", 5.0), ("hi1", 7.0), ("hi", 9.0)),
            Row(("category", "B"), ("lo", 2.0), ("lo1", 4.0), ("mid", 6.0), ("hi1", 8.0), ("hi", 11.0)),
        });

        PumpFrame(view);

        AssertThat(((BoxMark)view.Chart!.Marks[0]).MedianField).IsEqual("mid");
        // Two rows, so two box bodies - plus the frame's own rounded rect. The view's background alone would
        // satisfy "something was painted", which is what this replaces.
        AssertThat(fake.RoundRects.Count).IsEqual(3);

        Release(view);
    }

    /// <summary>
    /// The theme's mark defaults are the starting point for the mark a kind builds:
    /// <see cref="ChartTheme.CornerRadius"/> for a mark with a corner radius,
    /// <see cref="ChartTheme.StrokeWidth"/> for a stroked one. Kinds whose mark has neither are
    /// untouched.
    /// </summary>
    [TestCase]
    public void TheThemeMarkDefaultsReachTheMarkAKindBuilds()
    {
        Asserts.RequireSceneTree();

        var theme = ChartTheme.Dark().Clone();
        theme.CornerRadius = 7f;
        theme.StrokeWidth = 5f;

        AssertThat(MarkBuiltFor(ChartKind.Bar, theme) is IntervalMark { CornerRadius: 7f }).IsTrue();
        AssertThat(MarkBuiltFor(ChartKind.Box, theme) is BoxMark { CornerRadius: 7f }).IsTrue();
        AssertThat(MarkBuiltFor(ChartKind.Candlestick, theme) is CandlestickMark { CornerRadius: 7f }).IsTrue();
        AssertThat(MarkBuiltFor(ChartKind.Funnel, theme) is FunnelMark { CornerRadius: 7f }).IsTrue();
        AssertThat(MarkBuiltFor(ChartKind.Heatmap, theme) is HeatmapMark { CornerRadius: 7f }).IsTrue();
        AssertThat(MarkBuiltFor(ChartKind.Timeline, theme) is TimelineMark { CornerRadius: 7f }).IsTrue();
        AssertThat(MarkBuiltFor(ChartKind.Treemap, theme) is TreemapMark { CornerRadius: 7f }).IsTrue();
        AssertThat(MarkBuiltFor(ChartKind.Waffle, theme) is WaffleMark { CornerRadius: 7f }).IsTrue();
        AssertThat(MarkBuiltFor(ChartKind.Line, theme) is LineMark { StrokeWidth: 5f }).IsTrue();
        AssertThat(MarkBuiltFor(ChartKind.Radar, theme) is RadarMark { StrokeWidth: 5f }).IsTrue();
        AssertThat(MarkBuiltFor(ChartKind.Violin, theme) is ViolinMark { StrokeWidth: 5f }).IsTrue();

        // A kind whose mark carries neither knob keeps its own values (nothing to copy).
        AssertThat(MarkBuiltFor(ChartKind.Scatter, theme) is PointMark).IsTrue();
    }

    /// <summary>
    /// An explicit mark value wins over the theme: the theme defaults are applied before
    /// <see cref="ChartView.ConfigureMark"/> runs.
    /// </summary>
    [TestCase]
    public void ConfigureMarkWinsOverTheThemeMarkDefaults()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 160));
        var theme = ChartTheme.Dark().Clone();
        theme.StrokeWidth = 5f;
        theme.CornerRadius = 7f;
        view.CustomTheme = theme;

        view.Kind = ChartKind.Line;
        view.ConfigureMark(mark =>
        {
            if (mark is LineMark line) line.StrokeWidth = 9f;      // explicit mark value
        });
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });
        PumpFrame(view);

        AssertThat(((LineMark)view.Chart!.Marks[0]).StrokeWidth).IsEqual(9f);

        // The same order for a corner radius: the theme value is the starting point, not the last word.
        view.Kind = ChartKind.Bar;
        view.ConfigureMark(mark =>
        {
            if (mark is IntervalMark bar) bar.CornerRadius = 1f;
        });
        PumpFrame(view);

        AssertThat(((IntervalMark)view.Chart!.Marks[0]).CornerRadius).IsEqual(1f);

        Release(view);
    }

    /// <summary>The mark a view builds for a kind under the given theme, after one frame.</summary>
    private static Mark MarkBuiltFor(ChartKind kind, ChartTheme theme)
    {
        var view = AddView(new FakeCanvas2D(), new Vector2(240, 160));
        view.Kind = kind;
        view.CustomTheme = theme;
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });
        PumpFrame(view);

        var mark = view.Chart!.Marks[0];
        Release(view);
        return mark;
    }

    [TestCase]
    public void ChangingTheKindRebuildsTheChart()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(200, 150));
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });
        PumpFrame(view);
        AssertThat(view.Chart!.Marks[0] is IntervalMark).IsTrue();

        view.Kind = ChartKind.Pie;
        PumpFrame(view);

        AssertThat(view.Chart!.Marks[0] is PieMark).IsTrue();

        Release(view);
    }

    /// <summary>
    /// A knob that reaches the chart through the rebuild has to trigger one. These were plain
    /// auto-properties: setting one after the data left the old picture in place until something else marked
    /// the view dirty, while the neighbouring export (<see cref="ChartView.XAxisTickStep"/>,
    /// <see cref="ChartView.XAxisTickCount"/>) always rebuilt.
    /// </summary>
    [TestCase]
    public void ChangingAnAxisOrSectionKnobAfterTheDataRebuildsTheChart()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(400, 300));
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });
        PumpFrame(view);

        var knobs = new (string Knob, Action Set)[]
        {
            ("XAxisTickSpacing", () => view.XAxisTickSpacing = 90f),
            ("XAxisLabelFormat", () => view.XAxisLabelFormat = "0.0"),
            ("XAxisLabelRotation", () => view.XAxisLabelRotation = 45f),
            ("HeatmapMaxCells", () => view.HeatmapMaxCells = 100),
            ("DiagramMaxNodes", () => view.DiagramMaxNodes = 100),
            ("YAxisLabelFormat", () => view.YAxisLabelFormat = "0.0"),
            ("YAxisTickSpacing", () => view.YAxisTickSpacing = 90f),
            ("YAxisAutoScaleMargin", () => view.YAxisAutoScaleMargin = 0.2f),
            ("YAxisNiceDomain", () => view.YAxisNiceDomain = true),
            ("YAxisMinLimit", () => view.YAxisMinLimit = 0f),
            ("YAxisMaxLimit", () => view.YAxisMaxLimit = 50f),
            ("SectionLevels", () => view.SectionLevels = [15f]),
            ("SectionBandFrom", () => view.SectionBandFrom = 5f),
            ("SectionBandTo", () => view.SectionBandTo = 25f),
            ("SectionTarget", () => view.SectionTarget = ChartSectionTarget.X),
            ("SectionColor", () => view.SectionColor = Colors.Red),
            ("SectionDashed", () => view.SectionDashed = false),
            ("SectionLabelFormat", () => view.SectionLabelFormat = "{0:N0}"),
            ("IgnoreContentMinimumSize", () => view.IgnoreContentMinimumSize = true),
        };

        // Rebuild swaps in a fresh Chart, so "the view rebuilt" is exactly "the Chart instance changed".
        var stale = new List<string>();
        foreach (var (knob, set) in knobs)
        {
            var before = view.Chart;
            set();
            PumpFrame(view);
            if (ReferenceEquals(view.Chart, before)) stale.Add(knob);
        }
        AssertThat(string.Join(", ", stale)).IsEqual("");

        Release(view);
    }

    [TestCase]
    public void TheTitleAndDataArePushedToTheChart()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 160));
        view.Title = "Revenue";
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });

        PumpFrame(view);

        AssertThat(view.Chart!.Title).IsEqual("Revenue");
        AssertThat(view.Chart.Marks.Count).IsEqual(1);

        Release(view);
    }

    [TestCase]
    public void HeaderOnlyCsvYieldsNoRowsAndStillRendersCleanly()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(120, 100));
        view.SetCsv("category,value\n");

        PumpFrame(view);

        AssertThat(view.DataRows.Count).IsEqual(0);
        AssertThat(view.Chart!.Marks.Count).IsEqual(1);
        AssertThat(fake.NonFiniteCoordinateCount).IsEqual(0);
        AssertThat(fake.NegativeSizeRectCount).IsEqual(0);

        Release(view);
    }

    // ── Streaming ───────────────────────────────────────────────────────────

    [TestCase]
    public void WindowSizeKeepsOnlyTheNewestRows()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 200));
        view.Kind = ChartKind.Line;
        view.XField = "t";
        view.YField = "value";
        view.WindowSize = 3;

        for (int i = 0; i < 6; i++)
            view.AddRow(Row(("t", i), ("value", i * 10.0)));

        PumpFrame(view);

        AssertThat(view.DataRows.Count).IsEqual(3);
        AssertThat(view.DataRows[0].Get<long>("t")).IsEqual(3L);   // the oldest three are gone
        AssertThat(view.Rows.Count).IsEqual(3);                    // the inspector array follows

        Release(view);
    }

    [TestCase]
    public void AppendingUpdatesTheDataWithoutRebuildingTheChart()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(300, 200));
        view.Kind = ChartKind.Line;
        view.XField = "t";
        view.YField = "value";
        view.WindowSize = 48;
        for (int i = 0; i < 24; i++)
            view.AddRow(Row(("t", i), ("value", i * 2.0)));
        PumpFrame(view);

        var chart = view.Chart;
        var plot = chart!.CurrentPlotArea!.Value;
        view._GuiInput(new InputEventMouseMotion
        {
            Position = new Vector2(plot.X + plot.Width * 0.75f, plot.Y + plot.Height * 0.5f),
        });
        PumpFrame(view, 2);
        int hovered = chart.CurrentHoveredRowIndex;

        view.AddRow(Row(("t", 24), ("value", 48.0)));
        PumpFrame(view);

        // A streaming update must not throw the chart (and its animation/hover state) away.
        AssertThat(ReferenceEquals(view.Chart, chart)).IsTrue();
        AssertThat(view.Chart!.CurrentHoveredRowIndex).IsEqual(hovered);
        AssertThat(view.DataRows.Count).IsEqual(25);

        Release(view);
    }

    // ── Fonts ───────────────────────────────────────────────────────────────

    [TestCase]
    public void TheThemeFontReachesEveryPieceOfText()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(320, 220));
        var font = ThemeDB.FallbackFont;          // any Godot Font resource stands in for a CJK font
        view.CustomTheme = new ChartTheme { Font = font };   // the theme owns the typography

        view.Title = "标题";
        view.XAxisTitle = "月份";
        view.YAxisTitle = "收入";
        view.ColorField = "series";
        view.Legend = LegendPosition.Bottom;
        view.Kind = ChartKind.Bar;
        view.ConfigureMark(m => m.ShowLabel = true);
        view.SetData(new[]
        {
            Row(("category", "一月"), ("value", 1200), ("series", "北区")),
            Row(("category", "二月"), ("value", 1800), ("series", "南区")),
        });

        PumpFrame(view);

        // Hover so the tooltip text is drawn too.
        var plot = view.Chart!.CurrentPlotArea!.Value;
        view._GuiInput(new InputEventMouseMotion
        {
            Position = new Vector2(plot.X + plot.Width * 0.25f, plot.Y + plot.Height - 12f),
        });
        PumpFrame(view, 3);

        AssertThat(fake.TextDraws.Count > 0).IsTrue();

        var missing = fake.TextDraws
            .Where(d => !ReferenceEquals(d.Font.GodotFont, font))
            .Select(d => d.Text)
            .Distinct()
            .ToList();

        AssertThat(string.Join(", ", missing)).IsEqual("");

        Release(view);
    }

    [TestCase]
    public void AThemeFontFamilyCanBeSetWithoutAFontResource()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 160));
        view.CustomTheme = new ChartTheme { FontFamily = "Microsoft YaHei" };
        view.Title = "标题";
        view.SetValues(new[] { ("一月", 1200.0), ("二月", 1800.0) });

        PumpFrame(view);

        AssertThat(fake.TextDraws.Count > 0).IsTrue();
        AssertThat(fake.TextDraws.All(d => d.Font.Family == "Microsoft YaHei")).IsTrue();

        Release(view);
    }

    // ── View exports: preview, axis unit, clear, grouped bars ───────────────

    /// <summary>
    /// <see cref="ChartView.EditorPreview"/> is the editor-only switch: the node consults it in
    /// <c>_EnterTree</c> (<c>Engine.IsEditorHint() &amp;&amp; !EditorPreview</c>), so outside the editor it neither
    /// rebuilds nor repaints anything differently - a data or kind change still does both. What it does take part
    /// in is the rebuild snapshot, so toggling it forces exactly one rebuild.
    /// <para>
    /// The "off means frozen" half of the contract can only be observed where the flag is read, i.e. in the
    /// editor; a test run is not the editor, which this case states explicitly instead of pretending otherwise.
    /// </para>
    /// </summary>
    [TestCase]
    public void EditorPreviewOnlySuppressesThePreviewInsideTheEditor()
    {
        Asserts.RequireSceneTree();

        // The premise of the two assertions below, and what the editor builds the placeholder for instead.
        AssertThat(Engine.IsEditorHint()).IsFalse();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(200, 150));
        view.SetValues(new[] { ("A", 10.0), ("B", 20.0) });
        PumpFrame(view);
        AssertThat(view.Chart is not null).IsTrue();

        // EditorPreview is part of Snapshot.Of, so toggling it rebuilds the chart like any other export.
        var beforeToggle = view.Chart;
        view.EditorPreview = false;
        PumpFrame(view);
        AssertThat(ReferenceEquals(beforeToggle, view.Chart)).IsFalse();

        // Outside the editor the flag is not a rendering switch: the change still rebuilds and still draws.
        var afterToggle = view.Chart;
        int fills = fake.FillCount;
        view.Kind = ChartKind.Pie;
        PumpFrame(view);
        AssertThat(ReferenceEquals(afterToggle, view.Chart)).IsFalse();
        AssertThat(fake.FillCount > fills).IsTrue();

        Release(view);
    }

    /// <summary>
    /// <see cref="ChartView.XAxisUnit"/> reaches the X axis, where the axis zone reports it as part of the axis
    /// tooltip label (<c>"(unit)"</c>, next to the title and the description).
    /// </summary>
    [TestCase]
    public void XAxisUnitReachesTheAxisTooltip()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(280, 180));
        view.XAxisUnit = "s";
        view.SetValues(new[] { ("Jan", 1200.0), ("Feb", 1800.0) });
        PumpFrame(view);

        var plot = view.Chart!.CurrentPlotArea!.Value;
        var xAxisHit = view.Chart.HitTest(new Vector2(plot.X + plot.Width * 0.5f, plot.Y + plot.Height + 5f));

        AssertThat(xAxisHit is not null).IsTrue();
        AssertThat(xAxisHit!.MarkType).IsEqual("XAxis");
        // Only the unit is configured, so the label is exactly the unit - and it is the X axis zone that reports
        // it (the Y axis zone would report the Y unit).
        AssertThat(xAxisHit.Label!).IsEqual("(s)");

        Release(view);
    }

    /// <summary>
    /// <see cref="ChartView.Clear"/> empties both views of the data (the model and the exported array the
    /// inspector edits) and the next frame paints nothing but the background - the bars are gone, not stale.
    /// </summary>
    [TestCase]
    public void ClearEmptiesTheDataAndTheNextFrameIsOnlyBackground()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 180));
        // The inspector path, so both DataRows and Rows start out non-empty (SetData clears Rows on purpose).
        view.Rows =
        [
            new() { { "category", "A" }, { "value", 10.0 } },
            new() { { "category", "B" }, { "value", 20.0 } },
        ];
        PumpFrame(view);

        var background = ChartTheme.Dark().BackgroundColor;
        AssertThat(view.DataRows.Count).IsEqual(2);
        AssertThat(view.Rows.Count).IsEqual(2);
        AssertThat(fake.FillColors.Any(c => c.ToHtml() == background.ToHtml())).IsTrue();
        AssertThat(fake.FillColors.Count(c => c.ToHtml() != background.ToHtml())).IsEqual(2);

        int fillsBefore = fake.FillColors.Count;
        view.Clear();
        PumpFrame(view);

        AssertThat(view.DataRows.Count).IsEqual(0);
        AssertThat(view.Rows.Count).IsEqual(0);

        // Nothing but the background is left: every fill of the new frame is the theme's background colour.
        var newFills = fake.FillColors.Skip(fillsBefore).ToList();
        AssertThat(newFills.Count > 0).IsTrue();
        AssertThat(newFills.All(c => c.ToHtml() == background.ToHtml())).IsTrue();

        Release(view);
    }

    /// <summary>
    /// <see cref="ChartView.GroupedBars"/> is handed to the mark a bar kind builds: the node's own switch for a
    /// scene-only page, with the default (side-by-side off) kept as the mark's own default.
    /// </summary>
    [TestCase]
    public void GroupedBarsIsHandedToTheMark()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var view = AddView(fake, new Vector2(240, 160));
        view.ColorField = "series";
        view.SetData(Series());
        PumpFrame(view);

        AssertThat(view.Chart!.Marks[0] is IntervalMark).IsTrue();
        AssertThat(((IntervalMark)view.Chart!.Marks[0]).GroupedBars).IsFalse();

        view.GroupedBars = true;
        PumpFrame(view);

        AssertThat(((IntervalMark)view.Chart!.Marks[0]).GroupedBars).IsTrue();

        Release(view);
    }

    // ── Default backend (needs a rendering device) ──────────────────────────

    [TestCase]
    public void UsesTheRealBackendAndRendersAFrameByDefault()
    {
        // The Skia backend allocates a RenderingDevice texture; run with `python Tools/run_tests.py --render`
        // (or from the editor) to exercise this path.
        if (RenderingServer.GetRenderingDevice() is null)
        {
            GD.Print("[skip] UsesTheRealBackendAndRendersAFrameByDefault: no rendering device in this run");
            return;
        }

        Asserts.RequireSceneTree();

        var view = new ChartView { Size = new Vector2(256, 192), Kind = ChartKind.Bar };
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.Root.AddChild(view);

        view.SetValues(new[] { ("A", 10.0), ("B", 20.0), ("C", 15.0) });
        PumpFrame(view);

        AssertThat(view.Canvas is SkiaCanvas2DBackend).IsTrue();
        AssertThat(view.Canvas!.Texture is not null).IsTrue();
        AssertThat(view.Chart is not null).IsTrue();

        tree.Root.RemoveChild(view);
        view.Free();
    }
}
