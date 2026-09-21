using System;
using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.Typography.Core.Model;

/// <summary>
/// Defines the shape of an exclusion zone for text wrapping.
/// Can be a simple rectangle, a convex polygon, or an alpha-derived contour.
/// Analogous to CSS <c>shape-outside</c>.
/// </summary>
public abstract class WrapShape
{
    /// <summary>
    /// Get the horizontal extent of this shape at a given Y offset (relative to shape origin).
    /// Returns the left and right X boundaries at line Y, or <c>null</c> if this Y is outside the shape.
    /// This is the core query used by the layout engine for per-line available width.
    /// </summary>
    /// <param name="y">Y position relative to shape's top edge.</param>
    /// <param name="lineHeight">Height of the current text line.</param>
    /// <returns>(leftX, rightX) extent at this Y, or <c>null</c> if no intersection.</returns>
    public abstract (float Left, float Right)? GetExtentAtY(float y, float lineHeight);

    /// <summary>
    /// The intervals this shape occupies at a given Y, in left-to-right order: one for a rectangle or a convex
    /// shape, several for a shape that narrows in the middle (a C, two lobes, a polygon with a notch).
    /// <para>
    /// A line offers its text as many intervals as the shape leaves room for, so a shape that only ever reports one
    /// interval forces the layout to treat the whole line as excluded — which is what a polygon used to do.
    /// </para>
    /// <para>
    /// The default implementation answers with the single extent <see cref="GetExtentAtY"/> reports, so a shape that
    /// cannot narrow keeps working unchanged. Callers turn the occupied intervals into available ones by taking their
    /// complement; an empty list means this Y does not touch the shape at all.
    /// </para>
    /// </summary>
    /// <param name="y">Y position relative to the shape's top edge.</param>
    /// <param name="lineHeight">Height of the current text line.</param>
    /// <param name="spans">List the intervals are appended to, in left-to-right order.</param>
    public virtual void GetSpansAtY(float y, float lineHeight, List<LineSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        if (GetExtentAtY(y, lineHeight) is { } extent)
            spans.Add(new LineSpan(extent.Left, extent.Right));
    }

    /// <summary>Axis-aligned bounding box of the entire shape.</summary>
    public abstract Rect2 BoundingBox { get; }
}

/// <summary>
/// Rectangular exclusion shape (simplest case, equivalent to original WrapRegion behavior).
/// </summary>
public class RectWrapShape : WrapShape
{
    /// <summary>Width of the rectangle.</summary>
    public float Width { get; init; }

    /// <summary>Height of the rectangle.</summary>
    public float Height { get; init; }

    /// <inheritdoc />
    public override (float Left, float Right)? GetExtentAtY(float y, float lineHeight)
    {
        // No intersection if the line is entirely above or below the rectangle
        if (y + lineHeight <= 0 || y >= Height) return null;
        return (0, Width);
    }

    /// <inheritdoc />
    public override Rect2 BoundingBox => new(0, 0, Width, Height);
}

/// <summary>
/// Polygon-based exclusion shape derived from alpha contour or manual definition.
/// Stores a per-scanline extent lookup for O(1) per-line queries.
/// </summary>
public class PolygonWrapShape : WrapShape
{
    /// <summary>
    /// Sorted list of (Y, leftX, rightX) scanline extents.
    /// Pre-computed from polygon vertices or alpha contour for fast lookup.
    /// Y values are relative to the shape's top edge (0 = top).
    /// </summary>
    public List<(float Y, float Left, float Right)> ScanlineExtents { get; init; } = [];

    /// <summary>Vertical resolution of the scanline table in pixels.</summary>
    public float ScanlineStep { get; init; } = 1f;

    /// <summary>
    /// Occupied intervals per scanline (merged, in left-to-right order), for a shape that narrows in the middle.
    /// Empty for a shape built from <see cref="ScanlineExtents"/> alone, in which case the single-extent behaviour
    /// applies.
    /// </summary>
    public List<(float Y, List<(float Left, float Right)> Runs)> ScanlineRuns { get; init; } = [];

    /// <summary>Total height of the shape.</summary>
    public float TotalHeight { get; init; }

    /// <summary>Total width of the shape (max extent).</summary>
    public float TotalWidth { get; init; }

    /// <inheritdoc />
    public override (float Left, float Right)? GetExtentAtY(float y, float lineHeight)
    {
        if (ScanlineExtents.Count == 0) return null;
        if (y + lineHeight <= 0 || y >= TotalHeight) return null;

        // Find the union of all scanline extents that the line covers
        float lineTop = Math.Max(y, 0);
        float lineBottom = Math.Min(y + lineHeight, TotalHeight);

        float minLeft = float.MaxValue;
        float maxRight = float.MinValue;
        bool found = false;

        // Binary search for the first scanline >= lineTop
        int startIdx = FindScanlineIndex(lineTop);

        for (int i = startIdx; i < ScanlineExtents.Count; i++)
        {
            var (sy, sl, sr) = ScanlineExtents[i];
            if (sy >= lineBottom) break;
            if (sy + ScanlineStep <= lineTop) continue;

            if (sl < minLeft) minLeft = sl;
            if (sr > maxRight) maxRight = sr;
            found = true;
        }

        return found ? (minLeft, maxRight) : null;
    }

    /// <inheritdoc />
    public override void GetSpansAtY(float y, float lineHeight, List<LineSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        if (ScanlineRuns.Count == 0)
        {
            base.GetSpansAtY(y, lineHeight, spans);
            return;
        }

        // Every run of every scanline the line covers goes in, and then overlapping ones are merged: a shape that
        // narrows leaves two runs on the same scanline, and that is the case this exists for.
        var runs = new List<(float Left, float Right)>();

        foreach ((float scanY, List<(float Left, float Right)> scanRuns) in ScanlineRuns)
        {
            if (scanY + ScanlineStep <= y || scanY >= y + lineHeight)
                continue;

            runs.AddRange(scanRuns);
        }

        if (runs.Count == 0)
            return;

        runs.Sort((a, b) => a.Left.CompareTo(b.Left));

        float mergeLeft = runs[0].Left;
        float mergeRight = runs[0].Right;

        for (int i = 1; i < runs.Count; i++)
        {
            if (runs[i].Left <= mergeRight)
            {
                if (runs[i].Right > mergeRight)
                    mergeRight = runs[i].Right;

                continue;
            }

            spans.Add(new LineSpan(mergeLeft, mergeRight));
            mergeLeft = runs[i].Left;
            mergeRight = runs[i].Right;
        }

        spans.Add(new LineSpan(mergeLeft, mergeRight));
    }

    /// <summary>
    /// Build a polygon shape from its vertices by intersecting every scanline with the polygon (even-odd rule).
    /// <para>
    /// The even-odd rule is what makes a concave shape work: a scanline that crosses four edges yields two occupied
    /// intervals, which is the narrowing this shape exists to express. For a self-intersecting contour the rule makes
    /// the alternation the answer, which is a different shape from a hole; that is stated here rather than guessed at.
    /// </para>
    /// </summary>
    /// <param name="points">Vertices, clockwise or counter-clockwise.</param>
    /// <param name="step">Vertical resolution in pixels; 1 keeps per-pixel accuracy.</param>
    /// <returns>The shape, with its scanline runs filled in.</returns>
    /// <exception cref="ArgumentException">When fewer than three vertices are given or the step is not positive.</exception>
    public static PolygonWrapShape FromPolygon(IReadOnlyList<Vector2> points, float step = 1f)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentOutOfRangeException.ThrowIfLessThan(points.Count, 3);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);

        float minY = float.MaxValue, maxY = float.MinValue;
        float minX = float.MaxValue, maxX = float.MinValue;

        foreach (Vector2 point in points)
        {
            if (point.Y < minY) minY = point.Y;
            if (point.Y > maxY) maxY = point.Y;
            if (point.X < minX) minX = point.X;
            if (point.X > maxX) maxX = point.X;
        }

        var runs = new List<(float Y, List<(float Left, float Right)>)>();
        var crossings = new List<float>();

        for (float y = minY; y <= maxY; y += step)
        {
            crossings.Clear();

            for (int i = 0; i < points.Count; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % points.Count];

                // An edge is crossed when the scanline passes it with one endpoint included and the other excluded,
                // so a vertex that lies exactly on the scanline is not counted twice.
                bool upwards = a.Y <= y && b.Y > y;
                bool downwards = b.Y <= y && a.Y > y;

                if (!upwards && !downwards)
                    continue;

                float t = (y - a.Y) / (b.Y - a.Y);
                crossings.Add(a.X + (t * (b.X - a.X)));
            }

            if (crossings.Count < 2)
                continue;

            crossings.Sort();

            var lineRuns = new List<(float Left, float Right)>();

            for (int i = 0; i + 1 < crossings.Count; i += 2)
            {
                if (crossings[i + 1] > crossings[i])
                    lineRuns.Add((crossings[i], crossings[i + 1]));
            }

            if (lineRuns.Count > 0)
                runs.Add((y, lineRuns));
        }

        var extents = new List<(float Y, float Left, float Right)>();

        foreach ((float y, List<(float Left, float Right)> lineRuns) in runs)
        {
            float left = float.MaxValue, right = float.MinValue;

            foreach ((float runLeft, float runRight) in lineRuns)
            {
                if (runLeft < left) left = runLeft;
                if (runRight > right) right = runRight;
            }

            extents.Add((y, left, right));
        }

        return new PolygonWrapShape
        {
            ScanlineRuns = runs,
            ScanlineExtents = extents,
            ScanlineStep = step,
            TotalHeight = maxY - minY,
            TotalWidth = maxX - minX,
        };
    }

    /// <inheritdoc />
    public override Rect2 BoundingBox => new(0, 0, TotalWidth, TotalHeight);

    /// <summary>
    /// Find the index of the first scanline entry whose Y >= target.
    /// Uses binary search for efficiency.
    /// </summary>
    private int FindScanlineIndex(float targetY)
    {
        int lo = 0, hi = ScanlineExtents.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (ScanlineExtents[mid].Y < targetY)
                lo = mid + 1;
            else
                hi = mid;
        }
        // Back up one to include the scanline that starts before targetY but may overlap
        return lo > 0 ? lo - 1 : 0;
    }
}
