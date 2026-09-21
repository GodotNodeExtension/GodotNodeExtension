namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Marks;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Cross-mark data-boundary safety. A row that cannot be drawn must be skipped, never throw and
/// never contribute non-finite geometry.
/// <para>
/// Missing fields and explicitly null values mean "no value" for every mark: the element is
/// skipped. Non-finite values (NaN / +/-Infinity) are skipped as well - a value without a finite
/// position has nowhere to be drawn - and so is text in a numeric column, so no mark ever emits a
/// non-finite coordinate and a data set that is only partly broken still draws all its valid rows.
/// The "missing means no value" semantics of the numeric helpers exposed by <see cref="Mark"/> are
/// pinned down directly as well.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MarkDataSafetyTest
{
    /// <summary>How the value field of a data set is broken for the cross-mark safety checks.</summary>
    private enum DirtyKind
    {
        /// <summary>The value field is not present on the row.</summary>
        MissingField,

        /// <summary>The value field is present but explicitly null.</summary>
        NamedNull,

        /// <summary>The value field holds NaN or +/-Infinity.</summary>
        NonFinite,

        /// <summary>The value field holds text that is not a number (a colour string, a free-text label).</summary>
        NonNumericString,
    }

    private static string Report(IEnumerable<string> problems)
    {
        var list = problems.ToList();
        return list.Count == 0 ? "" : string.Join("\n", list);
    }

    /// <summary>
    /// Rebuild the rows of <paramref name="c"/> keeping only the category field and breaking the
    /// value field as requested. The rows are fresh objects, so the shared case data stays intact.
    /// </summary>
    private static List<DataRow> DirtyRows(MarkCases.MarkCase c, DirtyKind kind)
    {
        var rows = new List<DataRow>(c.Data.Count);
        for (int i = 0; i < c.Data.Count; i++)
        {
            var source = c.Data[i];
            var copy = new DataRow(2);

            if (source.Has(c.XField))
            {
                object? category = source.Get(c.XField);
                if (category is not null) copy.Set(c.XField, category);
            }

            switch (kind)
            {
                case DirtyKind.MissingField:
                    break; // the value field is simply absent
                case DirtyKind.NamedNull:
                    copy.Set(c.YField, null!);
                    break;
                case DirtyKind.NonFinite:
                    // The rows of the case cycle through the three non-finite values.
                    copy.Set(c.YField, i % 3 == 0 ? double.NaN
                        : i % 3 == 1 ? double.PositiveInfinity
                        : double.NegativeInfinity);
                    break;
                case DirtyKind.NonNumericString:
                    // What a numeric channel actually receives when a column is bound by mistake: a colour
                    // literal, and a free-text placeholder. Both used to raise InvalidCastException from
                    // the numeric helpers, which cost the whole mark's frame.
                    copy.Set(c.YField, i % 2 == 0 ? "#ff0000" : "n/a");
                    break;
            }

            rows.Add(copy);
        }
        return rows;
    }

    private static MarkContext ContextFor(FakeCanvas2D canvas, MarkCases.MarkCase c, List<DataRow> data)
    {
        var encodes = new EncodeSet();
        encodes.Set(Channel.X, new FieldEncode(c.XField));
        encodes.Set(Channel.Y, new FieldEncode(c.YField));
        if (c.ColorField != null)
            encodes.Set(Channel.Color, new FieldEncode(c.ColorField));

        // The scales are fitted from the *pristine* case data, the way the chart fits them for a valid data
        // set - the same thing RenderRows does. Fitting them from the broken rows instead would hand a mark
        // an empty ordinal domain or an ordinal scale where it expects a linear one, and a mark that finds
        // no usable scale returns early: the case then passed without rendering anything at all, which is
        // how the value-channel checks below missed most of the marks for a long time.
        var scales = new ScaleSet();
        scales.Set(Channel.X, FittedScale(c, c.XField, includeZero: false));
        scales.Set(Channel.Y, FittedScale(c, c.YField, includeZero: true));

        return TestContexts.Mark(canvas, data, encodes, scales);
    }

    /// <summary>
    /// Render a mark directly (not through the chart, which swallows render exceptions) with a
    /// broken value field and report the exception it threw, if any, plus its non-finite count.
    /// </summary>
    private static (string? Error, int NonFinite) Render(MarkCases.MarkCase c, DirtyKind kind)
    {
        var canvas = new FakeCanvas2D();
        var ctx = ContextFor(canvas, c, DirtyRows(c, kind));
        try
        {
            c.Create().Render(ctx);
        }
        catch (Exception ex)
        {
            return ($"{c.Name}: {ex.GetType().Name}: {ex.Message}", 0);
        }
        return (null, canvas.NonFiniteCoordinateCount);
    }

    private static List<string> RenderSafely(IEnumerable<MarkCases.MarkCase> cases, DirtyKind kind,
        bool requireNoNonFinite)
    {
        var problems = new List<string>();
        foreach (var c in cases)
        {
            var (error, nonFinite) = Render(c, kind);
            if (error != null) { problems.Add(error); continue; }
            if (requireNoNonFinite && nonFinite > 0)
                problems.Add($"{c.Name}: {nonFinite} non-finite coordinate(s)");
        }
        return problems;
    }

    [TestCase]
    public void NoMarkThrowsOrDrawsNonFiniteGeometryForRowsMissingTheValueField()
        => AssertThat(Report(RenderSafely(MarkCases.All, DirtyKind.MissingField, requireNoNonFinite: true))).IsEqual("");

    [TestCase]
    public void NoMarkThrowsOrDrawsNonFiniteGeometryForRowsWithANamedNullValue()
        => AssertThat(Report(RenderSafely(MarkCases.All, DirtyKind.NamedNull, requireNoNonFinite: true))).IsEqual("");

    // NoMarkThrowsForNonFiniteValues used to sit here: it is exactly the throw half of the case below
    // (requireNoNonFinite: true reports a throw the same way), so it was a duplicate.

    [TestCase]
    public void NoMarkDrawsNonFiniteGeometryForNonFiniteValues()
        => AssertThat(Report(RenderSafely(MarkCases.All, DirtyKind.NonFinite, requireNoNonFinite: true))).IsEqual("");

    /// <summary>
    /// Text in the value column is the case the render path used to die on: the numeric helpers threw
    /// <c>InvalidCastException</c>, <see cref="Chart"/>'s render stage caught it and logged one error, and
    /// the whole mark disappeared from the frame. It now reads as "no value" (a one-time warning names the
    /// field), which is the same contract a missing or non-finite value has.
    /// </summary>
    [TestCase]
    public void NoMarkThrowsOrDrawsNonFiniteGeometryForRowsWithANonNumericValue()
        => AssertThat(Report(RenderSafely(MarkCases.All, DirtyKind.NonNumericString, requireNoNonFinite: true))).IsEqual("");

    /// <summary>
    /// The stacked paths in particular: they build their geometry from <i>accumulated</i> values rather than
    /// from a single scale mapping, so a dirty value has to be skipped before the accumulation. It reaches
    /// the geometry as <c>NaN</c> now instead of raising, and an unguarded <c>NaN</c> would turn the band and
    /// every rectangle after it non-finite.
    /// </summary>
    [TestCase]
    public void StackedMarksSkipNonNumericValuesInsteadOfAccumulatingThem()
    {
        var problems = new List<string>();

        foreach (var stack in new[] { StackMode.None, StackMode.Stack, StackMode.Normalize })
        {
            foreach (var (name, mark) in new (string Name, Mark Mark)[]
            {
                ("IntervalMark", new IntervalMark { Stack = stack }),
                ("LineMark", new LineMark { Stack = stack, ShowLabel = false }),
            })
            {
                var rows = new List<DataRow>
                {
                    TestContexts.Row(("cat", "A"), ("value", 10.0), ("series", "S1")),
                    TestContexts.Row(("cat", "A"), ("value", "#ff0000"), ("series", "S1")),
                    TestContexts.Row(("cat", "B"), ("value", 20.0), ("series", "S1")),
                    TestContexts.Row(("cat", "B"), ("value", "n/a"), ("series", "S1")),
                    TestContexts.Row(("cat", "B"), ("value", 30.0), ("series", "S2")),
                };

                var canvas = new FakeCanvas2D();
                var encodes = new EncodeSet();
                encodes.Set(Channel.X, new FieldEncode("cat"));
                encodes.Set(Channel.Y, new FieldEncode("value"));
                encodes.Set(Channel.Color, new FieldEncode("series"));

                try
                {
                    mark.Render(TestContexts.Mark(canvas, rows, encodes,
                        TestContexts.CategoryScales(StackCategories, 0, 30)));
                }
                catch (Exception ex)
                {
                    problems.Add($"{name}/{stack}: {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                if (canvas.NonFiniteCoordinateCount > 0)
                    problems.Add($"{name}/{stack}: {canvas.NonFiniteCoordinateCount} non-finite coordinate(s)");
            }
        }

        AssertThat(Report(problems)).IsEqual("");
    }

    /// <summary>Categories the stacked checks encode their data against.</summary>
    private static readonly string[] StackCategories = { "A", "B" };

    // ── A broken row is skipped, the remaining rows keep drawing ─────────────

    /// <summary>
    /// The value a broken row carries instead of a number: the NaN the per-mark cases used. The
    /// remaining non-finite values are covered by the all-rows-broken checks above.
    /// </summary>
    private const double BrokenValue = double.NaN;

    /// <summary>
    /// The marks whose per-row skip contract is checked row by row. It is only the expected projection
    /// of <see cref="MarkCases.MarkCase.PerRowSkipContract"/>: the test below compares the two, so a
    /// renamed case or a mark that stops opting in fails instead of quietly shrinking the coverage
    /// (the old hand-written list had grown to cover 8 of the 20 cases without anybody noticing).
    /// </summary>
    private static readonly string[] ExpectedPerRowSkipMarks =
    {
        "BoxMark", "CandlestickMark", "GaugeMark", "IntervalMark",
        "LineMark", "LollipopMark", "PointMark", "ViolinMark",
    };

    /// <summary>The cases that declared the per-row skip contract in <see cref="MarkCases.All"/>.</summary>
    private static IEnumerable<MarkCases.MarkCase> PerRowSkipCases()
        => MarkCases.All.Where(c => c.PerRowSkipContract);

    /// <summary>Clone a row set so a broken copy never mutates the shared case data.</summary>
    private static List<DataRow> Copies(List<DataRow> rows)
    {
        var copies = new List<DataRow>(rows.Count);
        foreach (var row in rows)
        {
            var copy = new DataRow(row.Fields.Count);
            foreach (var (field, value) in row.Fields) copy.Set(field, value);
            copies.Add(copy);
        }
        return copies;
    }

    private static bool IsNumeric(object value)
        => value is byte or sbyte or short or ushort or int or uint or long or ulong
                or float or double or decimal;

    /// <summary>
    /// The scale the chart would auto-fit for one encoded field: numeric columns get a linear scale
    /// (zero-based on the value axis, free-floating on the position axis) and everything else an
    /// ordinal one.
    /// </summary>
    private static IScale FittedScale(MarkCases.MarkCase c, string field, bool includeZero)
    {
        var values = new List<object>();
        foreach (var row in c.Data)
        {
            if (!row.Has(field)) continue;
            object? value = row.Get(field);
            if (value is not null) values.Add(value);
        }

        if (values.Count > 0 && IsNumeric(values[0]))
        {
            var linear = new LinearScale { IncludeZero = includeZero };
            linear.Fit(values);
            return linear;
        }

        var ordinal = new OrdinalScale();
        ordinal.Fit(values);
        return ordinal;
    }

    /// <summary>
    /// Render one data set directly (not through the chart) with the scales the chart would have
    /// fitted for the case, and report the exception it threw, its drawing count and its non-finite
    /// coordinate count.
    /// </summary>
    private static (string? Error, int Drawn, int NonFinite) RenderRows(MarkCases.MarkCase c, List<DataRow> rows)
    {
        var canvas = new FakeCanvas2D();
        var scales = new ScaleSet();
        scales.Set(Channel.X, FittedScale(c, c.XField, includeZero: false));
        scales.Set(Channel.Y, FittedScale(c, c.YField, includeZero: true));

        var encodes = new EncodeSet();
        encodes.Set(Channel.X, new FieldEncode(c.XField));
        encodes.Set(Channel.Y, new FieldEncode(c.YField));
        if (c.ColorField != null)
            encodes.Set(Channel.Color, new FieldEncode(c.ColorField));

        try
        {
            c.Create().Render(TestContexts.Mark(canvas, rows, encodes, scales));
        }
        catch (Exception ex)
        {
            return ($"{c.Name}: {ex.GetType().Name}: {ex.Message}", 0, 0);
        }
        return (null, canvas.FillCount + canvas.StrokeCount + canvas.PathOpCount, canvas.NonFiniteCoordinateCount);
    }

    [TestCase]
    public void ThePerRowSkipContractIsDeclaredForExactlyTheExpectedMarks()
    {
        var declared = PerRowSkipCases().Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var expected = ExpectedPerRowSkipMarks.OrderBy(n => n, StringComparer.Ordinal).ToList();

        AssertThat(string.Join(",", declared)).IsEqual(string.Join(",", expected));
    }

    [TestCase]
    public void ABrokenRowIsSkippedWithoutDisturbingTheOtherRows()
    {
        var problems = new List<string>();
        foreach (var c in PerRowSkipCases())
        {
            var (cleanError, cleanDrawn, _) = RenderRows(c, Copies(c.Data));
            if (cleanError != null) { problems.Add(cleanError); continue; }
            if (cleanDrawn == 0) { problems.Add($"{c.Name}: the intact data drew nothing"); continue; }

            for (int i = 0; i < c.Data.Count; i++)
            {
                var rows = Copies(c.Data);
                rows[i].Set(c.YField, BrokenValue);
                var (error, drawn, nonFinite) = RenderRows(c, rows);
                if (error != null) { problems.Add(error); continue; }

                if (nonFinite > 0)
                    problems.Add($"{c.Name}: row {i} produced {nonFinite} non-finite coordinate(s)");
                else if (drawn == 0)
                    problems.Add($"{c.Name}: row {i} stopped the other rows from drawing");
                else if (drawn > cleanDrawn)
                    problems.Add($"{c.Name}: row {i} added geometry ({drawn} > {cleanDrawn})");
            }
        }

        AssertThat(Report(problems)).IsEqual("");
    }

    // ── Dirty fields beyond the value channel ───────────────────────────────

    /// <summary>
    /// Break one field of one row the way that field is read: a numeric field gets NaN, then +Infinity and
    /// then text (which is what a numeric column actually receives when a column is bound by mistake), a
    /// category field gets a missing value, then a foreign type and then text.
    /// </summary>
    private static void Break(MarkCases.DirtyField field, DataRow row, int variant)
    {
        if (field.Numeric)
        {
            row.Set(field.Name, variant switch
            {
                0 => double.NaN,
                1 => double.PositiveInfinity,
                _ => "#ff0000",
            });
            return;
        }

        row.Set(field.Name, variant switch
        {
            0 => null!,
            1 => 42.0,
            _ => "#ff0000",
        });
    }

    /// <summary>
    /// A mark may also read fields the value-only dirty path never touches: a category field (Heatmap,
    /// Milestone) or the interval endpoints of a Timeline. Each declared dirty field is broken for
    /// every row in turn, and the frame must neither throw nor - for the marks that declare the guard -
    /// emit non-finite geometry.
    /// </summary>
    [TestCase]
    public void DeclaredDirtyFieldsNeverThrowAndKeepTheGeometryFinite()
    {
        var problems = new List<string>();
        var cases = MarkCases.All.Where(c => c.DirtyFields is { Count: > 0 }).ToList();
        if (cases.Count == 0) problems.Add("no mark case declares dirty fields");

        foreach (var c in cases)
        {
            for (int rowIndex = 0; rowIndex < c.Data.Count; rowIndex++)
            {
                foreach (var field in c.DirtyFields!)
                {
                    for (int variant = 0; variant < 3; variant++)
                    {
                        var rows = Copies(c.Data);
                        Break(field, rows[rowIndex], variant);

                        var (error, _, nonFinite) = RenderRows(c, rows);
                        if (error != null)
                        {
                            problems.Add($"{c.Name}.{field.Name}[{rowIndex}]: {error}");
                            continue;
                        }
                        if (field.GuardsNonFinite && nonFinite > 0)
                            problems.Add($"{c.Name}.{field.Name}[{rowIndex}]: {nonFinite} non-finite coordinate(s)");
                    }
                }
            }
        }

        AssertThat(Report(problems)).IsEqual("");
    }

    // ── "missing means no value" semantics of the Mark helpers ──────────────

    [TestCase]
    public void NullFieldValueIsReportedAsMissing()
    {
        var mark = new ProbeMark();
        var row = TestContexts.Row(("num", 3.0), ("value", null));

        AssertThat(double.IsNaN(mark.ReadDouble(row, "value"))).IsTrue();
        AssertThat(double.IsNaN(mark.ConvertToDouble(null, "value"))).IsTrue();
        AssertThat(float.IsNaN(mark.ConvertToSingle(null, "value"))).IsTrue();
        AssertThat(mark.ReadDouble(row, "num")).IsEqual(3.0);
    }

    [TestCase]
    public void FieldPresenceChecksRejectMissingAndNullValues()
    {
        var mark = new ProbeMark();
        var row = TestContexts.Row(("num", 3.0), ("value", null));

        AssertThat(mark.HasOne(row, "num")).IsTrue();
        AssertThat(mark.HasOne(row, "value")).IsFalse();  // present but null
        AssertThat(mark.HasOne(row, "absent")).IsFalse(); // not present at all
        AssertThat(mark.HasAll(row, "num", "num", "num", "num", "value")).IsFalse();
        AssertThat(mark.HasAll(row, "num", "num", "num", "num", "num")).IsTrue();
    }

    [TestCase]
    public void StringFieldsReturnNullInsteadOfThrowing()
    {
        var mark = new ProbeMark();
        var row = TestContexts.Row(("label", "A"), ("value", null));

        AssertThat(mark.ReadString(row, "label")).IsEqual("A");
        AssertThat(mark.ReadString(row, "value") is null).IsTrue();
        AssertThat(mark.ReadString(row, "absent") is null).IsTrue();
    }
}
