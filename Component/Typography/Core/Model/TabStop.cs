namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// How content lines up at a tab stop.
/// </summary>
public enum TabAlignment
{
    /// <summary>The content starts at the stop.</summary>
    Left,

    /// <summary>The content is centred on the stop.</summary>
    Center,

    /// <summary>The content ends at the stop.</summary>
    Right,

    /// <summary>
    /// The content's decimal separator sits at the stop, which is how a column of numbers lines up. Content without a
    /// separator falls back to <see cref="Left"/>. (Named <c>DecimalPoint</c> rather than <c>Decimal</c> because an
    /// enumerator member that shadows a framework type name is a rule the analysis enforces.)
    /// </summary>
    DecimalPoint,
}

/// <summary>
/// One tab stop: where a tabulation character sends the content after it.
/// <para>
/// A tab is an alignment instruction, not a character with a width of its own — which is why the stop is a position
/// relative to the content origin and why the layout has to be told about it rather than measuring the tab.
/// </para>
/// </summary>
/// <param name="Position">Distance from the content origin, in pixels.</param>
/// <param name="Alignment">How the content after the tab lines up at that position.</param>
/// <param name="Leader">
/// Character that fills the gap between the text before the tab and the content at the stop (a table of contents
/// writes dots there). Null leaves the gap empty. The layout only says which character and how wide the gap is; the
/// consumer repeats the character across it, so nothing has to be shaped once the widths are known.
/// </param>
public readonly record struct TabStop(
    float Position,
    TabAlignment Alignment = TabAlignment.Left,
    char? Leader = null);
