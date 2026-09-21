namespace GodotNodeExtension.Tests.Typography;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core.Unicode;
using static GdUnit4.Assertions;

/// <summary>
/// Conformance for the first part of UAX #9: the paragraph level (rules P2 and P3).
/// <para>
/// The check runs Unicode's own <c>BidiCharacterTest.txt</c>, the same way the line-breaking and word-breaking
/// algorithms are checked against their suites. Every line whose base direction is "auto" carries the level the
/// reference implementation resolved, so the rules are compared position by position against the authority rather
/// than against cases written by hand. Lines with an explicit base direction are skipped: they do not exercise P2.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BidiConformanceTest
{
    private const string SuitePath = "res://Test/Typography/ucd/BidiCharacterTest.txt.gz";

    /// <summary>
    /// P2/P3 agree with the reference implementation on every auto-direction line of the official suite.
    /// </summary>
    [TestCase]
    public void ParagraphLevelFollowsTheOfficialSuite()
    {
        string path = ProjectSettings.GlobalizePath(SuitePath);

        if (!File.Exists(path))
        {
            GD.Print($"[bidi] {SuitePath} is missing; the conformance check was skipped");
            return;
        }

        int checkedLines = 0;
        int mismatches = 0;
        string firstMismatch = string.Empty;

        foreach (string line in ReadSuite(path))
        {
            string[] fields = line.Split(';');

            if (fields.Length < 4)
                continue;

            // Field 2 is the requested base direction: 0 = left-to-right, 1 = right-to-left, 2 = auto. Only "auto"
            // asks P2/P3 to decide.
            if (!int.TryParse(fields[1], out int baseDirection) || baseDirection != 2)
                continue;

            List<int> codePoints = ParseCodePoints(fields[0]);

            if (!int.TryParse(fields[2], out int expectedLevel))
                continue;

            int actual = BidiAlgorithm.ParagraphLevel([.. codePoints], BidiAlgorithm.Auto);
            checkedLines++;

            if (actual != expectedLevel)
            {
                mismatches++;

                if (firstMismatch.Length == 0)
                    firstMismatch = $"{fields[0]}: expected {expectedLevel}, got {actual}";
            }
        }

        GD.Print($"[bidi] paragraph level: {checkedLines} auto-direction lines checked, {mismatches} mismatched");

        // The suite states an explicit base direction for almost every line, so only a few dozen lines leave the
        // decision to P2/P3. That is all the data there is for this rule; the cases below cover what it does not.
        AssertThat(checkedLines).OverrideFailureMessage(
            "the suite contained no auto-direction line, so nothing was verified").IsGreater(20);

        AssertThat(mismatches).OverrideFailureMessage(
            firstMismatch.Length > 0 ? firstMismatch : "every auto-direction line matched").IsEqual(0);
    }

    /// <summary>
    /// P2's own rule, which the suite's few auto-direction lines do not exercise: text inside an isolate does not
    /// decide the paragraph's direction.
    /// </summary>
    [TestCase]
    public void IsolatesAreSkippedWhenTheParagraphDirectionIsDecided()
    {
        // A Latin word inside a right-to-left isolate, followed by a Hebrew letter: the letter decides.
        AssertThat(BidiAlgorithm.ParagraphLevel([0x2067, 0x0061, 0x2069, 0x05D0], BidiAlgorithm.Auto))
            .OverrideFailureMessage("the isolate's contents must be skipped")
            .IsEqual(BidiAlgorithm.RightToLeft);

        // The same shape the other way round: the Latin letter outside decides, the Hebrew inside is skipped.
        AssertThat(BidiAlgorithm.ParagraphLevel([0x0061, 0x2067, 0x05D0, 0x2069], BidiAlgorithm.Auto))
            .IsEqual(BidiAlgorithm.LeftToRight);

        // Nothing strong outside any isolate: P3 gives level 0.
        AssertThat(BidiAlgorithm.ParagraphLevel([0x2066, 0x0627, 0x2069], BidiAlgorithm.Auto))
            .OverrideFailureMessage("no strong type outside the isolates means level 0")
            .IsEqual(BidiAlgorithm.LeftToRight);

        // Digits and punctuation are not strong either.
        AssertThat(BidiAlgorithm.ParagraphLevel([0x0031, 0x0021, 0x0020], BidiAlgorithm.Auto))
            .IsEqual(BidiAlgorithm.LeftToRight);

        // An explicit base direction is taken as given, and normalised to a level.
        AssertThat(BidiAlgorithm.ParagraphLevel([0x0061], BidiAlgorithm.RightToLeft))
            .IsEqual(BidiAlgorithm.RightToLeft);
    }

    private static IEnumerable<string> ReadSuite(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);

        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            yield return trimmed;
        }
    }

    private static List<int> ParseCodePoints(string field)
    {
        var codePoints = new List<int>();

        foreach (string token in field.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            codePoints.Add(Convert.ToInt32(token, 16));

        return codePoints;
    }
}
