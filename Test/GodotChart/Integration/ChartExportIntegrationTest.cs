namespace GodotNodeExtension.Tests.GodotChart;

using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// The export path: <see cref="ChartView.SavePng"/> writes what the view is showing, at the size it is shown
/// in. That is what a tool, a build script or a documentation shot needs - and why the method lives on the node:
/// a <see cref="Chart"/> is bound to its canvas at construction, so rendering the same chart at another size
/// would be a different chart.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartExportIntegrationTest
{
    private const string Suite = nameof(ChartExportIntegrationTest);
    private const string PngPath = "user://chart-export-test.png";

    [TestCase]
    public void TheViewsSurfaceIsWrittenToAPng()
    {
        if (ChartRenderHarness.NoRenderingDevice(Suite)) return;

        var view = ChartRenderHarness.AddView(ChartKind.Line, ChartRenderHarness.ViewSize);
        try
        {
            ChartRenderHarness.Pump(view, 2);
            AssertThat(view.SavePng(PngPath)).IsTrue();

            using var file = FileAccess.Open(PngPath, FileAccess.ModeFlags.Read);
            AssertThat(file is not null).IsTrue();

            var image = new Image();
            AssertThat(image.LoadPngFromBuffer(file!.GetBuffer((long)file.GetLength()))).IsEqual(Error.Ok);
            AssertThat(image.GetWidth()).IsEqual((int)ChartRenderHarness.ViewSize.X);
            AssertThat(image.GetHeight()).IsEqual((int)ChartRenderHarness.ViewSize.Y);

            // The file has to hold a picture, not an empty frame: the chart's own background is in the corner,
            // so anything that differs from it is content.
            var background = image.GetPixel(2, 2);
            int content = 0;
            for (int y = 0; y < image.GetHeight(); y += 3)
            {
                for (int x = 0; x < image.GetWidth(); x += 3)
                {
                    if (!ChartRenderHarness.IsBackground(image.GetPixel(x, y), background)) content++;
                }
            }
            AssertThat(content).IsGreater(0);

            // ...and it has to be the frame the view is showing right now: a stale surface, a wrong palette or
            // a layer from an older frame would still satisfy "some non-background pixels". IsBackground
            // carries the comparison's tolerance, so the two images are walked pixel by pixel within it.
            using var live = view.Texture?.GetImage();
            AssertThat(live is not null).IsTrue();
            if (live is not null)
            {
                int stale = 0;
                for (int y = 0; y < image.GetHeight(); y += 2)
                {
                    for (int x = 0; x < image.GetWidth(); x += 2)
                    {
                        if (!ChartRenderHarness.IsBackground(image.GetPixel(x, y), live.GetPixel(x, y))) stale++;
                    }
                }
                AssertThat(stale).IsEqual(0);
            }
        }
        finally
        {
            ChartRenderHarness.Release(view);
            if (FileAccess.FileExists(PngPath)) DirAccess.RemoveAbsolute(PngPath);
        }
    }

    /// <summary>Nothing to write is reported instead of throwing: a headless or pathless call stays usable.</summary>
    [TestCase]
    public void AnEmptyPathIsRejected()
    {
        var view = ChartRenderHarness.AddView(ChartKind.Line, ChartRenderHarness.ViewSize);
        try
        {
            AssertThat(view.SavePng("")).IsFalse();
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }
}
