namespace GodotNodeExtension.Tests.GodotChart;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotSkia;
using static GdUnit4.Assertions;

/// <summary>
/// Creating, rendering and destroying <see cref="ChartView"/>s over and over. A view owns a surface, a chart,
/// a theme and an internal host node; all of them are native-backed, so a leak shows up as a counter that
/// drifts or a managed heap that grows with the cycle count - long before the machine runs out of memory.
/// <para>
/// The <see cref="SkiaCanvasTexture2D"/> equivalents live in <c>Test/GodotSkia/SkiaResourceLifecycleTest</c>;
/// this one covers the node layer on top of them.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ChartViewLifecycleIntegrationTest
{
    /// <summary>
    /// Cycles per case: enough that a per-instance leak is visible, small enough to stay quick. Each cycle
    /// renders two frames through the real backend, so it is much heavier than a bare texture cycle.
    /// </summary>
    private const int Cycles = 30;

    /// <summary>
    /// Managed heap growth the cycles may leave behind (bytes). A bound for "one instance leaked per cycle",
    /// not a tight budget: a leaked view would add far more than a few kilobytes each.
    /// </summary>
    private const long ManagedGrowthBudget = 4L * 1024 * 1024;

    private static ChartView AddView()
    {
        var c = ChartRenderCase.All[0];
        var view = ChartRenderHarness.AddView(c, ChartRenderHarness.ViewSize);
        view.SetData(c.Rows);
        return view;
    }

    /// <summary>
    /// Thirty views on and off the tree: while one is alive it holds exactly one shared-context reference,
    /// releasing it gives the reference back, and neither the counters nor the managed heap grow with the
    /// number of cycles.
    /// </summary>
    [TestCase]
    public void RepeatedCreateRenderAndReleaseKeepsTheContextBalanced()
    {
        const string name = nameof(RepeatedCreateRenderAndReleaseKeepsTheContextBalanced);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        int baselineRefs = SkiaCanvasTexture2D.SharedGrContextRefCount;
        int baselineCreates = SkiaCanvasTexture2D.GrContextCreateCount;
        long beforeBytes = GC.GetTotalMemory(forceFullCollection: true);

        var problems = new System.Collections.Generic.List<string>();
        for (int i = 0; i < Cycles; i++)
        {
            var view = AddView();
            try
            {
                ChartRenderHarness.Pump(view);
                if (SkiaCanvasTexture2D.SharedGrContextRefCount != baselineRefs + 1)
                {
                    problems.Add($"cycle {i}: {SkiaCanvasTexture2D.SharedGrContextRefCount} context reference(s), " +
                                 $"expected {baselineRefs + 1}");
                }
                if (ChartRenderHarness.Pixels(view) is null) problems.Add($"cycle {i}: no texture");
            }
            finally
            {
                ChartRenderHarness.Release(view);
            }
        }

        AssertThat(string.Join("; ", problems)).IsEqual("");
        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs);

        long growth = GC.GetTotalMemory(forceFullCollection: true) - beforeBytes;
        int createGrowth = SkiaCanvasTexture2D.GrContextCreateCount - baselineCreates;
        GD.Print($"{Cycles} view cycles: grcontext creates +{createGrowth}, managed heap growth {growth} bytes");
        AssertThat(growth < ManagedGrowthBudget).IsTrue();
    }

    /// <summary>
    /// Leaving the tree drops the chart instance (the surface that owned the canvas is gone) and re-entering
    /// builds a working one again - the node is meant to survive being re-parented.
    /// </summary>
    [TestCase]
    public void LeavingAndReenteringTheTreeRebuildsTheChart()
    {
        const string name = nameof(LeavingAndReenteringTheTreeRebuildsTheChart);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        var view = AddView();
        try
        {
            ChartRenderHarness.Pump(view);
            AssertThat(view.Chart is not null).IsTrue();

            var root = ((SceneTree)Engine.GetMainLoop()).Root;
            root.RemoveChild(view);
            AssertThat(view.Chart is null).IsTrue();

            root.AddChild(view);
            ChartRenderHarness.Pump(view);

            AssertThat(view.Chart is not null).IsTrue();
            AssertThat(ChartRenderHarness.ContentRatio(view, out _) > 0f).IsTrue();
        }
        finally
        {
            ChartRenderHarness.Release(view);
        }
    }

    /// <summary>
    /// Releasing a view gives its shared-context reference back exactly once. Removing the node from the tree
    /// releases the surface (which owns the canvas), and <c>Free</c> then tears the node down - the two paths
    /// together must leave the counter where it started.
    /// </summary>
    [TestCase]
    public void ReleasingAViewGivesItsContextReferenceBack()
    {
        const string name = nameof(ReleasingAViewGivesItsContextReferenceBack);
        if (ChartRenderHarness.NoRenderingDevice(name)) return;

        int baselineRefs = SkiaCanvasTexture2D.SharedGrContextRefCount;

        var view = AddView();
        ChartRenderHarness.Pump(view);
        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs + 1);

        ChartRenderHarness.Release(view);
        AssertThat(SkiaCanvasTexture2D.SharedGrContextRefCount).IsEqual(baselineRefs);
    }
}
