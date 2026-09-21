namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Globalization;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Example.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// The key cycles of the two "knobs" example pages, walked as functions. Each page key maps to one pure
/// function (<c>ChartLayoutDemo.NextRotation</c>, <c>ChartBigDataDemo.NextDecimate</c>, ...); these cases pin
/// that a full walk returns to where it started and that every entry of a cycle is a value the export it feeds
/// accepts - a format string that throws, an aspect ratio that is not a shape or two tick exports set at once
/// would all break the page silently (its readout only prints the values). Which key calls which function is
/// what <c>_Input</c> does per frame and stays a manual check.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ExampleDemoKeysTest
{
    /// <summary>Walk a cycle of <paramref name="length"/> entries and report the index it ends on.</summary>
    /// <param name="length">Entries in the cycle.</param>
    /// <returns>The index after one full walk (0 when the cycle wraps correctly).</returns>
    private static int Walk(int length)
    {
        int index = 0;
        for (int step = 0; step < length; step++)
            index = ChartLayoutDemo.NextIndex(index, length);
        return index;
    }

    // ── ChartLayoutDemo ─────────────────────────────────────────────────────

    [TestCase]
    public void TheLayoutPageRotationCycleWrapsAndStaysAnAngle()
    {
        AssertThat(Walk(ChartLayoutDemo.RotationCount)).IsEqual(0);

        for (int i = 0; i < ChartLayoutDemo.RotationCount; i++)
        {
            float rotation = ChartLayoutDemo.NextRotation(i);
            AssertThat(float.IsFinite(rotation)).IsTrue();
            AssertThat(rotation >= 0f && rotation < 180f).IsTrue();
        }
    }

    [TestCase]
    public void TheLayoutPageTickModesAskForOneDensityAtATime()
    {
        AssertThat(Walk(ChartLayoutDemo.TickModeCount)).IsEqual(0);

        // The first entry is the automatic one: all three exports at their "decide it yourself" value.
        (string _, float autoStep, float autoSpacing, int autoCount) = ChartLayoutDemo.NextTickMode(0);
        AssertThat(autoStep).IsEqual(0f);
        AssertThat(autoSpacing).IsEqual(0f);
        AssertThat(autoCount).IsEqual(0);

        for (int i = 0; i < ChartLayoutDemo.TickModeCount; i++)
        {
            (string name, float step, float spacing, int count) = ChartLayoutDemo.NextTickMode(i);
            AssertThat(name.Length).IsGreater(0);

            // A step in data units, the pixels one label may take and an exact count are alternatives: asking
            // for two of them at once would make the axis apply whichever it reads first.
            int asked = (step > 0f ? 1 : 0) + (spacing > 0f ? 1 : 0) + (count > 0 ? 1 : 0);
            AssertThat(asked).IsLess(2);
        }
    }

    [TestCase]
    public void TheLayoutPageLabelFormatsAllSurviveStringFormat()
    {
        AssertThat(Walk(ChartLayoutDemo.LabelFormatCount)).IsEqual(0);

        // An empty format keeps the scale's own text; every other one is fed to string.Format by the axis, so a
        // placeholder that asks for an argument nobody passes would throw inside the tick loop.
        AssertThat(ChartLayoutDemo.NextLabelFormat(0)).IsEqual("");

        for (int i = 1; i < ChartLayoutDemo.LabelFormatCount; i++)
        {
            string format = ChartLayoutDemo.NextLabelFormat(i);
            AssertThat(format.Length).IsGreater(0);
            AssertThat(string.Format(CultureInfo.InvariantCulture, format, 12.5).Length).IsGreater(0);
        }
    }

    [TestCase]
    public void TheLayoutPageAspectCycleShapesOrFills()
    {
        AssertThat(Walk(ChartLayoutDemo.AspectCount)).IsEqual(0);
        AssertThat(ChartLayoutDemo.NextAspect(0)).IsEqual(0f);      // 0 = leave the shape to the marks

        for (int i = 0; i < ChartLayoutDemo.AspectCount; i++)
        {
            float aspect = ChartLayoutDemo.NextAspect(i);
            AssertThat(float.IsFinite(aspect)).IsTrue();
            AssertThat(aspect >= 0f).IsTrue();
        }
    }

    [TestCase]
    public void TheLayoutPageAlignmentCycleCoversTheThreePositions()
    {
        AssertThat(Walk(ChartLayoutDemo.AlignmentCount)).IsEqual(0);

        var seen = new System.Collections.Generic.List<VerticalAlignment>();
        for (int i = 0; i < ChartLayoutDemo.AlignmentCount; i++)
        {
            VerticalAlignment alignment = ChartLayoutDemo.NextAlignment(i);
            AssertThat(Enum.IsDefined(alignment)).IsTrue();
            AssertThat(seen.Contains(alignment)).IsFalse();
            seen.Add(alignment);
        }

        AssertThat(seen.Count).IsEqual(3);      // Top, Center, Bottom - the whole enum
    }

    [TestCase]
    public void TheLayoutPageLimitCycleCoversEveryCombinationOnce()
    {
        AssertThat(Walk(ChartLayoutDemo.LimitModeCount)).IsEqual(0);

        var seen = new System.Collections.Generic.List<(bool Min, bool Max)>();
        for (int i = 0; i < ChartLayoutDemo.LimitModeCount; i++)
        {
            (string name, bool min, bool max) = ChartLayoutDemo.NextLimitMode(i);
            AssertThat(name.Length).IsGreater(0);
            AssertThat(seen.Contains((min, max))).IsFalse();
            seen.Add((min, max));
        }

        // Both ends fitted, each end pinned, then both: the four ways the two exports can be set.
        AssertThat(seen.Contains((false, false))).IsTrue();
        AssertThat(seen.Contains((true, false))).IsTrue();
        AssertThat(seen.Contains((false, true))).IsTrue();
        AssertThat(seen.Contains((true, true))).IsTrue();
        AssertThat(seen.Count).IsEqual(4);
    }

    // ── ChartBigDataDemo ────────────────────────────────────────────────────

    [TestCase]
    public void TheBigDataPageCyclesCoverTheirExports()
    {
        AssertThat(Walk(ChartBigDataDemo.DecimateCount)).IsEqual(0);
        AssertThat(Walk(ChartBigDataDemo.ZoomFactorCount)).IsEqual(0);
        AssertThat(Walk(ChartBigDataDemo.PanButtonCount)).IsEqual(0);

        var decimations = new System.Collections.Generic.List<DecimateMode>();
        for (int i = 0; i < ChartBigDataDemo.DecimateCount; i++)
        {
            DecimateMode mode = ChartBigDataDemo.NextDecimate(i);
            AssertThat(Enum.IsDefined(mode)).IsTrue();
            AssertThat(decimations.Contains(mode)).IsFalse();
            decimations.Add(mode);
        }

        AssertThat(decimations.Count).IsEqual(3);       // Auto, Off, On - the whole enum

        for (int i = 0; i < ChartBigDataDemo.ZoomFactorCount; i++)
        {
            float factor = ChartBigDataDemo.NextZoomFactor(i);
            AssertThat(float.IsFinite(factor)).IsTrue();
            AssertThat(factor > 1f).IsTrue();           // a zoom step below 1 would zoom the wrong way
        }

        var buttons = new System.Collections.Generic.List<MouseButton>();
        for (int i = 0; i < ChartBigDataDemo.PanButtonCount; i++)
        {
            MouseButton button = ChartBigDataDemo.NextPanButton(i);
            AssertThat(Enum.IsDefined(button)).IsTrue();
            AssertThat(buttons.Contains(button)).IsFalse();
            buttons.Add(button);
        }

        AssertThat(buttons.Count).IsEqual(3);
    }
}
