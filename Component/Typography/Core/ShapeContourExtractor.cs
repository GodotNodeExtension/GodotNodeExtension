using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;
using SkiaSharp;

namespace GodotNodeExtension.Component.Typography.Core;

/// <summary>
/// Extracts contour shapes from images based on alpha transparency.
/// Uses SkiaSharp for pixel-level access. The resulting <see cref="PolygonWrapShape"/>
/// can be used with <see cref="WrapRegionManager"/> for non-textual text wrapping.
/// </summary>
/// <remarks>
/// <para>
/// The polygon path (<see cref="ExtractFromTexture"/> / <see cref="ExtractFromBitmap"/>) produces a
/// <see cref="PolygonWrapShape"/>, which the layout honours: a shape may occupy several intervals on a line, so text
/// flows on both sides of it (see the multi-interval contract in the architecture note).
/// <para>
/// A hole in the shape is <em>filled</em>: each row contributes the interval between its first and last opaque pixel,
/// so text never flows into a hole. That is the "do not thread the needle" rule the wrap semantics state, and it is
/// what taking the first and last pixel per row means.
/// </para>
/// </para>
/// <para>
/// Algorithm:
/// <list type="number">
///   <item>Scan each row to find first/last pixel with alpha >= threshold → scanline table</item>
///   <item>Douglas-Peucker simplification on left and right contour point sequences</item>
///   <item>Scale to display size and expand by margin</item>
///   <item>Build <see cref="PolygonWrapShape"/> with O(1) per-line query</item>
/// </list>
/// </para>
/// </remarks>
public static class ShapeContourExtractor
{
    /// <summary>
    /// Extract a wrap shape from a Godot <see cref="Texture2D"/>'s alpha channel.
    /// Transparent areas (alpha below threshold) are excluded from the shape.
    /// </summary>
    /// <param name="texture">Source Godot image texture.</param>
    /// <param name="displaySize">The display size of the image in layout space.</param>
    /// <param name="alphaThreshold">Alpha value threshold (0-255). Pixels below this are transparent.</param>
    /// <param name="margin">Extra margin around the detected shape (pixels in layout space).</param>
    /// <param name="simplifyTolerance">Douglas-Peucker simplification tolerance in source pixels. 0 = no simplification.</param>
    /// <returns>A polygon wrap shape matching the image's visible area.</returns>
    public static PolygonWrapShape ExtractFromTexture(
        Texture2D texture,
        Vector2 displaySize,
        byte alphaThreshold = 10,
        float margin = 4f,
        float simplifyTolerance = 2f)
    {
        ArgumentNullException.ThrowIfNull(texture);

        var image = texture.GetImage();
        if (image == null)
            return CreateFallbackRect(displaySize, margin);

        int width = image.GetWidth();
        int height = image.GetHeight();
        if (width <= 0 || height <= 0)
            return CreateFallbackRect(displaySize, margin);

        // Read pixel data - scan row by row
        var scanlines = new List<(int Left, int Right)>(height);
        for (int row = 0; row < height; row++)
        {
            int left = -1;
            int right = -1;

            for (int col = 0; col < width; col++)
            {
                var pixel = image.GetPixel(col, row);
                byte alpha = (byte)(pixel.A * 255);
                if (alpha >= alphaThreshold)
                {
                    if (left < 0) left = col;
                    right = col;
                }
            }

            scanlines.Add((left, right));
        }

        return BuildShape(scanlines, width, height, displaySize, margin, simplifyTolerance);
    }

    /// <summary>
    /// Extract from a SkiaSharp bitmap directly (for use in background threads
    /// where Godot API access is not available).
    /// </summary>
    /// <param name="bitmap">Source SKBitmap.</param>
    /// <param name="displaySize">The display size of the image in layout space.</param>
    /// <param name="alphaThreshold">Alpha value threshold (0-255).</param>
    /// <param name="margin">Extra margin around the detected shape.</param>
    /// <param name="simplifyTolerance">Douglas-Peucker simplification tolerance. 0 = no simplification.</param>
    /// <returns>A polygon wrap shape matching the image's visible area.</returns>
    public static PolygonWrapShape ExtractFromBitmap(
        SKBitmap bitmap,
        Vector2 displaySize,
        byte alphaThreshold = 10,
        float margin = 4f,
        float simplifyTolerance = 2f)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        int width = bitmap.Width;
        int height = bitmap.Height;
        if (width <= 0 || height <= 0)
            return CreateFallbackRect(displaySize, margin);

        var scanlines = new List<(int Left, int Right)>(height);
        for (int row = 0; row < height; row++)
        {
            int left = -1;
            int right = -1;

            for (int col = 0; col < width; col++)
            {
                var pixel = bitmap.GetPixel(col, row);
                if (pixel.Alpha >= alphaThreshold)
                {
                    if (left < 0) left = col;
                    right = col;
                }
            }

            scanlines.Add((left, right));
        }

        return BuildShape(scanlines, width, height, displaySize, margin, simplifyTolerance);
    }

    /// <summary>
    /// Create a simple rectangular wrap shape (fallback / fast path).
    /// </summary>
    /// <param name="size">Size of the rectangle.</param>
    /// <param name="margin">Extra margin around the shape.</param>
    /// <returns>A rectangular wrap shape.</returns>
    public static RectWrapShape CreateRect(Vector2 size, float margin = 0f)
    {
        return new RectWrapShape
        {
            Width = size.X + margin * 2,
            Height = size.Y + margin * 2,
        };
    }

    /// <summary>
    /// Build the polygon shape from raw scanline data.
    /// </summary>
    private static PolygonWrapShape BuildShape(
        List<(int Left, int Right)> scanlines,
        int sourceWidth, int sourceHeight,
        Vector2 displaySize,
        float margin,
        float simplifyTolerance)
    {
        // Scale factors from source pixels to display space
        float scaleX = displaySize.X / sourceWidth;
        float scaleY = displaySize.Y / sourceHeight;

        // Collect left and right contour points
        var leftPoints = new List<(float Y, float X)>();
        var rightPoints = new List<(float Y, float X)>();

        for (int row = 0; row < scanlines.Count; row++)
        {
            var (left, right) = scanlines[row];
            if (left < 0) continue; // Fully transparent row

            float y = row * scaleY;
            leftPoints.Add((y, left * scaleX));
            rightPoints.Add((y, (right + 1) * scaleX)); // +1 to include the pixel
        }

        if (leftPoints.Count == 0)
        {
            return CreateFallbackRect(displaySize, margin);
        }

        // Simplify contours
        if (simplifyTolerance > 0)
        {
            float scaledTolerance = simplifyTolerance * Math.Max(scaleX, scaleY);
            leftPoints = DouglasPeuckerSimplify(leftPoints, scaledTolerance);
            rightPoints = DouglasPeuckerSimplify(rightPoints, scaledTolerance);
        }

        // Build scanline extents at display-space resolution
        // Use the finer resolution of: 1 source pixel height scaled, or scanlineStep
        float scanlineStep = scaleY;
        if (scanlineStep < 0.5f) scanlineStep = 0.5f;
        if (scanlineStep > 4f) scanlineStep = 4f;

        var extents = new List<(float Y, float Left, float Right)>();
        float maxRight = 0;

        for (float y = 0; y < displaySize.Y; y += scanlineStep)
        {
            float leftX = InterpolateContour(leftPoints, y) - margin;
            float rightX = InterpolateContour(rightPoints, y) + margin;

            leftX = Math.Max(leftX, 0);
            rightX = Math.Min(rightX, displaySize.X + margin * 2);

            if (leftX < rightX)
            {
                extents.Add((y, leftX, rightX));
                if (rightX > maxRight) maxRight = rightX;
            }
        }

        return new PolygonWrapShape
        {
            ScanlineExtents = extents,
            ScanlineStep = scanlineStep,
            TotalHeight = displaySize.Y,
            TotalWidth = maxRight,
        };
    }

    /// <summary>
    /// Interpolate a contour's X value at a given Y by finding the two nearest points
    /// and linearly interpolating between them.
    /// </summary>
    private static float InterpolateContour(List<(float Y, float X)> points, float y)
    {
        if (points.Count == 0) return 0;
        if (points.Count == 1) return points[0].X;

        // Clamp to contour range
        if (y <= points[0].Y) return points[0].X;
        if (y >= points[^1].Y) return points[^1].X;

        // Binary search for the interval containing y
        int lo = 0, hi = points.Count - 1;
        while (lo < hi - 1)
        {
            int mid = (lo + hi) / 2;
            if (points[mid].Y <= y)
                lo = mid;
            else
                hi = mid;
        }

        // Linear interpolation
        float y0 = points[lo].Y, y1 = points[hi].Y;
        float x0 = points[lo].X, x1 = points[hi].X;
        if (Math.Abs(y1 - y0) < 0.001f) return x0;

        float t = (y - y0) / (y1 - y0);
        return x0 + t * (x1 - x0);
    }

    /// <summary>
    /// Douglas-Peucker polyline simplification for (Y, X) point sequences.
    /// Reduces the number of contour points while preserving shape fidelity.
    /// </summary>
    private static List<(float Y, float X)> DouglasPeuckerSimplify(
        List<(float Y, float X)> points, float tolerance)
    {
        if (points.Count <= 2) return [.. points];

        // Find the point farthest from the line segment (first, last)
        float maxDist = 0;
        int maxIndex = 0;

        var first = points[0];
        var last = points[^1];

        for (int i = 1; i < points.Count - 1; i++)
        {
            float dist = PerpendicularDistance(points[i], first, last);
            if (dist > maxDist)
            {
                maxDist = dist;
                maxIndex = i;
            }
        }

        if (maxDist > tolerance)
        {
            // Recursively simplify both halves
            var left = DouglasPeuckerSimplify(points.GetRange(0, maxIndex + 1), tolerance);
            var right = DouglasPeuckerSimplify(points.GetRange(maxIndex, points.Count - maxIndex), tolerance);

            // Combine (remove duplicate point at junction)
            var result = new List<(float Y, float X)>(left.Count + right.Count - 1);
            result.AddRange(left);
            result.AddRange(right.GetRange(1, right.Count - 1));
            return result;
        }
        else
        {
            // All points between first and last are within tolerance
            return [first, last];
        }
    }

    /// <summary>
    /// Perpendicular distance from point P to line segment (A, B).
    /// Uses X as the perpendicular dimension for our (Y, X) coordinate system.
    /// </summary>
    private static float PerpendicularDistance(
        (float Y, float X) point,
        (float Y, float X) lineStart,
        (float Y, float X) lineEnd)
    {
        float dy = lineEnd.Y - lineStart.Y;
        float dx = lineEnd.X - lineStart.X;
        float lengthSq = dy * dy + dx * dx;

        if (lengthSq < 0.0001f) // Degenerate segment
        {
            float ddx = point.X - lineStart.X;
            float ddy = point.Y - lineStart.Y;
            return MathF.Sqrt(ddx * ddx + ddy * ddy);
        }

        // Distance from point to infinite line through lineStart-lineEnd
        float area = Math.Abs(dy * (point.X - lineStart.X) - dx * (point.Y - lineStart.Y));
        return area / MathF.Sqrt(lengthSq);
    }

    private static PolygonWrapShape CreateFallbackRect(Vector2 displaySize, float margin)
    {
        var extents = new List<(float Y, float Left, float Right)>();
        float step = Math.Max(1f, displaySize.Y / 100f);
        for (float y = 0; y < displaySize.Y; y += step)
        {
            extents.Add((y, 0, displaySize.X + margin * 2));
        }

        return new PolygonWrapShape
        {
            ScanlineExtents = extents,
            ScanlineStep = step,
            TotalHeight = displaySize.Y,
            TotalWidth = displaySize.X + margin * 2,
        };
    }
}
