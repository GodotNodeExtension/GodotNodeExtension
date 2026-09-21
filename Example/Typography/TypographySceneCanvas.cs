using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Server;
using SkiaSharp;

namespace GodotNodeExtension.Example.Typography;

/// <summary>
/// A canvas that shows what the typography engine laid out, drawn with SkiaSharp alone.
/// <para>
/// It exists to make the engine's input and output formats visible: it builds a handful of elements (see
/// <see cref="TypographySamples"/>), asks the server for a layout, and then draws exactly what came back - the
/// glyph runs, the annotations' finished geometry, the emphasis marks - without measuring or shaping anything
/// itself. The knobs are exported, so a scene decides which language, which sample and which writing mode it shows
/// and nothing about the picture lives in code.
/// </para>
/// <para>
/// The picture is the area the scene gives the canvas, and the canvas asks for more room only along the axis its
/// lines advance on - so the measure it lays the sample out at never comes back to it as a size it asked for.
/// </para>
/// <para>
/// Every drawing choice here is a rendering rule the output format states: a horizontal run is placed at the
/// baseline the layout reported, a column's glyphs walk down from the element's inline start with their own
/// offsets, a turned run is drawn rotated around its pen, and a column annotation is centred on the centre line the
/// layout gave. The optional debug overlay outlines the element boxes, the line boxes and the baselines the layout
/// published, which is what makes "the drawing and the grid agree" something a reader can check.
/// </para>
/// </summary>
/// <summary>Which of the sample's blocks the canvas outlines, so their laid-out range can be found.</summary>
public enum SampleOutline
{
    /// <summary>Nothing is outlined beyond the picture's frame and the content box.</summary>
    None,

    /// <summary>Only the blocks that demonstrate a rule: one box per example block.</summary>
    Example,

    /// <summary>Every block, the headings and the prose included.</summary>
    EveryBlock,
}

[Tool]
public partial class TypographySceneCanvas : Control
{
    /// <summary>Language the sample is laid out by, or empty for no language at all.</summary>
    [Export]
    public string LanguageTag { get; set; } = string.Empty;

    /// <summary>Which way the sample is set: along lines, or down columns that run right to left.</summary>
    [Export]
    public WritingMode WritingMode { get; set; } = WritingMode.HorizontalTb;

    /// <summary>Which sample to lay out.</summary>
    [Export]
    public TypographySamples.SampleId Sample { get; set; } = TypographySamples.SampleId.ChineseSimplified;

    /// <summary>Text size in pixels.</summary>
    [Export]
    public int FontSize { get; set; } = 26;

    /// <summary>How wide the content box is, i.e. the measure in horizontal writing.</summary>
    [Export]
    public float Measure { get; set; }

    /// <summary>
    /// How tall a column may be, i.e. a line's length in vertical writing. 0 fits the canvas' own height.
    /// </summary>
    [Export]
    public float ColumnHeight { get; set; }

    /// <summary>Whether to outline what the layout placed on top of the picture.</summary>
    [Export]
    public bool DebugOverlay { get; set; }

    /// <summary>
    /// Which blocks of the sample to outline. A sample is a walkthrough - a heading, the clause that states what a
    /// convention requires, and an example of that requirement being met - and the examples are what a reader
    /// compares against the geometry, so each of them gets a box around the range it was laid out into.
    /// </summary>
    [Export]
    public SampleOutline Outline { get; set; } = SampleOutline.Example;

    /// <summary>Line spacing the request asks for, in pixels.</summary>
    [Export]
    public float LineSpacing { get; set; }

    /// <summary>Colour the sample is drawn in.</summary>
    [Export]
    public Color TextColor { get; set; } = new(0.93f, 0.95f, 0.98f);

    /// <summary>Font the sample is shaped with; the theme's fallback is used when the scene leaves it empty.</summary>
    [Export]
    public Font? Font { get; set; }

    /// <summary>Optional label the scene wires up, which reports the language, the size and the timings.</summary>
    [Export]
    public Label? StatusLabel { get; set; }

    /// <summary>One line saying what this scene shows, taken from the scene so a reader needs no other document.</summary>
    [Export(PropertyHint.MultilineText)]
    public string Caption { get; set; } = string.Empty;

    /// <summary>
    /// Optional size control the scene wires up: turning it resizes the text, which means shaping and measuring it
    /// again, so a new layout is requested rather than the old one redrawn.
    /// </summary>
    [Export]
    public SpinBox? FontSizeControl { get; set; }

    /// <summary>
    /// Optional switch the scene wires up: turning it outlines what the layout placed - every element's box and the
    /// line its glyphs sit on - so the drawing and the geometry can be compared.
    /// </summary>
    [Export]
    public CheckBox? OverlayToggle { get; set; }

    private TypographyServer? _server;

    /// <summary>Kind of every element of the sample last requested, or null before the first request.</summary>
    private IReadOnlyList<TypographySamples.BlockKind?>? _kinds;
    private LayoutHandle _handle;
    private long _requestId;
    private LayoutResult? _result;
    private SKBitmap? _bitmap;
    private ImageTexture? _texture;

    /// <summary>Content size the last applied layout reported, which is what the picture needs.</summary>
    private Vector2 _contentSize;

    /// <summary>Margin the sample is drawn with, in pixels.</summary>
    private const float Margin = 16f;

    /// <summary>
    /// Smallest measure (or column length) a request is made with, so a canvas whose area is smaller than its own
    /// margins still asks the engine for something rather than for nothing.
    /// </summary>
    private const float MinMeasure = 64f;

    /// <summary>Largest side the picture may have, so a runaway layout cannot ask for a huge allocation.</summary>
    private const int MaxImageSide = 4096;

    /// <summary>Colour of the box around a block that demonstrates a rule: the part a reader compares against.</summary>
    private static readonly SKColor ExampleBoxColor = new(0x7F, 0xD8, 0xA0, 0x99);

    /// <summary>Colour of the box around the other blocks, when the scene asks for all of them.</summary>
    private static readonly SKColor BlockBoxColor = new(0x6A, 0x74, 0x86, 0x8C);

    /// <summary>Colour of the frame around the picture, which is what makes the canvas' own extent visible.</summary>
    private static readonly SKColor FrameColor = new(0x3A, 0x44, 0x50);

    /// <summary>
    /// Colour of the box the last layout reported as its content: the area the sample was laid out for, which is
    /// what a reader needs to tell "drawn for a 420px measure" from "drawn for the width of the window".
    /// </summary>
    private static readonly SKColor ContentBoxColor = new(0xE0, 0xA4, 0x58, 0x8C);

    /// <summary>Whether this canvas asked for columns rather than lines.</summary>
    private bool IsVertical => WritingMode != WritingMode.HorizontalTb;

    /// <summary>Elements the last applied layout produced, or 0 before the first one arrives.</summary>
    public int ElementCount { get; private set; }

    /// <summary>Extent of the content the last layout reported: the area the sample covers, margins excluded.</summary>
    public Vector2 ContentSize => _contentSize;

    /// <summary>Whether a layout has been applied and drawn.</summary>
    public bool HasContent => ElementCount > 0;

    /// <summary>Milliseconds the engine spent on the last layout, as it reported them.</summary>
    public double LayoutMs { get; private set; }

    /// <summary>Frames a request is given to come back before it is asked again.</summary>
    private const int RequestTimeoutFrames = 120;

    /// <summary>How many times one request may be re-asked before the canvas stops trying.</summary>
    private const int RequestRetries = 3;

    private int _framesSinceRequest;
    private int _retries;

    /// <summary>
    /// Whether the picture was rebuilt since it was last presented. The frame that uploads a new image is also the
    /// frame that draws it, and the upload lands after that drawing - so the new picture would only appear if
    /// something asked for another frame, which a canvas that never resizes has no reason to do.
    /// </summary>
    private bool _presentPending;

    /// <inheritdoc/>
    public override void _Ready()
    {
        _server = TypographyServer.Instance;
        _handle = _server.CreateHandle();

        if (StatusLabel is not null && Caption.Length > 0)
            StatusLabel.Text = Caption;

        if (FontSizeControl is not null)
        {
            FontSizeControl.Value = FontSize;
            FontSizeControl.ValueChanged += value =>
            {
                int size = Mathf.RoundToInt((float)value);

                if (size == FontSize)
                    return;

                FontSize = size;
                RequestLayout();
            };
        }

        if (OverlayToggle is not null)
        {
            OverlayToggle.ButtonPressed = DebugOverlay;
            OverlayToggle.Toggled += pressed =>
            {
                DebugOverlay = pressed;
                QueueRedraw();
            };
        }

        // A canvas that has no area yet asks for nothing: a container sizes its children after they enter the tree,
        // and the resize that follows is what submits the first request. Asking anyway laid the sample out at the
        // fallback measure (a column of one character per line) and then threw that work away when the container
        // sized the canvas - a picture nobody sees, from a request that only exists to be superseded. A measure the
        // scene declares makes the canvas independent of its own area, so it can ask right away.
        if (Measure > 0f || Size.X > 0f)
            RequestLayout();

        SetProcess(true);
    }

    /// <inheritdoc/>
    public override void _ExitTree()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _texture = null;

        // The handle owns a layout engine, its prepared content and its shapers, so a canvas that never hands it
        // back leaks one engine per scene the editor opens. The server is shared, so what is released here is
        // exactly this canvas' share of it.
        if (_server is not null && _server.IsHandleValid(_handle))
            _server.ReleaseHandle(_handle);
    }

    /// <summary>Send a fresh request: the first one, or one after a knob changed.</summary>
    /// <param name="isRetry">
    /// Whether this is an automatic retry rather than a request the scene asked for. A retry spends the retry
    /// budget; a request the scene made hands it back, because a knob the reader turned is a new reason to ask.
    /// </param>
    public void RequestLayout(bool isRetry = false)
    {
        if (_server is null)
            return;

        if (!isRetry)
            _retries = 0;

        Font font = Font ?? ThemeDB.FallbackFont;
        List<DrawElement> elements = TypographySamples.Build(Sample, font, FontSize, TextColor);
        _kinds = TypographySamples.BuildKinds(Sample);

        float width = Measure > 0f ? Measure : Mathf.Max(MinMeasure, Size.X - (Margin * 2f));
        float height = ColumnHeight > 0f ? ColumnHeight : Mathf.Max(MinMeasure, Size.Y - (Margin * 2f));

        var settings = new TypographySettings
        {
            LanguageTag = LanguageTag.Length > 0 ? LanguageTag : null,
            WritingMode = WritingMode,
            MaxWidth = width,
            MaxHeight = WritingMode == WritingMode.HorizontalTb ? 0f : height,
            LineSpacing = LineSpacing,
        };

        _requestId = _server.RequestFullLayout(_handle, [.. elements], settings);
        _result = null;
        _framesSinceRequest = 0;

        // A handle from before a server shutdown is refused with NotSubmitted and produces no result at all, so a
        // canvas that keeps asking on it would stay blank forever. The server has been restarted (running a test
        // inside the editor does exactly that), and a fresh handle belongs to the new generation, so take one.
        for (int attempt = 0; _requestId == TypographyServer.NotSubmitted && attempt < RequestRetries; attempt++)
        {
            _handle = _server.CreateHandle();
            _requestId = _server.RequestFullLayout(_handle, [.. elements], settings);
        }
    }

    /// <inheritdoc/>
    public override void _Notification(int what)
    {
        // The sample is laid out for the area it is shown in, so a resize lays it out again. A measure the scene
        // declares keeps the layout at that measure whatever the canvas is given.
        if (what == (int)NotificationResized && IsNodeReady() && Measure <= 0f)
            RequestLayout();
    }

    /// <inheritdoc/>
    public override void _Process(double delta)
    {
        if (_result is not null || _server is null)
            return;

        if (!_server.TryGetResult(_handle, out LayoutResult result))
        {
            // Nothing came back: the request may have been lost (a server that was shut down and started again
            // elsewhere invalidates the handles of everyone else). Asking again is how a page recovers; a few tries
            // keep a real failure from looping forever.
            if (++_framesSinceRequest >= RequestTimeoutFrames && _retries++ < RequestRetries)
                RequestLayout(isRetry: true);

            return;
        }

        if (result.RequestId != _requestId)
            return;

        // A result that carries an error is a failed layout, not a finished one. Painting the empty picture it
        // came with, saying nothing and never asking again is how a canvas went blank for the rest of a session
        // with no way back: the status label simply stayed empty, which reads exactly like "nothing happened yet".
        if (result.Error is not null)
        {
            GD.PushWarning($"[Typography] layout of {Sample} failed: {result.Error}");
            StatusLabel?.Text = $"{Caption}\n{Describe()} | layout failed: {result.Error}";

            // Ask again - on a fresh handle, because the failure may well be one this canvas' handle caused (a
            // server that was shut down refuses requests on the old generation with no result at all).
            if (_retries++ < RequestRetries)
            {
                _handle = _server.CreateHandle();
                RequestLayout(isRetry: true);
            }

            return;
        }

        if (result.Elements is not { Count: > 0 })
        {
            // An empty layout is a result, but it is an empty picture: report it rather than leave the canvas
            // blank and the reader guessing which of the two happened.
            _result = result;
            StatusLabel?.Text = $"{Caption}\n{Describe()} | the layout came back empty";
            return;
        }

        _result = result;
        ElementCount = result.Elements.Count;
        LayoutMs = result.Timings.TotalMs;
        _contentSize = result.ContentSize;

        // The picture is as big as the sample, and the container that gives the canvas that room is told so here:
        // a control whose minimum size changed has to say so, or a ScrollContainer keeps the size it computed from
        // the empty canvas and the rest of the sample is never reached - it was drawn, but outside the picture.
        UpdateMinimumSize();
        _presentPending = true;
        QueueRedraw();

        StatusLabel?.Text =
            $"{Caption}\n{Describe()} | measure {MeasureText()} | content {_contentSize.X:F0}×{_contentSize.Y:F0}px"
            + $" | canvas {Size.X:F0}×{Size.Y:F0}px | {ElementCount} elements | layout {LayoutMs:F1}ms"
            + (Outline != SampleOutline.None ? $" | outlining {Outline}" : string.Empty)
            + (DebugOverlay ? " | overlay on" : string.Empty);
    }

    /// <summary>
    /// Size the picture needs along each axis. The canvas is drawn into the area the scene gives it and only asks
    /// for more room along the axis its lines advance on - the page gets taller for horizontal writing, and wider
    /// for vertical writing, whose columns advance sideways. (That is the block axis in both modes.)
    /// <para>
    /// The other axis is deliberately left to the scene. Asking for room there as well - the whole content size,
    /// which is the obvious thing - makes the layout depend on itself whenever the scene does not declare a measure:
    /// the canvas asks for the width its content needs, the container gives it that width, the wider measure lays
    /// the sample out differently, and the picture grows again. One axis of growth, and never the one the measure
    /// is taken from, is what keeps the two apart.
    /// </para>
    /// </summary>
    /// <returns>The room the sample needs across the lines, and nothing along the measure.</returns>
    public override Vector2 _GetMinimumSize() => IsVertical
        ? new Vector2(_contentSize.X + (Margin * 2f), 0f)
        : new Vector2(0f, _contentSize.Y + (Margin * 2f));

    /// <inheritdoc/>
    public override void _Draw()
    {
        if (_result is null || _result.Elements is not { Count: > 0 } elements)
            return;


        Render(elements);
        DrawTexture(_texture, Vector2.Zero);
    }

    /// <summary>The measure the last request was made with, as a reader would read it off the scene.</summary>
    /// <returns>The measure in pixels, or the canvas' own inline extent when the scene declares none.</returns>
    private string MeasureText() =>
        Measure > 0f ? $"{Measure:F0}px" : $"{Mathf.Max(MinMeasure, Size.X - (Margin * 2f)):F0}px (canvas)";

    /// <summary>A caption for the status label: what this canvas is showing.</summary>
    /// <returns>The description.</returns>
    private string Describe()
    {
        string language = LanguageTag.Length > 0 ? LanguageTag : "und";
        string mode = WritingMode == WritingMode.HorizontalTb ? "horizontal" : "vertical";

        return $"{Sample} [{language}] {mode} {FontSize}px";
    }

    /// <summary>
    /// Draw the laid-out elements into the Skia bitmap: non-text elements first, then the glyph runs with their
    /// annotations, then the overlay when it is on. Nothing here measures text: every position and every glyph comes
    /// from the layout.
    /// </summary>
    /// <param name="elements">Elements the layout produced, in drawing order.</param>
    private void Render(List<LayoutElement> elements)
    {
        // The picture is the area the page gave the canvas: the scene owns the layout, and the canvas draws the
        // sample in whatever room it ends up with. The floor keeps a canvas that has not been laid out yet from
        // asking for a zero-sized allocation.
        int width = Mathf.Clamp((int)Mathf.Ceil(Size.X), 8, MaxImageSide);
        int height = Mathf.Clamp((int)Mathf.Ceil(Size.Y), 8, MaxImageSide);

        if (_bitmap is null || _bitmap.Width != width || _bitmap.Height != height)
        {
            _bitmap?.Dispose();
            _bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        }

        using var canvas = new SKCanvas(_bitmap);
        canvas.Clear(new SKColor(0x14, 0x17, 0x1C));

        // Columns advance leftward from the content box' right edge, so a vertical sample hangs off the right of its
        // area; a horizontal one is centred when the area is wider than the measure it was laid out with.
        float left = IsVertical
            ? Mathf.Max(Margin, Size.X - Margin - _contentSize.X)
            : Mathf.Max(Margin, (Size.X - _contentSize.X) * 0.5f);

        // The frame belongs to the picture, not to the content: it is drawn before the content transform so a
        // reader can see where the canvas ends and the page begins - the two fills are almost the same colour.
        using (var frame = new SKPaint())
        {
            frame.IsAntialias = false;
            frame.Style = SKPaintStyle.Stroke;
            frame.StrokeWidth = 1f;
            frame.Color = FrameColor;
            canvas.DrawRect(new SKRect(0.5f, 0.5f, width - 1f, height - 1f), frame);
        }

        canvas.Translate(Mathf.Floor(left), Margin);

        var paint = new SKPaint { IsAntialias = true, Color = Sk(TextColor) };

        // Non-text first: a background or a rule belongs behind the glyphs, and the layout worded the list that way.
        foreach (LayoutElement element in elements)
        {
            if (element.Type is DrawElement.ElementType.Rect)
            {
                paint.Color = Sk(element.Color);
                canvas.DrawRect(Box(element, 0f), paint);
            }

            if (element is { Type: DrawElement.ElementType.Text, BackgroundColor: { } background })
            {
                SKRect rect = Box(element, 0f, element.BackgroundPadding);
                paint.Color = Sk(background);
                if (element.BackgroundCornerRadius > 0f)
                    canvas.DrawRoundRect(rect, element.BackgroundCornerRadius, element.BackgroundCornerRadius, paint);
                else
                    canvas.DrawRect(rect, paint);

                paint.Color = Sk(TextColor);
            }
        }

        foreach (LayoutElement element in elements)
        {
            if (element.Type != DrawElement.ElementType.Text || element.GlyphRun is not { Glyphs.Length: > 0 } run)
                continue;

            DrawRun(canvas, element, run);
            DrawDecorations(canvas, element);
            DrawRuby(canvas, element);
            DrawEmphasis(canvas, element);
        }

        // The content box the layout reported, drawn last so it is never hidden by the sample: it is the answer
        // to "which area was this laid out for", and it is cheap enough to keep on for every scene.
        if (_contentSize.X > 0f && _contentSize.Y > 0f)
        {
            using var contentBox = new SKPaint();
            contentBox.IsAntialias = false;
            contentBox.Style = SKPaintStyle.Stroke;
            contentBox.StrokeWidth = 1f;
            contentBox.Color = ContentBoxColor;
            canvas.DrawRect(new SKRect(0.5f, 0.5f, _contentSize.X - 0.5f, _contentSize.Y - 0.5f), contentBox);
        }

        DrawBlockOutlines(canvas, elements);

        if (DebugOverlay)
            DrawOverlay(canvas, elements);

        // How big the picture wants to be is _GetMinimumSize's answer, not something to write back from inside a
        // draw: the bitmap is the area the canvas was given, and the minimum size is what makes a container (or a
        // scroll region) give it enough of that area to hold the sample.
        _texture ??= new ImageTexture();
        var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, _bitmap.GetPixelSpan().ToArray());
        _texture.SetImage(image);

        // Ask for the frame that presents it. The call is deferred because this one is running *inside* a draw,
        // where QueueRedraw has no effect: the drawn texture is the one from before the upload.
        if (_presentPending)
        {
            _presentPending = false;
            CallDeferred(CanvasItem.MethodName.QueueRedraw);
        }
    }

    /// <summary>
    /// A Godot colour as a Skia one. The example converts colours itself rather than reaching for a converter from
    /// another part of the project: this component depends on nothing but its own code and its NuGet packages.
    /// </summary>
    /// <param name="color">Godot colour.</param>
    /// <returns>The same colour for Skia.</returns>
    private static SKColor Sk(Color color) => new(
        (byte)Mathf.RoundToInt(Mathf.Clamp(color.R, 0f, 1f) * 255f),
        (byte)Mathf.RoundToInt(Mathf.Clamp(color.G, 0f, 1f) * 255f),
        (byte)Mathf.RoundToInt(Mathf.Clamp(color.B, 0f, 1f) * 255f),
        (byte)Mathf.RoundToInt(Mathf.Clamp(color.A, 0f, 1f) * 255f));

    /// <summary>The element's box in canvas coordinates, grown by an optional padding.</summary>
    /// <param name="element">Element to outline.</param>
    /// <param name="grow">How far to grow it on every side.</param>
    /// <param name="padding">Padding along the inline and block axes, added to <paramref name="grow"/>.</param>
    /// <returns>The rectangle.</returns>
    private SKRect Box(in LayoutElement element, float grow, Vector2 padding = default)
    {
        var position = new Vector2(element.Position.X, element.Position.Y);
        var size = new Vector2(element.Size.X, element.Size.Y);

        // The box grows along the block axis, which in vertical writing is leftward.
        if (IsVertical)
            position.X -= size.X;

        return new SKRect(
            position.X - grow - padding.X,
            position.Y - grow - padding.Y,
            position.X + size.X + grow + padding.X,
            position.Y + size.Y + grow + padding.Y);
    }

    /// <summary>
    /// The box an element actually draws into: its own box, plus the decorations that sit outside it. An annotation
    /// is wider than the character it annotates often enough (a reading of four kana over two ideographs), and in a
    /// column it runs past the character along the line; an emphasis mark is centred off the box on its own side.
    /// A box that stopped at the element's geometry left every decoration outside the grid it is compared with.
    /// </summary>
    /// <param name="element">Element to box.</param>
    /// <returns>The rectangle, in content coordinates.</returns>
    private SKRect InkBox(in LayoutElement element)
    {
        // Box() is what knows which way a box grows in the writing mode in use (leftward for a right-to-left
        // column), so the element's own box comes from it and only the decorations are added here.
        SKRect box = Box(element, 0f);

        if (element.Ruby is { } ruby)
        {
            float halfBand = ruby.BandWidth * 0.5f;
            SKRect annotation;

            if (ruby.Orientation == RubyOrientation.Vertical)
            {
                // Down a column the layout's X is the column's centre line and its baseline is where the column
                // starts, because the column's ink grows downwards from the pen.
                annotation = new SKRect(ruby.X - halfBand, ruby.BaselineY,
                    ruby.X + halfBand, ruby.BaselineY + ruby.Width);
            }
            else if (FontCatalog.Shared.TryGetTypeface(ruby.FontId, out SKTypeface? face) && face is not null)
            {
                // Along a line its X is the band's left edge and its baseline its own; how far above and below that
                // baseline the ink reaches is the annotation font's business, so ask the font.
                using var font = new SKFont(face, ruby.FontSize);
                SKFontMetrics metrics = font.Metrics;

                annotation = new SKRect(ruby.X,
                    ruby.BaselineY + Mathf.Min(metrics.Ascent, 0f),
                    ruby.X + ruby.Width,
                    ruby.BaselineY + Mathf.Max(metrics.Descent, 0f));
            }
            else
            {
                annotation = new SKRect(ruby.X, ruby.BaselineY, ruby.X + ruby.Width, ruby.BaselineY + ruby.FontSize);
            }

            box = SKRect.Union(box, annotation);
        }

        if (element.Emphasis is { } mark)
        {
            float half = mark.Size * 0.5f;
            box = SKRect.Union(box, new SKRect(mark.X - half, mark.CenterY - half,
                mark.X + half, mark.CenterY + half));
        }

        return box;
    }

    /// <summary>
    /// Draw one glyph run the way the output format describes it: a run that reads along a line is placed on the
    /// baseline the layout reported, and a run that reads down a column walks the glyphs' own advances from the
    /// element's inline start on the column's centre line.
    /// </summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="element">Element being drawn, which says which mode it was laid out in.</param>
    /// <param name="run">The shaped glyphs.</param>
    private void DrawRun(SKCanvas canvas, in LayoutElement element, GlyphRun run)
    {
        SKColor color = Sk(element.Color);

        if (!IsVertical)
        {
            DrawLineRun(canvas, run, color);
            return;
        }

        // A column is not drawn from the run's own origin: in vertical writing that pair is not a point in content
        // space. The pen starts at the element's own inline start - the top of its box - on the column's centre line,
        // which is half the box' block extent inside its start edge, and a turned run's glyphs grow towards +X from
        // the pen, so it starts back by most of a glyph height to keep its ink inside the column.
        float crossOffset = run.Rotation == GlyphRotation.ClockwiseQuarter ? run.FontSize * 0.375f : 0f;
        float centre = element.Position.X - (element.Size.X * 0.5f) - crossOffset;

        DrawColumnRun(canvas, run, centre, element.Position.Y, color);
    }

    /// <summary>
    /// Draw a run along a line: the layout's origin is the pen on the baseline of the first glyph, so a glyph's own
    /// advance carries the pen along it.
    /// </summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="run">Run to draw.</param>
    /// <param name="color">Colour to draw in.</param>
    private static void DrawLineRun(SKCanvas canvas, in GlyphRun run, SKColor color) =>
        DrawGlyphGroups(canvas, run, vertical: false, column: 0f, penStart: run.Origin.X, baseline: run.Origin.Y, color);

    /// <summary>
    /// Draw a run down a column: the pen walks the glyphs' advances from the top of the column, at the centre line
    /// the caller gives, and the shaper's inline offsets are distances from a glyph's vertical origin reported
    /// negated - so they are subtracted rather than added.
    /// </summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="run">Run to draw.</param>
    /// <param name="column">Content-space X of the column's centre line.</param>
    /// <param name="inlineStart">Content-space Y the column starts at.</param>
    /// <param name="color">Colour to draw in.</param>
    private static void DrawColumnRun(SKCanvas canvas, in GlyphRun run, float column, float inlineStart, SKColor color) =>
        DrawGlyphGroups(canvas, run, vertical: true, column, penStart: inlineStart, baseline: 0f, color);

    /// <summary>
    /// Draw a run's glyphs, one positioned run per face.
    /// <para>
    /// A run is not necessarily one face: the shaping fallback draws the parts of the text the primary face cannot
    /// cover with another one, and a glyph index only means something together with the face it came from. Walking
    /// the glyphs in groups that share a face and resolving each group's own face is therefore what keeps the
    /// picture identical to the measurement - drawing a fallback glyph with the primary face would rasterize a
    /// different character, or nothing at all.
    /// </para>
    /// </summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="run">Run to draw.</param>
    /// <param name="vertical">Whether the run reads down a column rather than along a line.</param>
    /// <param name="column">Content-space X of the column's centre line (vertical only).</param>
    /// <param name="penStart">Where the pen starts along the run's reading direction.</param>
    /// <param name="baseline">Content-space Y of the baseline (a line only).</param>
    /// <param name="color">Colour to draw in.</param>
    private static void DrawGlyphGroups(
        SKCanvas canvas, in GlyphRun run, bool vertical, float column, float penStart, float baseline, SKColor color)
    {
        using var paint = new SKPaint();
        paint.IsAntialias = true;
        paint.Color = color;

        int index = 0;

        while (index < run.Glyphs.Length)
        {
            ulong fontId = run.Glyphs[index].FontId;
            int start = index;

            while (index < run.Glyphs.Length && run.Glyphs[index].FontId == fontId)
                index++;

            int count = index - start;

            if (!FontCatalog.Shared.TryGetTypeface(fontId, out SKTypeface? face) || face is null)
                continue;

            using var font = new SKFont(face, run.FontSize);

            if (run.Rotation == GlyphRotation.ClockwiseQuarter)
            {
                DrawTurnedGlyphs(canvas, run, start, count, column, penStart, font, paint);
                continue;
            }

            var glyphIds = new List<ushort>(count);
            var positions = new List<SKPoint>(count);
            float pen = penStart;

            for (int i = start; i < start + count; i++)
            {
                Glyph glyph = run.Glyphs[i];

                glyphIds.Add(glyph.Id <= ushort.MaxValue ? (ushort)glyph.Id : (ushort)0);
                positions.Add(vertical
                    ? new SKPoint(column + glyph.OffsetX, pen - glyph.OffsetY)
                    : new SKPoint(pen + glyph.OffsetX, baseline + glyph.OffsetY));

                pen += glyph.Advance;
            }

            using var builder = new SKTextBlobBuilder();
            builder.AddPositionedRun([.. glyphIds], font, [.. positions]);

            if (builder.Build() is { } blob)
            {
                canvas.DrawText(blob, 0f, 0f, paint);
                blob.Dispose();
            }
        }
    }

    /// <summary>
    /// Draw the glyphs of a turned run one at a time: a text blob cannot turn individual glyphs, and a turned run is
    /// a word or a number rather than a page, so the cost stays where the writing mode asks for it.
    /// </summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="run">Run to draw.</param>
    /// <param name="start">Index of the group's first glyph.</param>
    /// <param name="count">Glyphs in the group.</param>
    /// <param name="column">Content-space X of the column's centre line.</param>
    /// <param name="inlineStart">Content-space Y the run starts at.</param>
    /// <param name="font">Font of this group's face.</param>
    /// <param name="paint">Paint to draw with.</param>
    private static void DrawTurnedGlyphs(SKCanvas canvas, in GlyphRun run, int start, int count, float column,
        float inlineStart, SKFont font, SKPaint paint)
    {
        float pen = inlineStart;

        for (int i = start; i < start + count; i++)
        {
            Glyph glyph = run.Glyphs[i];

            // Skia addresses glyphs with 16 bits; a higher index advances the pen but draws nothing.
            if (glyph.Id <= ushort.MaxValue && font.GetGlyphPath((ushort)glyph.Id) is { } path)
            {
                canvas.Save();
                canvas.Translate(column + glyph.OffsetX, pen - glyph.OffsetY);
                canvas.RotateDegrees(90f);
                canvas.DrawPath(path, paint);
                canvas.Restore();
                path.Dispose();
            }

            pen += glyph.Advance;
        }
    }

    /// <summary>
    /// Draw the underline and the strikethrough the element asks for.
    /// <para>
    /// Along a line they sit at the font's own vertical offsets below the baseline the layout reported. Down a column
    /// there is no horizontal baseline to sit on and the font's underline and strikeout positions are vertical
    /// metrics that say nothing about that direction, so a decoration runs down the glyphs instead: through their
    /// middle for a strikethrough, on their far side along the block axis for an underline, both as fractions of the
    /// font size.
    /// </para>
    /// </summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="element">Element being drawn.</param>
    private void DrawDecorations(SKCanvas canvas, in LayoutElement element)
    {
        if (!element.IsUnderline && !element.IsStrikethrough)
            return;

        if (!FontCatalog.Shared.TryGetTypeface(element.GlyphRun!.Value.FontId, out SKTypeface? face) || face is null)
            return;

        using var font = new SKFont(face, element.FontSize);

        using var paint = new SKPaint();
        paint.IsAntialias = true;
        paint.Color = Sk(element.Color);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = Mathf.Max(1.5f, element.FontSize * 0.08f);

        if (IsVertical)
        {
            // The glyphs start at the element's own inline start - no font metric is added to it - and the column is
            // as long as the element's box.
            float top = element.Position.Y;
            float bottom = element.Position.Y + element.Size.Y;
            float centre = element.Position.X - (element.Size.X * 0.5f);

            if (element.IsStrikethrough)
                canvas.DrawLine(centre, top, centre, bottom, paint);

            if (element.IsUnderline)
            {
                float x = centre - (element.FontSize * 0.35f);
                canvas.DrawLine(x, top, x, bottom, paint);
            }

            return;
        }

        SKRect box = Box(element, 0f);

        if (element.IsUnderline)
        {
            float y = element.BaselineY + (font.Metrics.UnderlinePosition ?? element.FontSize * 0.15f);
            canvas.DrawLine(box.Left, y, box.Right, y, paint);
        }

        if (element.IsStrikethrough)
        {
            float y = element.BaselineY + (font.Metrics.StrikeoutPosition ?? -element.FontSize * 0.3f);
            canvas.DrawLine(box.Left, y, box.Right, y, paint);
        }
    }

    /// <summary>
    /// Draw the annotation the layout placed, in the direction it was laid out in. Its geometry is the layout's, and
    /// the two orientations do not mean the same thing by it: along a line, X is the annotation's left edge and
    /// BaselineY its own baseline; down a column, X is the column's centre line and BaselineY is where the column
    /// starts, because the column's ink grows downwards from the pen.
    /// </summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="element">Element being drawn.</param>
    private static void DrawRuby(SKCanvas canvas, in LayoutElement element)
    {
        if (element.Ruby is not { Glyphs: { Length: > 0 } glyphs } ruby)
            return;

        var run = new GlyphRun
        {
            FontId = ruby.FontId,
            FontSize = ruby.FontSize,
            Glyphs = glyphs,
            Origin = new Vector2(ruby.X, ruby.BaselineY),
            Width = ruby.Width,
        };

        if (ruby.Orientation == RubyOrientation.Vertical)
            DrawColumnRun(canvas, run, ruby.X, ruby.BaselineY, Sk(element.Color));
        else
            DrawLineRun(canvas, run, Sk(element.Color));
    }

    /// <summary>
    /// Outline the range each block of the sample was laid out into: the union of the boxes of the elements that
    /// came from that block. The picture shows the sample's ink and nothing else, so without a box there is no way
    /// to tell where a block starts and ends - and the boxes are what make "this example was laid out over these
    /// three lines" something a reader can check.
    /// </summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="elements">Elements the layout produced.</param>
    private void DrawBlockOutlines(SKCanvas canvas, List<LayoutElement> elements)
    {
        if (_kinds is null || Outline == SampleOutline.None)
            return;

        using var paint = new SKPaint();
        paint.IsAntialias = false;
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1f;

        for (int sourceIndex = 0; sourceIndex < _kinds.Count; sourceIndex++)
        {
            if (_kinds[sourceIndex] is not { } kind)
                continue;

            if (Outline == SampleOutline.Example && kind != TypographySamples.BlockKind.Example)
                continue;

            bool started = false;
            SKRect box = default;

            foreach (LayoutElement element in elements)
            {
                if (element.SourceIndex != sourceIndex)
                    continue;

                SKRect rect = InkBox(element);
                box = started ? SKRect.Union(box, rect) : rect;
                started = true;
            }

            if (!started)
                continue;

            paint.Color = kind == TypographySamples.BlockKind.Example ? ExampleBoxColor : BlockBoxColor;
            canvas.DrawRect(box, paint);
        }
    }

    /// <summary>Draw the emphasis mark the layout placed, centred on its character.</summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="element">Element being drawn.</param>
    private static void DrawEmphasis(SKCanvas canvas, in LayoutElement element)
    {
        if (element.Emphasis is not { } mark)
            return;

        if (!FontCatalog.Shared.TryGetTypeface(element.GlyphRun!.Value.FontId, out SKTypeface? face) || face is null)
            return;

        using var font = new SKFont(face, mark.Size);
        using var paint = new SKPaint();
        paint.IsAntialias = true;
        paint.Color = Sk(element.Color);

        float width = font.MeasureText(mark.Mark);
        canvas.DrawText(mark.Mark, mark.X - (width * 0.5f), mark.CenterY + (mark.Size * 0.35f), font, paint);
    }

    /// <summary>
    /// Outline what the layout placed: every element's box, and the line its glyphs sit on - the baseline in
    /// horizontal writing, the column's centre line in vertical writing.
    /// </summary>
    /// <param name="canvas">Target canvas.</param>
    /// <param name="elements">Elements the layout produced.</param>
    private void DrawOverlay(SKCanvas canvas, List<LayoutElement> elements)
    {
        using var box = new SKPaint();
        box.IsAntialias = false;
        box.Style = SKPaintStyle.Stroke;
        box.StrokeWidth = 1f;
        box.Color = new SKColor(0x4D, 0xE6, 0xFF, 0x73);

        using var line = new SKPaint();
        line.IsAntialias = false;
        line.Style = SKPaintStyle.Stroke;
        line.StrokeWidth = 1f;
        line.Color = new SKColor(0xFF, 0x66, 0xE6, 0xBF);

        foreach (LayoutElement element in elements)
        {
            canvas.DrawRect(InkBox(element), box);

            if (float.IsNaN(element.BaselineY))
                continue;

            if (IsVertical)
            {
                float x = element.Position.X - (element.Size.X * 0.5f);
                canvas.DrawLine(x, element.Position.Y, x, element.Position.Y + element.Size.Y, line);
            }
            else
            {
                canvas.DrawLine(element.Position.X, element.BaselineY,
                    element.Position.X + element.Size.X, element.BaselineY, line);
            }
        }
    }
}
