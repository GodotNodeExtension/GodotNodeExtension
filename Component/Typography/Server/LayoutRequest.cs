using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;

namespace GodotNodeExtension.Component.Typography.Server;

/// <summary>
/// Opaque handle identifying a layout session/client.
/// Each consumer (e.g., a RichTextCanvas instance) creates one handle
/// and uses it for all subsequent requests. Similar to Godot's RID.
/// </summary>
public readonly struct LayoutHandle : IEquatable<LayoutHandle>
{
    internal readonly long Id;

    private readonly int _generation;

    internal LayoutHandle(long id, int generation)
    {
        Id = id;
        _generation = generation;
    }

    /// <summary>
    /// Whether a server has assigned this handle. It does <em>not</em> mean the handle is still usable:
    /// shutting the server down invalidates every handle, and only the server can tell — see
    /// <see cref="TypographyServer.IsHandleValid"/>. Requests on a stale handle produce
    /// no result at all, which is what used to leave a canvas blank forever after an assembly reload.
    /// </summary>
    public bool IsValid => Id > 0;

    /// <summary>
    /// Server generation this handle belongs to. A handle whose generation is older than the server's
    /// current one was invalidated by a shutdown.
    /// </summary>
    public int Generation => _generation;

    /// <inheritdoc />
    public bool Equals(LayoutHandle other) => Id == other.Id;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is LayoutHandle other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Id.GetHashCode();

    /// <summary>
    /// An invalid, unassigned handle: the same value as <c>default(LayoutHandle)</c>, i.e. id 0, which
    /// <see cref="LayoutHandle.IsValid"/> rejects.
    /// </summary>
    public static readonly LayoutHandle Invalid = new(0, 0);

    /// <summary>Two handles are equal when they refer to the same layout.</summary>
    public static bool operator ==(LayoutHandle left, LayoutHandle right) => left.Id == right.Id;

    /// <summary>Two handles differ when they refer to different layouts.</summary>
    public static bool operator !=(LayoutHandle left, LayoutHandle right) => left.Id != right.Id;
}

/// <summary>
/// Type of layout operation requested from the server.
/// </summary>
public enum LayoutRequestType
{
    /// <summary>Full layout: Prepare + Layout.</summary>
    FullLayout,

    /// <summary>Relayout only (reuse cached PreparedContent). Used for resize.</summary>
    Relayout,

    /// <summary>Stream append: incremental Prepare + partial Layout.</summary>
    StreamAppend,

    /// <summary>Stream flush: finalize all lines.</summary>
    StreamFlush,
}

/// <summary>
/// Layout request submitted to the typography server.
/// All fields relevant to the <see cref="Type"/> must be filled before submission.
/// </summary>
public class LayoutRequest
{
    /// <summary>Handle of the requesting client.</summary>
    public LayoutHandle Handle { get; init; }

    /// <summary>Monotonically increasing request ID for cancellation tracking.</summary>
    public long RequestId { get; init; }

    /// <summary>Type of layout operation.</summary>
    public LayoutRequestType Type { get; init; }

    /// <summary>Elements to lay out (for FullLayout). Null for Relayout.</summary>
    public DrawElement[]? Elements { get; init; }

    /// <summary>Typography settings for this request.</summary>
    public TypographySettings Settings { get; init; } = new();

    /// <summary>Single element to append (for StreamAppend).</summary>
    public DrawElement? AppendElement { get; init; }

    /// <summary>Source index for StreamAppend.</summary>
    public int AppendSourceIndex { get; init; }
}

/// <summary>
/// Result of a layout operation, delivered from the server thread to the caller.
/// Pure data structure, safe to read from any thread.
/// </summary>
public class LayoutResult
{
    /// <summary>Handle this result belongs to.</summary>
    public LayoutHandle Handle { get; set; }

    /// <summary>Request ID this result corresponds to.</summary>
    public long RequestId { get; init; }

    /// <summary>Whether this is a progressive (partial) result or final.</summary>
    public bool IsProgressive { get; set; }

    /// <summary>Laid-out elements ready for rendering.</summary>
    public List<LayoutElement>? Elements { get; init; }

    /// <summary>Laid-out lines (for fine-grained access).</summary>
    public IReadOnlyList<LayoutLine>? Lines { get; set; }

    /// <summary>Total content size after layout.</summary>
    public Vector2 ContentSize { get; init; }

    /// <summary>Error message if layout failed.</summary>
    public string? Error { get; init; }

    /// <summary>Whether the request was cancelled before completion.</summary>
    public bool IsCancelled { get; set; }

    // ── Diagnostics ──

    /// <summary>Number of lines in this result.</summary>
    public int LineCount { get; set; }

    /// <summary>Number of laid-out elements in this result.</summary>
    public int ElementCount { get; set; }

    /// <summary>
    /// Break candidates rejected by a line-start/line-end prohibition or an unbreakable-pair rule.
    /// A high count explains a line that ends well before its width would allow, without needing a
    /// full dump — that is otherwise one of the hardest things to diagnose in a typography engine.
    /// </summary>
    public int ProhibitedBreakSkips { get; set; }

    /// <summary>Per-phase timings measured on the layout thread.</summary>
    public LayoutTimings Timings { get; set; }

    /// <summary>
    /// One-line summary for logs. Includes the error when the request failed, so a caller that only
    /// prints this cannot lose the reason.
    /// </summary>
    /// <returns>A compact human-readable description.</returns>
    public string Describe()
    {
        if (IsCancelled)
            return $"layout {RequestId}: cancelled";

        if (Error != null)
            return $"layout {RequestId}: error={Error}";

        return $"layout {RequestId}: lines={LineCount} elements={ElementCount} " +
               $"content=({ContentSize.X:F1},{ContentSize.Y:F1}) " +
               $"prepare={Timings.PrepareMs:F2}ms boundaries={Timings.BoundaryMs:F2}ms " +
               $"break={Timings.BreakMs:F2}ms " +
               $"adjust={Timings.AdjustMs:F2}ms flatten={Timings.FlattenMs:F2}ms " +
               $"prohibitedSkips={ProhibitedBreakSkips}";
    }
}

/// <summary>
/// Wall-clock durations of the layout phases, measured on the layout thread.
/// <para>
/// The split matters because the prepare phase is the expensive, cacheable half while break and adjust
/// are the cheap half that a resize re-runs; without these numbers the promise behind
/// <see cref="TypographyServer.RequestRelayout"/> ("resize does not re-measure text") cannot be checked.
/// </para>
/// </summary>
/// <param name="PrepareMs">Segmentation, classification and shaping.</param>
/// <param name="BoundaryMs">
/// Building the boundary decisions. Zero on a relayout, because they are width-independent and are
/// reused from the previous layout.
/// </param>
/// <param name="BreakMs">Line breaking.</param>
/// <param name="AdjustMs">Line adjustment (squeeze, stretch, alignment).</param>
/// <param name="FlattenMs">Flattening lines into the output element stream.</param>
public readonly record struct LayoutTimings(
    double PrepareMs,
    double BoundaryMs,
    double BreakMs,
    double AdjustMs,
    double FlattenMs)
{
    /// <summary>
    /// Time spent in the width-independent half (measurement plus boundary decisions). A relayout that
    /// only changes the width must report zero here; that is the property the cache exists for.
    /// </summary>
    public double CompileMs => PrepareMs + BoundaryMs;

    /// <summary>Total time spent in the phases reported here.</summary>
    public double TotalMs => PrepareMs + BoundaryMs + BreakMs + AdjustMs + FlattenMs;
}
