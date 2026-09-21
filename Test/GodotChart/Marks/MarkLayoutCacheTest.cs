namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System.Collections.Generic;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Contract of the layout cache every mark inherits from <see cref="Mark"/>:
/// <c>Mark.CacheKey</c> / <c>Mark.LayoutCacheKey</c> decide when a cached layout is reused.
/// <para>
/// The key is the tuple (owning chart identity, layout version, data list instance, hidden-series
/// set). These tests pin that tuple down and verify the consequences the marks rely on: a mark
/// shared by two charts keeps one cached layout per chart, switching to the mark's own data
/// invalidates the cache, and hiding a series does too - a mark that keys its cache on a bare
/// version keeps painting the geometry and colours of the previous state.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MarkLayoutCacheTest
{
    /// <summary>Exposes the protected layout cache key so its equality semantics can be asserted.</summary>
    private sealed class CacheKeyProbe : Mark
    {
        /// <inheritdoc />
        public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

        /// <inheritdoc />
        public override void Render(MarkContext ctx) { }

        /// <summary>The layout cache key the base class would use for <paramref name="ctx"/>.</summary>
        public object Key(MarkContext ctx) => CacheKey(ctx);
    }

    private static MarkContext Context(List<DataRow> data, object owner, int layoutVersion,
                                      IReadOnlySet<string>? hiddenSeries = null)
    {
        var ctx = TestContexts.Mark(new FakeCanvas2D(), data, new EncodeSet(), new ScaleSet(),
            layoutVersion: layoutVersion, dataVersion: layoutVersion, hiddenSeries: hiddenSeries);
        ctx.OwnerId = owner;
        return ctx;
    }

    private static List<DataRow> Leaves(double scale = 1.0) =>

    [
        TestContexts.Row(("label", "A"), ("value", 30.0 * scale)),
        TestContexts.Row(("label", "B"), ("value", 20.0 * scale)),
        TestContexts.Row(("label", "C"), ("value", 10.0 * scale)),
    ];

    private static Chart TreemapChart(FakeCanvas2D canvas, TreemapMark mark, List<DataRow> data)
    {
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(data);
        chart.Mark(mark);
        chart.Encode(Channel.X, "label");
        chart.Encode(Channel.Y, "value");
        return chart;
    }

    [TestCase]
    public void LayoutCacheKeyCombinesOwnerVersionDataAndHiddenSeries()
    {
        var probe = new CacheKeyProbe();
        var owner = new object();
        var data = Leaves();

        var baseline = probe.Key(Context(data, owner, layoutVersion: 1));

        // identical owner + version + data instance + hidden set → equal key
        AssertThat(probe.Key(Context(data, owner, layoutVersion: 1)).Equals(baseline)).IsTrue();

        // a different layout version (data, encodes, scales, plot or theme changed) → new key
        AssertThat(probe.Key(Context(data, owner, layoutVersion: 2)).Equals(baseline)).IsFalse();

        // a different owning chart → new key
        AssertThat(probe.Key(Context(data, new object(), layoutVersion: 1)).Equals(baseline)).IsFalse();

        // a different data list instance → new key
        AssertThat(probe.Key(Context(Leaves(), owner, layoutVersion: 1)).Equals(baseline)).IsFalse();

        // a different hidden-series set → new key, even when nothing else moved: the cached layout
        // still contains the series that was just hidden.
        var hidden = new HashSet<string> { "B" };
        AssertThat(probe.Key(Context(data, owner, layoutVersion: 1, hidden)).Equals(baseline)).IsFalse();
        var hiddenKey = probe.Key(Context(data, owner, layoutVersion: 1, hidden));
        var hiddenKeyAgain = probe.Key(Context(data, owner, layoutVersion: 1, hidden));
        AssertThat(hiddenKey.Equals(hiddenKeyAgain)).IsTrue();
    }

    [TestCase]
    public void LayoutCacheKeyUsesTheDataInstanceNotItsContents()
    {
        var probe = new CacheKeyProbe();
        var owner = new object();
        var first = Leaves();
        var second = Leaves(); // same content, different instance

        var firstKey = probe.Key(Context(first, owner, 1));
        var firstKeyAgain = probe.Key(Context(first, owner, 1));
        AssertThat(firstKey.Equals(firstKeyAgain)).IsTrue();
        AssertThat(probe.Key(Context(first, owner, 1)).Equals(probe.Key(Context(second, owner, 1)))).IsFalse();
    }

    [TestCase]
    public void SharedMarkRebuildsItsLayoutForEachOwningChart()
    {
        var mark = new TreemapMark();

        var canvasA = new FakeCanvas2D();
        TreemapChart(canvasA, mark, Leaves()).Render();

        var canvasB = new FakeCanvas2D();
        TreemapChart(canvasB, mark, Leaves()).Render();

        // The owner is part of the key, so the second chart must not reuse the first chart's layout.
        AssertThat(mark.LayoutBuildCount).IsEqual(2);
        AssertThat(canvasA.FillCount > 0).IsTrue();
        AssertThat(canvasB.FillCount > 0).IsTrue();
    }

    [TestCase]
    public void MarkLocalDataSwitchesTheCachedLayout()
    {
        var canvas = new FakeCanvas2D();
        var mark = new TreemapMark();
        var chart = TreemapChart(canvas, mark, Leaves());
        chart.Render();
        AssertThat(mark.LayoutBuildCount).IsEqual(1);

        // Mark.Data overrides the chart data; the cache key's data component changes.
        mark.Data = Leaves(4.0);
        canvas.Rects.Clear();
        chart.Render();

        AssertThat(mark.LayoutBuildCount).IsEqual(2);
    }
}
