namespace GodotNodeExtension.Tests.Typography;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Core.Unicode;
using static GdUnit4.Assertions;

/// <summary>
/// Conformance for the bidi chain that is not the library's: the levels this component asks for, and the rule that
/// turns levels into a visual order.
/// <para>
/// Unicode's own <c>BidiCharacterTest.txt</c> states, for every line, the code points, the requested base
/// direction, the paragraph level, the level of every character and the visual order — which is exactly the two
/// things this component does itself: it picks the base direction (P2/P3 when the text does not declare one) and it
/// turns the levels into an order (L2). Lines that use explicit formatting characters are skipped: those are
/// resolved inside the library, and X9 removes characters from the levels while the visual order still lists them,
/// which is a different question from the one asked here.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BidiCharacterConformanceTest
{
    private const string SuitePath = "res://Test/Typography/ucd/BidiCharacterTest.txt.gz";

    /// <summary>Levels and visual order agree with the reference implementation, line by line.</summary>
    [TestCase]
    public void LevelsAndVisualOrderFollowTheOfficialSuite()
    {
        string path = ProjectSettings.GlobalizePath(SuitePath);

        if (!File.Exists(path))
        {
            GD.Print($"[bidi] {SuitePath} is missing; the conformance check was skipped");
            return;
        }

        int checkedLines = 0;
        int levelMismatches = 0;
        int orderMismatches = 0;
        string firstLevelMismatch = string.Empty;
        string firstOrderMismatch = string.Empty;

        foreach (string line in ReadSuite(path))
        {
            string[] fields = line.Split(';');

            if (fields.Length < 5)
                continue;

            List<int> codePoints = ParseCodePoints(fields[0]);

            // The explicit formatting characters change the levels of the text around them and are dropped by X9;
            // those lines belong to the library's own conformance, not to this check.
            if (ContainsExplicitFormatting(codePoints))
                continue;

            string[] levelTokens = fields[3].Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (Array.IndexOf(levelTokens, "x") >= 0)
                continue;

            if (levelTokens.Length != codePoints.Count)
                continue;

            if (!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int baseDirection))
                continue;

            var expectedLevels = new byte[levelTokens.Length];

            for (int i = 0; i < levelTokens.Length; i++)
                expectedLevels[i] = byte.Parse(levelTokens[i], CultureInfo.InvariantCulture);

            int expectedParagraphLevel = int.Parse(fields[2], CultureInfo.InvariantCulture);
            string text = CodePointsToText(codePoints);

            // The paragraph level: this component decides it (P2/P3) when the line asks for "auto", and otherwise
            // takes what the line declared.
            int ourParagraphLevel = baseDirection == 2
                ? BidiAlgorithm.ParagraphLevel([.. codePoints], BidiAlgorithm.Auto)
                : BidiAlgorithm.ParagraphLevel([.. codePoints], baseDirection);

            checkedLines++;

            if (ourParagraphLevel != expectedParagraphLevel)
            {
                levelMismatches++;

                if (firstLevelMismatch.Length == 0)
                {
                    firstLevelMismatch =
                        $"paragraph level: {fields[0]} base={baseDirection}: expected {expectedParagraphLevel}, "
                        + $"got {ourParagraphLevel}";
                }

                continue;
            }

            // Line levels: rule L1 (separators and trailing whitespace back to the paragraph level) is part of
            // what a line is laid out from, and the suite states its expected levels after that rule.
            byte[] ourLevels = value_LevelsFor(text, baseDirection);

            for (int i = 0; i < expectedLevels.Length; i++)
            {
                if (ourLevels[i] == expectedLevels[i])
                    continue;

                levelMismatches++;

                if (firstLevelMismatch.Length == 0)
                {
                    firstLevelMismatch =
                        $"level at {i}: {fields[0]} base={baseDirection}: expected {expectedLevels[i]}, "
                        + $"got {ourLevels[i]}";
                }

                break;
            }

            // Rule L2 on the expected levels: the order this component computes must be the suite's order.
            int[] order = BidiResolver.VisualOrder(expectedLevels, 0, expectedLevels.Length);
            int[] expectedOrder = ParseOrder(fields[4]);

            if (order.Length != expectedOrder.Length)
            {
                orderMismatches++;
                continue;
            }

            for (int i = 0; i < order.Length; i++)
            {
                if (order[i] == expectedOrder[i])
                    continue;

                orderMismatches++;

                if (firstOrderMismatch.Length == 0)
                {
                    firstOrderMismatch =
                        $"visual order: {fields[0]} base={baseDirection}: expected {fields[4]}, "
                        + $"got {string.Join(" ", order)}";
                }

                break;
            }
        }

        GD.Print(
            $"[bidi] character conformance: {checkedLines} lines, {levelMismatches} level mismatches, "
            + $"{orderMismatches} order mismatches");

        AssertThat(checkedLines).OverrideFailureMessage(
            "the suite contained no usable line, so nothing was verified").IsGreater(1000);

        AssertThat(levelMismatches).OverrideFailureMessage(
            firstLevelMismatch.Length > 0 ? firstLevelMismatch : "every level matched").IsEqual(0);

        AssertThat(orderMismatches).OverrideFailureMessage(
            firstOrderMismatch.Length > 0 ? firstOrderMismatch : "every order matched").IsEqual(0);
    }

    /// <summary>Levels as the line would be laid out: the base level stated, then rule L1 applied.</summary>
    /// <param name="text">The line's text.</param>
    /// <param name="baseDirection">Requested base direction: 0 or 1 forced, 2 left to the text.</param>
    /// <returns>One level per code unit.</returns>
    private static byte[] value_LevelsFor(string text, int baseDirection) => baseDirection switch
    {
        2 => BidiResolver.LineLevels(text, TextDirection.LeftToRight),
        _ => BidiResolver.LineLevels(text, baseDirection),
    };

    private static bool ContainsExplicitFormatting(List<int> codePoints)
    {
        foreach (int codePoint in codePoints)
        {
            if (BidiAlgorithm.IsExplicitFormatting(BidiClassData.Lookup(codePoint)))
                return true;
        }

        return false;
    }

    private static string CodePointsToText(List<int> codePoints)
    {
        var builder = new System.Text.StringBuilder(codePoints.Count);

        foreach (int codePoint in codePoints)
            builder.Append(char.ConvertFromUtf32(codePoint));

        return builder.ToString();
    }

    private static int[] ParseOrder(string field)
    {
        string[] tokens = field.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var order = new int[tokens.Length];

        for (int i = 0; i < tokens.Length; i++)
            order[i] = int.Parse(tokens[i], CultureInfo.InvariantCulture);

        return order;
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
            codePoints.Add(int.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

        return codePoints;
    }
}
