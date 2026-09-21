using System.IO;
using System.Linq;
using Godot;
using GodotNodeExtension.Component.GodotChart;

namespace GodotNodeExtension.Tests.GodotChart;

/// <summary>
/// Renders one PNG per chart in <c>Example/GodotChart/BasicsDemo.tscn</c> into
/// <c>Doc/GodotChart/assets/</c>, the images the component's documentation shows.
/// <para>
/// It walks the demo's grid, takes each cell's <see cref="ChartView"/>, re-parents it into a plain
/// <see cref="Control"/> of the size the picture wants and saves the surface once it stopped changing.
/// Re-parenting is what makes the size ours: inside the demo the view fills a 1/3-width grid cell, so
/// a capture of the cell would inherit whatever aspect the example browser happens to give it - which
/// is how an earlier batch of these pictures ended up taller than wide.
/// </para>
/// <para>
/// The canvas is picked per chart: cartesian charts want a landscape frame, polar and relational ones
/// (pie, donut, radar, sunburst, chord, gauge, funnel) want a square, because their marks declare a
/// square content shape and a landscape frame would only add empty margins.
/// </para>
/// <para>
/// Run it through <c>python Tools/GodotChart/capture.py</c>, which finds the engine, runs this scene
/// with a real rendering device and checks what came out. This file lives under <c>Test/</c> because it
/// has to be compiled, and <c>Test/</c> is not part of what the installer ships.
/// </para>
/// </summary>
public partial class DocsCapture : Control
{
    /// <summary>Cell name in the demo, output file stem, canvas size.</summary>
    private static readonly (string Cell, string File, int Width, int Height)[] Plan =
    [
        ("Bar", "bar", 960, 600),
        ("Grouped bar", "grouped-bar", 960, 600),
        ("Stacked bar", "stacked-bar", 960, 600),
        ("Line", "line", 960, 600),
        ("Area", "area", 960, 600),
        ("Stacked area", "stacked-area", 960, 600),
        ("Bubble", "bubble", 960, 600),
        ("Range area", "range-area", 960, 600),
        ("Pie", "pie", 720, 720),
        ("Donut", "donut", 720, 720),
        ("Radar", "radar", 720, 720),
        ("Violin", "violin", 960, 600),
        ("Box", "box", 960, 600),
        ("Candlestick", "candlestick", 960, 600),
        ("Heatmap", "heatmap", 960, 600),
        ("Treemap", "treemap", 960, 600),
        ("Sunburst", "sunburst", 720, 720),
        ("Sankey", "sankey", 960, 600),
        ("Chord", "chord", 720, 720),
        ("Gauge", "gauge", 720, 720),
        ("Funnel", "funnel", 720, 720),
        ("Waffle", "waffle", 960, 600),
        ("Timeline", "timeline", 960, 600),
        ("Lollipop", "lollipop", 960, 600),
        ("Milestone", "milestone", 960, 600),
        ("Reference lines", "reference-lines", 960, 600),
    ];

    private const string DemoScene = "res://Example/GodotChart/BasicsDemo.tscn";
    private const string OutputDir = "res://Doc/GodotChart/assets";
    private const int SettleFrames = 12;

    /// <summary>Set on exit: non-zero when something did not come out right.</summary>
    private int _failures;

    /// <inheritdoc />
    public override async void _Ready()
    {
        var packed = GD.Load<PackedScene>(DemoScene);
        if (packed is null)
        {
            GD.PrintErr($"docs capture: cannot load {DemoScene}");
            GetTree().Quit(1);
            return;
        }

        // The demo builds its charts when it enters the tree, so it has to be a child for a moment.
        var demo = packed.Instantiate();
        AddChild(demo);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        var grid = demo.GetNodeOrNull<Node>("Layout/Grid");
        if (grid is null)
        {
            GD.PrintErr("docs capture: the demo has no Layout/Grid");
            GetTree().Quit(1);
            return;
        }

        var cells = grid.GetChildren().OfType<Node>().ToDictionary(node => node.Name.ToString());
        Directory.CreateDirectory(ProjectSettings.GlobalizePath(OutputDir));

        foreach (var (cellName, file, width, height) in Plan)
        {
            if (!cells.TryGetValue(cellName, out var cell))
            {
                GD.PrintErr($"docs capture: no cell named '{cellName}'");
                _failures++;
                continue;
            }

            var view = cell.GetChildren().OfType<ChartView>().FirstOrDefault();
            if (view is null)
            {
                GD.PrintErr($"docs capture: '{cellName}' has no ChartView");
                _failures++;
                continue;
            }

            _failures += await Capture(view, file, width, height);
        }

        demo.QueueFree();
        GD.Print($"docs capture: {Plan.Length - _failures}/{Plan.Length} image(s) written to {OutputDir}");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>
    /// Give <paramref name="view"/> a canvas of the requested size, let it settle, save its surface.
    /// Returns 0 on success and 1 when the capture failed, so the exit code reports it.
    /// </summary>
    private async System.Threading.Tasks.Task<int> Capture(ChartView view, string file, int width, int height)
    {
        var size = new Vector2(width, height);

        // A plain Control (not a Container) keeps the size we set instead of laying the view out again.
        var host = new Control { Size = size };
        view.GetParent()?.RemoveChild(view);
        host.AddChild(view);
        AddChild(host);
        view.Position = Vector2.Zero;
        view.Size = size;

        await Settle();

        var image = view.Texture?.GetImage();
        if (image is null)
        {
            GD.PrintErr($"docs capture: '{file}' produced no surface");
            host.QueueFree();
            return 1;
        }

        if (image.GetWidth() != width || image.GetHeight() != height)
        {
            GD.PrintErr($"docs capture: '{file}' is {image.GetWidth()}x{image.GetHeight()}, " +
                        $"expected {width}x{height}");
            host.QueueFree();
            return 1;
        }

        var path = $"{OutputDir}/{file}.png";
        var error = image.SavePng(ProjectSettings.GlobalizePath(path).Replace('\\', '/'));
        if (error != Error.Ok)
        {
            GD.PrintErr($"docs capture: saving '{path}' failed ({error})");
            host.QueueFree();
            return 1;
        }

        GD.Print($"docs capture: {file}.png {width}x{height}");
        host.QueueFree();
        return 0;
    }

    /// <summary>
    /// Wait for the surface to stop changing. The charts animate their entry, so a fixed number of
    /// frames is not enough - the picture would catch a bar halfway up - and waiting forever is what a
    /// stalled frame would do instead.
    /// </summary>
    private async System.Threading.Tasks.Task Settle()
    {
        ulong previous = 0;
        for (var frame = 0; frame < 240; frame++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (frame < SettleFrames)
                continue;

            var image = CurrentSurface();
            if (image is null)
                continue;

            var fingerprint = ChartRenderHarness.PixelFingerprint(image);
            if (fingerprint == previous)
                return;
            previous = fingerprint;
        }
    }

    /// <summary>The surface of the view being captured - the last child that is a Control with a canvas.</summary>
    private Image? CurrentSurface()
    {
        for (var i = GetChildCount() - 1; i >= 0; i--)
        {
            if (GetChild(i) is Control host)
                foreach (var child in host.GetChildren().OfType<ChartView>())
                    if (child.Texture?.GetImage() is { } image)
                        return image;
        }
        return null;
    }
}
