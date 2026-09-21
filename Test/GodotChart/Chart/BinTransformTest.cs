namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for <see cref="BinTransform"/> (ChartCore.cs): binning a numeric column into equal-width
/// intervals. Covers the Sturges-rule default, an explicit bin count or width, degenerate and
/// negative domains, the inclusive maximum, and the dropped values (unusable entries and
/// non-finite numbers such as NaN or infinity).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BinTransformTest
{
    /// <summary>Approximate comparison for computed floating point values.</summary>
    private static void Approx(double actual, double expected, double tolerance = 1e-6)
        => AssertThat(Math.Abs(actual - expected) <= tolerance).IsTrue();

    [TestCase]
    public void BinTransformReturnsEmptyForEmptyOrUnusableInput()
    {
        var transform = new BinTransform { Field = "value" };

        AssertThat(transform.Apply([]).Count).IsEqual(0);

        // no row carries a usable number in the field
        var unusable = new List<DataRow>
        {
            TestContexts.Row(("other", 1.0)),
            TestContexts.Row(("value", "abc")),
            TestContexts.Row(("value", null)),
        };
        AssertThat(transform.Apply(unusable).Count).IsEqual(0);
    }

    [TestCase]
    public void BinTransformReturnsOneBinForEqualValues()
    {
        var data = new List<DataRow>
        {
            TestContexts.Row(("value", 10.0)),
            TestContexts.Row(("value", 10.0)),
            TestContexts.Row(("value", 10.0)),
        };

        var bins = new BinTransform { Field = "value" }.Apply(data);

        AssertThat(bins.Count).IsEqual(1);
        Approx(bins[0].Get<double>("BinStart"), 9.5);
        Approx(bins[0].Get<double>("BinEnd"), 10.5);
        Approx(bins[0].Get<double>("BinMid"), 10.0);
        AssertThat(bins[0].Get<int>("Count")).IsEqual(3);
    }

    [TestCase]
    public void BinTransformUsesSturgesRuleByDefault()
    {
        var data = new List<DataRow>();
        for (int i = 1; i <= 8; i++)
            data.Add(TestContexts.Row(("value", (double)i)));

        var bins = new BinTransform { Field = "value" }.Apply(data);

        // ceil(1 + log2(8)) = 4
        AssertThat(bins.Count).IsEqual(4);
        Approx(bins[0].Get<double>("BinStart"), 1.0);
        Approx(bins[3].Get<double>("BinEnd"), 8.0);
        AssertThat(bins.Sum(b => b.Get<int>("Count"))).IsEqual(8);
    }

    [TestCase]
    public void BinTransformHonoursAnExplicitBinCount()
    {
        var data = new List<DataRow>
        {
            TestContexts.Row(("value", 0.0)),
            TestContexts.Row(("value", 5.0)),
            TestContexts.Row(("value", 10.0)),
        };

        var bins = new BinTransform { Field = "value", BinCount = 2 }.Apply(data);

        AssertThat(bins.Count).IsEqual(2);
        Approx(bins[0].Get<double>("BinStart"), 0);
        Approx(bins[0].Get<double>("BinEnd"), 5);
        Approx(bins[0].Get<double>("BinMid"), 2.5);
        Approx(bins[1].Get<double>("BinStart"), 5);
        Approx(bins[1].Get<double>("BinEnd"), 10);
        Approx(bins[1].Get<double>("BinMid"), 7.5);
        AssertThat(bins[0].Get<int>("Count")).IsEqual(1);
        AssertThat(bins[1].Get<int>("Count")).IsEqual(2);

        // every output row carries the four documented fields
        foreach (var bin in bins)
        {
            AssertThat(bin.Has("BinStart")).IsTrue();
            AssertThat(bin.Has("BinEnd")).IsTrue();
            AssertThat(bin.Has("BinMid")).IsTrue();
            AssertThat(bin.Has("Count")).IsTrue();
        }
    }

    [TestCase]
    public void BinTransformClampsANonPositiveBinCountToOne()
    {
        var data = new List<DataRow>
        {
            TestContexts.Row(("value", 0.0)),
            TestContexts.Row(("value", 5.0)),
            TestContexts.Row(("value", 10.0)),
        };

        foreach (int binCount in new[] { 0, -5 })
        {
            var bins = new BinTransform { Field = "value", BinCount = binCount }.Apply(data);

            AssertThat(bins.Count).IsEqual(1);
            Approx(bins[0].Get<double>("BinStart"), 0);
            Approx(bins[0].Get<double>("BinEnd"), 10);
            AssertThat(bins[0].Get<int>("Count")).IsEqual(3);
        }
    }

    [TestCase]
    public void BinTransformPrefersBinWidthOverBinCount()
    {
        var data = new List<DataRow>
        {
            TestContexts.Row(("value", 0.0)),
            TestContexts.Row(("value", 5.0)),
            TestContexts.Row(("value", 10.0)),
        };

        var bins = new BinTransform { Field = "value", BinWidth = 2.5, BinCount = 100 }.Apply(data);

        AssertThat(bins.Count).IsEqual(4);   // ceil(10 / 2.5), the BinCount is ignored
        Approx(bins[0].Get<double>("BinEnd") - bins[0].Get<double>("BinStart"), 2.5);
        AssertThat(bins.Sum(b => b.Get<int>("Count"))).IsEqual(3);
    }

    [TestCase]
    public void BinTransformClampsTheMaximumIntoTheLastBin()
    {
        var data = new List<DataRow>
        {
            TestContexts.Row(("value", 0.0)),
            TestContexts.Row(("value", 10.0)),
        };

        var bins = new BinTransform { Field = "value", BinCount = 3 }.Apply(data);

        AssertThat(bins.Count).IsEqual(3);
        AssertThat(bins[2].Get<int>("Count")).IsEqual(1);   // the maximum is not dropped
        AssertThat(bins.Sum(b => b.Get<int>("Count"))).IsEqual(2);
    }

    [TestCase]
    public void BinTransformHandlesANegativeDomain()
    {
        var data = new List<DataRow>
        {
            TestContexts.Row(("value", -10.0)),
            TestContexts.Row(("value", -5.0)),
            TestContexts.Row(("value", -1.0)),
        };

        var bins = new BinTransform { Field = "value", BinCount = 3 }.Apply(data);

        AssertThat(bins.Count).IsEqual(3);
        Approx(bins[0].Get<double>("BinStart"), -10);
        Approx(bins[2].Get<double>("BinEnd"), -1);
        AssertThat(bins[2].Get<int>("Count")).IsEqual(1);
        AssertThat(bins.Sum(b => b.Get<int>("Count"))).IsEqual(3);
    }

    [TestCase]
    public void BinTransformSkipsNonFiniteValues()
    {
        // A NaN cannot be compared or binned, so it is dropped before the range and the indices are
        // computed; it must not poison the domain or land in an arbitrary bin.
        var data = new List<DataRow>
        {
            TestContexts.Row(("value", 5.0)),
            TestContexts.Row(("value", double.NaN)),
            TestContexts.Row(("value", 9.0)),
        };

        var bins = new BinTransform { Field = "value", BinCount = 2 }.Apply(data);

        AssertThat(bins.Count).IsEqual(2);
        AssertThat(bins.Sum(b => b.Get<int>("Count"))).IsEqual(2);   // only the two finite values
        Approx(bins[0].Get<double>("BinStart"), 5);
        Approx(bins[1].Get<double>("BinEnd"), 9);

        // A column made only of non-finite numbers yields no bins at all.
        var allNonFinite = new List<DataRow>
        {
            TestContexts.Row(("value", double.NaN)),
            TestContexts.Row(("value", double.PositiveInfinity)),
            TestContexts.Row(("value", double.NegativeInfinity)),
        };
        AssertThat(new BinTransform { Field = "value", BinCount = 2 }.Apply(allNonFinite).Count).IsEqual(0);
    }
}
