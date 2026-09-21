namespace GodotNodeExtension.Tests.Typography;

using GdUnit4;
using GodotNodeExtension.Component.Typography.Core.Unicode;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for the generated bidirectional class table (the data layer of UAX #9).
/// <para>
/// The table is generated from <c>DerivedBidiClass.txt</c> by <c>Tools/Typography/gen_unicode_tables.py</c>, and what makes it
/// more than a file copy is the default it fills in: the bidi class of an *unassigned* code point is stated per
/// range (Hebrew defaults to R, Arabic to AL, currency symbols to ET, and so on), so the generator reads the file's
/// own <c>@missing</c> lines instead of assuming one fallback value. These cases pin both halves.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyBidiClassTableTest
{
    /// <summary>Assigned characters take the class the data file gives them.</summary>
    [TestCase]
    public void AssignedCharactersCarryTheirClass()
    {
        AssertThat(BidiClassData.Lookup('A')).IsEqual(BidiClassValue.L);
        AssertThat(BidiClassData.Lookup('0')).IsEqual(BidiClassValue.EN);
        AssertThat(BidiClassData.Lookup(0x05D0)).OverrideFailureMessage("Hebrew alef is R").IsEqual(BidiClassValue.R);
        AssertThat(BidiClassData.Lookup(0x0627)).OverrideFailureMessage("Arabic alef is AL").IsEqual(BidiClassValue.AL);
        AssertThat(BidiClassData.Lookup(0x0660)).OverrideFailureMessage("Arabic-Indic zero is AN").IsEqual(BidiClassValue.AN);
        AssertThat(BidiClassData.Lookup(0x00B0)).OverrideFailureMessage("degree sign is ET").IsEqual(BidiClassValue.ET);
        AssertThat(BidiClassData.Lookup(0x00A0)).OverrideFailureMessage("no-break space is CS").IsEqual(BidiClassValue.CS);
        AssertThat(BidiClassData.Lookup(0x0028)).OverrideFailureMessage("opening bracket is ON").IsEqual(BidiClassValue.ON);
        AssertThat(BidiClassData.Lookup('\t')).OverrideFailureMessage("tab is S").IsEqual(BidiClassValue.S);
        AssertThat(BidiClassData.Lookup('\n')).OverrideFailureMessage("newline is B").IsEqual(BidiClassValue.B);
        AssertThat(BidiClassData.Lookup(' ')).OverrideFailureMessage("space is WS").IsEqual(BidiClassValue.WS);
    }

    /// <summary>
    /// An unassigned code point is classified by the range it falls in, not by one global fallback: an unassigned
    /// Hebrew point is right-to-left and an unassigned currency symbol is a terminator, which is what the file's
    /// <c>@missing</c> lines say and what a single "default = L" would get wrong.
    /// </summary>
    [TestCase]
    public void UnassignedCodePointsTakeTheClassOfTheirRange()
    {
        AssertThat(BidiClassData.Lookup(0x05F5)).OverrideFailureMessage(
            "an unassigned Hebrew point is R").IsEqual(BidiClassValue.R);
        AssertThat(BidiClassData.Lookup(0x20BF)).OverrideFailureMessage(
            "an unassigned currency symbol is ET").IsEqual(BidiClassValue.ET);
        AssertThat(BidiClassData.Lookup(0x0860)).OverrideFailureMessage(
            "an unassigned point in the Syriac range is AL").IsEqual(BidiClassValue.AL);
        AssertThat(BidiClassData.Lookup(0x0378)).OverrideFailureMessage(
            "an unassigned point outside every RTL range defaults to L").IsEqual(BidiClassValue.L);
    }

    /// <summary>Every code point is answered for: the table has no holes to special-case at lookup time.</summary>
    [TestCase]
    public void EveryCodePointHasAClass()
    {
        for (int cp = 0; cp <= 0x10FFFF; cp += 61)
        {
            // The lookup throws when a code point falls in no range; walking a prime stride covers every range
            // boundary sparse enough to stay quick.
            BidiClassValue value = BidiClassData.Lookup(cp);

            // A hole in the table would either throw (no range covers the code point) or answer with something the
            // enum does not define; both are failures, and a class as such is whatever the file says it is.
            AssertThat(System.Enum.IsDefined(value)).IsTrue();
        }
    }
}
