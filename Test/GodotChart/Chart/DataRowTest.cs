namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for <see cref="DataRow"/> (ChartCore.cs): a bag of named fields that distinguishes a
/// missing field from a present-but-null value. Covers <c>Get</c>/<c>Get&lt;T&gt;</c>,
/// <c>TryGet</c>, <c>Has</c>, fluent <c>Set</c> and the numeric conversion rules
/// (rounding, string parsing, and the exception contract).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class DataRowTest
{
    /// <summary>Approximate comparison for computed floating point values.</summary>
    private static void Approx(double actual, double expected, double tolerance = 1e-6)
        => AssertThat(Math.Abs(actual - expected) <= tolerance).IsTrue();

    /// <summary>Run <paramref name="action"/> and return the exception it threw (null when it did not throw).</summary>
    private static Exception? CaptureException(Action action)
    {
        try { action(); return null; }
        catch (Exception ex) { return ex; }
    }

    [TestCase]
    public void DataRowGetThrowsKeyNotFoundForMissingFields()
    {
        var row = new DataRow().Set("v", 1.0);

        AssertThat(CaptureException(() => row.Get<double>("ghost")) is KeyNotFoundException).IsTrue();
        AssertThat(CaptureException(() => row.Get("ghost")) is KeyNotFoundException).IsTrue();
        AssertThat(row.Has("ghost")).IsFalse();
        Approx(row.Get<double>("v"), 1.0);
    }

    [TestCase]
    public void DataRowGetThrowsInvalidCastForAnUnconvertibleValue()
    {
        var row = new DataRow().Set("v", "abc");

        var ex = CaptureException(() => row.Get<double>("v"));

        AssertThat(ex is InvalidCastException).IsTrue();
        AssertThat(ex!.Message.Contains('v')).IsTrue();
        AssertThat(ex.Message.Contains("Double")).IsTrue();
    }

    [TestCase]
    public void DataRowGetOnANamedNullValueIsNotAKeyError()
    {
        var row = new DataRow().Set("v", null!);

        AssertThat(row.Has("v")).IsTrue();   // the field exists ...
        AssertThat(row.Get("v")).IsNull();
        AssertThat(CaptureException(() => row.Get<double>("v")) is not KeyNotFoundException).IsTrue();
        AssertThat(row.TryGet<double>("v", out _)).IsFalse();   // ... but cannot be read as a number

        // A present-but-null field cannot be converted to a value type, so the typed getter reports
        // InvalidCastException (and names the field in the message) instead of dereferencing null.
        var ex = CaptureException(() => row.Get<double>("v"));
        AssertThat(ex is InvalidCastException).IsTrue();
        AssertThat(ex!.Message.Contains('v')).IsTrue();
        AssertThat(ex.Message.Contains("Double")).IsTrue();
    }

    [TestCase]
    public void DataRowGetReturnsNullForReferenceTypesSetToNull()
    {
        var row = new DataRow().Set("s", null!);

        AssertThat(row.Get<string>("s")).IsNull();

        // TryGet<string> reports success for the same null value, while TryGet<double> reports
        // failure — the two paths disagree about named nulls, which is the current convention.
        AssertThat(row.TryGet<string>("s", out var value)).IsTrue();
        AssertThat(value).IsNull();
    }

    [TestCase]
    public void DataRowNumericConversionRoundsHalfToEven()
    {
        var row = new DataRow().Set("v", 2.7).Set("w", 2.5).Set("x", 3.5);

        AssertThat(row.Get<int>("v")).IsEqual(3);
        AssertThat(row.Get<int>("w")).IsEqual(2);   // banker's rounding
        AssertThat(row.Get<int>("x")).IsEqual(4);
    }

    [TestCase]
    public void DataRowParsesNumericStringsSilently()
    {
        var row = new DataRow().Set("v", "42");

        Approx(row.Get<double>("v"), 42);
        AssertThat(row.Get<int>("v")).IsEqual(42);
        AssertThat(row.Get<string>("v")).IsEqual("42");
    }

    [TestCase]
    public void DataRowHasDistinguishesMissingFromNamedNull()
    {
        var row = new DataRow().Set("v", null!).Set("w", 1.0);

        AssertThat(row.Has("v")).IsTrue();
        AssertThat(row.Has("w")).IsTrue();
        AssertThat(row.Has("zz")).IsFalse();
    }

    [TestCase]
    public void DataRowSetReturnsTheSameRow()
    {
        var row = new DataRow();

        AssertThat(ReferenceEquals(row.Set("a", 1).Set("b", 2), row)).IsTrue();
        AssertThat(row.Has("a")).IsTrue();
        AssertThat(row.Has("b")).IsTrue();
        AssertThat(new DataRow(4).Set("a", 7).Get<int>("a")).IsEqual(7);
    }

    [TestCase]
    public void DataRowTryGetReturnsFalseForMissingAndNullFields()
    {
        var row = new DataRow().Set("v", null!);

        AssertThat(row.TryGet<double>("missing", out _)).IsFalse();
        AssertThat(row.TryGet<double>("v", out _)).IsFalse();
        AssertThat(row.TryGet<int>("v", out _)).IsFalse();

        var ok = new DataRow().Set("n", 5.0);
        AssertThat(ok.TryGet<double>("n", out var value)).IsTrue();
        Approx(value, 5);
        AssertThat(ok.TryGet<DateTime>("n", out _)).IsFalse();   // no coercion between unrelated types
    }
}
