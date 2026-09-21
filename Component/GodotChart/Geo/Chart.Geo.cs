namespace GodotNodeExtension.Component.GodotChart;

using System;
using Godot;

/// <summary>
/// Chart partial — the geographic coordinate frame and the map viewport a geographic mark is drawn
/// through. Nothing here is enabled by itself: a chart without a geographic mark keeps a frame it
/// never uses and a viewport that is null, and renders exactly as it did before.
/// </summary>
public partial class Chart
{
    private IGeoFrame _geoFrame = GeoFrames.Wgs84();
    private GeoViewport? _geoViewport;

    /// <summary>
    /// The coordinate frame geographic layers are placed in: what their coordinates mean and how they
    /// are projected. WGS84 with Web Mercator until the host says otherwise, so longitude/latitude data
    /// works out of the box.
    /// </summary>
    public IGeoFrame GeoFrame => _geoFrame;

    /// <summary>
    /// The map viewport, or null while nothing has asked for one. Reading it does not create it: the
    /// viewport exists once a view has been set (<see cref="SetGeoViewport"/>,
    /// <see cref="FitGeoBounds"/>, a gesture) or once a geographic mark needs one.
    /// </summary>
    public GeoViewport? GeoViewport => _geoViewport;

    /// <summary>
    /// Use a different coordinate frame. The viewport is dropped with it: its centre and zoom are
    /// expressed in the old frame's coordinates and would be meaningless in the new one.
    /// </summary>
    /// <param name="frame">Frame to place geographic layers in (see <see cref="GeoFrames"/>).</param>
    public Chart SetGeoFrame(IGeoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (ReferenceEquals(frame, _geoFrame)) return this;

        _geoFrame = frame;
        _geoViewport = null;
        InvalidateLayout();
        return this;
    }

    /// <summary>
    /// Centre the map viewport and set its zoom level, creating the viewport if needed.
    /// </summary>
    /// <param name="centerX">Horizontal coordinate to centre on (longitude for a WGS84 frame).</param>
    /// <param name="centerY">Vertical coordinate to centre on (latitude for a WGS84 frame).</param>
    /// <param name="zoomLevel">Zoom level: every step doubles the world's pixel size.</param>
    public Chart SetGeoViewport(double centerX, double centerY, double zoomLevel)
    {
        EnsureGeoViewport().SetView(centerX, centerY, zoomLevel);
        return this;
    }

    /// <summary>
    /// Zoom around a screen anchor: the map coordinate under the anchor stays under it, which is what a
    /// scroll wheel over a pointer does.
    /// </summary>
    /// <param name="factor">Zoom multiplier (&gt; 1 zooms in, &lt; 1 zooms out).</param>
    /// <param name="anchorPixels">Screen position to keep still.</param>
    /// <param name="plot">Plot rectangle being navigated.</param>
    public Chart ZoomGeo(double factor, Vector2 anchorPixels, in PlotArea plot)
    {
        EnsureGeoViewport().ZoomBy(factor, anchorPixels, plot);
        return this;
    }

    /// <summary>
    /// Pan the map by a screen displacement, so the content follows a drag.
    /// </summary>
    /// <param name="deltaPixels">
    /// Screen displacement in pixels: the amount the content moves, so a drag to the right scrolls the
    /// map to the right.
    /// </param>
    public Chart PanGeo(Vector2 deltaPixels)
    {
        EnsureGeoViewport().PanBy(deltaPixels);
        return this;
    }

    /// <summary>
    /// Fit a rectangle of frame coordinates into the plot, keeping the map's shape.
    /// </summary>
    /// <param name="minX">Left edge.</param>
    /// <param name="minY">Bottom edge.</param>
    /// <param name="maxX">Right edge.</param>
    /// <param name="maxY">Top edge.</param>
    /// <param name="plot">Rectangle to fit into.</param>
    /// <param name="paddingRatio">Fraction of the plot left free on each side (0.05 = 5% each side).</param>
    public Chart FitGeoBounds(double minX, double minY, double maxX, double maxY,
                              in PlotArea plot, float paddingRatio = 0.05f)
    {
        EnsureGeoViewport().Fit(GeoBounds.FromCorners(minX, minY, maxX, maxY), plot, paddingRatio);
        return this;
    }

    /// <summary>
    /// Fit the frame's own world into the plot: the whole map, as the frame defines it.
    /// </summary>
    /// <param name="plot">Rectangle to fit into.</param>
    /// <param name="paddingRatio">Fraction of the plot left free on each side.</param>
    public Chart FitGeoWorld(in PlotArea plot, float paddingRatio = 0.05f)
    {
        EnsureGeoViewport().FitWorld(plot, paddingRatio);
        return this;
    }

    /// <summary>
    /// Turn horizontal wrapping on or off; only a frame that wraps honours it (see
    /// <see cref="GeoViewport.WrapsX"/>).
    /// </summary>
    /// <param name="wrapsX">Whether the horizontal axis wraps.</param>
    public Chart SetGeoWrapX(bool wrapsX)
    {
        EnsureGeoViewport().SetWrapX(wrapsX);
        return this;
    }

    /// <summary>
    /// Limit where the map centre can be moved, or clear the limit.
    /// </summary>
    /// <param name="bounds">Bounds the centre stays inside, or null for no limit.</param>
    public Chart SetGeoPanBounds(GeoBounds? bounds)
    {
        EnsureGeoViewport().SetPanBounds(bounds);
        return this;
    }

    /// <summary>
    /// The viewport, created on first use. It reports every change back to the chart, so a mark's
    /// cached geometry is dropped when the map moves - the one failure mode a cached projection has.
    /// </summary>
    private GeoViewport EnsureGeoViewport()
    {
        if (_geoViewport != null) return _geoViewport;

        var viewport = new GeoViewport(_geoFrame);
        viewport.Changed += InvalidateLayout;
        _geoViewport = viewport;
        return viewport;
    }
}
