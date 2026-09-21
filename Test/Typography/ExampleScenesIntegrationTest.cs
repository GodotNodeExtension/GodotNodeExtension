namespace GodotNodeExtension.Tests.Typography;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Example.Typography;
using static GdUnit4.Assertions;

/// <summary>
/// Every example scene of this component, opened on a rendering device: the sample is laid out, drawn, and the
/// picture is checked for ink.
/// <para>
/// The scenes are the component's own explanation of itself, so a scene that loads but draws nothing - a sample
/// that no longer parses, a writing mode whose geometry the canvas cannot draw, an annotation that ends up off the
/// image - has to fail here rather than be noticed by whoever opens the browser next. The check is deliberately
/// coarse: it asks whether the canvas applied a layout and whether pixels changed, not what they look like, because
/// the shape of each language's result is what the golden layouts pin down.
/// </para>
/// <para>
/// The scenes are walked in one case rather than one case each: a case gets a fresh test context, and the first
/// canvas of a fresh context is not laid out yet when the first frame is drawn - the resource cache, the theme and
/// the font stack are all still cold. One case with a warm-up run removes that difference between the first scene
/// and the rest, which is the only thing the per-case split was measuring.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ExampleScenesIntegrationTest
{
    /// <summary>Directory the scenes live in.</summary>
    private const string SceneDirectory = "res://Example/Typography";

    /// <summary>Every scene name (without the extension), in the order the group lists them.</summary>
    private static readonly string[] Scenes =
    [
        "ChineseSimplifiedDemo",
        "ChineseTraditionalDemo",
        "JapaneseDemo",
        "KoreanDemo",
        "EnglishDemo",
        "RightToLeftDemo",
        "VerticalDemo",
        "MixedLanguagesDemo",
        "LineBreakingStressDemo",
        "EverythingDemo",
    ];

    /// <summary>Size of the viewport a scene is rendered into.</summary>
    private static readonly Vector2I ViewSize = new(960, 640);

    /// <summary>Frames a scene is given to lay its sample out.</summary>
    private const int LayoutFrameBudget = 180;

    /// <summary>Frames a drawn scene is given to put ink on the canvas.</summary>
    private const int DrawFrameBudget = 60;

    /// <summary>
    /// Window a scene is opened in when each example block's own range is checked. It is grown to the sample: a block
    /// below the fold would report no ink for a reason that has nothing to do with drawing.
    /// </summary>
    private static readonly Vector2I BlockCheckViewSize = new(1400, 900);

    /// <summary>
    /// Room the page adds around the canvas when a block is checked: the page's two margins, the controls row under
    /// the sample and the scroll bar.
    /// </summary>
    private static readonly Vector2I PageChrome = new(160, 280);

    /// <summary>Margin the canvas draws its sample with, in pixels.</summary>
    private const float CanvasMargin = 16f;

    /// <summary>
    /// Pixels of ink a drawn sample has to reach to count as drawn. Every sample fills a good part of the canvas
    /// with glyphs, so this is far below what a working scene produces and far above a blank one.
    /// </summary>
    private const int InkWanted = 2000;

    /// <summary>
    /// Every sample says which block each of the elements it builds came from, and every sample demonstrates
    /// something. The canvas outlines the example blocks, and the layout reports source indices rather than block
    /// kinds - so this mapping is the only thing that makes the outline possible, and a sample whose blocks stopped
    /// matching its elements would draw the boxes in the wrong places instead of failing.
    /// </summary>
    [TestCase]
    public void EverySampleMapsItsElementsToBlockKinds()
    {
        foreach (TypographySamples.SampleId sample in System.Enum.GetValues<TypographySamples.SampleId>())
        {
            IReadOnlyList<TypographySamples.BlockKind?> kinds = TypographySamples.BuildKinds(sample);
            List<DrawElement> elements = TypographySamples.Build(
                sample, ThemeDB.FallbackFont, 26, Colors.White);

            AssertThat(kinds.Count).OverrideFailureMessage(
                $"{sample}: {kinds.Count} kind(s) for {elements.Count} element(s)").IsEqual(elements.Count);

            foreach (TypographySamples.BlockKind kind in new[]
                     { TypographySamples.BlockKind.Heading, TypographySamples.BlockKind.Clause,
                       TypographySamples.BlockKind.Example })
            {
                AssertThat(kinds.Count(entry => entry == kind)).OverrideFailureMessage(
                    $"{sample}: the sample has no {kind} block").IsGreater(0);
            }

            for (int index = 0; index < elements.Count; index++)
            {
                if (elements[index].IsParagraphBreak)
                {
                    AssertThat(kinds[index] is null).OverrideFailureMessage(
                        $"{sample}: element {index} is a paragraph break but claims block kind {kinds[index]}")
                        .IsTrue();
                }
                else
                {
                    AssertThat(kinds[index] is not null).OverrideFailureMessage(
                        $"{sample}: element {index} carries no block kind").IsTrue();
                }
            }
        }
    }

    /// <summary>Each scene lays its sample out and draws something.</summary>
    [TestCase]
    public async Task EveryExampleSceneDrawsItsSample()
    {
        if (NoRenderingDevice(nameof(EveryExampleSceneDrawsItsSample))) return;
        if (!HasSceneTree()) return;

        var tree = (SceneTree)Engine.GetMainLoop();
        var failures = new System.Collections.Generic.List<string>();

        // Warm-up: the first scene in a fresh context is loaded, laid out and drawn once and then thrown away, so
        // the scenes that are checked all meet a warmed resource cache and a warmed font stack.
        await RunScene(tree, Scenes[0], warmUp: true, failures);

        foreach (string scene in Scenes)
            await RunScene(tree, scene, warmUp: false, failures);

        // The reasons only make sense when there are any: an assertion carrying an empty override message is refused.
        if (failures.Count > 0)
            AssertThat(false).OverrideFailureMessage(string.Join("\n", failures)).IsTrue();
    }

    /// <summary>Open one scene, wait for its layout and its drawing, and record what went wrong.</summary>
    /// <param name="tree">Scene tree the case runs in.</param>
    /// <param name="sceneName">Scene to open, without its extension.</param>
    /// <param name="warmUp">Whether this is the warm-up run, whose result is not checked.</param>
    /// <param name="failures">List the reason is added to when the scene fails.</param>
    /// <returns>A task that completes when the scene has been opened, drawn and closed.</returns>
    private static async Task RunScene(SceneTree tree, string sceneName, bool warmUp,
        System.Collections.Generic.List<string> failures)
    {
        var viewport = new SubViewport
        {
            Size = ViewSize,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
        };

        tree.Root.AddChild(viewport);

        try
        {
            var packed = ResourceLoader.Load<PackedScene>($"{SceneDirectory}/{sceneName}.tscn");

            if (packed is null)
            {
                if (!warmUp)
                    failures.Add($"{sceneName}: not a scene any more");

                return;
            }

            Node scene = packed.Instantiate();

            // A scene is laid out for a viewport when the browser opens it; here it is parented to a SubViewport,
            // which is not a Control, so the root is given the size the render target has and the nodes anchored
            // inside it resolve against it.
            if (scene is Control root)
                root.Size = ViewSize;

            viewport.AddChild(scene);

            TypographySceneCanvas? canvas = viewport
                .FindChildren("*", nameof(Control), true, false)
                .OfType<TypographySceneCanvas>()
                .FirstOrDefault();

            if (canvas is null)
            {
                if (!warmUp)
                    failures.Add($"{sceneName}: the scene has no typography canvas to lay anything out in");

                return;
            }

            // The scene, not the canvas, is what wires the two optional controls and the status line: they are
            // exported node references, so a scene that lost them renders the sample but tells the reader nothing,
            // and a ScrollContainer is what makes the part below the window reachable at all. Both are asserted
            // here because neither shows up as a rendering failure.
            if (!warmUp)
            {
                if (canvas.StatusLabel is null || canvas.FontSizeControl is null || canvas.OverlayToggle is null)
                {
                    failures.Add($"{sceneName}: the canvas' exported references are not wired: "
                        + $"status={canvas.StatusLabel is not null}, size={canvas.FontSizeControl is not null}, "
                        + $"overlay={canvas.OverlayToggle is not null}");
                }

                if (FindScrollContainer(canvas) is null)
                    failures.Add($"{sceneName}: the canvas is not inside a ScrollContainer, so a long sample is cut off");

                // The canvas is drawn into the area the scene gives it and grows only along the axis its lines
                // advance on: asking for the content size on *both* axes makes the layout depend on the measure it
                // produced, which is what a scene without a declared measure used to ratchet on. The axis the
                // measure is taken from must therefore ask for nothing.
                Vector2 minimum = canvas.GetCombinedMinimumSize();
                bool horizontal = canvas.WritingMode == WritingMode.HorizontalTb;
                float contentAcrossLines = horizontal ? canvas.ContentSize.Y : canvas.ContentSize.X;
                float askedAcrossLines = horizontal ? minimum.Y : minimum.X;
                float askedAlongLines = horizontal ? minimum.X : minimum.Y;

                if (askedAlongLines > 0.01f)
                {
                    failures.Add($"{sceneName}: the canvas asks for {askedAlongLines:F1}px along the measure, so the "
                        + "measure depends on the layout that used it");
                }

                if (askedAcrossLines < contentAcrossLines - 0.01f)
                {
                    failures.Add($"{sceneName}: the canvas asks for {askedAcrossLines:F1}px across its lines but the "
                        + $"sample covers {contentAcrossLines:F1}px, so part of it is unreachable");
                }
            }

            if (warmUp)
            {
                for (int frame = 0; frame < LayoutFrameBudget && !canvas.HasContent; frame++)
                    await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

                return;
            }

            for (int frame = 0; frame < LayoutFrameBudget && !canvas.HasContent; frame++)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            if (!canvas.HasContent)
            {
                failures.Add($"{sceneName}: no layout was applied within {LayoutFrameBudget} frames");
                return;
            }

            if (canvas.ElementCount <= 0)
            {
                failures.Add($"{sceneName}: the layout came back empty");
                return;
            }

            int ink = 0;

            for (int frame = 0; frame < DrawFrameBudget && ink <= InkWanted; frame++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

                if (viewport.GetTexture().GetImage() is { } image)
                    ink = CountInk(image);
            }

            if (ink <= InkWanted)
            {
                failures.Add(
                    $"{sceneName} ({canvas.Sample}): the canvas applied {canvas.ElementCount} elements but drew "
                    + $"{ink} pixels of ink");
            }
        }
        finally
        {
            viewport.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    /// <summary>
    /// Every example block of every sample puts ink on the picture.
    /// <para>
    /// The case above asks whether a scene drew anything at all, which a sample that lost one of its examples - a
    /// reading that no longer shapes, an annotation whose geometry moved off the image, a block that stopped being
    /// drawn - would still pass: the prose of a sample is tens of thousands of pixels on its own. So every example
    /// block - the blocks a sample is a walkthrough of, and the ones the canvas outlines - is laid out again here and
    /// its own range is checked for ink, which is the coarsest claim that cannot be true of a block nothing was drawn
    /// for.
    /// </para>
    /// <para>
    /// A block is checked in the window its scene was laid out for, so the scene is first opened small and then grown
    /// until the whole sample fits. The request the canvas made is reproduced from the values the canvas publishes
    /// (its area, the measure and column length its scene declares, its language and its spacing); the two layouts are
    /// compared by content size before any rectangle is used, so a canvas whose request changes fails this case
    /// instead of quietly checking ranges the picture does not have.
    /// </para>
    /// </summary>
    [TestCase]
    public async Task EveryExampleBlockPutsInkOnThePicture()
    {
        if (NoRenderingDevice(nameof(EveryExampleBlockPutsInkOnThePicture))) return;
        if (!HasSceneTree()) return;

        var tree = (SceneTree)Engine.GetMainLoop();
        var failures = new List<string>();

        foreach (string scene in Scenes)
            await CheckExampleBlocks(tree, scene, failures);

        if (failures.Count > 0)
            AssertThat(false).OverrideFailureMessage(string.Join("\n", failures)).IsTrue();
    }

    /// <summary>Open one scene, lay its sample out again and check every example block's own range for ink.</summary>
    /// <param name="tree">Scene tree the case runs in.</param>
    /// <param name="sceneName">Scene to open, without its extension.</param>
    /// <param name="failures">List the reason is added to when a block has no ink.</param>
    /// <returns>A task that completes when the scene has been opened, checked and closed.</returns>
    private static async Task CheckExampleBlocks(SceneTree tree, string sceneName, List<string> failures)
    {
        var viewport = new SubViewport
        {
            Size = BlockCheckViewSize,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
        };

        tree.Root.AddChild(viewport);

        try
        {
            var packed = ResourceLoader.Load<PackedScene>($"{SceneDirectory}/{sceneName}.tscn");

            if (packed is null)
            {
                failures.Add($"{sceneName}: not a scene any more");
                return;
            }

            Node scene = packed.Instantiate();

            if (scene is not Control root)
            {
                failures.Add($"{sceneName}: the scene's root is not a control");
                return;
            }

            root.Size = viewport.Size;
            viewport.AddChild(scene);

            TypographySceneCanvas? canvas = viewport
                .FindChildren("*", nameof(Control), true, false)
                .OfType<TypographySceneCanvas>()
                .FirstOrDefault();

            if (canvas is null)
            {
                failures.Add($"{sceneName}: the scene has no typography canvas to lay anything out in");
                return;
            }

            if (!await WaitForLayout(tree, canvas))
            {
                failures.Add($"{sceneName}: no layout was applied within {LayoutFrameBudget} frames");
                return;
            }

            var wanted = new Vector2I(
                Mathf.Max(BlockCheckViewSize.X, Mathf.CeilToInt(canvas.ContentSize.X) + PageChrome.X),
                Mathf.Max(BlockCheckViewSize.Y, Mathf.CeilToInt(canvas.ContentSize.Y) + PageChrome.Y));

            if (wanted != viewport.Size)
            {
                viewport.Size = wanted;
                root.Size = wanted;

                if (!await WaitForLayout(tree, canvas))
                {
                    failures.Add($"{sceneName}: no layout after growing the window to {wanted}");
                    return;
                }
            }

            for (int frame = 0; frame < DrawFrameBudget; frame++)
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            if (viewport.GetTexture().GetImage() is not { } picture)
            {
                failures.Add($"{sceneName}: the window drew no picture to check");
                return;
            }

            Font font = canvas.Font ?? ThemeDB.FallbackFont;
            List<DrawElement> elements = TypographySamples.Build(canvas.Sample, font, canvas.FontSize, canvas.TextColor);
            IReadOnlyList<TypographySamples.BlockKind?> kinds = TypographySamples.BuildKinds(canvas.Sample);

            float measure = canvas.Measure > 0f
                ? canvas.Measure
                : Mathf.Max(64f, canvas.Size.X - (CanvasMargin * 2f));

            float columnLength = canvas.ColumnHeight > 0f
                ? canvas.ColumnHeight
                : Mathf.Max(64f, canvas.Size.Y - (CanvasMargin * 2f));

            using var engine = new TypographyEngine(new TypographySettings
            {
                LanguageTag = canvas.LanguageTag.Length > 0 ? canvas.LanguageTag : null,
                WritingMode = canvas.WritingMode,
                MaxWidth = measure,
                MaxHeight = canvas.WritingMode == WritingMode.HorizontalTb ? 0f : columnLength,
                LineSpacing = canvas.LineSpacing,
            });

            DrawElement[] source = [.. elements];
            engine.PrepareAndLayout(source, out Vector2 contentSize);
            List<LayoutElement> laidOut = engine.GetLayoutElements(source);

            if ((contentSize - canvas.ContentSize).Length() > 0.5f)
            {
                failures.Add($"{sceneName}: the canvas laid its sample out for {canvas.ContentSize} while the same "
                    + $"request produces {contentSize}, so this case cannot know where a block's range is");
                return;
            }

            // Where the canvas puts the content inside the picture: the content's own origin on the canvas, carried
            // through the canvas' transform - which is what accounts for the page's margins, for the controls above
            // the sample and for anything a scroll container has moved.
            bool vertical = canvas.WritingMode != WritingMode.HorizontalTb;

            float left = vertical
                ? Mathf.Max(CanvasMargin, canvas.Size.X - CanvasMargin - contentSize.X)
                : Mathf.Max(CanvasMargin, (canvas.Size.X - contentSize.X) * 0.5f);

            Vector2 origin = canvas.GetGlobalTransformWithCanvas() * new Vector2(Mathf.Floor(left), CanvasMargin);
            var pictureBox = new Rect2(Vector2.Zero, picture.GetSize());
            int checkedBlocks = 0;

            for (int index = 0; index < kinds.Count; index++)
            {
                if (kinds[index] != TypographySamples.BlockKind.Example)
                    continue;

                if (!BlockRangeOf(laidOut, index, out Rect2 block))
                {
                    failures.Add($"{sceneName}: the example block '{Snippet(source[index].Text)}' laid out no element");
                    continue;
                }

                var rect = new Rect2(origin + block.Position, block.Size).Grow(-2f);

                if (!pictureBox.Intersects(rect, true))
                {
                    failures.Add($"{sceneName}: the example block '{Snippet(source[index].Text)}' was laid out at "
                        + $"{rect} in a picture of {pictureBox.Size}, so it is not in the picture at all");
                    continue;
                }

                if (CountInkIn(picture, rect) == 0)
                {
                    failures.Add($"{sceneName}: the example block '{Snippet(source[index].Text)}' put no ink on the "
                        + $"picture inside its own range {rect}, so it was laid out but not drawn");
                    continue;
                }

                checkedBlocks++;
            }

            if (checkedBlocks == 0)
                failures.Add($"{sceneName}: no example block of this sample was checked");
        }
        finally
        {
            viewport.QueueFree();
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    /// <summary>Range the elements of one source element were laid out into, in content coordinates.</summary>
    /// <param name="elements">Every laid-out element of the sample.</param>
    /// <param name="sourceIndex">Source element the block came from.</param>
    /// <param name="box">Output: the range, empty when the block has no element.</param>
    /// <returns>False when no element came from that block.</returns>
    private static bool BlockRangeOf(List<LayoutElement> elements, int sourceIndex, out Rect2 box)
    {
        bool started = false;
        box = default;

        foreach (LayoutElement element in elements)
        {
            if (element.SourceIndex != sourceIndex || element.Type != DrawElement.ElementType.Text)
                continue;

            var rect = new Rect2(element.Position, element.Size);
            box = started ? box.Merge(rect) : rect;
            started = true;
        }

        return started;
    }

    /// <summary>
    /// Ink inside one rectangle of a capture: how many of its pixels differ from the background the scenes use, which
    /// is the same test <see cref="CountInk"/> applies to a whole picture.
    /// </summary>
    /// <param name="image">Capture to scan.</param>
    /// <param name="box">Rectangle to count in, in the capture's pixels.</param>
    /// <returns>The number of pixels that are not background.</returns>
    private static int CountInkIn(Image image, Rect2 box)
    {
        int ink = 0;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(box.Position.X));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(box.Position.Y));
        int x1 = Mathf.Min(image.GetWidth(), Mathf.CeilToInt(box.End.X));
        int y1 = Mathf.Min(image.GetHeight(), Mathf.CeilToInt(box.End.Y));

        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                Color pixel = image.GetPixel(x, y);

                if (pixel.R > 0.2f || pixel.G > 0.2f || pixel.B > 0.2f)
                    ink++;
            }
        }

        return ink;
    }

    /// <summary>The start of a block's text, for a failure message.</summary>
    /// <param name="text">Text of the source element, or null.</param>
    /// <returns>The first characters of it.</returns>
    private static string Snippet(string? text) =>
        text is { Length: > 10 } ? text[..10] : text ?? string.Empty;

    /// <summary>Wait for the canvas to have applied a layout, up to the frame budget.</summary>
    /// <param name="tree">Scene tree the case runs in.</param>
    /// <param name="canvas">Canvas being waited for.</param>
    /// <returns>True when it has a layout by then.</returns>
    private static async Task<bool> WaitForLayout(SceneTree tree, TypographySceneCanvas canvas)
    {
        for (int frame = 0; frame < LayoutFrameBudget && !canvas.HasContent; frame++)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        return canvas.HasContent;
    }

    /// <param name="image">Rendered image.</param>
    /// <returns>The number of pixels that are not background.</returns>
    private static int CountInk(Image image)
    {
        int width = image.GetWidth();
        int height = image.GetHeight();
        int ink = 0;

        for (int y = 0; y < height; y += 2)
        {
            for (int x = 0; x < width; x += 2)
            {
                Color pixel = image.GetPixel(x, y);

                if (pixel.R > 0.2f || pixel.G > 0.2f || pixel.B > 0.2f)
                    ink += 4;
            }
        }

        return ink;
    }

    /// <summary>The ScrollContainer the canvas sits in, or null when the scene has none.</summary>
    /// <param name="canvas">The scene's canvas.</param>
    /// <returns>The nearest enclosing scroll container.</returns>
    private static ScrollContainer? FindScrollContainer(Node canvas)
    {
        for (Node? node = canvas.GetParent(); node is not null; node = node.GetParent())
        {
            if (node is ScrollContainer scroll)
                return scroll;
        }

        return null;
    }

    /// <summary>
    /// Whether the run has a rendering device, and a skip notice when it has not: the scenes draw with the real
    /// device, so a headless run cannot say anything about them.
    /// </summary>
    /// <param name="caseName">Name of the case, for the notice.</param>
    /// <returns>True when there is no device and the case should be skipped.</returns>
    private static bool NoRenderingDevice(string caseName)
    {
        if (RenderingServer.GetRenderingDevice() is not null)
            return false;

        GD.Print($"[skip] {caseName}: no rendering device");
        return true;
    }

    /// <summary>Whether a scene tree is available at all.</summary>
    /// <returns>True when the engine runs as a scene tree.</returns>
    private static bool HasSceneTree() => Engine.GetMainLoop() is SceneTree;
}
