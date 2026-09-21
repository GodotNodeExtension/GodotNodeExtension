using System;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Builds the symbol outlines of the shape vocabulary (<see cref="ShapeKind"/>), so marks and the
/// legend draw the same glyphs for the same <see cref="Channel.Shape"/> value.
/// Custom marks can call <see cref="Build"/> directly.
/// </summary>
public static class ShapeGeometry
{
    /// <summary>Add one symbol of <paramref name="radius"/> around (<paramref name="cx"/>, <paramref name="cy"/>) to an open path.</summary>
    public static void Build(IPath2D path, ShapeKind shape, float cx, float cy, float radius)
    {
        if (radius <= 0f) return;

        switch (shape)
        {
            case ShapeKind.Square:
                path.Rect(cx - radius, cy - radius, radius * 2f, radius * 2f);
                return;

            case ShapeKind.Triangle:
                AddPolygon(path, cx, cy, radius, 3, 1f);
                return;

            case ShapeKind.Diamond:
                AddPolygon(path, cx, cy, radius, 4, 1f);
                return;

            case ShapeKind.Star:
                AddPolygon(path, cx, cy, radius, 5, 0.45f);
                return;

            case ShapeKind.Cross:
                AddCross(path, cx, cy, radius);
                return;

            default:
                path.Circle(cx, cy, radius);
                return;
        }
    }

    /// <summary>
    /// The angle of vertex <paramref name="index"/> of a regular polygon with <paramref name="count"/> vertices:
    /// the first one points up and they run clockwise - the convention the symbols and the radar's rings and
    /// series polygons all share.
    /// </summary>
    public static float AngleAt(int index, int count) => -MathF.PI / 2f + MathF.Tau * index / count;

    /// <summary>
    /// Add a closed ring band to an open path: the outer arc, the inner arc reversed, and a close. A pie slice
    /// that is exploded shifts the whole band by <paramref name="offsetX"/> / <paramref name="offsetY"/>.
    /// </summary>
    public static void AddRingBand(IPath2D path, float cx, float cy, float outerR, float innerR,
        float startAngle, float endAngle, float offsetX = 0f, float offsetY = 0f)
    {
        float centreX = cx + offsetX, centreY = cy + offsetY;
        path.MoveTo(centreX + MathF.Cos(startAngle) * outerR, centreY + MathF.Sin(startAngle) * outerR);
        path.ArcTo(centreX, centreY, outerR, startAngle, endAngle);
        path.ArcTo(centreX, centreY, innerR, endAngle, startAngle, clockwise: true);
        path.Close();
    }

    /// <summary>Regular polygon starting at the top; an <paramref name="innerRatio"/> below 1 makes a star.</summary>
    private static void AddPolygon(
        IPath2D path, float cx, float cy, float radius, int points, float innerRatio)
    {
        int count = innerRatio < 1f ? points * 2 : points;
        for (int i = 0; i < count; i++)
        {
            float r = innerRatio < 1f && i % 2 == 1 ? radius * innerRatio : radius;
            float angle = AngleAt(i, count);
            float px = cx + MathF.Cos(angle) * r;
            float py = cy + MathF.Sin(angle) * r;
            if (i == 0) path.MoveTo(px, py);
            else        path.LineTo(px, py);
        }
        path.Close();
    }

    /// <summary>Plus sign: two bars of <paramref name="radius"/> length and 36% width.</summary>
    private static void AddCross(IPath2D path, float cx, float cy, float radius)    {
        float arm = radius * 0.36f;
        (float X, float Y)[] points =
        [
            (cx - arm, cy - radius), (cx + arm, cy - radius),
            (cx + arm, cy - arm),    (cx + radius, cy - arm),
            (cx + radius, cy + arm), (cx + arm, cy + arm),
            (cx + arm, cy + radius), (cx - arm, cy + radius),
            (cx - arm, cy + arm),    (cx - radius, cy + arm),
            (cx - radius, cy - arm), (cx - arm, cy - arm)
        ];

        path.MoveTo(points[0].X, points[0].Y);
        for (int i = 1; i < points.Length; i++) path.LineTo(points[i].X, points[i].Y);
        path.Close();
    }
}

/// <summary>
/// The polar arithmetic the round marks share: how far a point is from a centre, what angle it sits at, and
/// the "never a whole turn" cap the arc drawing needs.
/// </summary>
internal static class PolarGeometry
{
    /// <summary>
    /// Angles stay this far below a full turn. An arc of exactly 2π comes back as zero from the backend's
    /// angle modulo, which draws nothing instead of a closed ring - so a full circle is capped, not passed on.
    /// </summary>
    public const float FullTurnCap = MathF.Tau - 1e-4f;

    /// <summary>Distance from a centre to a point.</summary>
    public static float Distance(float cx, float cy, float x, float y)
    {
        float dx = x - cx, dy = y - cy;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Centre of the disc a round mark draws: the middle of the plot.</summary>
    public static (float Cx, float Cy) Center(PlotArea plot)
        => (plot.X + plot.Width / 2f, plot.Y + plot.Height / 2f);

    /// <summary>Outer radius a round mark draws at: half the plot's short edge, times the mark's own factor.</summary>
    public static float Radius(PlotArea plot, float radiusFactor)
        => Math.Min(plot.Width, plot.Height) / 2f * radiusFactor;

    /// <summary>Centre and outer radius together, for the marks that place their disc in one step.</summary>
    public static (float Cx, float Cy, float Radius) CenterAndRadius(PlotArea plot, float radiusFactor)
    {
        var (cx, cy) = Center(plot);
        return (cx, cy, Radius(plot, radiusFactor));
    }

    /// <summary>Angle of a point around a centre, normalised to [0, 2π).</summary>
    public static float AngleOf(float cx, float cy, float x, float y)
    {
        float angle = MathF.Atan2(y - cy, x - cx);
        return angle < 0f ? angle + MathF.Tau : angle;
    }

    /// <summary>An angle measured from <paramref name="start"/>, normalised to [0, 2π).</summary>
    public static float RelativeAngle(float angle, float start)
        => ((angle - start) % MathF.Tau + MathF.Tau) % MathF.Tau;
}
