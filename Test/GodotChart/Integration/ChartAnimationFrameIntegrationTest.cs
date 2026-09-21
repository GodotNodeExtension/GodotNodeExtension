namespace GodotNodeExtension.Tests.GodotChart;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// The entry animation on the engine's real backend. The unit cases drive it with a fake clock and a fake
/// canvas, which says nothing about what the surface ends up showing: a frame sequence has to grow without
/// holes and the last frame of the animation has to be the chart's own settled picture (not one pixel off it).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartAnimationFrameIntegrationTest
{
    private static readonly Vector2 ViewSize = new(320f, 200f);

    /// <summary>The bars of the first case: tall enough that their growth dominates the content count.</summary>
    private static ChartRenderCase Cases => ChartRenderCase.All[0];

    private static ChartView AddAnimatedView(ChartTheme theme)
    {
        var c = Cases;
        var view = new ChartView
        {
            Kind = c.Kind,
            XField = c.XField,
            YField = c.YField,
            ColorField = c.ColorField,
            CustomTheme = theme,
            Size = ViewSize,
        };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(view);
        view.SetData(c.Rows);
        // One frame so the chart exists: Animate() is a call on the chart instance, which the first
        // _Process builds.
        ChartRenderHarness.Pump(view, 1);
        return view;
    }

    /// <summary>Draw one frame at the given entry progress through the real surface.</summary>
    private static float RenderFrameAt(ChartView view, float progress, out ulong fingerprint)
    {
        view.Chart!.Animate(progress);
        view.Repaint();
        ChartRenderHarness.Pump(view, 1);

        var image = ChartRenderHarness.Pixels(view);
        AssertThat(image is not null).IsTrue();
        fingerprint = ChartRenderHarness.PixelFingerprint(image!);
        return ChartRenderHarness.ContentRatio(view, out _);
    }

    /// <summary>
    /// The entry animation grows the picture and ends exactly on the static one: the content measure never
    /// goes backwards (a hole in the sequence would mean a frame that draws less than the one before), the
    /// first frame draws clearly less than the last, and progress 1 is pixel-identical to the same chart
    /// rendered without animating at all.
    /// </summary>
    [TestCase]
    public void TheEntryAnimationGrowsMonotonicallyAndSettlesOnTheStaticFrame()
    {
        const string name = nameof(TheEntryAnimationGrowsMonotonicallyAndSettlesOnTheStaticFrame);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        var theme = ChartTheme.Dark().Clone();
        theme.EnableAnimation = true;

        // Reference frame: the same theme and data, never animated (entry progress 1 throughout).
        ulong settled;
        var reference = AddAnimatedView(theme);
        try
        {
            ChartRenderHarness.Pump(reference, 2);
            var image = ChartRenderHarness.Pixels(reference);
            AssertThat(image is not null).IsTrue();
            settled = ChartRenderHarness.PixelFingerprint(image!);
        }
        finally
        {
            ChartRenderHarness.Release(reference);
        }

        var view = AddAnimatedView(theme);
        try
        {
            var ratios = new List<float>();
            var steps = new[] { 0f, 0.25f, 0.5f, 0.75f, 1f };
            var frames = new List<ulong>();

            foreach (float progress in steps)
            {
                ratios.Add(RenderFrameAt(view, progress, out ulong fingerprint));
                frames.Add(fingerprint);
            }

            var report = new List<string>();
            for (int i = 1; i < ratios.Count; i++)
            {
                // A little tolerance for the antialiasing of the bar edges, but not for a real step back.
                if (ratios[i] < ratios[i - 1] - 0.002f)
                    report.Add($"frame {i} draws less than frame {i - 1}: {ratios[i]:P2} < {ratios[i - 1]:P2}");
            }
            if (ratios[^1] <= ratios[0] + 0.01f)
                report.Add($"the animation grew nothing: {ratios[0]:P2} -> {ratios[^1]:P2}");
            if (frames[0] == frames[^1])
                report.Add("the first and the last frame are the same picture");
            AssertThat(string.Join("; ", report)).IsEqual("");

            // Progress 1 is the end state, and the end state is what a non-animated chart draws.
            AssertThat(frames[^1]).IsEqual(settled);
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }

    /// <summary>
    /// With <see cref="ChartTheme.EnableAnimation"/> off, driving the progress must not animate at all: the
    /// theme is the master switch, so a host that keeps calling <c>Animate</c> still gets the settled picture.
    /// </summary>
    [TestCase]
    public void WithAnimationsDisabledTheSurfaceShowsTheEndState()
    {
        const string name = nameof(WithAnimationsDisabledTheSurfaceShowsTheEndState);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        var theme = ChartTheme.Dark().Clone();
        theme.EnableAnimation = false;

        ulong settled;
        var reference = AddAnimatedView(theme);
        try
        {
            ChartRenderHarness.Pump(reference, 2);
            var image = ChartRenderHarness.Pixels(reference);
            AssertThat(image is not null).IsTrue();
            settled = ChartRenderHarness.PixelFingerprint(image!);
        }
        finally
        {
            ChartRenderHarness.Release(reference);
        }

        var view = AddAnimatedView(theme);
        try
        {
            foreach (float progress in new[] { 0f, 0.3f })
            {
                RenderFrameAt(view, progress, out ulong fingerprint);
                AssertThat(fingerprint).IsEqual(settled);
            }
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }
}
