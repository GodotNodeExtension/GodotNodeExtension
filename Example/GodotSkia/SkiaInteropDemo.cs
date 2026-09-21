using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotSkia;
using SkiaSharp;

namespace GodotNodeExtension.Example.GodotSkia;

/// <summary>
/// The converter and interop side of <see cref="SkiaCanvasTexture2D"/>: a panel that runs every
/// <see cref="SkiaGodotConverter"/> round trip live and shows the result, next to four small surfaces that
/// share one GRContext.
/// <para>
/// The other page (<c>GodotSkiaDemo</c>) draws through the SkiaSharp API and owns a single surface; this one
/// answers the two questions that page cannot: "what exactly does a Godot type look like after a round trip
/// through Skia" and "what does it cost to keep several surfaces alive". The counters come from the
/// component's own diagnostics, so the panel stays honest when the implementation changes.
/// </para>
/// <para>
/// Everything the page <i>draws</i> is created here (the textures are the component's own product), while the
/// layout and the node references come from <c>SkiaInteropDemo.tscn</c>. Without a rendering device the page
/// reports it in its status line instead of failing, so it can be opened headless.
/// </para>
/// <para>
/// The surfaces keep the pixel size they were created with, and the nodes presenting them are given exactly
/// that size - one panel of 430x330 and four of 150x110. Following the node rect instead (and calling
/// <see cref="SkiaCanvasTexture2D.Resize"/> whenever it changes) is what the other page demonstrates; here it
/// would only add a rebuild per layout pass, and the panel's text is easier to read at its natural size.
/// </para>
/// </summary>
public partial class SkiaInteropDemo : Control
{
    // ── Scene wiring (see SkiaInteropDemo.tscn) ──────────────────────────────

    /// <summary>The surface the converter panel is drawn into.</summary>
    [Export] public TextureRect ConverterSurface { get; set; } = null!;

    /// <summary>First of the four small surfaces that share the process-wide GPU context.</summary>
    [Export] public TextureRect PoolA { get; set; } = null!;

    /// <summary>Second small surface.</summary>
    [Export] public TextureRect PoolB { get; set; } = null!;

    /// <summary>Third small surface.</summary>
    [Export] public TextureRect PoolC { get; set; } = null!;

    /// <summary>Fourth small surface.</summary>
    [Export] public TextureRect PoolD { get; set; } = null!;

    /// <summary>Line that reports the driver, the mode and the frame rate.</summary>
    [Export] public Label Status { get; set; } = null!;

    /// <summary>Block that reports the shared-context counters and the size of every surface.</summary>
    [Export] public Label Readout { get; set; } = null!;

    // ── State ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Pixel size of the converter panel. The node presenting it is given the same size, so the surface is
    /// never resized: pixels and layout coordinates stay identical.
    /// </summary>
    private static readonly Vector2I ConverterSize = new(430, 330);

    /// <summary>Pixel size of one pool surface (the same as its node's).</summary>
    private static readonly Vector2I PoolSize = new(150, 110);

    /// <summary>The panel surface, or null when this run has no rendering device.</summary>
    private SkiaCanvasTexture2D? _converter;

    /// <summary>The four small surfaces, index-aligned with <see cref="_poolNodes"/>.</summary>
    private readonly List<SkiaCanvasTexture2D> _pool = [];

    /// <summary>The nodes presenting <see cref="_pool"/>, same order.</summary>
    private readonly List<TextureRect> _poolNodes = [];

    private float _time;

    /// <summary>Seconds since the last readout/status refresh (they are not worth a string per frame).</summary>
    private float _sinceReadout;

    private int _framesSinceReadout;

    /// <summary>Frames per second over the last refresh window.</summary>
    private float _fps;

    /// <summary>Result of the converter checks, recomputed on every readout refresh.</summary>
    private List<Check> _checks = [];

    /// <summary>One line of the converter panel: what was checked, the verdict, and the value to draw.</summary>
    /// <param name="Label">Short name of the conversion.</param>
    /// <param name="Verdict">Whether the round trip survived, and by how much.</param>
    /// <param name="Swatch">Colour to paint next to the line, or null for no swatch.</param>
    private readonly record struct Check(string Label, string Verdict, Color? Swatch);

    /// <inheritdoc />
    public override void _Ready()
    {
        if (!WiringIsComplete()) return;

        if (!SkiaCanvasTexture2D.HasRenderingDevice)
        {
            // The page stays open (and readable) instead of failing: no surface can be created here at all.
            Status.Text = "no rendering device in this run (a headless run, or --rendering-driver dummy): " +
                          "this page needs one for its surfaces";
            return;
        }

        _converter = new SkiaCanvasTexture2D(ConverterSize.X, ConverterSize.Y);
        ConverterSurface.Texture = _converter;

        foreach (var node in new[] { PoolA, PoolB, PoolC, PoolD })
        {
            var surface = new SkiaCanvasTexture2D(PoolSize.X, PoolSize.Y);
            node.Texture = surface;
            _pool.Add(surface);
            _poolNodes.Add(node);
        }

        _checks = RunConverterChecks();
        Status.Text = "starting";
    }

    /// <summary>
    /// Every exported reference has to be connected in the scene. An unwired one would otherwise surface as a
    /// null dereference in the first frame, which says nothing about what to fix.
    /// </summary>
    private bool WiringIsComplete()
    {
        var missing = new List<string>();
        Require(ConverterSurface, nameof(ConverterSurface), missing);
        Require(PoolA, nameof(PoolA), missing);
        Require(PoolB, nameof(PoolB), missing);
        Require(PoolC, nameof(PoolC), missing);
        Require(PoolD, nameof(PoolD), missing);
        Require(Status, nameof(Status), missing);
        Require(Readout, nameof(Readout), missing);
        if (missing.Count == 0) return true;

        GD.PushError($"{GetType().Name}: not wired in the scene: {string.Join(", ", missing)}. " +
                     "Connect them in SkiaInteropDemo.tscn (node_paths) - the page cannot run without them.");
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
    private static void Require(Node? node, string name, List<string> missing)
    {
        if (node is null) missing.Add(name);
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_converter is not { } converter) return;

        _time += (float)delta;
        _framesSinceReadout++;
        _sinceReadout += (float)delta;

        DrawConverterPanel(converter);
        converter.UpdateTexture();

        for (int i = 0; i < _pool.Count; i++)
        {
            DrawPoolPanel(_pool[i], i);
            _pool[i].UpdateTexture();
        }

        // The readout and the checks are rebuilt a few times a second, not per frame: they allocate, and
        // nothing on the panel changes faster than that.
        if (_sinceReadout < 0.5f) return;

        _fps = _framesSinceReadout / _sinceReadout;
        _framesSinceReadout = 0;
        _sinceReadout = 0f;

        _checks = RunConverterChecks();
        Status.Text = $"{RenderingServer.GetCurrentRenderingDriverName()} · " +
                      $"{(converter.IsGpuMode ? "GPU surface (shared GRContext)" : "CPU fallback (bitmap upload)")} · " +
                      $"{converter.Width}x{converter.Height} + 4x{PoolSize.X}x{PoolSize.Y} · {_fps:F0} fps";
        Readout.Text = DescribeContext();
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        // The surfaces are native resources owned by this page: release them (and detach them from the nodes
        // that present them) when the page leaves the tree.
        ConverterSurface.Texture = null;
        _converter?.ReleaseResources();
        _converter = null;

        for (int i = 0; i < _pool.Count; i++)
        {
            _poolNodes[i].Texture = null;
            _pool[i].ReleaseResources();
        }
        _pool.Clear();
        _poolNodes.Clear();
    }

    // ── Converter panel ──────────────────────────────────────────────────────

    /// <summary>
    /// The palette the probe texture is painted with: the converter ships these constants
    /// (<see cref="SkiaGodotConverter.Colors"/>), so the page paints with them instead of hand-rolled
    /// <c>SKColor</c> literals.
    /// </summary>
    private static readonly SKColor[] ProbeColours =
    [
        SkiaGodotConverter.Colors.Red,
        SkiaGodotConverter.Colors.Green,
        SkiaGodotConverter.Colors.Blue,
        SkiaGodotConverter.Colors.Yellow,
    ];

    /// <summary>
    /// A small <see cref="Texture2D"/> holding four flat quadrants in <see cref="ProbeColours"/>. It is the
    /// source of the <c>Texture2D -> SKImage</c> round trip: the four colours are known, so the panel compares
    /// what came back instead of only drawing it.
    /// </summary>
    private static SkiaCanvasTexture2D BuildProbeTexture()
    {
        var texture = new SkiaCanvasTexture2D(24, 24);
        var canvas = texture.Canvas ?? throw new InvalidOperationException("the probe texture has no canvas");
        canvas.Clear(SkiaGodotConverter.Colors.Black);

        for (int i = 0; i < ProbeColours.Length; i++)
        {
            // Properties after construction rather than an initializer: an initializer that threw would leave
            // the SKPaint undisposed (UsingStatementResourceInitialization).
            using var paint = new SKPaint();
            paint.Color = ProbeColours[i];
            paint.IsAntialias = false;

            // The probe grid is 2x2: integer division gives the row on purpose (i / 2 * 12f reads as a
            // fraction and costs a rounding question - PossibleLossOfFraction).
            int column = i % 2;
            int row = i / 2;
            float x = column * 12f;
            float y = row * 12f;
            canvas.DrawRect(new SKRect(x, y, x + 12f, y + 12f), paint);
        }

        texture.UpdateTexture();
        return texture;
    }

    /// <summary>
    /// Run every converter round trip and report how it came out. The values are recomputed rather than
    /// cached from a comment: an implementation that starts losing precision shows up on screen.
    /// </summary>
    private static List<Check> RunConverterChecks()
    {
        var checks = new List<Check>();

        // Color: Godot stores floats, Skia 8 bit channels, so the round trip is lossy by design and the panel
        // reports the largest step instead of claiming equality it does not have.
        var godotColour = new Color(0.42f, 0.71f, 0.19f, 0.63f);
        Color backColour = godotColour.ToSkColor().ToGodotColor();
        float colourDelta = MathF.Max(MathF.Abs(backColour.R - godotColour.R),
            MathF.Max(MathF.Abs(backColour.G - godotColour.G),
                MathF.Max(MathF.Abs(backColour.B - godotColour.B), MathF.Abs(backColour.A - godotColour.A))));
        checks.Add(new Check("Color -> SKColor -> Color",
            $"largest step {colourDelta * 255f:F1}/255", backColour));

        // Geometry: floats travel unchanged through both structs.
        var vector = new Vector2(3.25f, -4.5f);
        Vector2 vectorBack = vector.ToSkPoint().ToVector2();
        checks.Add(new Check("Vector2 -> SKPoint -> Vector2",
            vectorBack == vector ? "exact" : $"off by {vectorBack - vector}", null));

        var size2 = new Vector2(120.5f, 40.25f);
        Vector2 sizeBack = size2.ToSkSize().ToVector2();
        checks.Add(new Check("Vector2 -> SKSize -> Vector2",
            sizeBack == size2 ? "exact" : $"off by {sizeBack - size2}", null));

        var rect = new Rect2(1f, 2f, 3f, 4f);
        Rect2 rectBack = rect.ToSkRect().ToRect2();
        checks.Add(new Check("Rect2 -> SKRect -> Rect2",
            rectBack == rect ? "exact" : $"{rectBack}", null));

        // Transform2D -> SKMatrix -> Transform2D: the fields are compared, and the matrix itself is used to
        // place the rotating quad the panel draws.
        var transform = new Transform2D(0.5f, new Vector2(12f, -7f));
        SKMatrix matrix = transform.ToSkMatrix();
        Transform2D transformBack = matrix.ToTransform2D();
        bool transformExact = transformBack.X.IsEqualApprox(transform.X)
                              && transformBack.Y.IsEqualApprox(transform.Y)
                              && transformBack.Origin.IsEqualApprox(transform.Origin);
        checks.Add(new Check("Transform2D -> SKMatrix -> Transform2D",
            transformExact ? "exact" : $"{transformBack}", null));

        // Image -> SKBitmap -> Image: a pattern whose pixels are known, so "0 differing" is a real statement.
        using (var pattern = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8))
        {
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    bool on = (x + y) % 2 == 0;
                    pattern.SetPixel(x, y, on ? new Color(0.95f, 0.75f, 0.30f) : new Color(0.15f, 0.35f, 0.65f));
                }
            }

            using var bitmap = pattern.ToSkBitmap();
            using var back = bitmap.ToGodotImage();
            int differing = 0;
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                    if (pattern.GetPixel(x, y) != back.GetPixel(x, y)) differing++;
            checks.Add(new Check("Image -> SKBitmap -> Image", $"{differing}/64 pixels differ", null));
        }

        // Font: the synthetic style is derived from a Godot variation, and the width of the resolved font is
        // what text layout on the surface uses.
        Font godotFont = ThemeDB.FallbackFont;
        using (var font = godotFont.ToSkFont(13f))
        {
            float width = font.MeasureText("GodotSkia");
            SkiaGodotConverter.SkiaFontStyle style = SkiaGodotConverter.SyntheticStyleOf(godotFont);
            checks.Add(new Check($"Font -> SKFont ({font.Typeface?.FamilyName ?? "default"})",
                $"{width:F1} px · synthetic bold={style.Bold} italic={style.Italic}", null));
        }

        // Texture2D -> SKImage -> Image: the other direction of the image conversions, and the only path that
        // reads a Godot texture. The probe is painted through the converter's own palette, so a channel lost on
        // the way shows up as a wrong quadrant rather than as a slightly different picture.
        using (var probe = BuildProbeTexture())
        using (var skImage = probe.ToSkImage())
        using (var back = skImage.ToGodotImage())
        {
            int differing = 0;
            for (int i = 0; i < ProbeColours.Length; i++)
            {
                Color expected = ProbeColours[i].ToGodotColor();
                if (!back.GetPixel(i % 2 * 12 + 6, i / 2 * 12 + 6).IsEqualApprox(expected)) differing++;
            }

            checks.Add(new Check("Texture2D -> SKImage -> Image", $"{differing}/4 quadrants differ", null));
        }

        return checks;
    }

    /// <summary>
    /// Draw the converter panel: a caption, one line per round trip with its verdict, and a swatch pair for the
    /// colour check.
    /// <para>
    /// A verdict that does not fit beside its label (the Font line carries a family name and a style report)
    /// is drawn on its own right-aligned line under the label, so nothing overlaps and nothing is shortened.
    /// The rotating quad the transform check places comes last, in the block under the rows.
    /// </para>
    /// </summary>
    private void DrawConverterPanel(SkiaCanvasTexture2D surface)
    {
        var canvas = surface.Canvas;
        if (canvas is null) return;

        int width = surface.Width;
        int height = surface.Height;
        canvas.Clear(new SKColor(16, 19, 28));

        // The caption uses the documented helper that builds a paint+font pair, so the page also exercises
        // CreateTextPaintAndFont (nothing else in the repository calls it).
        var (titlePaint, titleFont) = SkiaGodotConverter.CreateTextPaintAndFont(
            ThemeDB.FallbackFont, 14f, new Color(0.88f, 0.92f, 1f));
        var (labelPaint, labelFont) = SkiaGodotConverter.CreateTextPaintAndFont(
            ThemeDB.FallbackFont, 11f, new Color(0.72f, 0.78f, 0.90f));
        var (verdictPaint, verdictFont) = SkiaGodotConverter.CreateTextPaintAndFont(
            ThemeDB.FallbackFont, 11f, new Color(0.55f, 0.85f, 0.62f));
        try
        {
            canvas.DrawText("SkiaGodotConverter", 12f, 24f, titleFont, titlePaint);

            int row = 52;
            const int rowHeight = 27;
            const float columnGap = 12f;        // least space between the label column and the verdict column
            const int verdictLineHeight = 14;   // height the verdict takes when it moves to its own line
            foreach (var check in _checks)
            {
                if (row > height - 60) break;   // a small surface shows fewer lines rather than drawing over them
                canvas.DrawText(check.Label, 12f, row, labelFont, labelPaint);

                // Measure before placing: the Font line reports a resolved family name plus a style report, and
                // its verdict is wide, so the right-aligned column can reach back into the label.
                float verdictWidth = verdictFont.MeasureText(check.Verdict);
                float verdictX = width - 12f - verdictWidth;
                if (verdictX < 12f + labelFont.MeasureText(check.Label) + columnGap)
                {
                    // No room beside the label: the verdict goes on its own right-aligned line rather than
                    // overlapping it, and rather than shortening either measured string.
                    row += verdictLineHeight;
                    verdictX = width - 12f - verdictWidth;
                }
                canvas.DrawText(check.Verdict, verdictX, row, verdictFont, verdictPaint);

                if (check.Swatch is { } swatch)
                {
                    // Fill and stroke from two documented paint helpers, at the row's right edge.
                    using var fill = SkiaGodotConverter.CreatePaint(swatch);
                    using var stroke = SkiaGodotConverter.CreateStrokePaint(new Color(0.9f, 0.9f, 0.95f, 0.7f), 1f);
                    var swatchRect = new SKRect(verdictX - 30f, row - 11f, verdictX - 16f, row + 1f);
                    canvas.DrawRect(swatchRect, fill);
                    canvas.DrawRect(swatchRect, stroke);
                }

                row += rowHeight;
            }

            DrawRotationProbe(canvas, width, height, row, labelFont, labelPaint);
            DrawConvertedProbe(canvas, height, row, labelFont, labelPaint);
        }
        finally
        {
            titlePaint.Dispose();
            titleFont.Dispose();
            labelPaint.Dispose();
            labelFont.Dispose();
            verdictPaint.Dispose();
            verdictFont.Dispose();
        }
    }

    /// <summary>
    /// Draw the quad whose corners are placed through <see cref="SkiaGodotConverter.ToSkMatrix"/>: the conversion is
    /// not just displayed, it positions the shape (a rotation and a translation, both visible as motion). The
    /// label is drawn with the row font, so the block reads as one more line of the panel with the shape under it.
    /// </summary>
    private void DrawRotationProbe(SKCanvas canvas, int width, int height, int top,
        SKFont labelFont, SKPaint labelPaint)
    {
        // The label takes the block's first line; the quad is centred in what is left of the panel below it.
        // A surface too short for both gets no probe at all instead of a clipped one.
        if (height - top < 44) return;

        float labelY = top + 16f;
        float centreX = width - 60f;
        float centreY = (labelY + 14f + height) * 0.5f;

        int save = canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(0f, top, width, height));
            canvas.DrawText("Transform2D -> SKMatrix", 12f, labelY, labelFont, labelPaint);

            var transform = new Transform2D(_time, new Vector2(centreX, centreY));
            SKMatrix matrix = transform.ToSkMatrix();

            // The matrix's own fields place the corners: (x, y) -> (ScaleX*x + SkewX*y + TransX, ...).
            var quad = new SKPath();
            for (int corner = 0; corner < 4; corner++)
            {
                float localX = corner is 0 or 3 ? -18f : 18f;
                float localY = corner < 2 ? -18f : 18f;
                float x = matrix.ScaleX * localX + matrix.SkewX * localY + matrix.TransX;
                float y = matrix.SkewY * localX + matrix.ScaleY * localY + matrix.TransY;
                if (corner == 0) quad.MoveTo(x, y);
                else quad.LineTo(x, y);
            }
            quad.Close();

            canvas.DrawPath(quad, _probeFill!);
            canvas.DrawPath(quad, _probeStroke!);
            quad.Dispose();
        }
        finally
        {
            canvas.RestoreToCount(save);
        }
    }

    /// <summary>
    /// Draw the texture the <c>Texture2D -> SKImage</c> check converted, at its own size in the block under the
    /// rows: that blit is what <see cref="SkiaGodotConverter.ToSkImage"/> is for (a Godot texture drawn through
    /// Skia). A panel without room for it shows the check only, like the transform probe above.
    /// </summary>
    private static void DrawConvertedProbe(SKCanvas canvas, int height, int top,
        SKFont labelFont, SKPaint labelPaint)
    {
        const int side = 64;
        const int labelHeight = 16;
        if (height - top < side + labelHeight + 12) return;

        using var probe = BuildProbeTexture();
        using var skImage = probe.ToSkImage();

        float left = 12f;
        float imageTop = top + labelHeight + 6f;
        canvas.DrawText("Texture2D -> SKImage", left, top + 12f, labelFont, labelPaint);
        canvas.DrawImage(skImage, new SKRect(left, imageTop, left + side, imageTop + side));
    }

    // ── Pool panels ──────────────────────────────────────────────────────────

    /// <summary>
    /// Draw one of the four small surfaces. They deliberately draw different things: four identical pictures
    /// would look the same if the surfaces were mixed up.
    /// </summary>
    private void DrawPoolPanel(SkiaCanvasTexture2D surface, int index)
    {
        var canvas = surface.Canvas;
        if (canvas is null) return;

        int width = surface.Width;
        int height = surface.Height;
        var background = new SKColor((byte)(18 + index * 6), (byte)(22 + index * 4), (byte)(34 + index * 8));
        canvas.Clear(background);

        // The brushes belong to the page and are reused (see EnsureProbes): four panels redraw every frame, and
        // a brush per panel per frame would be pure garbage. Their colours are set here, which is also why they
        // are fields rather than `using` locals - a `using` local whose properties are assigned outside its
        // constructor is what ReSharper's UsingStatementResourceInitialization reports on.
        var paint = _poolFill!;
        var edge = _poolEdge!;
        paint.Color = index switch
        {
            0 => new SKColor(90, 170, 255, 220),
            2 => new SKColor(255, 190, 90, 220),
            _ => new SKColor(210, 140, 255, 210),
        };
        edge.Color = index == 0 ? new SKColor(60, 90, 130, 160) : new SKColor(220, 226, 240, 200);

        switch (index)
        {
            case 0:   // a sweeping bar
                float sweep = (MathF.Sin(_time * 1.6f) * 0.5f + 0.5f) * (width - 24f);
                canvas.DrawRect(new SKRect(12f + sweep, 20f, 20f + sweep, height - 20f), paint);
                canvas.DrawRect(new SKRect(12f, 20f, width - 12f, height - 20f), edge);
                break;

            case 1:   // expanding rings, each one its own brush so the fade is part of its initialisation
            {
                var ringPaint = _poolRing!;
                for (int ring = 0; ring < 3; ring++)
                {
                    float phase = (_time * 0.7f + ring / 3f) % 1f;
                    ringPaint.Color = new SKColor(120, 235, 170, (byte)(200 * (1f - phase)));
                    canvas.DrawCircle(width * 0.5f, height * 0.5f,
                        6f + phase * (MathF.Min(width, height) * 0.42f), ringPaint);
                }
                break;
            }

            case 2:   // a bar chart of a moving sine
                int bars = 5;
                float slot = (width - 24f) / bars;
                for (int bar = 0; bar < bars; bar++)
                {
                    float value = 0.5f + 0.5f * MathF.Sin(_time * 2f + bar * 0.9f);
                    float barHeight = 12f + value * (height - 44f);
                    canvas.DrawRect(new SKRect(
                        12f + bar * slot + 2f, height - 16f - barHeight,
                        12f + bar * slot + slot - 2f, height - 16f), paint);
                }
                break;

            default:  // a rotating triangle plus its centre dot, placed through a matrix
            {
                var spin = new Transform2D(_time * 0.9f, new Vector2(width * 0.5f, height * 0.5f));
                SKMatrix matrix = spin.ToSkMatrix();
                using var triangle = new SKPath();
                for (int corner = 0; corner < 3; corner++)
                {
                    float angle = corner * (MathF.Tau / 3f);
                    float localX = MathF.Cos(angle) * 26f;
                    float localY = MathF.Sin(angle) * 26f;
                    float x = matrix.ScaleX * localX + matrix.SkewX * localY + matrix.TransX;
                    float y = matrix.SkewY * localX + matrix.ScaleY * localY + matrix.TransY;
                    if (corner == 0) triangle.MoveTo(x, y);
                    else triangle.LineTo(x, y);
                }
                triangle.Close();
                canvas.DrawPath(triangle, paint);
                paint.Color = SKColors.White;
                canvas.DrawCircle(width * 0.5f, height * 0.5f, 3f, paint);
                break;
            }
        }

        // Every surface names itself, so a swapped picture is obvious in a screenshot.
        canvas.DrawText(PoolLabel(index), 10f, height - 6f, _probeFont!, _probePaint!);
    }

    /// <summary>Name drawn into one pool surface.</summary>
    private static string PoolLabel(int index) => index switch
    {
        0 => "A - sweep",
        1 => "B - rings",
        2 => "C - bars",
        _ => "D - spin",
    };

    // ── Diagnostics ──────────────────────────────────────────────────────────

    /// <summary>
    /// The counters behind the page's claim: the number of live references and the number of contexts created
    /// are the component's own diagnostics, so "four surfaces, one context" is measured, not asserted.
    /// </summary>
    private string DescribeContext()
    {
        var lines = new List<string>
        {
            $"surfaces alive: {_pool.Count + 1}",
            $"shared GRContext references: {SkiaCanvasTexture2D.SharedGrContextRefCount}",
            $"GRContexts created in this process: {SkiaCanvasTexture2D.GrContextCreateCount}",
            $"sharing one context: {SkiaCanvasTexture2D.ShareGrContext}",
            $"canvas size: {_converter?.Width}x{_converter?.Height}, pools {PoolSize.X}x{PoolSize.Y}",
        };

        if (_converter is { IsGpuMode: false })
            lines.Add("note: no shared context is held in CPU fallback mode");

        return string.Join('\n', lines);
    }

    /// <summary>Paint and font the small panels share (no text API of the canvas abstraction is involved).</summary>
    private SKPaint? _probePaint;

    private SKFont? _probeFont;

    /// <summary>Fill and stroke for the rotating quad, created once with the page.</summary>
    private SKPaint? _probeFill;

    private SKPaint? _probeStroke;

    /// <summary>Fill brush the pool panels share (its colour is set per panel and per pattern).</summary>
    private SKPaint? _poolFill;

    /// <summary>Stroke brush for the pool panels' outlines and bars.</summary>
    private SKPaint? _poolEdge;

    /// <summary>Stroke brush for the expanding rings.</summary>
    private SKPaint? _poolRing;

    /// <summary>Create the shared paints/font once, before the first frame draws with them.</summary>
    private void EnsureProbes()
    {
        if (_probeFont is not null) return;

        _probePaint = new SKPaint { Color = new SKColor(190, 200, 220), IsAntialias = true };
        _probeFont = SkiaGodotConverter.ConfigureFont(
            ThemeDB.FallbackFont.ToSkFont(10f), SkiaGodotConverter.SyntheticStyleOf(ThemeDB.FallbackFont));
        _probeFill = new SKPaint { Color = new SKColor(255, 120, 90, 190), IsAntialias = true };
        _probeStroke = new SKPaint
        {
            Color = new SKColor(255, 220, 210, 230),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
        };
        _poolFill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        _poolEdge = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2f };
        _poolRing = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f };
    }

    /// <inheritdoc />
    public override void _EnterTree()
    {
        EnsureProbes();
    }

    /// <summary>Release the paints and the font with the page (they are native objects).</summary>
    public override void _Notification(int what)
    {
        if (what != NotificationPredelete) return;

        _probePaint?.Dispose();
        _probePaint = null;
        _probeFont?.Dispose();
        _probeFont = null;
        _probeFill?.Dispose();
        _probeFill = null;
        _probeStroke?.Dispose();
        _probeStroke = null;
        _poolFill?.Dispose();
        _poolFill = null;
        _poolEdge?.Dispose();
        _poolEdge = null;
        _poolRing?.Dispose();
        _poolRing = null;
    }
}
