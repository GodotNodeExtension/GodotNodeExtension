using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// Describes a block-level region with internally-managed layout.
/// <para>
/// Fixed-size blocks (<see cref="IsAutoSize"/> == false): the typography engine places
/// the block as a single unit and does not rearrange its internal elements.
/// Block-internal elements use positions relative to the block origin.
/// </para>
/// <para>
/// Auto-size blocks (<see cref="IsAutoSize"/> == true): the engine recursively lays out
/// the block's content in a nested context and determines the block height automatically.
/// </para>
/// </summary>
public class BlockLayout
{
    /// <summary>
    /// Total measured size of the block (width x height).
    /// When <see cref="Vector2.Zero"/>, the engine computes the size automatically.
    /// </summary>
    public Vector2 Size { get; init; }

    /// <summary>
    /// When true, the block spans the full content width and always starts on a new line.
    /// When false, the block may be placed inline if it fits within the remaining line width.
    /// </summary>
    public bool FullWidth { get; init; }

    /// <summary>
    /// Left indent for block content layout (auto-size blocks only).
    /// Content inside the block is offset rightward by this amount.
    /// </summary>
    public float LeftIndent { get; init; }

    /// <summary>
    /// Right indent for block content layout (auto-size blocks only).
    /// Content inside the block has its max width reduced by this amount.
    /// </summary>
    public float RightIndent { get; init; }

    /// <summary>
    /// Internal padding for auto-size blocks (X = horizontal, Y = vertical).
    /// Content is offset by padding and block height includes padding on both sides.
    /// </summary>
    public Vector2 Padding { get; init; }

    // ── Block decorations (emitted automatically during layout expansion) ──

    /// <summary>
    /// Background color for the block. When non-null, a filled rectangle is emitted
    /// behind all block content during layout expansion.
    /// </summary>
    public Color? BackgroundColor { get; init; }

    /// <summary>
    /// Corner radius for the background rectangle. 0 for sharp corners.
    /// </summary>
    public float BackgroundCornerRadius { get; init; }

    /// <summary>
    /// Left border color. When non-null, a vertical line is drawn at the left edge.
    /// </summary>
    public Color? LeftBorderColor { get; init; }

    /// <summary>
    /// Left border width in pixels.
    /// </summary>
    public float LeftBorderWidth { get; init; }

    /// <summary>
    /// Left border X offset from the block left edge.
    /// </summary>
    public float LeftBorderOffset { get; init; }

    // ── Block marker (for list bullets, ordered numbers) ──

    /// <summary>
    /// Marker text drawn at the block's left edge, outside the content area.
    /// Used for list bullets ("•") and ordered numbers ("1.").
    /// The marker is vertically aligned with the first line of content.
    /// </summary>
    public string? MarkerText { get; init; }

    /// <summary>
    /// Font for the marker text.
    /// </summary>
    public Font? MarkerFont { get; init; }

    /// <summary>
    /// Font size for the marker text.
    /// </summary>
    public int MarkerFontSize { get; init; }

    /// <summary>
    /// Color for the marker text.
    /// </summary>
    public Color MarkerColor { get; init; }

    /// <summary>
    /// Custom marker elements drawn at the block's left edge, outside the content area.
    /// Alternative to <see cref="MarkerText"/> for non-text markers (e.g., checkboxes).
    /// Element positions are relative to the marker area origin (left edge, first content line Y).
    /// </summary>
    public List<DrawElement>? MarkerElements { get; init; }

    /// <summary>
    /// Wrap regions for the block's internal layout (auto-size blocks only).
    /// When non-empty, the nested layout uses per-line available span queries
    /// for content wrapping (e.g., drop-cap text wrap).
    /// </summary>
    public List<WrapRegion>? WrapRegions { get; init; }

    /// <summary>
    /// Whether this block uses automatic height calculation.
    /// True when Size is zero.
    /// </summary>
    public bool IsAutoSize => Size == Vector2.Zero;

    /// <summary>
    /// Horizontal alignment of the block content within the available width.
    /// Affects fixed-size blocks that are narrower than the parent width.
    /// Default is <see cref="TextAlignment.Left"/>.
    /// </summary>
    public TextAlignment ContentAlignment { get; init; }
}
