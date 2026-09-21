namespace GodotNodeExtension.Tests.GodotSkia;

using System;
using System.Collections.Generic;
using System.Reflection;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotSkia;
using GodotNodeExtension.Example.GodotSkia;
using GodotNodeExtension.Tests.Support;   // EngineMessageLog (the engine's error/warning stream capture)
using SkiaSharp;
using static GdUnit4.Assertions;

/// <summary>
/// The GodotSkia example pages as test assets: they are the component's public face (they are what the demo
/// browser shows and what a reader copies), and nothing checked them before.
/// <para>
/// The wiring cases run with and without a rendering device - the pages report a missing device in their own
/// caption instead of failing, which is itself part of what is checked here. The two pixel cases at the end need
/// a device to have anything to read and say so when there is none, like the canvas integration suite.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SkiaExampleScenesIntegrationTest
{
    private const string Directory = "res://Example/GodotSkia";

    /// <summary>Example files in the component's example directory that end in <paramref name="extension"/>.</summary>
    private static List<string> FilesEndingWith(string extension)
    {
        var files = new List<string>();
        using var dir = DirAccess.Open(Directory);
        if (dir is null) return files;

        foreach (string file in dir.GetFiles())
        {
            if (file.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) files.Add(file);
        }
        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>Every descendant of <paramref name="node"/>, including internal children, depth first.</summary>
    private static List<Node> Descendants(Node node)
    {
        var result = new List<Node>();
        foreach (var child in node.GetChildren(includeInternal: true))
        {
            result.Add(child);
            result.AddRange(Descendants(child));
        }
        return result;
    }

    /// <summary>True when the run has a rendering device; otherwise a pixel case skips itself.</summary>
    private static bool HasDevice(string caseName)
    {
        if (RenderingServer.GetRenderingDevice() is not null) return true;
        GD.Print($"[skip] {caseName}: no rendering device in this run");
        return false;
    }

    /// <summary>Run one frame of every script in the instance, the way the engine would.</summary>
    private static void PumpScene(Node instance, int frames)
    {
        var nodes = new List<Node> { instance };
        nodes.AddRange(Descendants(instance));

        for (int frame = 0; frame < frames; frame++)
            foreach (var node in nodes)
                if (node.IsInsideTree())
                    node._Process(0.016);
    }

    /// <summary>Exported properties of <paramref name="type"/> whose type is a node (the ones a scene wires).</summary>
    private static IEnumerable<PropertyInfo> ExportedNodeProperties(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<ExportAttribute>() is null) continue;
            if (!typeof(Node).IsAssignableFrom(property.PropertyType)) continue;
            yield return property;
        }
    }

    /// <summary>
    /// Every page instantiates, survives three frames of its own script, and has all of its exported node
    /// references wired - on a machine with a device it also builds its surfaces, which is what makes this
    /// case cover the new interop page's use of the converter API.
    /// </summary>
    [TestCase]
    public void EveryExampleSceneRunsItsFramesWithItsExportsWired()
    {
        var files = FilesEndingWith(".tscn");
        AssertThat(files.Count).IsGreater(0);

        var log = EngineMessageLog.Attach();
        var problems = new List<string>();
        var root = ((SceneTree)Engine.GetMainLoop()).Root;

        try
        {
            foreach (string file in files)
            {
                var scene = GD.Load<PackedScene>($"{Directory}/{file}");
                if (scene is null)
                {
                    problems.Add($"{file}: could not be loaded");
                    continue;
                }

                Node? instance = null;
                try
                {
                    instance = scene.Instantiate();
                    root.AddChild(instance);
                    PumpScene(instance, 3);

                    foreach (var node in Descendants(instance))
                    {
                        foreach (var property in ExportedNodeProperties(node.GetType()))
                        {
                            if (property.GetValue(node) is null)
                                problems.Add($"{file}: {node.GetType().Name}.{property.Name} is not wired");
                        }
                    }
                }
                catch (Exception ex)
                {
                    problems.Add($"{file}: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    if (instance is not null)
                    {
                        instance.GetParent()?.RemoveChild(instance);
                        instance.Free();
                    }
                }
            }

            var errors = log.ErrorsContaining("");
            if (errors.Length > 0)
            {
                var first = new List<string>();
                for (int i = 0; i < errors.Length && i < 5; i++) first.Add(errors[i]);
                problems.Add($"{errors.Length} engine error(s) while running the pages: {string.Join(" | ", first)}");
            }
        }
        finally
        {
            log.Detach();
        }

        GD.Print($"GodotSkia examples: {files.Count} page(s) instantiated and pumped");
        AssertThat(string.Join("; ", problems)).IsEqual("");
    }

    /// <summary>
    /// The component's examples use <b>only</b> its own API. GodotSkia declares no component dependency, so an
    /// example that drew through another component's canvas abstraction would not build for somebody who
    /// installed GodotSkia on its own - and nothing in the compiler would notice, because the whole repository
    /// is one assembly.
    /// </summary>
    [TestCase]
    public void TheExamplesUseNoOtherComponent()
    {
        var offenders = new List<string>();
        var files = FilesEndingWith(".cs");

        // Guard against a vacuous pass: without the scripts there is nothing to inspect.
        AssertThat(files.Count).IsGreater(0);

        foreach (string file in files)
        {
            using var handle = FileAccess.Open($"{Directory}/{file}", FileAccess.ModeFlags.Read);
            if (handle is null)
            {
                offenders.Add($"{file}: could not be read");
                continue;
            }

            string text = handle.GetAsText();
            if (text.Contains("GodotChart", StringComparison.Ordinal))
                offenders.Add($"{file}: references GodotChart");
        }

        AssertThat(string.Join("; ", offenders)).IsEqual("");
    }

    /// <summary>
    /// Every page of the directory is listed in <c>demos.json</c>, and every entry of <c>demos.json</c> exists:
    /// the browser uses the file for the title and the description, so a page that is missing from it shows up
    /// with a placeholder, and a stale entry points at a scene that is gone.
    /// </summary>
    [TestCase]
    public void DemosJsonListsEveryExamplePage()
    {
        using var handle = FileAccess.Open($"{Directory}/demos.json", FileAccess.ModeFlags.Read);
        AssertThat(handle is not null).IsTrue();
        string text = handle!.GetAsText();

        var json = new Json();
        Error parse = json.Parse(text);
        AssertThat(parse == Error.Ok).IsTrue();

        var listed = new List<string>();
        if (json.Data.VariantType == Variant.Type.Dictionary
            && json.Data.AsGodotDictionary()["demos"].VariantType == Variant.Type.Array)
        {
            foreach (Variant entry in json.Data.AsGodotDictionary()["demos"].AsGodotArray())
            {
                if (entry.VariantType != Variant.Type.Dictionary) continue;
                string scene = entry.AsGodotDictionary()["scene"].AsString();
                if (scene.Length > 0) listed.Add(scene);
            }
        }

        var problems = new List<string>();
        foreach (string file in FilesEndingWith(".tscn"))
            if (!listed.Contains(file)) problems.Add($"{file} is not listed in demos.json");
        foreach (string scene in listed)
            if (!FileAccess.FileExists($"{Directory}/{scene}")) problems.Add($"demos.json lists the missing {scene}");

        AssertThat(string.Join("; ", problems)).IsEqual("");
    }

    // ── Pixel assertions ─────────────────────────────────────────────────────
    // "The engine error log is empty" says nothing about what a page drew, and these pages exist for what they
    // draw: the cases below read the surfaces the pages present (and the probe the interop page paints) and check
    // the colours the pages put there.

    /// <summary>
    /// Tolerance of every pixel comparison below, in colour-channel units: the surfaces are read back through an
    /// 8 bit channel and rasterised either on the GPU or by the CPU fallback, so a channel can land one step off
    /// the value the page drew. The chart harness uses the same 2/255 for the same reason.
    /// </summary>
    private const float ChannelTolerance = 2f / 255f;

    /// <summary>The clear colour the interop page's converter panel paints (its own <c>DrawConverterPanel</c>).</summary>
    private static readonly Color ConverterPanelBackground = new(16f / 255f, 19f / 255f, 28f / 255f);

    /// <summary>Whether two colours are the same within <see cref="ChannelTolerance"/>.</summary>
    private static bool IsClose(Color actual, Color expected)
        => Math.Abs(actual.R - expected.R) <= ChannelTolerance
           && Math.Abs(actual.G - expected.G) <= ChannelTolerance
           && Math.Abs(actual.B - expected.B) <= ChannelTolerance
           && Math.Abs(actual.A - expected.A) <= ChannelTolerance;

    /// <summary>A colour written as the eight bit values a reader can compare with the page.</summary>
    private static string Describe(Color colour)
        => $"({colour.R * 255f:F0}, {colour.G * 255f:F0}, {colour.B * 255f:F0}, {colour.A * 255f:F0})";

    /// <summary>Record a pixel that is not the colour the page draws there.</summary>
    private static void ExpectPixel(Image image, int x, int y, Color expected, string what, List<string> problems)
    {
        Color actual = image.GetPixel(x, y);
        if (!IsClose(actual, expected))
            problems.Add($"{what}: ({x}, {y}) is {Describe(actual)} instead of {Describe(expected)}");
    }

    /// <summary>Pixels of a rectangle that are not <paramref name="background"/> - what "ink" means here.</summary>
    private static int InkIn(Image image, Color background, int left, int top, int right, int bottom)
    {
        int ink = 0;
        for (int y = Math.Max(0, top); y < Math.Min(image.GetHeight(), bottom); y++)
        {
            for (int x = Math.Max(0, left); x < Math.Min(image.GetWidth(), right); x++)
            {
                if (!IsClose(image.GetPixel(x, y), background)) ink++;
            }
        }
        return ink;
    }

    /// <summary>
    /// Record a rectangle with fewer ink pixels than the page's own drawing needs, and answer how many it has.
    /// </summary>
    private static int ExpectInk(Image image, Color background, int left, int top, int right, int bottom,
        int wanted, string what, List<string> problems)
    {
        int ink = InkIn(image, background, left, top, right, bottom);
        if (ink < wanted)
        {
            problems.Add($"{what} ({left}, {top})-({right}, {bottom}): {ink} pixel(s) differ from " +
                         $"{Describe(background)}, wanted at least {wanted}");
        }
        return ink;
    }

    /// <summary>Record a rectangle that has anything in it at all, where the page draws nothing.</summary>
    private static void ExpectNoInk(Image image, Color background, int left, int top, int right, int bottom,
        string what, List<string> problems)
    {
        int ink = InkIn(image, background, left, top, right, bottom);
        if (ink > 0)
        {
            problems.Add($"{what} ({left}, {top})-({right}, {bottom}): {ink} pixel(s) differ from " +
                         $"{Describe(background)} where the page draws nothing");
        }
    }

    /// <summary>
    /// The pixels of the Skia surface <paramref name="presenter"/> displays, or null - with the reason added to
    /// <paramref name="problems"/> - when it displays something else: a page that never built its surface shows up
    /// as the missing texture instead of as an empty picture.
    /// </summary>
    private static Image? SurfacePixels(TextureRect presenter, string page, List<string> problems)
    {
        if (presenter.Texture is not SkiaCanvasTexture2D surface)
        {
            problems.Add($"{page}: {presenter.Name} presents " +
                         (presenter.Texture is null ? "nothing" : presenter.Texture.GetType().Name) +
                         " instead of a Skia surface");
            return null;
        }

        var image = surface.GetImage();
        if (image.GetWidth() > 0 && image.GetHeight() > 0) return image;

        image.Dispose();
        problems.Add($"{page}: {presenter.Name} presents a surface with no pixels");
        return null;
    }

    /// <summary>
    /// The demo page's own pixels, in two of its four modes: the shapes mode (the page's default) has to put the
    /// colour the scene's sliders ask for into the interiors of its filled shapes on a white surface, and the text
    /// mode has to put glyphs into the band its first line of text occupies. Both are the page's documented output,
    /// so a mode that quietly stops drawing - a lost upload, a clear that no longer happens, a font that resolves
    /// to nothing - fails here instead of on whoever opens the page next.
    /// </summary>
    [TestCase]
    public void TheDemoPagePaintsItsShapesAndItsTextIntoTheSurfaceItPresents()
    {
        if (!HasDevice(nameof(TheDemoPagePaintsItsShapesAndItsTextIntoTheSurfaceItPresents))) return;

        var problems = new List<string>();
        var log = EngineMessageLog.Attach();
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        Node? instance = null;

        try
        {
            var scene = GD.Load<PackedScene>($"{Directory}/GodotSkiaDemo.tscn");
            AssertThat(scene is not null).IsTrue();
            instance = scene!.Instantiate();
            root.AddChild(instance);
            PumpScene(instance, 3);     // _Ready draws the shapes mode; the first _Process uploads it

            if (instance is not GodotSkiaDemo page)
            {
                problems.Add($"GodotSkiaDemo.tscn: instantiated {instance.GetType().Name}");
            }
            else
            {
                // What the page draws with: the scene's sliders are hue 0, saturation 100%, lightness 50%.
                var drawn = new Color(1f, 0f, 0f);

                using (var shapes = SurfacePixels(page.TextureRect, "GodotSkiaDemo.tscn (shapes mode)", problems))
                {
                    if (shapes is not null)
                    {
                        int width = shapes.GetWidth();
                        int height = shapes.GetHeight();

                        // The mode clears the surface and then draws its picture at absolute canvas coordinates;
                        // these points are the interiors of its filled rectangle, circle and rounded rectangle
                        // (its own DrawGeometricShapes), so the colour the sliders ask for has to be there.
                        ExpectPixel(shapes, 100, 90, drawn, "demo page, shapes mode: filled rectangle", problems);
                        ExpectPixel(shapes, 250, 100, drawn, "demo page, shapes mode: filled circle", problems);
                        ExpectPixel(shapes, 100, 250, drawn, "demo page, shapes mode: filled rounded rectangle",
                            problems);

                        // The rest of the surface is the clear colour: nothing is drawn above the first shape
                        // (which starts at y = 50), and x = 30 is left of every shape.
                        ExpectPixel(shapes, 30, height - 30, Colors.White, "demo page, shapes mode: background",
                            problems);
                        ExpectNoInk(shapes, Colors.White, 0, 0, width, 46, "demo page, shapes mode: above the picture",
                            problems);

                        int ink = ExpectInk(shapes, Colors.White, 0, 0, width, height, 8000,
                            "demo page, shapes mode: the picture", problems);
                        GD.Print($"GodotSkia examples: the demo page's shapes mode drew {ink} non-white pixel(s) " +
                                 $"into its {width}x{height} surface");
                    }
                }

                // The text mode, switched the way the engine switches it: the option button reports the item
                // index, the page reads that item's id (2 is "Text Effects" in GodotSkiaDemo.tscn).
                int index = page.DrawModeOption.GetItemIndex(2);
                AssertThat(index).IsGreater(-1);
                page.DrawModeOption.Select(index);
                page.DrawModeOption.EmitSignal(OptionButton.SignalName.ItemSelected, index);
                PumpScene(instance, 2);

                using (var text = SurfacePixels(page.TextureRect, "GodotSkiaDemo.tscn (text mode)", problems))
                {
                    if (text is not null)
                    {
                        int width = text.GetWidth();
                        int height = text.GetHeight();

                        // "Hello Skia!" is drawn at (50, 100) with a 24 px font (its own DrawTextEffects), so this
                        // band holds that line and nothing else of the mode: glyphs have to be in it.
                        int glyphs = ExpectInk(text, Colors.White, 40, 70, 200, 106, 150,
                            "demo page, text mode: the first line of text", problems);

                        // ... and the picture of the shapes mode is gone with the mode switch - the text mode
                        // clears the surface first, and nothing of its own drawing reaches these points (the
                        // filled circle and the rounded rectangle of the shapes mode were there).
                        ExpectPixel(text, 250, 100, Colors.White, "demo page, text mode: the cleared circle",
                            problems);
                        ExpectPixel(text, 100, 250, Colors.White, "demo page, text mode: the cleared rectangle",
                            problems);
                        ExpectPixel(text, 30, height - 30, Colors.White, "demo page, text mode: background",
                            problems);
                        ExpectNoInk(text, Colors.White, 0, 0, width, 46, "demo page, text mode: above the text",
                            problems);

                        GD.Print($"GodotSkia examples: the demo page's text mode drew {glyphs} glyph pixel(s) " +
                                 $"into its {width}x{height} surface");
                    }
                }
            }

            problems.AddRange(EngineErrorProblems(log, "GodotSkiaDemo.tscn"));
        }
        finally
        {
            if (instance is not null)
            {
                instance.GetParent()?.RemoveChild(instance);
                instance.Free();
            }
            log.Detach();
        }

        AssertThat(string.Join("; ", problems)).IsEqual("");
    }

    /// <summary>
    /// The interop page's own pixels: the converter panel (its clear colour at the corners of the surface it
    /// presents, plus ink where its caption, its eight check rows and the transform probe go), the four pool
    /// surfaces (each with the clear colour its index asks for, which is what makes a mixed-up surface visible),
    /// and the converter probe those checks convert. "The engine log is empty" cannot see any of it: a panel that
    /// is never uploaded reads back as a cleared surface with no ink, and one that keeps another surface's pixels
    /// still reads back as a picture.
    /// </summary>
    [TestCase]
    public void TheInteropPagePaintsItsPanelItsFourSurfacesAndItsProbe()
    {
        if (!HasDevice(nameof(TheInteropPagePaintsItsPanelItsFourSurfacesAndItsProbe))) return;

        var problems = new List<string>();
        var log = EngineMessageLog.Attach();
        var root = ((SceneTree)Engine.GetMainLoop()).Root;
        Node? instance = null;

        try
        {
            var scene = GD.Load<PackedScene>($"{Directory}/SkiaInteropDemo.tscn");
            AssertThat(scene is not null).IsTrue();
            instance = scene!.Instantiate();
            root.AddChild(instance);
            PumpScene(instance, 3);     // _Ready builds the five surfaces; _Process draws and uploads them

            if (instance is not SkiaInteropDemo page)
            {
                problems.Add($"SkiaInteropDemo.tscn: instantiated {instance.GetType().Name}");
            }
            else
            {
                CheckConverterPanel(page.ConverterSurface, problems);
                CheckPoolSurface(page.PoolA, 0, problems);
                CheckPoolSurface(page.PoolB, 1, problems);
                CheckPoolSurface(page.PoolC, 2, problems);
                CheckPoolSurface(page.PoolD, 3, problems);
                CheckConverterProbeRoundTrip(problems);
            }

            problems.AddRange(EngineErrorProblems(log, "SkiaInteropDemo.tscn"));
        }
        finally
        {
            if (instance is not null)
            {
                instance.GetParent()?.RemoveChild(instance);
                instance.Free();
            }
            log.Detach();
        }

        AssertThat(string.Join("; ", problems)).IsEqual("");
    }

    /// <summary>The engine errors pushed while <paramref name="page"/> ran, as failure reasons.</summary>
    private static List<string> EngineErrorProblems(EngineMessageLog log, string page)
    {
        var problems = new List<string>();
        foreach (string error in log.ErrorsContaining(""))
            problems.Add($"{page}: engine error while the page ran: {error}");
        return problems;
    }

    /// <summary>
    /// The converter panel: its own clear colour at the four corners of the surface it presents (the panel clears
    /// to it before drawing anything), and ink in the three blocks it draws - the caption, the check rows and the
    /// transform probe with its label. The bands are the page's own canvas coordinates.
    /// </summary>
    private static void CheckConverterPanel(TextureRect presenter, List<string> problems)
    {
        using var panel = SurfacePixels(presenter, "SkiaInteropDemo.tscn (converter panel)", problems);
        if (panel is null) return;

        int width = panel.GetWidth();
        int height = panel.GetHeight();

        // The panel clears its whole surface to this colour before drawing anything, and nothing it draws reaches
        // the corners: its text starts at x = 12, its right-aligned verdicts stop at width - 12, and the rotating
        // quad of the transform probe is centred at width - 60.
        ExpectPixel(panel, 4, 4, ConverterPanelBackground, "interop page: panel corner (top left)", problems);
        ExpectPixel(panel, width - 6, 4, ConverterPanelBackground, "interop page: panel corner (top right)",
            problems);
        ExpectPixel(panel, 4, height - 6, ConverterPanelBackground, "interop page: panel corner (bottom left)",
            problems);
        ExpectPixel(panel, width - 6, height - 6, ConverterPanelBackground, "interop page: panel corner (bottom right)",
            problems);

        // The three blocks the panel draws, in its own canvas coordinates: the caption (baseline y = 24), the eight
        // check rows (they start at y = 52 and step 27 down, so the last one ends above y = 250) and the transform
        // probe with its label under them.
        int caption = ExpectInk(panel, ConverterPanelBackground, 8, 8, width - 8, 32, 200,
            "interop page: the panel's caption", problems);
        int rows = ExpectInk(panel, ConverterPanelBackground, 8, 44, width - 8, 250, 2000,
            "interop page: the panel's check rows", problems);
        int probe = ExpectInk(panel, ConverterPanelBackground, 8, 252, width - 8, height, 400,
            "interop page: the transform probe", problems);

        GD.Print($"GodotSkia examples: the interop panel {width}x{height} drew {caption} caption, {rows} check-row " +
                 $"and {probe} probe pixel(s)");
    }

    /// <summary>
    /// The clear colour the interop page's pool panel <paramref name="index"/> paints, index by index
    /// (its own <c>DrawPoolPanel</c>): the four panels deliberately differ, so a surface that shows another
    /// panel's picture is visible in the pixels.
    /// </summary>
    private static Color PoolBackground(int index)
        => new((18 + index * 6) / 255f, (22 + index * 4) / 255f, (34 + index * 8) / 255f);

    /// <summary>
    /// One of the four small surfaces: its own clear colour in a corner that no shape of its pattern reaches, plus
    /// ink where that pattern is drawn. The fourth one also has a white dot at its centre (its rotating triangle is
    /// drawn with a dot on top), which no other panel has.
    /// </summary>
    private static void CheckPoolSurface(TextureRect presenter, int index, List<string> problems)
    {
        string page = $"SkiaInteropDemo.tscn (pool panel {index})";
        using var surface = SurfacePixels(presenter, page, problems);
        if (surface is null) return;

        int width = surface.GetWidth();
        int height = surface.GetHeight();
        Color background = PoolBackground(index);

        // The corner is clear of every pattern the page draws (they stay inside x 12..138, y 20..94, and the rings
        // reach at most 54 px from the centre), so it holds the panel's own clear colour: the four panels differ
        // by index, which is what makes a surface showing another panel's picture visible in the pixels.
        ExpectPixel(surface, 3, 3, background, $"{page}: clear colour", problems);
        ExpectPixel(surface, width - 4, 3, background, $"{page}: clear colour", problems);

        int ink = ExpectInk(surface, background, 0, 0, width, height, 400, $"{page}: its own pattern", problems);

        // The fourth panel draws its rotating triangle with a white dot on top of it: pure white, at a point its
        // pattern does not move away from while the page animates.
        if (index == 3)
            ExpectPixel(surface, width / 2, height / 2, Colors.White, $"{page}: centre dot", problems);

        GD.Print($"GodotSkia examples: {page} {width}x{height} drew {ink} pixel(s) of its own pattern");
    }

    /// <summary>
    /// The converter probe the interop page paints (its quadrants are the converter's own colour constants) comes
    /// back with the same pixels from the documented <c>Texture2D -> SKImage -> Image</c> round trip. The probe is
    /// a private fixture of the page rather than part of the surface it presents - and it has to be reached that
    /// way, because the page only blits it when there is room for the 64 px picture: on its own 430x330 panel the
    /// row cursor ends at y = 268, so the blit (which needs 92 px below the rows) is skipped and the four colours
    /// are nowhere in the panel's pixels. Repainting the probe here would only test this file.
    /// </summary>
    private static void CheckConverterProbeRoundTrip(List<string> problems)
    {
        MethodInfo? build = typeof(SkiaInteropDemo).GetMethod("BuildProbeTexture",
            BindingFlags.NonPublic | BindingFlags.Static);
        AssertThat(build is not null).IsTrue();

        using var probe = (SkiaCanvasTexture2D)build!.Invoke(null, null)!;

        // The probe is 24x24 with 12x12 quadrants, and each of these points is the centre of its own quadrant, so
        // no comparison sits on a boundary. The colours are the converter's constants - the page paints the probe
        // with them instead of with literals.
        var quadrants = new (string Name, SKColor Colour, int X, int Y)[]
        {
            ("red (top left)", SkiaGodotConverter.Colors.Red, 6, 6),
            ("green (top right)", SkiaGodotConverter.Colors.Green, 18, 6),
            ("blue (bottom left)", SkiaGodotConverter.Colors.Blue, 6, 18),
            ("yellow (bottom right)", SkiaGodotConverter.Colors.Yellow, 18, 18),
        };

        using var painted = probe.GetImage();
        using var skImage = ((Texture2D)probe).ToSkImage();
        using var roundTripped = skImage.ToGodotImage();

        foreach (var (name, colour, x, y) in quadrants)
        {
            Color expected = colour.ToGodotColor();
            ExpectPixel(painted, x, y, expected, $"interop page's converter probe: {name} quadrant", problems);
            ExpectPixel(roundTripped, x, y, expected,
                $"converter probe after Texture2D -> SKImage -> Image: {name} quadrant", problems);
        }

        GD.Print($"GodotSkia examples: the converter probe's {quadrants.Length} quadrant(s) survived the " +
                 "Texture2D -> SKImage -> Image round trip");
    }
}
