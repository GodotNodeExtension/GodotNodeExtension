namespace GodotNodeExtension.Tests.GodotChart;

using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;
using GodotNodeExtension.Component.GodotSkia;
using static GdUnit4.Assertions;

/// <summary>
/// Different consumers of the same GPU bridge side by side: a <see cref="ChartView"/> (which hosts its own
/// surface), a bare <see cref="Canvas2DControl"/> driven by a hand-built <see cref="Chart"/>, and a raw
/// <see cref="SkiaCanvasTexture2D"/>. They share one GRContext and one Vulkan queue, which is the situation
/// a real project is in as soon as it uses two of these components - and the one where a leak or a stale
/// layout in any of them would take the others down with it.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartMixedComponentsIntegrationTest
{
    private static readonly Vector2 ViewSize = new(240f, 160f);

    /// <summary>
    /// All three kinds of consumer at once still build <b>one</b> GPU context, all of them draw, and a chart
    /// view keeps its picture while another kind is created, drawn and destroyed around it.
    /// </summary>
    [TestCase]
    public void MixedConsumersShareOneContextWithoutDisturbingEachOther()
    {
        const string name = nameof(MixedConsumersShareOneContextWithoutDisturbingEachOther);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        int baselineRefs = SkiaCanvasTexture2D.SharedGrContextRefCount;
        int baselineCreates = SkiaCanvasTexture2D.GrContextCreateCount;

        var node = new Canvas2DControl { Size = ViewSize, Name = "BareCanvas" };
        var view = ChartRenderHarness.AddView(ChartRenderCase.All[0], ViewSize);
        var raw = new SkiaCanvasTexture2D(64, 64);

        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        root.AddChild(node);
        try
        {
            var c = ChartRenderCase.All[0];
            view.SetData(c.Rows);
            ChartRenderHarness.Pump(view, 2);

            var chartViewFrame = ChartRenderHarness.Pixels(view);
            AssertThat(chartViewFrame is not null).IsTrue();
            ulong before = ChartRenderHarness.PixelFingerprint(chartViewFrame!);

            // The bare host draws its own chart (the "several charts on one canvas" pattern).
            AssertThat(node.Canvas is not null).IsTrue();
            var bare = new Chart(node.Canvas!) { Width = ViewSize.X, Height = ViewSize.Y };
            bare.Data(c.Rows);
            bare.Mark(new IntervalMark());
            bare.Encode(Channel.X, c.XField);
            bare.Encode(Channel.Y, c.YField);
            bare.Render();
            node._Process(0.016);

            // ...and the raw texture draws directly through the Skia API.
            raw.Canvas!.Clear(new SkiaSharp.SKColor(200, 60, 60, 255));
            raw.UpdateTexture();
            using (var rawPixels = raw.GetImage())
                AssertThat(rawPixels.GetPixel(32, 32).ToSkColor().Red).IsEqual((byte)200);

            // Three consumers, one context.
            int createGrowth = SkiaCanvasTexture2D.GrContextCreateCount - baselineCreates;
            GD.Print($"mixed consumers: grcontext creates +{createGrowth}");
            AssertThat(createGrowth).IsEqual(1);

            // The chart view is unmoved by everything that happened around it.
            ChartRenderHarness.Pump(view, 1);
            var after = ChartRenderHarness.Pixels(view);
            AssertThat(after is not null).IsTrue();
            AssertThat(ChartRenderHarness.PixelFingerprint(after!)).IsEqual(before);

            // Tearing the other consumers down must not touch the view's surface either.
            raw.Dispose();
            bare.Dispose();
            ChartRenderHarness.Pump(view, 1);
            var survivor = ChartRenderHarness.Pixels(view);
            AssertThat(survivor is not null).IsTrue();
            AssertThat(ChartRenderHarness.PixelFingerprint(survivor!)).IsEqual(before);
        }
        finally
        {
            raw.Dispose();
            ChartRenderHarness.Release(view);
            root.RemoveChild(node);
            node.Free();
        }

        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs);
    }

    /// <summary>
    /// A <see cref="Canvas2DControl"/> keeps presenting its texture while a chart view next to it is
    /// resized: each surface sizes itself from its own node, so one resizing must not resize the other.
    /// </summary>
    [TestCase]
    public void ResizingOneConsumerLeavesTheOtherAlone()
    {
        const string name = nameof(ResizingOneConsumerLeavesTheOtherAlone);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        var node = new Canvas2DControl { Size = ViewSize, Name = "BareCanvas" };
        var view = ChartRenderHarness.AddView(ChartRenderCase.All[2], ViewSize);
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        root.AddChild(node);
        try
        {
            var c = ChartRenderCase.All[2];
            view.SetData(c.Rows);
            ChartRenderHarness.Pump(view, 2);

            var viewSurface = ChartRenderHarness.Pixels(view);
            AssertThat(viewSurface is not null).IsTrue();
            int viewWidthBefore = viewSurface!.GetWidth();
            int viewHeightBefore = viewSurface.GetHeight();

            node.Size = ChartRenderHarness.ResizedViewSize;
            node._Process(0.016);

            var bare = node.Texture;
            AssertThat(bare is not null).IsTrue();
            AssertThat(bare!.GetWidth()).IsEqual((int)ChartRenderHarness.ResizedViewSize.X);
            AssertThat(bare.GetHeight()).IsEqual((int)ChartRenderHarness.ResizedViewSize.Y);

            // The view kept its own size and its picture.
            var viewAfter = ChartRenderHarness.Pixels(view);
            AssertThat(viewAfter is not null).IsTrue();
            AssertThat(viewAfter!.GetWidth()).IsEqual(viewWidthBefore);
            AssertThat(viewAfter.GetHeight()).IsEqual(viewHeightBefore);
        }
        finally
        {
            ChartRenderHarness.Release(view);
            root.RemoveChild(node);
            node.Free();
        }
    }
}
