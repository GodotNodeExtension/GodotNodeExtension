using Godot;

namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// A colored text span returned by syntax highlighting tokenization.
/// </summary>
public struct ColoredSpan
{
    /// <summary>Text content of this span.</summary>
    public string Text { get; init; }

    /// <summary>Color for this span.</summary>
    public Color Color { get; init; }
}
