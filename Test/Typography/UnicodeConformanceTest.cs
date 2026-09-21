namespace GodotNodeExtension.Tests.Typography;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core.Unicode;
using static GdUnit4.Assertions;

/// <summary>
/// Unicode conformance for the line and word breaking algorithms, run against Unicode's own test suites.
/// <para>
/// This is the strongest available statement about the algorithms: 19338 line break cases and 1944 word
/// break cases, each a sequence of code points with the expected break status at every position, covering
/// every pair of property values. An engine that passes them implements UAX #14 and UAX #29 as published;
/// one that does not will fail here rather than in a subtly wrong line break six months later.
/// </para>
/// <para>
/// The suites are stored gzipped (3.4 MB of text becomes 234 KB) under <c>Test/Typography/ucd/</c>, with
/// the SHA-256 of the uncompressed upstream file recorded in the accompanying README, so the data can be
/// verified against unicode.org. Regenerate with:
/// <c>curl -o LineBreakTest.txt https://www.unicode.org/Public/UCD/latest/ucd/auxiliary/LineBreakTest.txt</c>.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class UnicodeConformanceTest
{
    private const string UcdDirectory = "res://Test/Typography/ucd";

    /// <summary>
    /// UAX #14: every break position in every test case must match.
    /// </summary>
    [TestCase]
    public void LineBreakAlgorithmConformsToUax14()
    {
        var cases = ParseCases($"{UcdDirectory}/LineBreakTest.txt.gz");
        AssertThat(cases.Count > 19000).OverrideFailureMessage(
            $"expected the full suite, got {cases.Count} cases").IsTrue();

        int positions = 0;
        string? firstFailure = null;

        foreach (var testCase in cases)
        {
            for (int i = 1; i < testCase.CodePoints.Count; i++)
            {
                positions++;

                LineBreakOpportunity opportunity =
                    LineBreakAlgorithm.OpportunityAt(testCase.CodePoints.ToArray(), i);

                bool actual = opportunity != LineBreakOpportunity.Prohibited;

                if (actual != testCase.BreakBefore[i])
                {
                    firstFailure ??= Describe("line", testCase, i, testCase.BreakBefore[i], actual);
                }
            }
        }

        AssertThat(firstFailure).OverrideFailureMessage(
            $"{(firstFailure ?? string.Empty)}\n({positions} positions checked)").IsNull();
    }

    /// <summary>
    /// UAX #29: every word boundary in every test case must match.
    /// </summary>
    [TestCase]
    public void WordBreakAlgorithmConformsToUax29()
    {
        var cases = ParseCases($"{UcdDirectory}/WordBreakTest.txt.gz");
        AssertThat(cases.Count > 1800).OverrideFailureMessage(
            $"expected the full suite, got {cases.Count} cases").IsTrue();

        int positions = 0;
        string? firstFailure = null;

        foreach (var testCase in cases)
        {
            for (int i = 1; i < testCase.CodePoints.Count; i++)
            {
                positions++;

                bool actual = WordBreakAlgorithm.IsBoundary(testCase.CodePoints.ToArray(), i);

                if (actual != testCase.BreakBefore[i])
                {
                    firstFailure ??= Describe("word", testCase, i, testCase.BreakBefore[i], actual);
                }
            }
        }

        AssertThat(firstFailure).OverrideFailureMessage(
            $"{(firstFailure ?? string.Empty)}\n({positions} positions checked)").IsNull();
    }

    /// <summary>
    /// The generated tables must state the Unicode version they came from: a line breaking rule whose data
    /// version is unknown cannot be reproduced.
    /// </summary>
    [TestCase]
    public void GeneratedTablesRecordTheirUnicodeVersion()
    {
        foreach (string version in new[]
                 {
                     LineBreakData.UnicodeVersion, WordBreakData.UnicodeVersion,
                     ScriptData.UnicodeVersion, EastAsianWidthData.UnicodeVersion,
                     ExtendedPictographicData.UnicodeVersion,
                 })
        {
            AssertThat(version != "unknown" && version.Length > 0).OverrideFailureMessage(
                "every generated table must name its Unicode version").IsTrue();
        }

        AssertThat(LineBreakData.SourceSha256.Length).IsEqual(64);
        AssertThat(LineBreakData.Lookup('A')).IsEqual(LineBreakValue.AL);
        AssertThat(WordBreakData.Lookup('A')).IsEqual(WordBreakValue.ALetter);
        AssertThat(ScriptData.Lookup('漢')).IsEqual(ScriptValue.Han);
    }

    // ── Helpers ──

    /// <summary>A parsed conformance case: the code points and the expected status before each one.</summary>
    private sealed record Case(List<int> CodePoints, List<bool> BreakBefore, string Raw);

    /// <summary>
    /// Read a gzipped conformance suite. Comments are stripped first: they contain code point names and
    /// rule numbers that would otherwise be mistaken for test data.
    /// </summary>
    /// <param name="resourcePath">Path of the gzipped suite.</param>
    /// <returns>The parsed cases.</returns>
    private static List<Case> ParseCases(string resourcePath)
    {
        string absolute = ProjectSettings.GlobalizePath(resourcePath);
        var cases = new List<Case>();

        using var file = File.OpenRead(absolute);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        while (reader.ReadLine() is { } raw)
        {
            string line = raw.Split('#')[0].Trim();

            if (line.Length == 0 || line[0] == '@')
                continue;

            var codePoints = new List<int>();
            var breaks = new List<bool>();

            foreach (string token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token == "\u00d7")
                {
                    breaks.Add(false);
                }
                else if (token == "\u00f7")
                {
                    breaks.Add(true);
                }
                else if (int.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cp))
                {
                    codePoints.Add(cp);
                }
            }

            cases.Add(new Case(codePoints, breaks, line));
        }

        return cases;
    }

    private static string Describe(string kind, Case testCase, int index, bool expected, bool actual)
    {
        var text = new StringBuilder();

        foreach (int code in testCase.CodePoints)
            text.Append(CultureInfo.InvariantCulture, $"U+{code:X4} ");

        return $"{kind} break mismatch at position {index} of {text}— " +
               $"expected {(expected ? "break" : "no break")}, got {(actual ? "break" : "no break")}\n" +
               $"  case: {testCase.Raw}";
    }
}
