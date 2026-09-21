namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.IO;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using static GdUnit4.Assertions;

/// <summary>
/// The shared plumbing of the chart integration suites: build a <see cref="ChartView"/> on the engine's real
/// backend, run the frames the engine would, read the surface back, and measure it.
/// <para>
/// Every integration suite needs the same five things (create, pump, read, measure, release) and the same
/// device check, so they live here instead of being copied per file - the copies drift, and a drifted copy
/// silently measures something else. <see cref="ChartViewRenderIntegrationTest"/> keeps its own private
/// wrappers for now (its 1900 lines of call sites are not worth rewriting); they delegate to this class.
/// </para>
/// <para>
/// The suites that use it only run where there is a rendering device: <see cref="NoRenderingDevice"/>
/// reports the skip and the case returns, exactly like the other device-dependent suites.
/// </para>
/// </summary>
internal static class ChartRenderHarness
{
    /// <summary>Size of the views the suites build unless they say otherwise.</summary>
    public static readonly Vector2 ViewSize = new(320f, 200f);

    /// <summary>Size used by the resize cases.</summary>
    public static readonly Vector2 ResizedViewSize = new(480f, 300f);

    /// <summary>
    /// Skip the case when this run has no rendering device (headless): the Skia backend allocates a
    /// RenderingDevice texture and cannot render without one.
    /// </summary>
    public static bool NoRenderingDevice(string caseName)
    {
        if (RenderingServer.GetRenderingDevice() is not null) return false;
        GD.Print($"[skip] {caseName}: no rendering device in this run");
        return true;
    }

    /// <summary>Create a view on the <b>default</b> backend (no fake canvas injected), sized, in the tree.</summary>
    public static ChartView AddView(ChartRenderCase c, Vector2 size)
    {
        var view = new ChartView
        {
            Kind = c.Kind,
            XField = c.XField,
            YField = c.YField,
            ColorField = c.ColorField,
            Size = size,
        };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(view);
        return view;
    }

    /// <summary>Create a view of one kind without data, sized, in the tree.</summary>
    public static ChartView AddView(ChartKind kind, Vector2 size)
    {
        var view = new ChartView { Kind = kind, Size = size };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(view);
        return view;
    }

    /// <summary>
    /// Run the frames the engine would: the view rebuilds its chart, the surface it hosts then draws.
    /// </summary>
    public static void Pump(ChartView view, int frames = 2)
    {
        for (int i = 0; i < frames; i++)
        {
            view._Process(0.016);
            foreach (var child in view.GetChildren(includeInternal: true))
                if (child is Canvas2DControl surface)
                    surface._Process(0.016);
        }
    }

    /// <summary>
    /// Pump until two consecutive frames present the same pixels, and report how many frames that took.
    /// <para>
    /// Fixed frame counts ("five frames is enough for the tooltip to fade in") are the usual source of
    /// flakiness: they encode a frame budget that a slower machine or a longer fade breaks. Waiting for the
    /// surface to settle states the real precondition, and a surface that never settles is a failure worth
    /// reporting rather than something to hide behind a bigger constant.
    /// </para>
    /// </summary>
    /// <param name="view">View to pump.</param>
    /// <param name="maxFrames">Upper bound; when the surface never settles this returns 0.</param>
    /// <returns>Frames used (>= 2), or 0 when the surface kept changing.</returns>
    public static int PumpUntilStable(ChartView view, int maxFrames = 30)
    {
        Image? previous = Pixels(view);
        for (int frame = 1; frame <= maxFrames; frame++)
        {
            Pump(view, 1);
            var current = Pixels(view);
            if (previous is not null && current is not null && DifferingPixels(previous, current) == 0)
                return frame;
            previous = current;
        }
        return 0;
    }

    /// <summary>
    /// Remove the view from the tree and free it. Tolerant on purpose: a failing assertion must not turn
    /// into a cascade of secondary errors.
    /// </summary>
    public static void Release(ChartView view)
    {
        try
        {
            view.GetParent()?.RemoveChild(view);
        }
        finally
        {
            view.Free();
        }
    }

    /// <summary>
    /// The pixels of the surface the view presents, or null when it has none. Read through the standard
    /// <see cref="Texture2D.GetImage"/> API: the Skia texture implements Godot's virtual, so the base call
    /// returns the surface it just drew into.
    /// </summary>
    public static Image? Pixels(ChartView view) => view.Texture?.GetImage();

    /// <summary>True when the pixel is the given background within the rounding of an 8 bit channel.</summary>
    public static bool IsBackground(Color pixel, Color background)
        => Math.Abs(pixel.R - background.R) <= 2f / 255f
           && Math.Abs(pixel.G - background.G) <= 2f / 255f
           && Math.Abs(pixel.B - background.B) <= 2f / 255f;

    /// <summary>Number of pixels that differ from the theme's dark background, and the total pixel count.</summary>
    public static (int Content, int Total) Measure(Image image)
        => Measure(image, ChartTheme.Dark().BackgroundColor);

    /// <summary>Number of pixels that differ from the given background colour, and the total pixel count.</summary>
    public static (int Content, int Total) Measure(Image image, Color background)
    {
        int content = 0;
        int width = image.GetWidth();
        int height = image.GetHeight();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                if (!IsBackground(image.GetPixel(x, y), background))
                    content++;
        return (content, width * height);
    }

    /// <summary>
    /// Fraction of the surface that carries <paramref name="colour"/> - the inverse of what
    /// <see cref="Measure(Image, Color)"/> counts, phrased for "the theme's background covers the surface".
    /// </summary>
    public static float BackgroundRatio(Image image, Color colour)
    {
        var measured = Measure(image, colour);
        return measured.Total == 0 ? 0f : 1f - (float)measured.Content / measured.Total;
    }

    /// <summary>Number of pixels that differ between two images of the same size (-1 on a size mismatch).</summary>
    public static int DifferingPixels(Image first, Image second)
    {
        if (first.GetWidth() != second.GetWidth() || first.GetHeight() != second.GetHeight()) return -1;

        int differing = 0;
        for (int y = 0; y < first.GetHeight(); y++)
            for (int x = 0; x < first.GetWidth(); x++)
                if (first.GetPixel(x, y) != second.GetPixel(x, y))
                    differing++;
        return differing;
    }

    /// <summary>Content ratio of what the view currently presents (0 when there is nothing to read).</summary>
    public static float ContentRatio(ChartView view, out int content)
    {
        content = 0;
        var image = Pixels(view);
        if (image is null) return 0f;
        var measured = Measure(image);
        content = measured.Content;
        return measured.Total == 0 ? 0f : (float)measured.Content / measured.Total;
    }

    /// <summary>
    /// Order-independent fingerprint of a frame's pixels (FNV-1a over the colour bytes). Storing one number
    /// per frame lets a case compare whole frame sequences - "these three frames differ" and "these two are
    /// the same" - without keeping every image alive.
    /// </summary>
    public static ulong PixelFingerprint(Image image)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offset;
        for (int y = 0; y < image.GetHeight(); y++)
        {
            for (int x = 0; x < image.GetWidth(); x++)
            {
                Color pixel = image.GetPixel(x, y);
                foreach (float channel in new[] { pixel.R, pixel.G, pixel.B, pixel.A })
                {
                    // Quantised to the 8 bit channel the surface actually stores.
                    var value = (byte)Math.Clamp(MathF.Round(channel * 255f), 0f, 255f);
                    hash = (hash ^ value) * prime;
                }
            }
        }
        return hash;
    }

    /// <summary>True when the pixel is clearly more than a decoration: see the integration suite's probes.</summary>
    public static bool IsStrongContent(Color pixel, Color background, float threshold = 0.15f)
        => MathF.Max(MathF.Abs(pixel.R - background.R),
              MathF.Max(MathF.Abs(pixel.G - background.G), MathF.Abs(pixel.B - background.B))) >= threshold;

    /// <summary>The directory <c>CHART_INTEGRATION_OUT</c> names, or an empty string when it is unset.</summary>
    public static string DumpDirectory()
        => System.Environment.GetEnvironmentVariable("CHART_INTEGRATION_OUT")?.Trim() ?? "";

    // ── Golden baselines ────────────────────────────────────────────────────

    /// <summary>Where the version-controlled baselines live (see <see cref="AssertMatchesGolden"/>).</summary>
    public const string GoldenDirectory = "res://Test/GodotChart/Integration/baseline";

    /// <summary>
    /// How far one channel may differ before a pixel counts as different. Antialiasing is not bit-exact across
    /// drivers, so a couple of channel values of difference is noise; a real regression (a missing element, a
    /// shifted axis, a colour that stopped following the theme) moves far more than that.
    /// </summary>
    public const float GoldenChannelTolerance = 4f / 255f;

    /// <summary>Share of the pixels that may differ beyond <see cref="GoldenChannelTolerance"/>.</summary>
    public const float GoldenMaxDifferingRatio = 0.01f;

    /// <summary>
    /// Compare a captured frame with its recorded baseline, or record the baseline when there is none yet.
    /// <para>
    /// Recording passes and says so in the log: a fresh checkout has no image yet, and the file the run writes is
    /// what has to be looked at before it is committed. Re-recording is "delete the file and run the case again",
    /// the same workflow the typography baselines use (see
    /// <c>Test/RichTextCanvas/Integration/TypographyRenderIntegrationTest</c>) - a picture that legitimately
    /// changed is reviewed, not silently accepted.
    /// </para>
    /// </summary>
    /// <param name="name">Baseline name without extension, e.g. <c>cartesian-decorated</c>.</param>
    /// <param name="rendered">The frame the case just captured.</param>
    public static void AssertMatchesGolden(string name, Image rendered)
    {
        string path = ProjectSettings.GlobalizePath($"{GoldenDirectory}/{name}.png");

        if (!File.Exists(path))
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            rendered.SavePng(path.Replace('\\', '/'));
            GD.Print($"[render-baseline] recorded {name}.png — re-run to compare against it");
            return;
        }

        Image baseline = Image.LoadFromFile(path);
        AssertThat(baseline.GetWidth() == rendered.GetWidth() && baseline.GetHeight() == rendered.GetHeight())
            .OverrideFailureMessage(
                $"{name}.png is {baseline.GetWidth()}x{baseline.GetHeight()} while this frame is "
                + $"{rendered.GetWidth()}x{rendered.GetHeight()}: the fixture or the view size changed, so the "
                + $"baseline has to be re-recorded (delete {path})")
            .IsTrue();

        (float ratio, float maxDelta) = CompareGolden(baseline, rendered);

        AssertThat(ratio <= GoldenMaxDifferingRatio && maxDelta <= GoldenChannelTolerance)
            .OverrideFailureMessage(
                $"{name} differs from its baseline: {ratio:P2} of the pixels differ beyond "
                + $"{GoldenChannelTolerance * 255f:F0}/255 (limit {GoldenMaxDifferingRatio:P2}), largest channel "
                + $"difference {maxDelta * 255f:F0}/255. Compare the frame with {path}; delete that file and "
                + "re-run the case to re-record it, then review the new image.")
            .IsTrue();
    }

    /// <summary>
    /// Share of the pixels that differ beyond <see cref="GoldenChannelTolerance"/>, and the largest channel
    /// difference seen - the two numbers the failure message reports, so a red case says how far off it is.
    /// </summary>
    /// <param name="baseline">The recorded image.</param>
    /// <param name="rendered">The freshly rendered image.</param>
    /// <returns>The differing pixel ratio and the largest channel delta.</returns>
    public static (float Ratio, float MaxDelta) CompareGolden(Image baseline, Image rendered)
    {
        int width = Math.Min(baseline.GetWidth(), rendered.GetWidth());
        int height = Math.Min(baseline.GetHeight(), rendered.GetHeight());
        int differing = 0;
        float maxDelta = 0f;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color a = baseline.GetPixel(x, y);
                Color b = rendered.GetPixel(x, y);

                float delta = MathF.Max(
                    MathF.Max(MathF.Abs(a.R - b.R), MathF.Abs(a.G - b.G)),
                    MathF.Max(MathF.Abs(a.B - b.B), MathF.Abs(a.A - b.A)));

                if (delta > maxDelta)
                    maxDelta = delta;

                if (delta > GoldenChannelTolerance)
                    differing++;
            }
        }

        return ((float)differing / (width * height), maxDelta);
    }

    /// <summary>Write one PNG per kind when the dump switch is on; returns the problem text, if any.</summary>
    public static string DumpPng(string directory, string label, Image image)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, label + ".png");
            // SavePng takes a Godot path: an absolute OS path is accepted with forward slashes only.
            image.SavePng(path.Replace('\\', '/'));
            return File.Exists(path) ? "" : $"{label}: {path} was not written";
        }
        catch (Exception ex)
        {
            return $"{label}: dump failed ({ex.GetType().Name}: {ex.Message})";
        }
    }
}
