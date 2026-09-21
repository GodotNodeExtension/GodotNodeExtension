namespace GodotNodeExtension.Tests.GodotChart.Scales;

using System;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="TimeScale"/>: DateTime mapping with clamping, fitting
/// from DateTime/DateTimeOffset/ISO text/Unix milliseconds/integral ticks, degenerate and reversed
/// ranges, and range-dependent label formatting.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TimeScaleTest
{
    /// <summary>Assert two doubles are within <paramref name="eps"/> (IsEqual is exact for doubles).</summary>
    private static void Approx(double actual, double expected, double eps = 1e-9)
        => AssertThat(Math.Abs(actual - expected) <= eps).IsTrue();

    /// <summary>True when <paramref name="action"/> throws exactly <typeparamref name="T"/>.</summary>
    private static bool Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (T)
        {
            return true;
        }
    }

    /// <summary>Fixed timestamp used by the TimeScale cases (2021-06-15 12:00:00).</summary>
    private static DateTime T0() => new(2021, 6, 15, 12, 0, 0);

    // ── Mapping ────────────────────────────────────────────────────────────

    [TestCase]
    public void TimeScaleMapsEndpointsAndMidpoint()
    {
        var t0 = T0();
        var scale = new TimeScale(t0, t0.AddHours(10));

        Approx(scale.Map(t0), 0.0);
        Approx(scale.Map(t0.AddHours(5)), 0.5);
        Approx(scale.Map(t0.AddHours(10)), 1.0);
    }

    [TestCase]
    public void TimeScaleClampsOutOfRangeDatesByDefault()
    {
        var t0 = T0();
        var scale = new TimeScale(t0, t0.AddHours(10));

        Approx(scale.Map(t0.AddHours(-1)), 0.0);
        Approx(scale.Map(t0.AddHours(11)), 1.0);
    }

    [TestCase]
    public void TimeScaleWithClampDisabledReportsRawPositions()
    {
        var t0 = T0();
        var scale = new TimeScale(t0, t0.AddHours(10)) { Clamp = false };

        Approx(scale.Map(t0.AddHours(-1)), -0.1);
        Approx(scale.Map(t0.AddHours(11)), 1.1);
    }

    [TestCase]
    public void TimeScaleMapReportsNaNForOutOfRangeTicks()
    {
        var t0 = T0();
        var scale = new TimeScale(t0, t0.AddHours(10));

        // A tick count beyond DateTime's range is not a date either, so it is reported the same way.
        AssertThat(double.IsNaN(scale.Map(long.MaxValue))).IsTrue();
        AssertThat(double.IsNaN(scale.Map(long.MinValue))).IsTrue();
    }

    /// <summary>
    /// A value that is not a date has no position: it reports NaN (the contract every scale has) instead of
    /// raising out of <c>Map</c>, where a single bad value used to cost the whole frame.
    /// </summary>
    [TestCase]
    public void TimeScaleMapReportsNaNForNullAndUnparseableValues()
    {
        var t0 = T0();
        var scale = new TimeScale(t0, t0.AddHours(10));

        AssertThat(double.IsNaN(scale.Map(null!))).IsTrue();
        AssertThat(double.IsNaN(scale.Map("not a date"))).IsTrue();
    }

    [TestCase]
    public void TimeScaleMapTreatsEveryIntegralTypeAsDotNetTicks()
    {
        var t0 = T0();
        var scale = new TimeScale(t0, t0.AddHours(10));

        // Convention: an integral CLR value denotes .NET ticks (not text and not Unix ms). A long
        // carrying real ticks therefore lands on its true position.
        Approx(scale.Map(t0.Ticks), 0.0);
        Approx(scale.Map(t0.AddHours(5).Ticks), 0.5);
        Approx(scale.Map(t0.AddHours(10).Ticks), 1.0);

        // Narrower integral types are ticks too: each value is a valid (very early) DateTime, so its
        // position clamps to the axis start. Previously an int fell through to the string parser and
        // threw a FormatException.
        Approx(scale.Map(1234567890), 0.0);        // int
        Approx(scale.Map(1234567890u), 0.0);       // uint
        Approx(scale.Map(1234567890L), 0.0);       // long
        Approx(scale.Map(1234567890ul), 0.0);      // ulong
        Approx(scale.Map((short)1234), 0.0);       // short
        Approx(scale.Map((ushort)1234), 0.0);      // ushort
        Approx(scale.Map((byte)200), 0.0);         // byte
        Approx(scale.Map((sbyte)100), 0.0);        // sbyte
    }

    // ── Fitting ────────────────────────────────────────────────────────────

    [TestCase]
    public void TimeScaleFitWithAnEmptyCollectionKeepsTheDomain()
    {
        var t0 = T0();
        var scale = new TimeScale(t0, t0.AddHours(10));

        scale.Fit(Array.Empty<object>());

        AssertThat(scale.Min == t0).IsTrue();
        AssertThat(scale.Max == t0.AddHours(10)).IsTrue();
    }

    [TestCase]
    public void TimeScaleFitAcceptsIsoStrings()
    {
        var scale = new TimeScale();

        scale.Fit(new object[] { "2020-01-01T00:00:00", "2020-01-02T06:00:00" });

        AssertThat(scale.Min == new DateTime(2020, 1, 1)).IsTrue();
        AssertThat(scale.Max == new DateTime(2020, 1, 2, 6, 0, 0)).IsTrue();
    }

    [TestCase]
    public void TimeScaleFitAcceptsUnixMillisecondsAsDouble()
    {
        var scale = new TimeScale();

        scale.Fit(new object[] { 0.0, 86_400_000.0 });

        AssertThat(scale.Min == new DateTime(1970, 1, 1)).IsTrue();
        AssertThat(scale.Max == new DateTime(1970, 1, 2)).IsTrue();
        Approx(scale.Map(43_200_000.0), 0.5); // 12 h, the middle of the day
    }

    [TestCase]
    public void TimeScaleFitAcceptsDateTimeOffsetAndDropsTheOffset()
    {
        var scale = new TimeScale();

        scale.Fit(new object[] { new DateTimeOffset(2020, 5, 5, 3, 0, 0, TimeSpan.FromHours(2)) });

        // Current convention: only the clock time survives; the +02:00 offset is discarded.
        AssertThat(scale.Min == new DateTime(2020, 5, 5, 3, 0, 0)).IsTrue();
        AssertThat(scale.Max == scale.Min).IsTrue();
    }

    // ── Degenerate and reversed ranges ─────────────────────────────────────

    [TestCase]
    public void TimeScaleDefaultConstructorIsDegenerateAndClamps()
    {
        var scale = new TimeScale();

        AssertThat(scale.Min == DateTime.MinValue).IsTrue();
        AssertThat(scale.Max == DateTime.MinValue).IsTrue();
        AssertThat(scale.Clamp).IsTrue();
        Approx(scale.Map(T0()), 0.5); // range 0 -> centred
    }

    [TestCase]
    public void TimeScaleDegenerateDomainCentresEveryDate()
    {
        var t0 = T0();
        var scale = new TimeScale(t0, t0);

        Approx(scale.Map(t0), 0.5);
        Approx(scale.Map(t0.AddYears(3)), 0.5);
    }

    [TestCase]
    public void TimeScaleReversedDomainCentresEveryDate()
    {
        var t0 = T0();
        var scale = new TimeScale(t0.AddDays(1), t0); // rangeMs < 0

        Approx(scale.Map(t0), 0.5);
        Approx(scale.Map(t0.AddDays(1)), 0.5);
    }

    // ── Formatting ─────────────────────────────────────────────────────────

    [TestCase]
    public void TimeScaleFormatSwitchesOnTheRange()
    {
        var t0 = T0();

        var minutes = new TimeScale(t0, t0.AddMinutes(30)); // range < 1 h
        AssertThat(minutes.Format(t0.AddMinutes(7))).IsEqual("07:00");

        var oneHour = new TimeScale(t0, t0.AddHours(1)); // boundary: exactly 1 h
        AssertThat(oneHour.Format(t0.AddMinutes(30))).IsEqual("12:30");

        var hours = new TimeScale(t0, t0.AddHours(5));
        AssertThat(hours.Format(t0.AddHours(2).AddMinutes(30))).IsEqual("14:30");

        var oneDay = new TimeScale(t0, t0.AddDays(1)); // boundary: exactly 1 day
        AssertThat(oneDay.Format(t0)).IsEqual("06-15");

        var days = new TimeScale(t0, t0.AddDays(10));
        AssertThat(days.Format(t0)).IsEqual("06-15");

        var oneMonth = new TimeScale(t0, t0.AddDays(30)); // boundary: exactly 30 days
        AssertThat(oneMonth.Format(t0)).IsEqual("21-06");

        var months = new TimeScale(t0, t0.AddDays(100));
        AssertThat(months.Format(t0)).IsEqual("21-06");
    }
}
