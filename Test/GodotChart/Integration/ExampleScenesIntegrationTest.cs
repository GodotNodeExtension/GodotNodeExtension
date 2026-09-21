namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using System.Reflection;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Tests.Support;
using static GdUnit4.Assertions;

/// <summary>
/// The example scenes as test assets. They are the only place where the whole feature set is configured the
/// way a user would configure it - in a <c>.tscn</c>, with exported node references - and until now nothing
/// checked them: a scene that no longer instantiates, an export that was never wired, or a chart kind that
/// lost its example would only be noticed by opening the example browser by hand.
/// <para>
/// These cases run <b>without</b> a rendering device too (the examples degrade to their placeholder), so they
/// guard the normal pull-request gate as well. An engine <c>ERROR:</c> raised while a scene runs fails the
/// whole run through the runner's log check, which is the second half of this suite's coverage.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ExampleScenesIntegrationTest
{
    private const string Directory = "res://Example/GodotChart";

    /// <summary>Every <c>.tscn</c> of the component's example directory, sorted for a stable report.</summary>
    private static List<string> SceneFiles()
    {
        var files = new List<string>();
        using var dir = DirAccess.Open(Directory);
        if (dir is null) return files;

        foreach (string file in dir.GetFiles())
        {
            if (file.EndsWith(".tscn", StringComparison.OrdinalIgnoreCase))
                files.Add(file);
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

    /// <summary>
    /// Run one frame of every script in the instance the way the engine would: <c>_Process</c> is what the
    /// examples use to feed data and drive their animation, and a test body does not advance the engine clock.
    /// </summary>
    private static void PumpScene(Node instance, int frames)
    {
        var nodes = new List<Node> { instance };
        nodes.AddRange(Descendants(instance));

        for (int frame = 0; frame < frames; frame++)
            foreach (var node in nodes)
                if (node.IsInsideTree())
                    node._Process(0.016);
    }

    /// <summary>
    /// Exported properties of <paramref name="type"/> whose type is a node: the ones a scene wires up with
    /// <c>node_paths</c>. A missing connection is invisible until the code dereferences it at runtime.
    /// </summary>
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
    /// Every scene instantiates, survives three frames of its own script, and has all of its exported node
    /// references wired.
    /// </summary>
    [TestCase]
    public void EveryExampleSceneRunsItsFramesWithItsExportsWired()
    {
        var files = SceneFiles();
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
                problems.Add($"{errors.Length} engine error(s) while running the scenes: {string.Join(" | ", first)}");
            }
        }
        finally
        {
            log.Detach();
        }

        GD.Print($"example scenes: {files.Count} scene(s) instantiated and pumped");
        AssertThat(string.Join("; ", problems)).IsEqual("");
    }

    /// <summary>
    /// Every <see cref="ChartKind"/> appears in at least one example scene: the kinds are the library's feature
    /// set, and a kind that loses its example is a kind nobody notices going wrong.
    /// </summary>
    [TestCase]
    public void EveryChartKindIsShownByAnExampleScene()
    {
        var shown = new HashSet<ChartKind>();
        var files = SceneFiles();
        var root = ((SceneTree)Engine.GetMainLoop()).Root;

        foreach (string file in files)
        {
            var scene = GD.Load<PackedScene>($"{Directory}/{file}");
            if (scene is null) continue;

            Node? instance = null;
            try
            {
                instance = scene.Instantiate();
                root.AddChild(instance);
                foreach (var node in Descendants(instance))
                    if (node is ChartView view)
                        shown.Add(view.Kind);
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

        var missing = new List<string>();
        foreach (ChartKind kind in Enum.GetValues<ChartKind>())
            if (!shown.Contains(kind)) missing.Add(kind.ToString());

        GD.Print($"example scenes: {shown.Count}/{Enum.GetValues<ChartKind>().Length} chart kind(s) shown");
        AssertThat(string.Join(",", missing)).IsEqual("");
    }
}
