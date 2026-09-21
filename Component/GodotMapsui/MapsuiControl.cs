// Copyright (c) GodotNodeExtension contributors.
// Licensed under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GodotNodeExtension.Component.GodotSkia;
using Mapsui;
using Mapsui.Extensions;
using Mapsui.Layers;
using Mapsui.Manipulations;
using Mapsui.Rendering.Skia;

namespace GodotNodeExtension.Component.GodotMapsui;

/// <summary>
/// A Godot Control node that displays an interactive map using Mapsui and SkiaSharp.
/// Supports pan, zoom, and tile-based map layers (e.g., OpenStreetMap) with
/// Vulkan GPU-accelerated rendering via <see cref="SkiaCanvasTexture2D"/>.
/// </summary>
[Tool]
[GlobalClass]
public partial class MapsuiControl : Control
{
    // === Export Properties ===

    /// <summary>
    /// Initial latitude in WGS84 degrees. Applied when the control is first ready.
    /// </summary>
    [Export] public double InitialLatitude { get; set; }

    /// <summary>
    /// Initial longitude in WGS84 degrees. Applied when the control is first ready.
    /// </summary>
    [Export] public double InitialLongitude { get; set; }

    /// <summary>
    /// Initial zoom level (0 = world view, 20 = building detail).
    /// </summary>
    [Export(PropertyHint.Range, "0,20")]
    public int InitialZoomLevel { get; set; } = 2;

    /// <summary>
    /// Whether user interaction (pan/zoom) is enabled.
    /// </summary>
    [Export] public bool EnableInteraction { get; set; } = true;

    /// <summary>
    /// Whether fling (inertia) gesture is enabled after a drag release.
    /// </summary>
    [Export] public bool EnableFling { get; set; } = true;

    /// <summary>
    /// Render scale of the map texture: 1 draws one texture pixel per control pixel, 2 renders at twice
    /// the resolution and downscales it into the control (crisp labels on HiDPI displays or when the
    /// control is scaled up). Input, hit testing and <see cref="GetSnapshot"/> all use texture pixels, so
    /// this factor is applied to the incoming pointer positions too.
    /// </summary>
    [Export(PropertyHint.Range, "0.5,4.0")]
    public float PixelDensity
    {
        get => _pixelDensity;
        set
        {
            var clamped = Math.Clamp(value, 0.5f, 4.0f);
            if (Mathf.IsEqualApprox(clamped, _pixelDensity)) return;
            _pixelDensity = clamped;
            RecreateTexture();
        }
    }

    private float _pixelDensity = 1.0f;

    // === Tile Source Configuration ===

    /// <summary>
    /// The type of tile source used for the base map layer.
    /// Change this to switch between predefined sources or use a custom XYZ URL.
    /// </summary>
    [ExportGroup("Tile Source")]
    [Export] public MapTileSourceType TileSource { get; set; } = MapTileSourceType.OpenStreetMap;

    /// <summary>
    /// Custom XYZ tile URL template. Used when <see cref="TileSource"/> is <see cref="MapTileSourceType.CustomXyz"/>.
    /// Supports placeholders: {z} (zoom), {x} (column), {y} (row), {s} (subdomain), {k} (API key).
    /// Example: "https://tile.openstreetmap.org/{z}/{x}/{y}.png"
    /// </summary>
    [Export] public string CustomTileUrl { get; set; } = "";

    /// <summary>
    /// Comma-separated list of subdomains for the {s} placeholder in the tile URL.
    /// Example: "a,b,c"
    /// </summary>
    [Export] public string CustomTileSubdomains { get; set; } = "";

    /// <summary>
    /// API key for tile sources that require authentication.
    /// Maps to the {k} placeholder in the URL template.
    /// </summary>
    [Export] public string TileApiKey { get; set; } = "";

    /// <summary>
    /// Minimum zoom level for tile fetching (0-20).
    /// </summary>
    [Export(PropertyHint.Range, "0,20")]
    public int TileMinZoom { get; set; }

    /// <summary>
    /// Maximum zoom level for tile fetching (0-20).
    /// </summary>
    [Export(PropertyHint.Range, "0,20")]
    public int TileMaxZoom { get; set; } = 20;

    /// <summary>
    /// When true the user cannot zoom (wheel, pinch, double click); programmatic navigation through
    /// <see cref="SetZoomLevel"/>, <see cref="ZoomBy"/>, <see cref="NavigateTo"/> still works. Use it for
    /// a map with a fixed scale.
    /// </summary>
    [Export] public bool ZoomLocked
    {
        get => _zoomLocked;
        set
        {
            _zoomLocked = value;
            _map?.Navigator.ZoomLock = value;
        }
    }

    private bool _zoomLocked;

    /// <summary>
    /// User agent sent with tile requests. OpenStreetMap's tile usage policy requires a client that
    /// identifies itself, so keep this meaningful when you ship a product.
    /// </summary>
    [Export] public string TileUserAgent { get; set; } = "GodotNodeExtension GodotMapsui/1.0";

    /// <summary>
    /// Draw <see cref="AttributionText"/> in the bottom-left corner with Godot's own font. Off by
    /// default because the Mapsui renderer already prints the tile source's attribution into the map
    /// texture (bottom-right); enable it when the built-in one is missing (a host that replaced the tile
    /// layer) or when you want a label that follows the control's theme.
    /// </summary>
    [Export] public bool ShowAttribution { get; set; }

    /// <summary>
    /// Attribution line used for <see cref="MapTileSourceType.CustomXyz"/>. The predefined sources
    /// report their own required text through <see cref="AttributionText"/>.
    /// </summary>
    [Export] public string CustomAttribution { get; set; } = "";

    /// <summary>
    /// Print lifecycle and per-frame diagnostics to the Godot console. Off by default: the component
    /// stays silent unless a host is troubleshooting it. Real failures are always reported.
    /// </summary>
    [Export] public bool DebugLogging { get; set; }

    // === Signals ===

    /// <summary>
    /// Emitted when the map is tapped/clicked. Provides WGS84 latitude and longitude.
    /// </summary>
    [Signal] public delegate void MapTappedEventHandler(double latitude, double longitude);

    /// <summary>
    /// Emitted when a map feature is tapped. Provides the feature info including
    /// the layer name, feature ID, and WGS84 coordinates.
    /// </summary>
    [Signal] public delegate void MapFeatureTappedEventHandler(
        string layerName, long featureId, double latitude, double longitude);

    /// <summary>
    /// Emitted when the map background is tapped (no feature hit).
    /// Provides WGS84 latitude and longitude of the tap location.
    /// </summary>
    [Signal] public delegate void MapBackgroundTappedEventHandler(double latitude, double longitude);

    /// <summary>
    /// Emitted when the viewport (center/zoom/rotation) changes.
    /// </summary>
    [Signal] public delegate void ViewportChangedEventHandler();

    /// <summary>
    /// Emitted when the map is fully initialized and ready to use.
    /// </summary>
    [Signal] public delegate void MapReadyEventHandler();

    // === Internal State ===

    private SkiaCanvasTexture2D? _skiaTexture;
    private MapRenderer? _mapRenderer;
    private Map? _map;
    private bool _needsRedraw = true;
    private bool _isReady;
    private bool _mapInitialized;
    private Vector2 _lastSize;
    private Vector2I _lastPixelSize;
    private Vector2I _texturePixelSize;
    private bool _textureErrorReported;
    private Callable _resizeCallable;
    private bool _resizeHooked;
    private bool _renderErrorReported;
    private ViewportSnapshot _lastSnapshot;

    /// <summary>
    /// The underlying Mapsui <see cref="Map"/> instance for advanced configuration.
    /// Use this to add custom layers, configure the navigator, etc.
    /// </summary>
    public Map? MapInstance => _map;

    /// <inheritdoc />
    public override void _Ready()
    {
        EnsureResizeHook();
        InitializeMap();
    }

    /// <inheritdoc />
    public override void _EnterTree()
    {
        // Re-entering the tree after leaving it must restore the resize hook: _Ready only runs once
        // per instance, so a hook installed only there would be gone for good.
        if (_isReady)
            EnsureResizeHook();
    }

    /// <inheritdoc />
    public override void _ExitTree() => UnhookResize();

    /// <summary>
    /// Connect the resize handler through an explicit <see cref="Callable"/>: unlike a C# event
    /// subscription it can be handed to <see cref="GodotObject.IsConnected"/>, which keeps the
    /// disconnect safe when the engine delivers EXIT_TREE twice or drops the connection itself
    /// (an editor script reload does that).
    /// </summary>
    private void EnsureResizeHook()
    {
        if (_resizeHooked) return;

        _resizeCallable = Callable.From(OnResized);
        if (!IsConnected(Control.SignalName.Resized, _resizeCallable))
            Connect(Control.SignalName.Resized, _resizeCallable);
        _resizeHooked = true;
    }

    /// <summary>Disconnect the resize handler if it is still connected. Safe to call repeatedly.</summary>
    private void UnhookResize()
    {
        if (!_resizeHooked) return;

        if (IsConnected(Control.SignalName.Resized, _resizeCallable))
            Disconnect(Control.SignalName.Resized, _resizeCallable);
        _resizeHooked = false;
    }

    /// <summary>Write a diagnostic line when <see cref="DebugLogging"/> is on.</summary>
    private void LogDebug(string message)
    {
        if (DebugLogging)
            GD.Print($"[MapsuiControl] {message}");
    }

    private void InitializeMap()
    {
        _map = new Map();
        _mapRenderer = new MapRenderer();
        _map.Navigator.ZoomLock = _zoomLocked;

        // Create tile layer from Export configuration
        var tileLayer = MapsuiHelper.CreateTileLayer(
            TileSource, CustomTileUrl, CustomTileSubdomains,
            TileApiKey, TileMinZoom, TileMaxZoom, TileUserAgent);
        if (tileLayer != null)
            _map.Layers.Add(tileLayer);

        // Subscribe to map events
        _map.RefreshGraphicsRequest += OnMapRefreshGraphicsRequest;
        _map.DataChanged += OnMapDataChanged;

        _isReady = true;
        LogDebug($"Initialized. Size: {Size}");

        // Try to create texture if size is already known
        TryInitializeViewport();
    }

    /// <summary>
    /// Called when control is resized. Creates/recreates the texture and applies the first viewport.
    /// </summary>
    private void OnResized()
    {
        TryInitializeViewport();
    }

    /// <summary>
    /// Bring the map up to date with the control size. Called from <c>_Ready</c> and from every frame:
    /// a container can hand the control its size after <c>_Ready</c> has already run, and the map would
    /// otherwise never get a viewport (no initial view, no MapReady, no navigation).
    /// </summary>
    private void TryInitializeViewport()
    {
        var controlSize = Size;
        if (controlSize.X <= 0 || controlSize.Y <= 0)
            return;

        SyncViewportSize(controlSize);
        EnsureTexture();

        if (!_mapInitialized)
        {
            SetInitialViewport();
            _mapInitialized = true;
            _map?.Refresh();
            LogDebug($"Map viewport initialized: {controlSize.X}x{controlSize.Y}");
            EmitSignal(SignalName.MapReady);
        }
    }

    /// <summary>Texture pixel size of the current control size and render scale.</summary>
    private Vector2I TexturePixelSize(Vector2 controlSize) => new(
        Mathf.Max(1, (int)MathF.Round(controlSize.X * RenderScale)),
        Mathf.Max(1, (int)MathF.Round(controlSize.Y * RenderScale)));

    /// <summary>
    /// Tell the navigator how large the viewport is. This is what makes the camera work, so it is kept
    /// separate from the surface: the map stays navigable (and testable) even when no Skia surface can
    /// be created.
    /// </summary>
    private void SyncViewportSize(Vector2 controlSize)
    {
        var pixelSize = TexturePixelSize(controlSize);
        if (_lastSize == controlSize && _lastPixelSize == pixelSize) return;

        _lastSize = controlSize;
        _lastPixelSize = pixelSize;
        _map?.Navigator.SetSize(pixelSize.X, pixelSize.Y);
        _needsRedraw = true;
    }

    /// <summary>
    /// Create or resize the Skia surface. Best effort: a run without a rendering device (headless CI, a
    /// machine without a GPU) reports it once instead of throwing out of the frame loop.
    /// </summary>
    private void EnsureTexture()
    {
        var pixelSize = _lastPixelSize;
        if (pixelSize.X <= 0 || pixelSize.Y <= 0) return;
        if (_skiaTexture != null && _texturePixelSize == pixelSize) return;

        ReleaseTexture();

        try
        {
            _skiaTexture = new SkiaCanvasTexture2D(pixelSize.X, pixelSize.Y);
            _texturePixelSize = pixelSize;
            _textureErrorReported = false;
        }
        catch (Exception ex)
        {
            _skiaTexture = null;
            _texturePixelSize = Vector2I.Zero;
            if (!_textureErrorReported)
            {
                _textureErrorReported = true;
                GD.PushWarning($"[MapsuiControl] No map surface available ({ex.GetType().Name}: {ex.Message}). " +
                               "The camera keeps working, but nothing is drawn - a rendering device is required.");
            }
        }
    }

    /// <summary>Dispose the current surface, if any.</summary>
    private void ReleaseTexture()
    {
        if (_skiaTexture == null) return;

        _skiaTexture.ReleaseResources();
        if (GodotObject.IsInstanceValid(_skiaTexture))
            _skiaTexture.Dispose();
        _skiaTexture = null;
        _texturePixelSize = Vector2I.Zero;
    }

    private void SetInitialViewport()
    {
        if (_map == null) return;

        var (x, y) = MapsuiHelper.ToSphericalMercator(InitialLatitude, InitialLongitude);
        var resolution = MapsuiHelper.ZoomLevelToResolution(InitialZoomLevel);
        _map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), resolution);
    }

    /// <summary>
    /// Bring the surface in line with the control size and render scale. The navigator size is applied
    /// first (see <see cref="SyncViewportSize"/>), so a missing surface never stops the camera.
    /// </summary>
    private void RecreateTexture()
    {
        var controlSize = Size;
        if (controlSize.X <= 0 || controlSize.Y <= 0)
            return;

        SyncViewportSize(controlSize);
        EnsureTexture();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (!_isReady || _map == null) return;

        // Re-sync size, surface and first viewport: the Resized signal is not guaranteed to arrive
        // before the first frame, and a control whose size becomes known late still has to come up.
        TryInitializeViewport();

        // Update navigator animations (zoom, pan, fling)
        // This is separate from Map.UpdateAnimations() which only handles layer animations.
        if (_map.Navigator.UpdateAnimations())
        {
            _needsRedraw = true;
        }

        // Update layer animations
        if (_map.UpdateAnimations())
        {
            _needsRedraw = true;
        }

        if (_needsRedraw && _skiaTexture != null)
        {
            RenderMap();
            _needsRedraw = false;
        }

        ReportViewportChange();
    }

    /// <summary>
    /// One place that reports viewport changes: wheel zooms, fling and animations move the map without
    /// any input handler running, and the handlers used to fire the signal themselves - which missed
    /// exactly those cases. Comparing a cheap snapshot per frame covers every source.
    /// </summary>
    private void ReportViewportChange()
    {
        if (_map == null) return;

        var viewport = _map.Navigator.Viewport;
        var snapshot = new ViewportSnapshot(
            viewport.CenterX, viewport.CenterY, viewport.Resolution, viewport.Rotation);

        if (snapshot.Equals(_lastSnapshot)) return;

        _lastSnapshot = snapshot;
        EmitSignal(SignalName.ViewportChanged);
    }

    /// <summary>Value comparison of the parts of the viewport a host can observe.</summary>
    private readonly record struct ViewportSnapshot(
        double CenterX, double CenterY, double Resolution, double Rotation);

    /// <inheritdoc />
    public override void _Draw()
    {
        if (_skiaTexture == null) return;

        var size = Size;
        if (size.X <= 0 || size.Y <= 0) return;

        DrawTextureRect(_skiaTexture, new Rect2(Vector2.Zero, size), false);
        DrawAttribution();
    }

    /// <summary>
    /// Draw the attribution line of the tile source into the bottom-left corner. Drawn by the control
    /// (instead of rendering it into the map texture) so it stays readable at any zoom and pixel density.
    /// </summary>
    private void DrawAttribution()
    {
        string text = AttributionText;
        if (!ShowAttribution || string.IsNullOrEmpty(text)) return;

        var size = Size;
        var font = GetThemeDefaultFont();
        int fontSize = Mathf.Max(8, GetThemeDefaultFontSize() - 3);
        var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, fontSize);
        if (size.X < textSize.X + 16 || size.Y < textSize.Y + 12) return;   // too small to show it

        var origin = new Vector2(6, size.Y - 5);
        DrawRect(new Rect2(origin.X - 4, origin.Y - textSize.Y - 2, textSize.X + 8, textSize.Y + 4),
                 new Color(0f, 0f, 0f, 0.45f));
        DrawString(font, origin, text, HorizontalAlignment.Left, -1, fontSize,
                   new Color(1f, 1f, 1f, 0.85f));
    }

    // === Public API ===

    /// <summary>True once the map exists and the first viewport is set up (see <see cref="MapReady"/>).</summary>
    public bool IsReady => _mapInitialized;

    /// <summary>True while a layer is still fetching data (e.g. tiles) - drive a busy indicator with it.</summary>
    public bool IsBusy => _map != null && _map.Layers.Any(layer => layer.Busy);

    /// <summary>Current (fractional) zoom level; 0 is the whole world, 20 is building detail.</summary>
    public double ZoomLevel => _map == null
        ? 0
        : MapsuiHelper.ResolutionToZoomLevel(_map.Navigator.Viewport.Resolution);

    /// <summary>Current map centre in WGS84 degrees.</summary>
    public (double Latitude, double Longitude) Center
    {
        get
        {
            if (_map == null) return (InitialLatitude, InitialLongitude);
            var viewport = _map.Navigator.Viewport;
            return MapsuiHelper.ToLatLon(viewport.CenterX, viewport.CenterY);
        }
    }

    /// <summary>Current rotation in degrees clockwise from true north.</summary>
    public new double Rotation => _map != null ? _map.Navigator.Viewport.Rotation : 0;

    /// <summary>
    /// True when a map surface exists, i.e. the map can actually be drawn. The camera works without one
    /// (<see cref="IsReady"/>), which is what a headless run or a machine without a rendering device
    /// reports.
    /// </summary>
    public bool HasTexture => _skiaTexture != null;

    /// <summary>
    /// Size of the rendered texture in pixels - the control size multiplied by <see cref="PixelDensity"/>.
    /// <see cref="GetSnapshot"/> and screen/world conversions work in these pixels; it is zero when no
    /// surface is available.
    /// </summary>
    public Vector2I TextureSize => _skiaTexture is null
        ? Vector2I.Zero
        : new Vector2I(_skiaTexture.GetWidth(), _skiaTexture.GetHeight());

    /// <summary>
    /// Attribution line of the configured tile source. Drawn by the control when
    /// <see cref="ShowAttribution"/> is on; expose it if the host shows the credit elsewhere.
    /// </summary>
    public string AttributionText => MapsuiHelper.AttributionFor(TileSource, CustomAttribution);

    /// <summary>A snapshot of the layers currently on the map, bottom-most first.</summary>
    public IReadOnlyList<ILayer> Layers =>
        _map is null ? Array.Empty<ILayer>() : _map.Layers.ToList();

    /// <summary>
    /// Adds a layer to the map.
    /// </summary>
    /// <param name="layer">The Mapsui layer to add.</param>
    public void AddLayer(ILayer layer)
    {
        _map?.Layers.Add(layer);
    }

    /// <summary>
    /// Inserts a layer at a position (0 = bottom-most, right above nothing). Use it to place an overlay
    /// under or above the tile layer that <see cref="TileSource"/> created.
    /// </summary>
    /// <param name="index">Index to insert at.</param>
    /// <param name="layer">The Mapsui layer to insert.</param>
    public void InsertLayer(int index, ILayer layer)
    {
        _map?.Layers.Insert(Math.Clamp(index, 0, Math.Max(0, _map.Layers.Count)), layer);
    }

    /// <summary>
    /// Removes a layer from the map.
    /// </summary>
    /// <param name="layer">The Mapsui layer to remove.</param>
    public void RemoveLayer(ILayer layer)
    {
        _map?.Layers.Remove(layer);
    }

    /// <summary>Removes every layer, including the tile layer created from <see cref="TileSource"/>.</summary>
    public void ClearLayers()
    {
        _map?.Layers.Clear();
        _needsRedraw = true;
    }

    /// <summary>
    /// Sets the map center to the given WGS84 coordinates.
    /// </summary>
    /// <param name="latitude">Latitude in degrees.</param>
    /// <param name="longitude">Longitude in degrees.</param>
    /// <param name="duration">Optional animation duration in milliseconds. -1 for no animation.</param>
    public void SetCenter(double latitude, double longitude, long duration = -1)
    {
        if (_map == null) return;

        var (x, y) = MapsuiHelper.ToSphericalMercator(latitude, longitude);
        _map.Navigator.CenterOn(x, y, duration);
    }

    /// <summary>
    /// Sets the zoom level (0 = world, 20 = building detail).
    /// </summary>
    /// <param name="level">Zoom level (0-20).</param>
    /// <param name="duration">Optional animation duration in milliseconds. -1 for no animation.</param>
    public void SetZoomLevel(int level, long duration = -1)
    {
        if (_map == null) return;

        var resolution = MapsuiHelper.ZoomLevelToResolution(Math.Clamp(level, 0, 20));
        _map.Navigator.ZoomTo(resolution, duration);
    }

    /// <summary>
    /// Zooms by whole or fractional levels, keeping the given point (in control pixels) fixed - the
    /// behaviour of a mouse wheel or a pinch. Without a point the map zooms around its centre.
    /// </summary>
    /// <param name="levels">Levels to zoom in (positive) or out (negative); 1 doubles the scale.</param>
    /// <param name="anchor">Point to keep in place, in control pixels; null uses the viewport centre.</param>
    /// <param name="duration">Optional animation duration in milliseconds.</param>
    public void ZoomBy(double levels, Vector2? anchor = null, long duration = -1)
    {
        if (_map == null || !double.IsFinite(levels) || levels == 0) return;

        double resolution = _map.Navigator.Viewport.Resolution * Math.Pow(2, -levels);
        resolution = Math.Clamp(resolution, MapsuiHelper.ZoomLevelToResolution(24),
                                             MapsuiHelper.ZoomLevelToResolution(0));

        if (anchor is { } point)
            _map.Navigator.ZoomTo(resolution, ToMapPosition(point), duration);
        else
            _map.Navigator.ZoomTo(resolution, duration);
    }

    /// <summary>
    /// Rotates the map. 0 = north up (the default).
    /// </summary>
    /// <param name="degrees">Rotation in degrees clockwise from true north.</param>
    /// <param name="duration">Optional animation duration in milliseconds.</param>
    public void SetRotation(double degrees, long duration = -1)
    {
        _map?.Navigator.RotateTo(degrees, duration);
    }

    /// <summary>
    /// Navigates to the given coordinates and zoom level with animation.
    /// </summary>
    /// <param name="latitude">Target latitude in degrees.</param>
    /// <param name="longitude">Target longitude in degrees.</param>
    /// <param name="zoomLevel">Target zoom level (0-20).</param>
    /// <param name="duration">Animation duration in milliseconds.</param>
    public void NavigateTo(double latitude, double longitude, int zoomLevel, long duration = 500)
    {
        if (_map == null) return;

        var (x, y) = MapsuiHelper.ToSphericalMercator(latitude, longitude);
        var resolution = MapsuiHelper.ZoomLevelToResolution(Math.Clamp(zoomLevel, 0, 20));
        _map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), resolution, duration);
    }

    /// <summary>
    /// Fits an area into the viewport. The two corners may be given in any order; a small padding keeps
    /// the edges visible.
    /// </summary>
    /// <param name="latitude1">First corner latitude in degrees.</param>
    /// <param name="longitude1">First corner longitude in degrees.</param>
    /// <param name="latitude2">Opposite corner latitude in degrees.</param>
    /// <param name="longitude2">Opposite corner longitude in degrees.</param>
    /// <param name="paddingRatio">Fraction of the box size added on every side (0.05 = 5%).</param>
    /// <param name="duration">Optional animation duration in milliseconds.</param>
    public void FitToBounds(double latitude1, double longitude1, double latitude2, double longitude2,
                            double paddingRatio = 0.05, long duration = -1)
    {
        if (_map == null) return;

        var box = MapsuiHelper.LatLonToMercatorBox(latitude1, longitude1, latitude2, longitude2, paddingRatio);
        _map.Navigator.ZoomToBox(box, MBoxFit.Fit, duration);
    }

    /// <summary>
    /// Restricts panning to an area, so the user cannot drag the map away from the region of interest.
    /// The corners may be given in any order; <see cref="ClearPanBounds"/> removes the restriction.
    /// </summary>
    /// <param name="latitude1">First corner latitude in degrees.</param>
    /// <param name="longitude1">First corner longitude in degrees.</param>
    /// <param name="latitude2">Opposite corner latitude in degrees.</param>
    /// <param name="longitude2">Opposite corner longitude in degrees.</param>
    /// <param name="paddingRatio">Fraction of the box size added on every side (0 = exact bounds).</param>
    public void SetPanBounds(double latitude1, double longitude1, double latitude2, double longitude2,
                             double paddingRatio = 0)
    {
        // The default pan bounds come from the map extent; this overrides them.
        _map?.Navigator.OverridePanBounds =
            MapsuiHelper.LatLonToMercatorBox(latitude1, longitude1, latitude2, longitude2, paddingRatio);
    }

    /// <summary>Removes the panning restriction set by <see cref="SetPanBounds"/>.</summary>
    public void ClearPanBounds()
    {
        _map?.Navigator.OverridePanBounds = null;
    }

    /// <summary>
    /// Re-applies the exported <see cref="InitialLatitude"/> / <see cref="InitialLongitude"/> /
    /// <see cref="InitialZoomLevel"/>. The inspector values are only used while the control comes up, so
    /// call this to return to them (or after changing them at runtime).
    /// </summary>
    /// <param name="duration">Optional animation duration in milliseconds.</param>
    public void ApplyInitialView(long duration = -1)
    {
        if (_map == null) return;

        var (x, y) = MapsuiHelper.ToSphericalMercator(InitialLatitude, InitialLongitude);
        var resolution = MapsuiHelper.ZoomLevelToResolution(Math.Clamp(InitialZoomLevel, 0, 20));
        _map.Navigator.CenterOnAndZoomTo(new MPoint(x, y), resolution, duration);
    }

    /// <summary>
    /// Converts a point in control pixels to WGS84 degrees - the inverse of <see cref="LatLonToScreen"/>.
    /// Use it to place a marker or a Godot Control at a map position.
    /// </summary>
    /// <param name="position">Position in control pixels (the control's local coordinates).</param>
    /// <returns>WGS84 latitude and longitude in degrees.</returns>
    public (double Latitude, double Longitude) ScreenToLatLon(Vector2 position)
    {
        if (_map == null) return (InitialLatitude, InitialLongitude);

        var world = _map.Navigator.Viewport.ScreenToWorld(ToMapPosition(position));
        return MapsuiHelper.ToLatLon(world.X, world.Y);
    }

    /// <summary>Converts WGS84 degrees to a point in control pixels - the inverse of <see cref="ScreenToLatLon"/>.</summary>
    /// <param name="latitude">Latitude in degrees.</param>
    /// <param name="longitude">Longitude in degrees.</param>
    /// <returns>Position in control pixels.</returns>
    public Vector2 LatLonToScreen(double latitude, double longitude)
    {
        if (_map == null) return Vector2.Zero;

        var (x, y) = MapsuiHelper.ToSphericalMercator(latitude, longitude);
        var screen = _map.Navigator.Viewport.WorldToScreen(new MPoint(x, y));
        float scale = RenderScale;
        return new Vector2((float)(screen.X / scale), (float)(screen.Y / scale));
    }

    /// <summary>
    /// Forces a complete refresh of map data and rendering.
    /// </summary>
    public void RefreshMap()
    {
        _map?.Refresh();
        _needsRedraw = true;
    }

    /// <summary>
    /// Captures the current map view as a Godot <see cref="Image"/>. The image is in texture pixels, so
    /// it is <see cref="PixelDensity"/> times the control size.
    /// </summary>
    /// <returns>A Godot Image snapshot of the current map, or null if not ready.</returns>
    public Image? GetSnapshot()
    {
        return _skiaTexture?.GetImage();
    }

    /// <summary>The render scale actually in use (the export clamped to the supported range).</summary>
    private float RenderScale => Math.Clamp(_pixelDensity, 0.5f, 4.0f);

    /// <summary>Map a point in control pixels to the texture pixel space Mapsui works in.</summary>
    private ScreenPosition ToMapPosition(Vector2 position)
    {
        float scale = RenderScale;
        return new ScreenPosition(position.X * scale, position.Y * scale);
    }

    // === Callbacks ===

    private void OnMapRefreshGraphicsRequest(object? sender, EventArgs e)
    {
        _needsRedraw = true;
    }

    private void OnMapDataChanged(object? sender, Mapsui.Fetcher.DataChangedEventArgs? e)
    {
        _needsRedraw = true;
    }

    // === Cleanup ===

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UnhookResize();

            if (_map != null)
            {
                _map.RefreshGraphicsRequest -= OnMapRefreshGraphicsRequest;
                _map.DataChanged -= OnMapDataChanged;
                _map.Dispose();
                _map = null;
            }

            ReleaseTexture();
            _mapRenderer = null;
        }

        base.Dispose(disposing);
    }
}
