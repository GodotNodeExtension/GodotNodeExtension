using System;
using System.Collections.Generic;
using GodotNodeExtension.Component.Typography.Core.Model;

namespace GodotNodeExtension.Component.Typography.Core;

/// <summary>
/// Manages all wrap regions and provides per-line available span queries.
/// Used by the layout engine during line-breaking to determine where text can flow.
/// Each query returns one or more horizontal intervals at a given Y, accounting for
/// all exclusion regions positioned in the content area.
/// </summary>
public class WrapRegionManager
{
    private readonly List<WrapRegion> _regions = [];

    /// <summary>Buffer for the intervals one region's shape occupies on a line, reused per query.</summary>
    private readonly List<LineSpan> _shapeSpans = [];
    private readonly float _maxWidth;

    /// <summary>
    /// Create a new WrapRegionManager for the given content width.
    /// </summary>
    /// <param name="maxWidth">Total available layout width.</param>
    public WrapRegionManager(float maxWidth)
    {
        _maxWidth = maxWidth;
    }

    /// <summary>Number of registered exclusion regions.</summary>
    public int RegionCount => _regions.Count;

    /// <summary>
    /// Add an exclusion region. Regions can be added in any order.
    /// </summary>
    /// <param name="region">The wrap region to add.</param>
    public void AddRegion(WrapRegion region)
    {
        ArgumentNullException.ThrowIfNull(region);
        _regions.Add(region);
    }

    /// <summary>
    /// Remove all registered regions.
    /// </summary>
    public void Clear()
    {
        _regions.Clear();
    }

    /// <summary>
    /// Get the available horizontal spans at a given Y for text placement.
    /// Returns one or more (left, right) intervals, accounting for all exclusion regions.
    /// If no regions intersect this Y, returns a single span covering the full width.
    /// </summary>
    /// <param name="y">Y position in content space.</param>
    /// <param name="lineHeight">Height of the text line being placed.</param>
    /// <param name="lineIndex">Which line this is, which decides whether a float's lifecycle covers it.</param>
    /// <returns>Available horizontal spans for text (may be empty if fully occluded).</returns>
    public List<(float Left, float Right)> GetAvailableSpans(float y, float lineHeight, int lineIndex = 0)
    {
        // Collect all exclusion intervals at this Y
        var exclusions = new List<(float Left, float Right)>();

        foreach (var region in _regions)
        {
            // A float only affects the lines it belongs to; outside that range it is not there at all.
            if (lineIndex < region.FirstLine || (region.LastLine >= 0 && lineIndex > region.LastLine))
                continue;

            // Query shape at local-Y (relative to shape origin). A shape may occupy several intervals on one line
            // (a polygon that narrows), and each of them excludes text on its own.
            float localY = y - region.Position.Y;
            _shapeSpans.Clear();
            region.Shape.GetSpansAtY(localY, lineHeight, _shapeSpans);

            foreach (LineSpan shapeSpan in _shapeSpans)
            {
                // Transform to content space
                float contentLeft = region.Position.X + shapeSpan.Left - region.Margin;
                float contentRight = region.Position.X + shapeSpan.Right + region.Margin;

                // Clamp to layout bounds
                contentLeft = Math.Max(contentLeft, 0);
                contentRight = Math.Min(contentRight, _maxWidth);

                if (contentLeft < contentRight)
                {
                    exclusions.Add((contentLeft, contentRight));
                }
            }
        }

        if (exclusions.Count == 0)
        {
            return [(0, _maxWidth)];
        }

        // Sort exclusions by left edge
        exclusions.Sort((a, b) => a.Left.CompareTo(b.Left));

        // Merge overlapping exclusions
        var merged = new List<(float Left, float Right)>();
        var current = exclusions[0];
        for (int i = 1; i < exclusions.Count; i++)
        {
            if (exclusions[i].Left <= current.Right)
            {
                current.Right = Math.Max(current.Right, exclusions[i].Right);
            }
            else
            {
                merged.Add(current);
                current = exclusions[i];
            }
        }
        merged.Add(current);

        // Compute available spans = full width minus merged exclusions
        var spans = new List<(float Left, float Right)>();
        float cursor = 0;

        foreach (var (exLeft, exRight) in merged)
        {
            if (cursor < exLeft)
            {
                spans.Add((cursor, exLeft));
            }
            cursor = Math.Max(cursor, exRight);
        }

        if (cursor < _maxWidth)
        {
            spans.Add((cursor, _maxWidth));
        }

        return spans;
    }

    /// <summary>
    /// Get the lowest Y that is past all current wrap regions.
    /// Useful for clearing floats (analogous to CSS <c>clear: both</c>).
    /// </summary>
    /// <returns>Y value below all region bounding boxes.</returns>
    public float GetClearY()
    {
        float maxBottom = 0;
        foreach (var region in _regions)
        {
            float bottom = region.Position.Y + region.Shape.BoundingBox.Size.Y + region.Margin;
            if (bottom > maxBottom) maxBottom = bottom;
        }
        return maxBottom;
    }
}
