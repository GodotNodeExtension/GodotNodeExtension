using System;
using Godot;
using GodotNodeExtension.Component.GodotSkia;
using SkiaSharp;

namespace GodotNodeExtension.Example.GodotSkia;

/// <summary>
/// Example page for the Skia component: one <see cref="SkiaCanvasTexture2D"/> drawn into with the
/// SkiaSharp API directly and presented by a plain <see cref="TextureRect"/> - no chart canvas in
/// between (the same picture drawn through the backend-agnostic <c>ICanvas2D</c> API, hosted by a
/// <c>Canvas2DControl</c>, is the SkiaCanvas2DDemo page).
/// <para>
/// Beyond the four draw modes the page shows how a host is supposed to own the surface: the frame loop
/// uploads it (<see cref="SkiaCanvasTexture2D.UpdateTexture"/> runs once per frame, not once per input
/// event), the surface follows the <see cref="TextureRect"/> size (<see cref="SkiaCanvasTexture2D.Resize"/>,
/// driven by the <c>Resized</c> signal), Free Drawing keeps the pixels that are already on the surface,
/// and every native Skia object is released by whoever created it: the per-shape paths, fonts and paints
/// in a <c>using</c> scope, the surface and the three shared paints once, in <see cref="Dispose(bool)"/>.
/// </para>
/// <para>
/// A Skia surface needs a rendering device. A headless run (or <c>--rendering-driver dummy</c>) has none:
/// the caption then reads "Skia canvas: unavailable" instead of failing, and everything else - the colour
/// sliders, the brush size, the mode switch and the buttons - keeps working.
/// </para>
/// </summary>
public partial class GodotSkiaDemo : Control
{
    // ── Draw modes ───────────────────────────────────────────────────────────

    // The ids the DrawModeOption items carry in the scene, so the switch in RedrawCanvas() reads a name
    // instead of a bare number.
    private const int DrawModeShapes = 0;
    private const int DrawModeFreeDrawing = 1;
    private const int DrawModeText = 2;
    private const int DrawModeAnimated = 3;

    /// <summary>Surface size used until the <see cref="TextureRect"/> has a size of its own.</summary>
    private const int DefaultSurfaceSize = 512;

    // ── Scene wiring ─────────────────────────────────────────────────────────
    // Wired in GodotSkiaDemo.tscn (node_paths + NodePath): the control panel, the canvas rect and its
    // caption live in the scene - this script only drives them.

    /// <summary>Hue of the drawing colour, in degrees.</summary>
    [Export] public HSlider HueSlider { get; set; } = null!;

    /// <summary>Saturation of the drawing colour, in percent.</summary>
    [Export] public HSlider SatSlider { get; set; } = null!;

    /// <summary>Lightness of the drawing colour, in percent.</summary>
    [Export] public HSlider LightSlider { get; set; } = null!;

    /// <summary>Brush size in canvas pixels; the painted dot uses twice this value as its radius.</summary>
    [Export] public SpinBox LineWidthSpinBox { get; set; } = null!;

    /// <summary>Draw mode switch; its item ids are the <c>DrawMode*</c> constants above.</summary>
    [Export] public OptionButton DrawModeOption { get; set; } = null!;

    /// <summary>Runs the animation the animated mode draws.</summary>
    [Export] public CheckBox AnimationCheckBox { get; set; } = null!;

    /// <summary>Clears the surface.</summary>
    [Export] public Button ClearButton { get; set; } = null!;

    /// <summary>Writes the surface to a PNG file.</summary>
    [Export] public Button SaveButton { get; set; } = null!;

    /// <summary>Presents the surface; its rect is the size the surface follows and the input area.</summary>
    // Wired in the scene: the page cannot work without it, so the contract is non-null. IsInstanceValid
    // below covers the only real failure (the node was already freed).
    [Export] public TextureRect TextureRect { get; set; } = null!;

    /// <summary>Caption of the canvas area, also used to report a machine without a rendering device.</summary>
    [Export] public Label CanvasLabel { get; set; } = null!;

    /// <summary>
    /// Save dialog of the "Save" button. It lives in the scene like every other control: it is a child of
    /// this page and is used repeatedly, so the scene owns it instead of the script looking it up by name
    /// (which silently created a second dialog whenever the node was renamed).
    /// </summary>
    [Export] public FileDialog SaveCanvasDialog { get; set; } = null!;

    // ── State ────────────────────────────────────────────────────────────────

    // The surface, or null when this engine instance has no rendering device (a headless run): every
    // drawing path below checks it instead of assuming it exists.
    private SkiaCanvasTexture2D? _skiaCanvasTex;

    // Set by everything that draws, cleared by the frame loop: this is what keeps the upload at one per
    // frame instead of one per input event.
    private bool _canvasDirty;

    private float _time;
    private bool _isAnimating;

    private SKColor _currentColor = SKColors.Red;
    private float _lineWidth = 2.0f;
    private int _drawMode = DrawModeShapes;

    // Owned for the whole lifetime of the node: created here, disposed once in ReleaseNativeResources()
    // (through Dispose(bool)). Their colour and width are applied per draw, so nothing else has to touch
    // them - and being non-nullable, no draw path needs a null check.
    private readonly SKPaint _strokePaint = new() { Style = SKPaintStyle.Stroke, IsAntialias = true };
    private readonly SKPaint _fillPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private readonly SKPaint _textPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };

    // ── Setup ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public override void _Ready()
    {
        if (!WiringIsComplete()) return;

        SetupSkiaCanvas();
        ConnectSignals();
        UpdateColor();
        RedrawCanvas();
    }

    /// <summary>
    /// Every exported reference has to be connected in the scene. Without this check an unwired property
    /// surfaced as a null dereference somewhere in the first frame, which says nothing about what to fix.
    /// </summary>
    private bool WiringIsComplete()
    {
        var missing = new System.Collections.Generic.List<string>();
        Require(HueSlider, nameof(HueSlider), missing);
        Require(SatSlider, nameof(SatSlider), missing);
        Require(LightSlider, nameof(LightSlider), missing);
        Require(LineWidthSpinBox, nameof(LineWidthSpinBox), missing);
        Require(DrawModeOption, nameof(DrawModeOption), missing);
        Require(AnimationCheckBox, nameof(AnimationCheckBox), missing);
        Require(ClearButton, nameof(ClearButton), missing);
        Require(SaveButton, nameof(SaveButton), missing);
        Require(TextureRect, nameof(TextureRect), missing);
        Require(CanvasLabel, nameof(CanvasLabel), missing);
        Require(SaveCanvasDialog, nameof(SaveCanvasDialog), missing);
        if (missing.Count == 0) return true;

        GD.PushError($"{GetType().Name}: not wired in the scene: {string.Join(", ", missing)}. " +
                     "Connect them in GodotSkiaDemo.tscn (node_paths) - the page cannot run without them.");
        return false;
    }

    /// <summary>
    /// Collect the name of an exported reference the scene did not connect.
    /// <para>
    /// The parameter is nullable on purpose: the properties are declared non-null (a scene is expected to wire
    /// them), so testing them where they are used would be a comparison the compiler can prove false - and the
    /// check would be reported as dead code instead of protecting anything.
    /// </para>
    /// </summary>
    private static void Require(Node? node, string name, System.Collections.Generic.List<string> missing)
    {
        if (node is null) missing.Add(name);
    }

    /// <summary>
    /// Create the surface and hand it to the <see cref="TextureRect"/>, or report that this engine
    /// instance cannot have one.
    /// <para>
    /// A <see cref="SkiaCanvasTexture2D"/> needs a rendering device and its constructor throws
    /// <see cref="InvalidOperationException"/> without one. This page builds the texture by hand instead
    /// of letting a <c>Canvas2DControl</c> host it, so the probe and the report are its own job: the
    /// caption names the missing device and the rest of the page stays usable.
    /// </para>
    /// </summary>
    private void SetupSkiaCanvas()
    {
        var size = SurfacePixelSize();
        try
        {
            // HasRenderingDevice answers false before the constructor can throw; the catch keeps a
            // device that exists but cannot host a surface (no Vulkan loader, surface creation failed)
            // on the same "unavailable" path.
            _skiaCanvasTex = SkiaCanvasTexture2D.HasRenderingDevice
                ? new SkiaCanvasTexture2D(size.X, size.Y)
                : null;
        }
        catch (Exception ex)
        {
            _skiaCanvasTex = null;
            GD.PushWarning($"GodotSkiaDemo: could not create the Skia surface " +
                           $"({ex.GetType().Name}: {ex.Message}).");
        }

        if (_skiaCanvasTex is { } texture)
        {
            TextureRect.Texture = texture;
            return;
        }

        TextureRect.Texture = null;
        CanvasLabel.Text = "Skia canvas: unavailable - this page needs a rendering device";
    }

    /// <summary>
    /// Size of the surface in pixels: the <see cref="TextureRect"/> rect when the layout already ran,
    /// <see cref="DefaultSurfaceSize"/> otherwise. Keeping the two in step makes control coordinates and
    /// canvas coordinates the same thing.
    /// </summary>
    /// <returns>The surface size to create or resize the texture with.</returns>
    private Vector2I SurfacePixelSize()
    {
        var size = TextureRect.Size;
        return size is { X: >= 1f, Y: >= 1f }
            ? new Vector2I(Mathf.RoundToInt(size.X), Mathf.RoundToInt(size.Y))
            : new Vector2I(DefaultSurfaceSize, DefaultSurfaceSize);
    }

    private void ConnectSignals()
    {
        // Color sliders
        HueSlider.ValueChanged += OnColorChanged;
        SatSlider.ValueChanged += OnColorChanged;
        LightSlider.ValueChanged += OnColorChanged;

        // Other controls
        LineWidthSpinBox.ValueChanged += OnLineWidthChanged;
        DrawModeOption.ItemSelected += OnDrawModeChanged;
        AnimationCheckBox.Toggled += OnAnimationToggled;

        // Buttons
        ClearButton.Pressed += OnClearPressed;
        SaveButton.Pressed += OnSavePressed;

        // The dialog itself is a scene node now, so its signal is connected here instead of at creation.
        SaveCanvasDialog.FileSelected += OnSaveFileSelected;

        // Mouse input for drawing, and the rect the surface follows
        TextureRect.GuiInput += OnTextureRectInput;
        TextureRect.Resized += OnTextureRectResized;
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        // Leaving the tree only unhooks the signals. The native objects (the surface and the three
        // paints) are released once, in Dispose(bool) - releasing them here as well disposed the same
        // objects twice and left the fields pointing at disposed objects for the second pass.
        HueSlider.ValueChanged -= OnColorChanged;
        SatSlider.ValueChanged -= OnColorChanged;
        LightSlider.ValueChanged -= OnColorChanged;
        LineWidthSpinBox.ValueChanged -= OnLineWidthChanged;
        DrawModeOption.ItemSelected -= OnDrawModeChanged;
        AnimationCheckBox.Toggled -= OnAnimationToggled;
        ClearButton.Pressed -= OnClearPressed;
        SaveButton.Pressed -= OnSavePressed;
        SaveCanvasDialog.FileSelected -= OnSaveFileSelected;
        TextureRect.GuiInput -= OnTextureRectInput;
        TextureRect.Resized -= OnTextureRectResized;
    }

    // ── Controls ─────────────────────────────────────────────────────────────

    private void OnColorChanged(double value)
    {
        UpdateColor();
        RedrawCanvas();
    }

    private void UpdateColor()
    {
        var hue = (float)HueSlider.Value;
        var saturation = (float)SatSlider.Value / 100.0f;
        var lightness = (float)LightSlider.Value / 100.0f;

        _currentColor = SKColor.FromHsl(hue, saturation * 100, lightness * 100);

        _strokePaint.Color = _currentColor;
        _fillPaint.Color = _currentColor;
        _textPaint.Color = _currentColor;
    }

    private void OnLineWidthChanged(double value)
    {
        _lineWidth = (float)value;
        _strokePaint.StrokeWidth = _lineWidth;
        RedrawCanvas();
    }

    /// <summary>Switch the draw mode and redraw the surface with the new one.</summary>
    /// <param name="index">Item index the <see cref="OptionButton"/> reports; its id is the draw mode.</param>
    private void OnDrawModeChanged(long index)
    {
        // ItemSelected reports the item *index*: reading the id keeps the mode in step with the scene if
        // the items are ever reordered.
        _drawMode = DrawModeOption.GetItemId((int)index);
        RedrawCanvas();
    }

    private void OnAnimationToggled(bool pressed)
    {
        _isAnimating = pressed;
        if (_isAnimating)
        {
            _time = 0.0f;
        }
    }

    private void OnClearPressed()
    {
        if (SurfaceCanvas() is not { } canvas) return;

        canvas.Clear(SKColors.White);
        _canvasDirty = true;
    }

    /// <summary>
    /// Ask for a path and write the surface to it. The dialog is a scene node (<see cref="SaveCanvasDialog"/>)
    /// and is reused for every click: the script used to look one up by name and create it on first use, which
    /// left a second dialog behind whenever the node was renamed.
    /// </summary>
    private void OnSavePressed()
    {
        if (_skiaCanvasTex is null) return;    // no surface at all: nothing to save, nothing to ask

        // One dialog, declared by the scene (see SaveCanvasDialog): it is reused for every click, so a
        // sequence of saves does not leave a stack of dialogs behind.
        SaveCanvasDialog.CurrentFile = $"skia_canvas_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        SaveCanvasDialog.PopupCentered();
    }

    /// <summary>
    /// Save the surface to the path the dialog reported.
    /// <para>
    /// The callback can outlive the texture (the page may already have been left). The pixels are read
    /// through <see cref="Texture2D.GetImage"/>, which answers an <b>empty</b> image once the
    /// surface is released instead of throwing - so a stale callback would quietly write a blank PNG;
    /// it is reported and skipped instead.
    /// </para>
    /// </summary>
    /// <param name="path">Absolute path the user picked in the dialog.</param>
    private void OnSaveFileSelected(string path)
    {
        if (_skiaCanvasTex is not { } texture)
        {
            GD.PushWarning($"Skia canvas is gone: nothing was written to '{path}'.");
            return;
        }

        var error = texture.GetImage().SavePng(path);
        if (error == Error.Ok)
            GD.Print($"Image saved to: {path}");
        else
            GD.PushError($"Could not save '{path}': {error}.");
    }

    // ── Pointer input ────────────────────────────────────────────────────────

    /// <summary>
    /// Pointer input on the canvas rect: in <see cref="DrawModeFreeDrawing"/> a press and every drag step
    /// paint one brush dot, in every other mode the pointer is ignored.
    /// <para>
    /// The dot is <b>drawn</b> here and <b>uploaded</b> by the frame loop (<c>_Process</c>), so a drag
    /// costs one texture upload per frame instead of one per motion event.
    /// </para>
    /// </summary>
    /// <param name="event">GUI event of the <see cref="TextureRect"/>, positioned locally to it.</param>
    private void OnTextureRectInput(InputEvent @event)
    {
        if (_drawMode != DrawModeFreeDrawing) return;

        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } button:
                DrawAtPosition(button.Position);
                break;
            case InputEventMouseMotion motion when Input.IsMouseButtonPressed(MouseButton.Left):
                DrawAtPosition(motion.Position);
                break;
        }
    }

    /// <summary>
    /// Paint one brush dot at a point of the <see cref="TextureRect"/>. The surface has the size of that
    /// rect (see <see cref="OnTextureRectResized"/>), so control pixels and canvas pixels coincide; the
    /// ratio is still computed rather than assumed.
    /// </summary>
    /// <param name="position">Local position inside the <see cref="TextureRect"/>.</param>
    private void DrawAtPosition(Vector2 position)
    {
        if (_skiaCanvasTex is not { } texture || SurfaceCanvas() is not { } canvas) return;

        var size = TextureRect.Size;    // local rect of the control the pointer hit
        if (size.X < 1f || size.Y < 1f) return;

        canvas.DrawCircle(position.X * (texture.Width / size.X), position.Y * (texture.Height / size.Y),
            _lineWidth * 2f, _fillPaint);
        _canvasDirty = true;
    }

    /// <summary>
    /// Keep the surface in step with the <see cref="TextureRect"/> (one canvas pixel per control pixel).
    /// Resizing rebuilds the surface and discards what is on it, so the current drawing is put back right
    /// away - which also means a resize mid-stroke loses the free drawing, as the texture documents.
    /// </summary>
    private void OnTextureRectResized()
    {
        if (_skiaCanvasTex is not { } texture) return;

        var size = SurfacePixelSize();
        if (texture.Width == size.X && texture.Height == size.Y) return;

        texture.Resize(size.X, size.Y);

        // The rebuilt surface comes back blank, so the page is white again whatever the mode - in Free
        // Drawing mode the strokes it had went with the old surface.
        SurfaceCanvas()?.Clear(SKColors.White);
        RedrawCanvas();
    }

    /// <summary>
    /// The surface's canvas, or null when there is no surface (a machine without a rendering device) or
    /// the Skia context is gone. Every drawing path goes through it instead of repeating the two checks.
    /// </summary>
    /// <returns>The Skia canvas to draw on, or <c>null</c> when there is nothing to draw on.</returns>
    private SKCanvas? SurfaceCanvas() => _skiaCanvasTex?.Canvas;

    // ── Drawing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Draw the current mode into the surface and mark it dirty (the frame loop uploads it).
    /// <para>
    /// Only the modes that replace the whole picture clear the surface first. Free Drawing is
    /// deliberately additive - it exists to paint on top of what is already there - so it does not clear,
    /// neither when the mode is selected nor on the redraw that follows a colour change.
    /// </para>
    /// </summary>
    private void RedrawCanvas()
    {
        if (SurfaceCanvas() is not { } canvas) return;

        if (_drawMode != DrawModeFreeDrawing)
            canvas.Clear(SKColors.White);

        switch (_drawMode)
        {
            case DrawModeShapes:
                DrawGeometricShapes(canvas);
                break;
            case DrawModeText:
                DrawTextEffects(canvas);
                break;
            case DrawModeAnimated:
                DrawAnimatedGraphics(canvas);
                break;
            case DrawModeFreeDrawing:
                break;    // nothing to add: the user's strokes are already on the surface
        }

        _canvasDirty = true;
    }

    /// <summary>Draw one rectangle, circle, triangle, rounded rectangle, line and oval.</summary>
    /// <param name="canvas">Surface to draw on.</param>
    private void DrawGeometricShapes(SKCanvas canvas)
    {
        // Rectangle
        canvas.DrawRect(50, 50, 100, 80, _strokePaint);
        canvas.DrawRect(60, 60, 80, 60, _fillPaint);

        // Circle
        canvas.DrawCircle(250, 100, 50, _strokePaint);
        canvas.DrawCircle(250, 100, 30, _fillPaint);

        // Triangle: an SKPath is a native object with no finalizer, so the scope that creates it also
        // disposes it - once per redraw, not once per lifetime.
        using var trianglePath = new SKPath();
        trianglePath.MoveTo(400, 150);
        trianglePath.LineTo(350, 50);
        trianglePath.LineTo(450, 50);
        trianglePath.Close();
        canvas.DrawPath(trianglePath, _strokePaint);

        // Rounded rectangle, 100x100 at (50, 200): bottom and right must be greater than top and left,
        // otherwise the rect has a negative size and draws nothing.
        using var roundRect = new SKRoundRect(new SKRect(50, 200, 150, 300), 20, 20);
        canvas.DrawRoundRect(roundRect, _fillPaint);

        // Line
        canvas.DrawLine(250, 200, 400, 300, _strokePaint);

        // Oval
        canvas.DrawOval(new SKRect(300, 350, 450, 450), _strokePaint);
    }

    /// <summary>Draw plain, large, small, path-bound and outlined text.</summary>
    /// <param name="canvas">Surface to draw on.</param>
    private void DrawTextEffects(SKCanvas canvas)
    {
        // Fonts, the path and the two local paints are native objects as well: one using scope each, so
        // switching modes back and forth cannot leak a font or a paint per redraw.
        using var skFont = new SKFont(SKTypeface.Default, 24);
        using var largeFont = new SKFont(SKTypeface.Default, 32);
        using var smallFont = new SKFont(SKTypeface.Default, 16);

        // Simple text
        canvas.DrawText("Hello Skia!", 50, 100, SKTextAlign.Left, skFont, _textPaint);

        // Text with different sizes
        canvas.DrawText("Large Text", 50, 150, SKTextAlign.Left, largeFont, _textPaint);
        canvas.DrawText("Small text here", 50, 180, SKTextAlign.Left, smallFont, _textPaint);

        // Text on path
        using var textPath = new SKPath();
        textPath.AddArc(new SKRect(200, 200, 400, 400), 0, 180);
        canvas.DrawTextOnPath("Text on curved path!", textPath, 0, -10, SKTextAlign.Left, skFont, _textPaint);

        // Outlined text: a black outline first, the current colour as the fill on top of it
        using var outlinePaint = new SKPaint();
        outlinePaint.Style = SKPaintStyle.Stroke;
        outlinePaint.StrokeWidth = 2;
        outlinePaint.Color = SKColors.Black;
        outlinePaint.IsAntialias = true;

        canvas.DrawText("Outlined", 50, 350, SKTextAlign.Left, skFont, outlinePaint);

        using var fillTextPaint = new SKPaint();
        fillTextPaint.Style = SKPaintStyle.Fill;
        fillTextPaint.Color = _currentColor;
        fillTextPaint.IsAntialias = true;

        canvas.DrawText("Outlined", 50, 350, SKTextAlign.Left, skFont, fillTextPaint);
    }

    /// <summary>Draw the orbiting dots, the pulsating centre circle and the spiral of one animation frame.</summary>
    /// <param name="canvas">Surface to draw on.</param>
    private void DrawAnimatedGraphics(SKCanvas canvas)
    {
        var centerX = 256f;
        var centerY = 256f;
        var radius = 100f;

        // Rotating shapes
        for (int i = 0; i < 8; i++)
        {
            var angle = (_time + i * 45) * Math.PI / 180.0;
            var x = centerX + Math.Cos(angle) * radius;
            var y = centerY + Math.Sin(angle) * radius;

            canvas.DrawCircle((float)x, (float)y, 20, _fillPaint);
        }

        // Pulsating center circle
        var pulseRadius = 30 + Math.Sin(_time * 0.1) * 10;
        canvas.DrawCircle(centerX, centerY, (float)pulseRadius, _strokePaint);

        // Spiral: rebuilt every frame, so the path is scoped to this call - a field would have to be
        // reset each time instead.
        using var spiralPath = new SKPath();
        for (float t = 0; t < 360; t += 5)
        {
            var spiralAngle = (t + _time * 2) * Math.PI / 180.0;
            var spiralRadius = t * 0.3f;
            var spiralX = centerX + Math.Cos(spiralAngle) * spiralRadius;
            var spiralY = centerY + Math.Sin(spiralAngle) * spiralRadius;

            if (t == 0f)
                spiralPath.MoveTo((float)spiralX, (float)spiralY);
            else
                spiralPath.LineTo((float)spiralX, (float)spiralY);
        }

        canvas.DrawPath(spiralPath, _strokePaint);
    }

    // ── Frame loop ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_isAnimating)
        {
            _time += (float)delta * 60.0f; // 60 degrees per second
            if (_drawMode == DrawModeAnimated) // Only redraw if in animated mode
            {
                RedrawCanvas();
            }
        }

        // The drawing the frame produced - the animation above, a slider change, a brush dot - is
        // committed here, once per frame: UpdateTexture flushes Skia and waits for the GPU, so calling it
        // from the input handlers as well would cost one wait per mouse event for nothing.
        if (!_canvasDirty || _skiaCanvasTex is not { } texture) return;

        _canvasDirty = false;
        texture.UpdateTexture();
    }

    // ── Ownership ────────────────────────────────────────────────────────────

    /// <summary>
    /// Release the native objects this page owns: the three paints it shares between the draw modes and
    /// the surface texture (which owns a Skia surface, both of its RIDs and a reference to the shared
    /// GRContext).
    /// <para>
    /// This is the <b>only</b> release path, reached through <see cref="Dispose(bool)"/>. The fields are
    /// nulled afterwards, so a second call - Godot's free path and an explicit <c>Dispose()</c>, or a save
    /// callback that arrives late - finds nothing to release instead of touching disposed objects. The
    /// <c>?.</c> covers a texture and paints that were never created, i.e. a page disposed before its
    /// <c>_Ready</c>.
    /// </para>
    /// </summary>
    private void ReleaseNativeResources()
    {
        // The paints are created with the node and never null: Dispose is idempotent, this method runs
        // exactly once (Dispose(bool) guards it), and they are not nulled so no draw path can be surprised.
        _strokePaint.Dispose();
        _fillPaint.Dispose();
        _textPaint.Dispose();

        // Drop the presentation first: the TextureRect must not keep a texture that is about to be freed.
        if (GodotObject.IsInstanceValid(TextureRect))
            TextureRect.Texture = null;

        // The texture owns a Skia surface, both of its RIDs and a shared-GRContext reference:
        // ReleaseResources frees them deterministically, and Dispose then frees the Godot object itself.
        if (_skiaCanvasTex is { } texture)
        {
            texture.ReleaseResources();
            if (GodotObject.IsInstanceValid(texture))
                texture.Dispose();
            _skiaCanvasTex = null;
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            ReleaseNativeResources();

        base.Dispose(disposing);
    }
}
