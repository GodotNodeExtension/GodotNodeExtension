using System;
using Godot;

namespace GodotNodeExtension.Component.GodotChart.Canvas;

/// <summary>
/// A <see cref="Control"/> that hosts a <see cref="ICanvas2D"/> for you: it creates the canvas (via
/// <see cref="Canvas2DFactory"/>), keeps its surface in sync with the node size, runs the frame
/// loop (<see cref="ICanvas2D.Tick"/>, begin/draw/end frame) and presents the resulting texture.
/// <para>
/// Draw into it by handling <see cref="CanvasDraw"/> (or overriding <see cref="OnCanvasDraw"/>) and
/// calling <see cref="Invalidate"/> whenever the content changes - then the node re-runs that
/// delegate on the next frame and redraws itself:
/// </para>
/// <code>
/// var view = new Canvas2DControl { AutoResize = true };
/// AddChild(view);
/// view.CanvasDraw += (control, canvas) =>
/// {
///     canvas.Clear(new Color(0.08f, 0.08f, 0.12f));
///     chart.Render();
/// };
/// view.Invalidate();
/// </code>
/// <para>
/// The canvas is exposed as <see cref="Canvas"/> and its surface as <see cref="Texture"/>, so a host
/// that would rather present it elsewhere (a <see cref="Sprite2D"/>, a <see cref="TextureRect"/>,
/// several consumers sharing one canvas) can use <see cref="Canvas2DFactory"/> directly and skip
/// this node entirely.
/// </para>
/// </summary>
[GlobalClass]
public partial class Canvas2DControl : Control
{
    /// <summary>Backend used to create the canvas. <see cref="CanvasBackendType.Auto"/> picks the best one.</summary>
    [Export] public CanvasBackendType Backend { get; set; } = CanvasBackendType.Auto;

    /// <summary>
    /// When true (default) the canvas surface follows the node size, so drawing coordinates match
    /// local control coordinates.
    /// <para>
    /// When false the surface keeps the size it was created with: the first canvas is still built at
    /// the node size (a zero <see cref="CanvasSize"/> always falls back to it), but resizing the node
    /// afterwards no longer touches the surface - every later size change has to go through
    /// <see cref="ResizeCanvas"/>.
    /// </para>
    /// </summary>
    [Export] public bool AutoResize { get; set; } = true;

    /// <summary>Clear the surface before every redraw. Turn off for content that accumulates between frames.</summary>
    [Export] public bool ClearBeforeDraw { get; set; } = true;

    /// <summary>Colour used to clear the surface when <see cref="ClearBeforeDraw"/> is on.</summary>
    [Export] public Color BackgroundColor { get; set; } = Colors.Transparent;

    /// <summary>
    /// Advisory switch for the component drawn on this canvas: it says that the component may keep the part
    /// of its frame that does not depend on the pointer in an image and redraw only the overlay between
    /// frames (a chart does that with <c>Chart.UseLayerCache</c>, which <c>ChartView.LayeredRendering</c>
    /// mirrors here). The control itself does not change how it draws.
    /// <para>
    /// What it does check is the precondition such a cache has: the surface has to be cleared before every
    /// redraw, because a cached layer carries its own alpha and would otherwise be composited onto the
    /// previous frame. With <see cref="ClearBeforeDraw"/> off and this switch on, the control reports that
    /// once instead of leaving a component to cache into a surface that accumulates.
    /// </para>
    /// </summary>
    [Export] public bool LayeredRendering { get; set; }

    /// <summary>
    /// When true (default) the surface is drawn into the whole node rect; when false it is drawn at
    /// its natural pixel size from the top-left corner.
    /// </summary>
    [Export] public bool StretchToNodeSize { get; set; } = true;

    /// <summary>
    /// True (default) when leaving the tree disposes the hosted canvas. Set false when the canvas is
    /// shared with another consumer that owns its lifetime.
    /// </summary>
    public bool OwnsCanvas { get; set; } = true;

    /// <summary>
    /// Optional canvas factory override, used instead of <see cref="CreateCanvas"/>. Set it to host a
    /// canvas you built yourself (a custom backend, a shared canvas, or a headless canvas in tests).
    /// Set it before the node enters the tree.
    /// </summary>
    public Func<int, int, ICanvas2D>? CanvasFactory { get; set; }

    /// <summary>The canvas hosted by this node, or null before <c>_Ready</c> / after leaving the tree.</summary>
    public ICanvas2D? Canvas { get; private set; }

    /// <summary>The texture the canvas draws into (null when there is no canvas).</summary>
    public Texture2D? Texture => Canvas?.Texture;

    /// <summary>Pixel size of the canvas surface.</summary>
    public Vector2I CanvasSize { get; private set; }

    /// <summary>True once the canvas exists and can be drawn into.</summary>
    public bool IsReady => Canvas != null;

    /// <summary>
    /// Raised on the main thread when the node redraws itself - i.e. after <see cref="Invalidate"/>
    /// and on every resize, once per frame at most. Receives this node and its canvas.
    /// </summary>
    public event Action<Canvas2DControl, ICanvas2D>? CanvasDraw;

    private bool _dirty = true;
    private bool _presented;
    private bool _resizeHooked;
    private Callable _resizeCallable;

    /// <summary>
    /// Mark the content as changed: the canvas is re-drawn on the next frame and the node redraws.
    /// Calling this several times in one frame costs one redraw.
    /// </summary>
    public void Invalidate()
    {
        _dirty = true;
        QueueRedraw();
    }

    /// <summary>
    /// Resize the canvas surface explicitly (used when <see cref="AutoResize"/> is off, or to render
    /// at a fixed resolution that is then scaled to the node). With <see cref="AutoResize"/> off this
    /// is the only way to change the surface size: the first canvas is created at the node size, and
    /// only a call to this method moves it onto another resolution.
    /// </summary>
    public void ResizeCanvas(Vector2I size)
    {
        size = new Vector2I(Mathf.Max(1, size.X), Mathf.Max(1, size.Y));

        // CanvasSize is this node's bookkeeping only - Canvas is public, so a host may have resized the
        // surface itself. The surface decides whether there is anything to do.
        Vector2I surfaceSize = Canvas?.Texture is { } surface ? (Vector2I)surface.GetSize() : CanvasSize;
        if (size == surfaceSize)
        {
            CanvasSize = size;
            return;
        }

        CanvasSize = size;
        Canvas?.Resize(size.X, size.Y);
        // The rebuilt surface is empty: presenting it before the next draw would flash a blank frame.
        _presented = false;
        Invalidate();
    }

    /// <inheritdoc />
    public override void _EnterTree()
    {
        // Entering/leaving the tree are symmetric and can happen repeatedly (re-parenting), while
        // _Ready only runs once per instance - so both the resize hook and the canvas live here.
        // The flag makes the disconnect idempotent: the engine can deliver EXIT_TREE more than once
        // during shutdown, and a second "disconnect" would otherwise be reported as an error.
        if (!_resizeHooked)
        {
            // Connect through an explicit Callable: it can be handed to IsConnected() later, which a
            // C# event subscription cannot - and the editor's script reload can drop the connection
            // behind our back, turning a blind Disconnect into an engine error.
            _resizeCallable = Callable.From(OnResized);
            if (!IsConnected(Control.SignalName.Resized, _resizeCallable))
                Connect(Control.SignalName.Resized, _resizeCallable);
            _resizeHooked = true;
        }

        // Re-entering after leaving re-creates the canvas that _ExitTree released.
        if (Canvas is null)
            CreateHostedCanvas();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_resizeHooked)
        {
            if (IsConnected(Control.SignalName.Resized, _resizeCallable))
                Disconnect(Control.SignalName.Resized, _resizeCallable);
            _resizeHooked = false;
        }

        // The canvas owns native resources; release them with the node. Anything presenting the
        // texture (this node included) must stop using it from here on.
        if (OwnsCanvas)
            Canvas?.Dispose();
        Canvas = null;
        _presented = false;
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (Canvas is null) return;

        Canvas.Tick();
        if (!_dirty) return;

        WarnIfCachingIntoAnUnclearedSurface();

        bool failed = false;
        try
        {
            Canvas.BeginFrame();

            // Clearing belongs to the frame: a backend prepares its surface in BeginFrame, so a Clear
            // issued before it would be undone by that preparation.
            if (ClearBeforeDraw)
                Canvas.Clear(BackgroundColor);
            OnCanvasDraw(Canvas);
            CanvasDraw?.Invoke(this, Canvas);
        }
        catch (Exception ex)
        {
            failed = true;
            ReportDrawFailure(ex);
        }
        finally
        {
            // EndFrame runs even when a draw callback threw: the backend still has to flush the
            // commands it recorded and release its frame state. A failure of the flush itself is
            // reported like the others instead of escaping the finally block.
            try
            {
                Canvas.EndFrame();
            }
            catch (Exception ex)
            {
                failed = true;
                ReportDrawFailure(ex);
            }
        }

        // The flag is only cleared on success: a failed frame stays dirty so the next one retries,
        // instead of freezing the canvas behind "if (!_dirty) return" until something calls
        // Invalidate() again. Presenting the last committed frame is what the finally block above
        // already guarantees, so the presentation flags are updated on both paths.
        _dirty = failed;
        _presented = true;
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        var texture = Texture;
        if (texture is null || !_presented) return;

        // A host that sizes this node explicitly, or whose layout pass has not run yet, can leave the
        // node rect empty; presenting at the surface size keeps the canvas visible in that case.
        var rect = StretchToNodeSize && Size is { X: > 0f, Y: > 0f }
            ? new Rect2(Vector2.Zero, Size)
            : new Rect2(Vector2.Zero, texture.GetSize());

        DrawTextureRect(texture, rect, tile: false);
    }

    /// <summary>
    /// Called once per redraw with the canvas, after it has been cleared and before the frame is
    /// committed. Override in a subclass, or subscribe to <see cref="CanvasDraw"/>.
    /// </summary>
    protected virtual void OnCanvasDraw(ICanvas2D canvas) { }

    /// <summary>
    /// Create the canvas this node hosts, at the given surface size. The default uses
    /// <see cref="Canvas2DFactory"/> with <see cref="Backend"/>; override it to plug in a custom
    /// backend (or to inject a canvas in tests).
    /// </summary>
    protected virtual ICanvas2D CreateCanvas(int width, int height)
        => Canvas2DFactory.Create(width, height, Backend);

    private void CreateHostedCanvas()
    {
        // CanvasSize is (0,0) until ResizeCanvas is called. With AutoResize off that would clamp the
        // surface to the 1x1 minimum and the node would present a blank 1-pixel canvas, so the first
        // surface always falls back to the node size - only later changes need an explicit resize.
        var size = AutoResize || CanvasSize.X <= 0 || CanvasSize.Y <= 0
            ? new Vector2I(Mathf.Max(1, (int)Size.X), Mathf.Max(1, (int)Size.Y))
            : CanvasSize;

        CanvasSize = new Vector2I(Mathf.Max(1, size.X), Mathf.Max(1, size.Y));
        try
        {
            // The factory is invoked once: a second call just to test the result would build two canvases
            // and leak the first one (the control's test counts the calls).
            Canvas = CanvasFactory?.Invoke(CanvasSize.X, CanvasSize.Y)
                     ?? CreateCanvas(CanvasSize.X, CanvasSize.Y);
        }
        catch (Exception ex)
        {
            // No rendering device (headless, or a backend that failed): stay usable and let the host
            // draw a placeholder instead of raising every frame.
            GD.PushWarning($"{GetType().Name}: could not create a canvas ({ex.GetType().Name}: {ex.Message}).");
            Canvas = null;
        }
        Invalidate();
    }

    private void OnResized()
    {
        if (AutoResize)
            ResizeCanvas(new Vector2I((int)Size.X, (int)Size.Y));
        else
            Invalidate(); // presentation rect changed even though the surface did not
    }

    /// <summary>
    /// Report a failed step of the redraw started in <see cref="_Process"/>. The frame is retried on
    /// the next one, so a single throwing callback neither stops the node from rendering nor hides
    /// the error.
    /// </summary>
    private void ReportDrawFailure(Exception ex)
        => GD.PushError($"{GetType().Name}: canvas draw failed ({ex.GetType().Name}: {ex.Message}).");

    /// <summary>
    /// Check the precondition of a cached layer once: a component may only keep the part of its frame that
    /// does not depend on the pointer when the surface is cleared before every redraw - a cached image carries
    /// its own alpha and would otherwise be composited onto the previous frame, which looks like a picture
    /// that never settles. Reported instead of silently drawing that way.
    /// </summary>
    private void WarnIfCachingIntoAnUnclearedSurface()
    {
        if (!LayeredRendering || ClearBeforeDraw || _warnedAboutClearing) return;

        _warnedAboutClearing = true;
        GD.PushWarning(
            $"{GetType().Name}: LayeredRendering is on while ClearBeforeDraw is off; the surface keeps the " +
            "previous frame, so a cached layer would be drawn on top of it. Turn ClearBeforeDraw on (or have " +
            "the component redraw everything every frame).");
    }

    private bool _warnedAboutClearing;

}
