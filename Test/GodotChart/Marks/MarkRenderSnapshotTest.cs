namespace GodotNodeExtension.Tests.GodotChart.Marks;

using System;
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Characterisation snapshots of the 20 mark types: the recorded draw operations of one frame must
/// stay byte-for-byte identical. They protect refactors that are meant to be output-neutral (reusing
/// path/paint objects, reusing contexts) from silently changing what is drawn.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MarkRenderSnapshotTest
{
    /// <summary>
    /// Expected snapshot hash per mark. Regenerate only when a change is intended.
    /// Filled in when <see cref="FakeCanvas2D.Snapshot"/> was extended (stroke widths/colours,
    /// fill opacities, rounded-rect radii, line segments, images, save/restore stack).
    /// Re-recorded after the 1.0 behaviour fixes: <c>SankeyMark</c> draws its node labels through
    /// <c>DrawTextCentered</c> (centre anchor instead of left/right alignment) and <c>HeatmapMark</c>
    /// places its rows through the Y scale (bottom-up) instead of mirroring the category order.
    /// <c>MilestoneMark</c> was re-checked as well (label anchor clamped into the plot) - its hash
    /// already matched, so it did not move again.
    /// <para>
    /// Re-recorded for the five marks whose own default now equals the theme default (the same chart used to
    /// look different depending on whether a <c>ChartView</c> built it or a host built it by hand):
    /// <c>CornerRadius</c> 2/1/0 -> 3 on <c>BoxMark</c> / <c>CandlestickMark</c> / <c>HeatmapMark</c>, and
    /// <c>StrokeWidth</c> 1.5 -> 2 on <c>ViolinMark</c> and <c>RangeAreaMark</c>. None of them changed shape or
    /// data, only the radius/width the inspector shows by default.
    /// </para>
    /// <para>
    /// <c>TimelineMark</c> moved with a real fix: the mark read its category from a fixed channel, so when the
    /// ordinal scale sat on the other one (as it does when a host hands the marks its own scales) it read the
    /// numeric value field, and the ordinal scale reported every unknown category as 0 - both bars landed in
    /// the same lane. It now resolves the category from the channel whose scale is ordinal, and skips a row
    /// whose category the scale does not know instead of drawing it on the first lane.
    /// </para>
    /// <para>
    /// Re-recorded for the two marks whose ring bands now go through the shared
    /// <c>ShapeGeometry.AddRingBand</c>: chord and sunburst relied on the arc call moving to its own start
    /// point, and the shared helper spells that move out (an explicit <c>MoveTo</c> before the outer arc). The
    /// pixels are identical - the recorded path just has the point the backend used to add implicitly.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> Expected = new()
{
        ["IntervalMark"] = "D3E61228336B505F",
        ["LineMark"] = "F662208BCF0D8372",
        ["PointMark"] = "9B851C59AA6005B4",
        ["PieMark"] = "2FB16388561A788E",
        ["RadarMark"] = "D8969055674A38F2",
        ["ViolinMark"] = "52F370BCF0109A2A",
        ["WaffleMark"] = "FD7965AA14E94608",
        ["FunnelMark"] = "84AC3F8ECA3F0D42",
        ["GaugeMark"] = "CC90B69E23B6E0F0",
        ["SankeyMark"] = "AF95C547B7EFDE0C",
        ["ChordMark"] = "A7A77E6A5EB82FF2",
        ["SunburstMark"] = "9439D5F4F183015B",
        ["TreemapMark"] = "ACBEAD55F1CDC170",
        ["TimelineMark"] = "3273D2A642AC4F63",
        ["LollipopMark"] = "8CE87EF5AA764C7F",
        ["RangeAreaMark"] = "EBCB1765B10C81D7",
        ["BoxMark"] = "8529B3F7CBBD2D4D",
        ["CandlestickMark"] = "3E7B562D38627F33",
        ["HeatmapMark"] = "FA6F2FC3794C11D1",
        ["MilestoneMark"] = "5AEF214ED028D18E",
        // An annotation mark: the reference lines and the band it draws from its levels.
        ["SectionMark"] = "C52FFB5630D326B7",
    };

    [TestCase]
    public void MarkSnapshotsAreUnchanged()
    {
        var actual = new List<string>();
        foreach (var c in MarkCases.All)
        {
            var canvas = new FakeCanvas2D();
            MarkCases.Build(canvas, c).Render();
            actual.Add($"{c.Name}={canvas.SnapshotHash()}");
            GD.Print($"SNAPSHOT {c.Name}={canvas.SnapshotHash()}");
        }

        var problems = actual.Where(line =>
        {
            var parts = line.Split('=');
            return Expected.TryGetValue(parts[0], out var want) && want != parts[1];
        }).ToList();

        // A name that is not in the table would be silently ignored, so a new or renamed case would shrink
        // the coverage without failing. The two sets have to match exactly.
        var caseNames = MarkCases.All.Select(c => c.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var expectedNames = Expected.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
        if (!caseNames.SequenceEqual(expectedNames))
            problems.Add("case/expectation mismatch - only in cases: " +
                         string.Join(",", caseNames.Except(expectedNames)) + "; only in expectations: " +
                         string.Join(",", expectedNames.Except(caseNames)));

        if (problems.Count > 0) WriteActualSnapshots(actual);

        AssertThat(string.Join("\n", problems)).IsEqual("");

    }

    /// <summary>
    /// Write the hashes that were just computed, one <c>Name=HASH</c> per line, so a regenerated table can be
    /// taken from a file: a runner truncates a long failure message (only its first line survives), and the
    /// engine prints do not reach every log a client keeps.
    /// </summary>
    private static void WriteActualSnapshots(List<string> actual)
    {
        // The component's own tmp/ directory: a hashes dump is this suite's scratch, not a tool log
        // (AGENTS.md §2 keeps tmp/logs/ for the tools' own logs).
        DirAccess.MakeDirRecursiveAbsolute("res://tmp/GodotChart");
        using var file = FileAccess.Open("res://tmp/GodotChart/mark-snapshots-actual.txt", FileAccess.ModeFlags.Write);
        if (file == null) return;

        foreach (string line in actual) file.StoreLine(line);
    }
}
