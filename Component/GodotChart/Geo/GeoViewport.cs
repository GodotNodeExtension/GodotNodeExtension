namespace GodotNodeExtension.Component.GodotChart;

using System;
using Godot;

/// <summary>
/// The window a geographic chart looks at its frame through: a centre in frame coordinates and a zoom
/// level, from which every point is projected into the plot rectangle.
/// <para>
/// It is deliberately not a pair of per-axis windows (the way <c>ZoomDomain</c> / <c>PanDomain</c>
/// window a Cartesian chart): a map's horizontal and vertical scales are tied together, and two
/// independent scalar windows would stretch the world whenever the plot is not the world's shape.
/// One zoom level is a single resolution, so the aspect ratio survives by construction.
/// </para>
/// <para>
/// The mapping is a world of <c>256 * 2^zoom</c> pixels per normalized unit (see
/// <see cref="GeoMath.WorldSizeAtZoomZero"/>), with the plot's centre showing the viewport's centre.
/// Every mutation raises <see cref="Changed"/>, which a host uses to drop whatever it cached from the
/// projection: a viewport change that does not invalidate a mark's cached geometry freezes the
/// picture while the map is dragged.
/// </para>
/// </summary>
public sealed class GeoViewport
{
    private (double U, double V) _centerUv;

    /// <summary>
    /// Create a viewport over a frame, showing the whole world (zoom 0) with the world at the centre.
    /// Fit it to the data, or set a view, before drawing anything.
    /// </summary>
    /// <param name="frame">Frame the viewport's coordinates are expressed in.</param>
    public GeoViewport(IGeoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        Frame = frame;
        WrapsX = frame.WrapsX;
        (CenterX, CenterY) = frame.Denormalize(0.5, 0.5);
        _centerUv = (0.5, 0.5);
    }

    /// <summary>The frame this viewport projects through. Fixed for the lifetime of the viewport.</summary>
    public IGeoFrame Frame { get; }

    /// <summary>Horizontal coordinate the viewport is centred on (longitude for a WGS84 frame).</summary>
    public double CenterX { get; private set; }

    /// <summary>Vertical coordinate the viewport is centred on (latitude for a WGS84 frame).</summary>
    public double CenterY { get; private set; }

    /// <summary>Zoom level: every step doubles the pixel size of the world. Continuous, so 4.5 is fine.</summary>
    public double ZoomLevel { get; private set; }

    /// <summary>
    /// Whether the horizontal axis wraps around its world (a sphere frame; a plane frame's edges are
    /// edges). It starts from the frame and can be turned off for a view that must show the split at
    /// the antimeridian instead of the geometry that crosses it.
    /// </summary>
    public bool WrapsX { get; private set; }

    /// <summary>
    /// Limits the centre can be moved inside, or null (the default) for no limit. A map of one country
    /// wants to stay over that country when the user drags.
    /// </summary>
    public GeoBounds? PanBounds { get; private set; }

    /// <summary>Raised whenever the centre, the zoom, the wrapping or the pan bounds changed.</summary>
    public event Action? Changed;

    /// <summary>Pixels the frame's whole world spans horizontally at the current zoom.</summary>
    public double WorldWidthPixels => GeoMath.WorldSizeAtZoomZero * Math.Pow(2.0, ZoomLevel);

    /// <summary>Pixels the frame's whole world spans vertically at the current zoom.</summary>
    public double WorldHeightPixels => WorldWidthPixels / Aspect;

    /// <summary>
    /// Pixels one of the frame's own horizontal units covers at the current zoom (a degree of
    /// longitude for a WGS84 frame, a world unit for a plane frame). Vertical distances need the
    /// frame's own mapping: a sphere frame's vertical scale varies with latitude.
    /// </summary>
    public double PixelsPerUnit => Frame.WorldWidth > 0 ? WorldWidthPixels / Frame.WorldWidth : 1.0;

    /// <summary>The frame's aspect, with a degenerate one read as square instead of dividing by zero.</summary>
    private double Aspect => Frame.Aspect > 0 ? Frame.Aspect : 1.0;

    /// <summary>
    /// Project a frame coordinate to screen pixels.
    /// </summary>
    /// <param name="x">Horizontal frame coordinate.</param>
    /// <param name="y">Vertical frame coordinate.</param>
    /// <param name="plot">Rectangle to project into: its centre shows the viewport's centre.</param>
    /// <returns>The screen position in pixels; finite even for a degenerate plot rectangle.</returns>
    public Vector2 Project(double x, double y, in PlotArea plot)
    {
        var (u, v) = Frame.Normalize(x, y);
        double du = u - _centerUv.U;
        if (WrapsX) du -= Math.Round(du); // the copy of the world nearest the viewport's centre

        return new Vector2(
            plot.X + plot.Width * 0.5f + (float)(du * WorldWidthPixels),
            plot.Y + plot.Height * 0.5f - (float)((v - _centerUv.V) * WorldHeightPixels));
    }

    /// <summary>
    /// The inverse of <see cref="Project"/>: screen pixels back to a frame coordinate.
    /// <para>
    /// A wrapping viewport reports the equivalent coordinate it lands on, unrolled: a point may come
    /// back as longitude 190 rather than -170, which keeps <c>Unproject(Project(p))</c> exact (use
    /// <see cref="IGeoFrame.WrapX"/> to fold it into the frame's own range).
    /// </para>
    /// </summary>
    /// <param name="screen">Screen position in pixels.</param>
    /// <param name="plot">Rectangle the position was projected into.</param>
    public (double X, double Y) Unproject(Vector2 screen, in PlotArea plot)
    {
        double u = _centerUv.U + (screen.X - (plot.X + plot.Width * 0.5f)) / WorldWidthPixels;
        double v = _centerUv.V - (screen.Y - (plot.Y + plot.Height * 0.5f)) / WorldHeightPixels;
        return Frame.Denormalize(u, v);
    }

    /// <summary>
    /// The frame coordinates the plot rectangle currently shows, from its two opposite corners (the
    /// mapping is monotone, so they bound the rest).
    /// </summary>
    /// <param name="plot">Rectangle to measure.</param>
    public GeoBounds VisibleBounds(in PlotArea plot)
    {
        var (x0, y0) = Unproject(new Vector2(plot.X, plot.Y), plot);
        var (x1, y1) = Unproject(new Vector2(plot.X + plot.Width, plot.Y + plot.Height), plot);
        return GeoBounds.FromCorners(x0, y0, x1, y1);
    }

    /// <summary>
    /// Move the viewport to a centre and a zoom level in one step.
    /// </summary>
    /// <param name="centerX">Horizontal coordinate to centre on.</param>
    /// <param name="centerY">Vertical coordinate to centre on.</param>
    /// <param name="zoomLevel">Zoom level; clamped to the supported range.</param>
    public void SetView(double centerX, double centerY, double zoomLevel)
    {
        if (!double.IsFinite(centerX) || !double.IsFinite(centerY)) return;
        SetState(centerX, centerY, GeoMath.ClampZoom(zoomLevel));
    }

    /// <summary>
    /// Centre the viewport on a coordinate, keeping the zoom level.
    /// </summary>
    /// <param name="centerX">Horizontal coordinate to centre on.</param>
    /// <param name="centerY">Vertical coordinate to centre on.</param>
    public void SetCenter(double centerX, double centerY) => SetView(centerX, centerY, ZoomLevel);

    /// <summary>
    /// Set the zoom level, keeping the centre.
    /// </summary>
    /// <param name="zoomLevel">Zoom level; clamped to the supported range.</param>
    public void SetZoom(double zoomLevel) => SetView(CenterX, CenterY, zoomLevel);

    /// <summary>
    /// Turn horizontal wrapping on or off; see <see cref="WrapsX"/>.
    /// </summary>
    /// <param name="wrapsX">Whether the horizontal axis wraps.</param>
    public void SetWrapX(bool wrapsX)
    {
        if (WrapsX == wrapsX) return;
        WrapsX = wrapsX;
        Changed?.Invoke();
    }

    /// <summary>
    /// Limit where the centre can be moved, or clear the limit.
    /// </summary>
    /// <param name="bounds">Bounds the centre stays inside, or null for no limit.</param>
    public void SetPanBounds(GeoBounds? bounds)
    {
        PanBounds = bounds;
        // Re-apply the current view so a centre that is already outside the new bounds moves in.
        SetState(CenterX, CenterY, ZoomLevel);
    }

    /// <summary>
    /// Zoom by a factor around a screen anchor: the frame coordinate under the anchor stays under it.
    /// This is what makes a scroll wheel zoom behave like a map's.
    /// </summary>
    /// <param name="factor">Zoom multiplier (&gt; 1 zooms in, &lt; 1 zooms out).</param>
    /// <param name="anchorPixels">Screen position that must not move.</param>
    /// <param name="plot">Rectangle being navigated.</param>
    public void ZoomBy(double factor, Vector2 anchorPixels, in PlotArea plot)
    {
        if (!(factor > 0) || !double.IsFinite(factor)) return;

        var (anchorX, anchorY) = Unproject(anchorPixels, plot);
        SetZoom(ZoomLevel + Math.Log2(factor));

        // The anchor moved by the zoom: shift the centre back so it is under the same pixel again.
        var projected = Project(anchorX, anchorY, plot);
        PanBy(anchorPixels - projected);
    }

    /// <summary>
    /// Pan by a screen displacement: the picture follows the pixels, so the content under the pointer
    /// stays under it while dragging.
    /// </summary>
    /// <param name="deltaPixels">
    /// Screen displacement in pixels: the amount the content moves, so a drag right scrolls the map
    /// right.
    /// </param>
    public void PanBy(Vector2 deltaPixels)
    {
        if (!double.IsFinite(deltaPixels.X) || !double.IsFinite(deltaPixels.Y)) return;

        // Screen Y grows downwards while the normalized vertical axis grows upwards.
        var (x, y) = Frame.Denormalize(
            _centerUv.U - deltaPixels.X / WorldWidthPixels,
            _centerUv.V + deltaPixels.Y / WorldHeightPixels);
        SetState(x, y, ZoomLevel);
    }

    /// <summary>
    /// Fit a rectangle of frame coordinates into the plot, keeping its shape.
    /// <para>
    /// A rectangle that crosses the antimeridian is given with unrolled coordinates (170° … 190°),
    /// which is what <see cref="IGeoFrame.Normalize"/> expects of a wrapped longitude.
    /// </para>
    /// </summary>
    /// <param name="bounds">Rectangle to show. Its corners may be given in any order.</param>
    /// <param name="plot">Rectangle to fit into.</param>
    /// <param name="paddingRatio">Fraction of the plot left free on each side (0.05 = 5% each side).</param>
    /// <returns>False when there is nothing to fit (an empty rectangle or a degenerate plot), in which case the viewport is left alone.</returns>
    public bool Fit(GeoBounds bounds, in PlotArea plot, float paddingRatio = 0.05f)
    {
        var target = GeoBounds.FromCorners(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY);
        if (target.IsEmpty) return false;

        double pad = Math.Clamp(paddingRatio, 0f, 0.4f);
        double availableWidth  = plot.Width * (1.0 - 2.0 * pad);
        double availableHeight = plot.Height * (1.0 - 2.0 * pad);
        if (!(availableWidth > 0) || !(availableHeight > 0)) return false;

        // Fit in the normalized world, where a sphere frame's projection has already straightened the
        // vertical axis: the same world width then satisfies both axes, and the smaller of the two
        // requirements is what fits.
        var (u0, v0) = Frame.Normalize(target.MinX, target.MinY);
        var (u1, v1) = Frame.Normalize(target.MaxX, target.MaxY);
        double uSpan = Math.Abs(u1 - u0);
        double vSpan = Math.Abs(v1 - v0);

        double worldWidth = double.PositiveInfinity;
        if (uSpan > 0) worldWidth = Math.Min(worldWidth, availableWidth / uSpan);
        if (vSpan > 0) worldWidth = Math.Min(worldWidth, availableHeight * Aspect / vSpan);
        if (!(worldWidth > 0) || !double.IsFinite(worldWidth)) return false;

        var (centerX, centerY) = Frame.Denormalize((u0 + u1) / 2.0, (v0 + v1) / 2.0);
        SetView(centerX, centerY, Math.Log2(worldWidth / GeoMath.WorldSizeAtZoomZero));
        return true;
    }

    /// <summary>
    /// Fit the frame's own world into the plot.
    /// </summary>
    /// <param name="plot">Rectangle to fit into.</param>
    /// <param name="paddingRatio">Fraction of the plot left free on each side.</param>
    /// <returns>False when there is nothing to fit, in which case the viewport is left alone.</returns>
    public bool FitWorld(in PlotArea plot, float paddingRatio = 0.05f)
    {
        var (x0, y0) = Frame.Denormalize(0.0, 0.0);
        var (x1, y1) = Frame.Denormalize(1.0, 1.0);
        return Fit(GeoBounds.FromCorners(x0, y0, x1, y1), plot, paddingRatio);
    }

    /// <summary>
    /// Apply a candidate view: clamped to <see cref="PanBounds"/>, folded by a wrapping frame, and
    /// published only when it differs from the current one.
    /// </summary>
    private void SetState(double centerX, double centerY, double zoomLevel)
    {
        if (PanBounds is { } bounds)
        {
            var limits = GeoBounds.FromCorners(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY);
            centerX = Math.Clamp(centerX, limits.MinX, limits.MaxX);
            centerY = Math.Clamp(centerY, limits.MinY, limits.MaxY);
        }
        if (WrapsX) centerX = Frame.WrapX(centerX);

        if (Same(centerX, CenterX) && Same(centerY, CenterY) && Same(zoomLevel, ZoomLevel)) return;

        CenterX = centerX;
        CenterY = centerY;
        ZoomLevel = zoomLevel;
        _centerUv = Frame.Normalize(centerX, centerY);
        Changed?.Invoke();
    }

    /// <summary>
    /// Whether a candidate value is the current one, exactly - not within a tolerance. This answers "did
    /// anything change": a view that moved at all has to rebuild its projection, and a host that re-applies
    /// its saved view every frame must not drop the caches every frame.
    /// </summary>
    /// <remarks>
    /// Compares through <see cref="double.CompareTo(double)"/> deliberately: an equality comparison of two
    /// floats reads like a bug everywhere else in the code base, so the one place where it is the point says
    /// so instead of looking like one.
    /// </remarks>
    private static bool Same(double candidate, double current) => candidate.CompareTo(current) == 0;
}
