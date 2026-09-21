namespace GodotNodeExtension.Component.GodotChart;

using Godot;

/// <summary>
/// The projection step of a coordinate system: normalized data space to screen pixels, and back.
/// <para>
/// A plain Cartesian chart projects through the plot rectangle (<see cref="PlanarMapper"/>), a
/// geographic chart through a map viewport, a spatial one through a camera. Marks receive the mapper
/// of the frame through <see cref="MarkContext.Mapper"/>; a mark that ships a coordinate system of
/// its own projects through it instead of reading <see cref="PlotArea"/> directly.
/// </para>
/// <para>
/// The input is always normalized: <c>x</c>, <c>y</c> and <c>z</c> are scale outputs, each in
/// <c>[0, 1]</c>. A mark that owns its geometry normalizes what it reads before calling in.
/// </para>
/// </summary>
public interface ICoordinateMapper
{
    /// <summary>
    /// Project a normalized point to screen pixels.
    /// </summary>
    /// <param name="x">Normalized horizontal position in [0, 1].</param>
    /// <param name="y">Normalized vertical position in [0, 1], measured upwards.</param>
    /// <param name="z">Normalized depth in [0, 1]. A 2D mapper ignores it.</param>
    /// <param name="depth">
    /// Depth of the projected point in camera space, for draw-order and annotation decisions. A 2D
    /// mapper reports 0: everything is in one plane.
    /// </param>
    /// <returns>The screen position in pixels.</returns>
    Vector2 Project(double x, double y, double z, out float depth);

    /// <summary>
    /// The inverse of <see cref="Project"/>: screen pixels back to normalized space.
    /// </summary>
    /// <param name="screen">Screen position in pixels.</param>
    /// <param name="viewportSize">
    /// Pixel size of the surface the point lives in. A mapper whose camera depends on the aspect ratio
    /// needs it; a planar one does not, because its plot rectangle carries its own extent.
    /// </param>
    /// <param name="x">Normalized horizontal position.</param>
    /// <param name="y">Normalized vertical position, measured upwards.</param>
    /// <param name="z">Normalized depth.</param>
    /// <returns>False when the projection is degenerate and cannot be inverted.</returns>
    bool TryUnproject(Vector2 screen, Vector2 viewportSize, out double x, out double y, out double z);
}

/// <summary>
/// The projection of a Cartesian (2D) chart: the plot rectangle, mapping normalized [0, 1] to pixels
/// exactly as <see cref="PlotArea.MapX"/> and <see cref="PlotArea.MapY"/> do - X to the right, Y
/// flipped, depth ignored.
/// </summary>
internal sealed class PlanarMapper : ICoordinateMapper
{
    /// <summary>
    /// The rectangle to project into. The chart rewrites it in place when the layout changes instead
    /// of allocating a mapper per frame.
    /// </summary>
    internal PlotArea Plot { get; set; }

    /// <inheritdoc />
    public Vector2 Project(double x, double y, double z, out float depth)
    {
        depth = 0f;
        return new Vector2(Plot.MapX(x), Plot.MapY(y));
    }

    /// <inheritdoc />
    public bool TryUnproject(Vector2 screen, Vector2 viewportSize, out double x, out double y, out double z)
    {
        x = 0;
        y = 0;
        z = 0;
        if (!(Plot.Width > 0f) || !(Plot.Height > 0f))
            return false;

        x = (screen.X - Plot.X) / Plot.Width;
        y = (Plot.Y + Plot.Height - screen.Y) / Plot.Height;
        return true;
    }
}
