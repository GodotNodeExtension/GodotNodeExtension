namespace GodotNodeExtension.Tests.GodotChart;

using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// The geographic layer on a real rendering device: a Cartesian chart that carries a coordinate frame
/// and a configured map viewport has to present the same pixels it presented before, and still hear
/// about the viewport change.
/// <para>
/// This is the pixel-level half of the guarantee <c>ChartGeoViewportTest</c> checks on the recorded
/// draw calls: the projection layer of a frame is added to every chart, so "the 2D picture did not
/// move" is the property that lets it be added at all.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class ChartGeoIsolationIntegrationTest
{
    /// <summary>Configuring the geographic layer of a Cartesian chart must not change one pixel.</summary>
    [TestCase]
    public void AConfiguredGeoViewportChangesNoPixelOfACartesianView()
    {
        if (ChartRenderHarness.NoRenderingDevice(nameof(AConfiguredGeoViewportChangesNoPixelOfACartesianView)))
            return;

        ChartView? plain = null;
        ChartView? geographic = null;
        try
        {
            plain = ChartRenderHarness.AddView(ChartKind.Bar, ChartRenderHarness.ResizedViewSize);
            geographic = ChartRenderHarness.AddView(ChartKind.Bar, ChartRenderHarness.ResizedViewSize);
            var values = new[] { ("A", 10.0), ("B", 20.0), ("C", 15.0) };
            plain.SetValues(values);
            geographic.SetValues(values);

            AssertThat(ChartRenderHarness.PumpUntilStable(plain)).IsGreater(0);
            AssertThat(ChartRenderHarness.PumpUntilStable(geographic)).IsGreater(0);
            var before = ChartRenderHarness.Pixels(geographic);
            AssertThat(before is not null).IsTrue();

            int layoutBefore = geographic!.Chart!.EffectiveLayoutVersion;
            geographic.Chart.SetGeoViewport(116.4, 39.9, 6.0)
                             .SetGeoPanBounds(new GeoBounds(-180.0, -85.0, 180.0, 85.0));
            AssertThat(geographic.Chart.EffectiveLayoutVersion != layoutBefore).IsTrue();
            AssertThat(ChartRenderHarness.PumpUntilStable(geographic)).IsGreater(0);

            var after = ChartRenderHarness.Pixels(geographic);
            var reference = ChartRenderHarness.Pixels(plain);
            AssertThat(after is not null && reference is not null).IsTrue();

            // Same view, same picture; and the same picture as the view that never heard of geography.
            AssertThat(ChartRenderHarness.PixelFingerprint(after!)).IsEqual(ChartRenderHarness.PixelFingerprint(before!));
            AssertThat(ChartRenderHarness.DifferingPixels(reference!, after!)).IsEqual(0);
        }
        finally
        {
            if (plain is not null) ChartRenderHarness.Release(plain);
            if (geographic is not null) ChartRenderHarness.Release(geographic);
        }
    }
}
