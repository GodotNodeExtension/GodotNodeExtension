namespace GodotNodeExtension.Tests.GodotMapsui;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotMapsui;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for the lifecycle of <see cref="MapsuiControl"/>: the resize hook must be
/// installed and removed symmetrically (leaving the tree, re-entering it, a double exit and an
/// explicit dispose all have to stay quiet - a blind disconnect used to print an engine error), and
/// the component must not write to the console unless <see cref="MapsuiControl.DebugLogging"/> is on.
/// <para>
/// The controls here are created with a zero size and <see cref="MapTileSourceType.None"/>, so no GPU
/// texture is allocated and no tile request is issued: the lifecycle is verifiable on any machine.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MapsuiControlTest
{
    private static bool HasSceneTree()
    {
        if (Engine.GetMainLoop() is SceneTree) return true;
        GD.Print("[skip] MapsuiControl tests need a SceneTree; none in this run");
        return false;
    }

    /// <summary>A control that allocates nothing: no tile layer, no surface (size stays 0).</summary>
    private static MapsuiControl NewControl() => new()
    {
        TileSource = MapTileSourceType.None,
        DebugLogging = false,
    };

    /// <summary>
    /// A control with a viewport size. The camera only works on a sized viewport, so the navigation,
    /// conversion and input cases need one; it stays on <see cref="MapTileSourceType.None"/>, so no
    /// tile request is issued.
    /// </summary>
    private static MapsuiControl SizedControl(float width = 320, float height = 200)
    {
        var control = NewControl();
        control.Size = new Vector2(width, height);
        return control;
    }

    private static SceneTree Tree() => (SceneTree)Engine.GetMainLoop();

    [TestCase]
    public void AddingAndRemovingLeavesTheResizeHookBalanced()
    {
        if (!HasSceneTree()) return;

        var control = NewControl();
        Tree().Root.AddChild(control);

        // Leaving the tree (and re-entering it) must not raise "disconnect a nonexistent connection".
        Tree().Root.RemoveChild(control);
        Tree().Root.AddChild(control);
        Tree().Root.RemoveChild(control);

        control.Free();
    }

    [TestCase]
    public void ExitingTwiceAndBeingFreedIsSafe()
    {
        if (!HasSceneTree()) return;

        var control = NewControl();
        Tree().Root.AddChild(control);
        Tree().Root.RemoveChild(control);

        control._ExitTree();   // the engine can deliver EXIT_TREE more than once
        control.Free();        // freeing runs Dispose(bool) - the path that used to error out
    }

    [TestCase]
    public void BeingFreedWithoutEverEnteringTheTreeIsSafe()
    {
        // Never added, so the resize hook was never installed: releasing it must not try to
        // disconnect a connection that does not exist.
        NewControl().Free();
    }

    [TestCase]
    public void DebugLoggingIsOffByDefault()
    {
        var control = NewControl();

        AssertThat(control.DebugLogging).IsFalse();

        control.Free();
    }

    // ── Camera: zoom levels, bounds, conversions ────────────────────────────

    [TestCase]
    public void ZoomLevelAndResolutionAreInversesOfEachOther()
    {
        foreach (int level in new[] { 0, 2, 8, 12, 20 })
        {
            double resolution = MapsuiHelper.ZoomLevelToResolution(level);
            AssertThat(Math.Abs(MapsuiHelper.ResolutionToZoomLevel(resolution) - level) < 1e-9).IsTrue();
        }

        // Zooming in halves the resolution; a whole level is a factor of two.
        AssertThat(Math.Abs(MapsuiHelper.ZoomLevelToResolution(5)
                            - MapsuiHelper.ZoomLevelToResolution(4) / 2) < 1e-9).IsTrue();

        // A resolution that cannot be mapped reports zoom 0 instead of NaN/Infinity.
        AssertThat(MapsuiHelper.ResolutionToZoomLevel(0)).IsEqual(0.0);
        AssertThat(MapsuiHelper.ResolutionToZoomLevel(double.NaN)).IsEqual(0.0);
        AssertThat(MapsuiHelper.ResolutionToZoomLevel(-5)).IsEqual(0.0);
    }

    [TestCase]
    public void BoundsConversionAcceptsAnyCornerOrderAndPads()
    {
        var ordered = MapsuiHelper.LatLonToMercatorBox(10, 20, 30, 40, paddingRatio: 0);
        var swapped = MapsuiHelper.LatLonToMercatorBox(30, 40, 10, 20, paddingRatio: 0);

        AssertThat(Math.Abs(ordered.MinX - swapped.MinX) < 1e-6).IsTrue();
        AssertThat(Math.Abs(ordered.MaxY - swapped.MaxY) < 1e-6).IsTrue();
        AssertThat(ordered.MinX < ordered.MaxX).IsTrue();
        AssertThat(ordered.MinY < ordered.MaxY).IsTrue();

        // Padding grows the box on every side, so the fitted view keeps a margin.
        var padded = MapsuiHelper.LatLonToMercatorBox(10, 20, 30, 40, paddingRatio: 0.1);
        AssertThat(padded.MinX < ordered.MinX).IsTrue();
        AssertThat(padded.MaxX > ordered.MaxX).IsTrue();
    }

    [TestCase]
    public void BuiltInTileSourcesReportTheirAttribution()
    {
        AssertThat(MapsuiHelper.AttributionFor(MapTileSourceType.OpenStreetMap)).Contains("OpenStreetMap");
        AssertThat(MapsuiHelper.AttributionFor(MapTileSourceType.EsriWorldTopo)).Contains("Esri");
        AssertThat(MapsuiHelper.AttributionFor(MapTileSourceType.EsriWorldDarkGray)).Contains("Esri");
        AssertThat(MapsuiHelper.AttributionFor(MapTileSourceType.CustomXyz, "© Example")).IsEqual("© Example");
        AssertThat(MapsuiHelper.AttributionFor(MapTileSourceType.None)).IsEqual("");
    }

    [TestCase]
    public void TheControlReportsTheCameraAndTheAttribution()
    {
        if (!HasSceneTree()) return;

        var control = SizedControl();
        Tree().Root.AddChild(control);

        AssertThat(control.IsReady).IsTrue();                 // InitializeMap runs on entering the tree
        AssertThat(control.AttributionText).IsEqual("");       // TileSource.None needs none
        AssertThat(control.Rotation).IsEqual(0.0);

        control.SetZoomLevel(6);
        AssertThat(Math.Abs(control.ZoomLevel - 6) < 1e-6).IsTrue();

        control.SetCenter(48.8566, 2.3522);
        var (lat, lon) = control.Center;
        AssertThat(Math.Abs(lat - 48.8566) < 1e-6).IsTrue();
        AssertThat(Math.Abs(lon - 2.3522) < 1e-6).IsTrue();

        control.SetRotation(30);
        AssertThat(Math.Abs(control.Rotation - 30) < 1e-6).IsTrue();

        // An initial view is applied once on startup; the API can re-apply it at any time.
        control.InitialLatitude = 35.6762;
        control.InitialLongitude = 139.6503;
        control.InitialZoomLevel = 9;
        control.ApplyInitialView();
        AssertThat(Math.Abs(control.ZoomLevel - 9) < 1e-6).IsTrue();
        var (initLat, _) = control.Center;
        AssertThat(Math.Abs(initLat - 35.6762) < 1e-6).IsTrue();

        control.Free();
    }

    [TestCase]
    public void FittingBoundsViewsTheWholeArea()
    {
        if (!HasSceneTree()) return;

        var control = SizedControl();
        Tree().Root.AddChild(control);

        control.FitToBounds(10, 20, 30, 40, paddingRatio: 0);

        // The corners of the control show the corners of the requested box (the control is 320x200).
        var (topLeftLat, topLeftLon) = control.ScreenToLatLon(Vector2.Zero);
        var (bottomRightLat, bottomRightLon) = control.ScreenToLatLon(new Vector2(320, 200));

        // The box fills the viewport height exactly (a taller box in a 16:10 viewport is height-limited) ...
        AssertThat(Math.Abs(topLeftLat - 30) < 0.2).IsTrue();
        AssertThat(Math.Abs(bottomRightLat - 10) < 0.2).IsTrue();

        // ... and the width follows from the aspect ratio, so the view reaches at least across the box
        // without zooming out much further than fitting requires.
        AssertThat(topLeftLon <= 20.001).IsTrue();
        AssertThat(bottomRightLon >= 39.999).IsTrue();
        AssertThat(bottomRightLon - topLeftLon < 60).IsTrue();

        control.Free();
    }

    [TestCase]
    public void ScreenAndMapCoordinatesAreInverses()
    {
        if (!HasSceneTree()) return;

        var control = SizedControl();
        Tree().Root.AddChild(control);
        control.SetCenter(51.5074, -0.1278);
        control.SetZoomLevel(10);

        foreach (var pixel in new[] { new Vector2(10, 10), new Vector2(40, 25) })
        {
            var (lat, lon) = control.ScreenToLatLon(pixel);
            var back = control.LatLonToScreen(lat, lon);
            AssertThat(Math.Abs(back.X - pixel.X) < 0.5).IsTrue();
            AssertThat(Math.Abs(back.Y - pixel.Y) < 0.5).IsTrue();
        }

        // London sits at the centre of the 320x200 control (the origin is its top-left corner).
        var centerLatLon = control.ScreenToLatLon(new Vector2(160, 100));
        AssertThat(Math.Abs(centerLatLon.Latitude - 51.5074) < 0.01).IsTrue();
        AssertThat(Math.Abs(centerLatLon.Longitude + 0.1278) < 0.01).IsTrue();

        control.Free();
    }

    [TestCase]
    public void TheMapComesUpWhenItsSizeArrivesAfterReady()
    {
        if (!HasSceneTree()) return;

        // A control inside a scroll container starts with no height (the container takes its minimum
        // size): the map must initialise once the size arrives, not only during _Ready.
        var control = NewControl();
        Tree().Root.AddChild(control);
        AssertThat(control.IsReady).IsFalse();

        int ready = 0;
        control.MapReady += () => ready++;

        control.Size = new Vector2(320, 200);
        control._Process(0.016);

        AssertThat(control.IsReady).IsTrue();
        AssertThat(ready).IsEqual(1);

        control.SetZoomLevel(5);
        AssertThat(Math.Abs(control.ZoomLevel - 5) < 1e-6).IsTrue();

        control.Free();
    }

    [TestCase]
    public void ZoomLockBlocksUserZoomButNotTheApi()
    {
        if (!HasSceneTree()) return;

        var control = SizedControl();
        Tree().Root.AddChild(control);
        control.SetZoomLevel(10);
        control.ZoomLocked = true;

        control._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = new Vector2(20, 20),
        });
        control._Process(0.016);

        AssertThat(Math.Abs(control.ZoomLevel - 10) < 0.05).IsTrue();   // the wheel was ignored

        control.SetZoomLevel(12);                                       // programmatic navigation still works
        AssertThat(Math.Abs(control.ZoomLevel - 12) < 1e-6).IsTrue();

        control.Free();
    }

    [TestCase]
    public void DoubleClickZoomsInAtThePointer()
    {
        if (!HasSceneTree()) return;

        var control = SizedControl();
        Tree().Root.AddChild(control);
        control.SetZoomLevel(10);

        control._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = true,
            Position = new Vector2(240, 60),
        });

        // The zoom is animated (200 ms), so pump frames until it settles.
        for (int i = 0; i < 60 && control.ZoomLevel < 10.99; i++)
        {
            System.Threading.Thread.Sleep(10);
            control._Process(0.016);
        }

        AssertThat(Math.Abs(control.ZoomLevel - 11) < 0.05).IsTrue();

        control.Free();
    }

    [TestCase]
    public void PanBoundsKeepTheUserNearTheArea()
    {
        if (!HasSceneTree()) return;

        var control = SizedControl();
        Tree().Root.AddChild(control);
        control.SetZoomLevel(9);
        control.SetCenter(20, 30);          // inside the bounds below
        control.SetPanBounds(10, 20, 30, 40);

        // Drag far to the west; the limiter has to keep the view inside the area.
        control._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(300, 100),
        });
        for (int i = 0; i < 10; i++)
            control._GuiInput(new InputEventMouseMotion { Position = new Vector2(300 - i * 25, 100) });
        control._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(75, 100),
        });
        control._Process(0.016);

        // The limiter keeps the view inside the box: dragging far west stops at the eastern edge.
        var (lat, lon) = control.Center;
        AssertThat(lon is >= 19.9 and <= 40.1).IsTrue();
        AssertThat(lat is >= 9.9 and <= 30.1).IsTrue();

        // Releasing the restriction lets the same drag move further.
        control.ClearPanBounds();
        control._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(300, 100),
        });
        for (int i = 0; i < 10; i++)
            control._GuiInput(new InputEventMouseMotion { Position = new Vector2(300 - i * 25, 100) });
        control._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(75, 100),
        });
        control._Process(0.016);
        AssertThat(control.Center.Longitude > lon + 1).IsTrue();

        control.ClearPanBounds();
        control.Free();
    }

    // ── Layers ─────────────────────────────────────────────────────────────

    [TestCase]
    public void LayersCanBeAddedInsertedAndCleared()
    {
        if (!HasSceneTree()) return;

        var control = NewControl();          // TileSource.None: the map starts without layers
        Tree().Root.AddChild(control);

        AssertThat(control.Layers.Count).IsEqual(0);

        var bottom = new Mapsui.Layers.MemoryLayer("bottom");
        var top = new Mapsui.Layers.MemoryLayer("top");
        control.AddLayer(bottom);
        control.InsertLayer(0, top);

        AssertThat(control.Layers.Count).IsEqual(2);
        AssertThat(control.Layers[0].Name).IsEqual("top");       // index 0 is drawn first
        AssertThat(control.Layers[1].Name).IsEqual("bottom");

        control.RemoveLayer(top);
        AssertThat(control.Layers.Count).IsEqual(1);

        control.ClearLayers();
        AssertThat(control.Layers.Count).IsEqual(0);

        control.Free();
    }

    // ── Input ──────────────────────────────────────────────────────────────

    [TestCase]
    public void ATapIsOnlyReportedWhenThePointerBarelyMoved()
    {
        if (!HasSceneTree()) return;

        var control = NewControl();
        Tree().Root.AddChild(control);
        int taps = 0;
        control.MapTapped += (_, _) => taps++;

        // Press, drag a whole screen, release: a pan, not a tap.
        control._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(50, 50),
        });
        control._GuiInput(new InputEventMouseMotion { Position = new Vector2(120, 90) });
        control._GuiInput(new InputEventMouseMotion { Position = new Vector2(300, 200) });
        control._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(300, 200),
        });
        AssertThat(taps).IsEqual(0);

        // A click without movement is a tap.
        control._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = true, Position = new Vector2(20, 20),
        });
        control._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = false, Position = new Vector2(21, 20),
        });
        AssertThat(taps).IsEqual(1);

        control.Free();
    }

    [TestCase]
    public void APinchGestureNeverReportsATap()
    {
        if (!HasSceneTree()) return;

        var control = NewControl();
        Tree().Root.AddChild(control);
        int taps = 0;
        control.MapTapped += (_, _) => taps++;

        // Two fingers down, both lifted: a pinch, even though the fingers hardly moved.
        control._GuiInput(new InputEventScreenTouch { Index = 0, Pressed = true, Position = new Vector2(40, 40) });
        control._GuiInput(new InputEventScreenTouch { Index = 1, Pressed = true, Position = new Vector2(60, 40) });
        control._GuiInput(new InputEventScreenTouch { Index = 0, Pressed = false, Position = new Vector2(40, 40) });
        control._GuiInput(new InputEventScreenTouch { Index = 1, Pressed = false, Position = new Vector2(60, 40) });

        AssertThat(taps).IsEqual(0);

        control.Free();
    }

    [TestCase]
    public void TrackpadPanGesturesMoveTheMap()
    {
        if (!HasSceneTree()) return;

        var control = SizedControl();
        Tree().Root.AddChild(control);
        control.SetZoomLevel(10);
        control.SetCenter(0, 0);
        var before = control.Center;

        control._GuiInput(new InputEventPanGesture
        {
            Position = new Vector2(100, 100), Delta = new Vector2(40, 25),
        });

        var after = control.Center;
        AssertThat(Math.Abs(after.Longitude - before.Longitude) > 1e-6).IsTrue();

        control.Free();
    }

    [TestCase]
    public void ViewportChangedFollowsTheActualViewport()
    {
        if (!HasSceneTree()) return;

        var control = SizedControl();
        Tree().Root.AddChild(control);
        int changes = 0;
        control.ViewportChanged += () => changes++;

        control.SetZoomLevel(8);
        control._Process(0.016);
        int afterZoom = changes;
        AssertThat(afterZoom).IsEqual(1);

        // Nothing moved: no further signal.
        control._Process(0.016);
        AssertThat(changes).IsEqual(afterZoom);

        // A wheel zoom moves the viewport even though no drag handler ran.
        control._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = new Vector2(30, 30),
        });
        control._Process(0.016);
        AssertThat(changes).IsEqual(afterZoom + 1);

        control.Free();
    }

    // ── Render scale (needs a GPU surface) ─────────────────────────────────

    [TestCase]
    public void PixelDensityScalesTheRenderedTexture()
    {
        if (!HasSceneTree()) return;
        if (RenderingServer.GetRenderingDevice() is null)
        {
            GD.Print("[skip] PixelDensityScalesTheRenderedTexture: no rendering device in this run");
            return;
        }

        var control = NewControl();
        control.Size = new Vector2(160, 120);
        Tree().Root.AddChild(control);

        AssertThat(control.TextureSize.X).IsEqual(160);
        AssertThat(control.TextureSize.Y).IsEqual(120);

        control.PixelDensity = 2f;

        AssertThat(control.TextureSize.X).IsEqual(320);
        AssertThat(control.TextureSize.Y).IsEqual(240);

        // The snapshot is in texture pixels as well.
        var snapshot = control.GetSnapshot();
        AssertThat(snapshot is not null).IsTrue();
        AssertThat(snapshot!.GetWidth()).IsEqual(320);

        // Out-of-range values are clamped instead of allocating an enormous surface.
        control.PixelDensity = 99f;
        AssertThat(control.PixelDensity).IsEqual(4f);

        control.Free();
    }

}
