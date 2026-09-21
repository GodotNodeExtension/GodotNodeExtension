using System;
using System.Collections.Generic;
using Godot;
using FileAccess = Godot.FileAccess;

namespace GodotNodeExtension;

/// <summary>
/// Example browser. The left side is a tree: every component is a group and each group holds all of its
/// demo scenes, so a complex component can ship several focused scenes (the chart library splits its
/// kinds across feature galleries) instead of one giant demo.
/// <para>
/// A group is a directory under <c>res://Example/</c>. Its scenes are the <c>*.tscn</c> files in it,
/// ordered and described by an optional <c>demos.json</c>; anything not listed there is picked up
/// automatically, and sub-directories become nested groups.
/// </para>
/// </summary>
public partial class MainScene : Control
{
    [Export] public Tree ExampleTree { get; set; } = null!;
    [Export] public Label ExampleTitleLabel { get; set; } = null!;
    [Export] public Label ExampleAuthorLabel { get; set; } = null!;
    [Export] public RichTextLabel ExampleDescriptionLabel { get; set; } = null!;
    [Export] public Container ExampleViewport { get; set; } = null!;

    /// <summary>One selectable scene in the tree.</summary>
    private sealed record DemoEntry(string Title, string Description, string ScenePath, string Author);

    private readonly Dictionary<ulong, DemoEntry> _entries = [];
    private Control? _currentExample;
    private TreeItem? _firstLeaf;

    /// <summary>Height the loaded page declares for itself (<c>custom_minimum_size.y</c> of its root scene).</summary>
    private float _exampleMinimumHeight;

    /// <inheritdoc />
    public override void _Ready()
    {
        ExampleTree.ItemSelected += OnItemSelected;
        BuildTree();

        // Open the first entry so the panel is never empty.
        if (_firstLeaf is not null)
        {
            _firstLeaf.Select(0);
            OnItemSelected();
        }
    }

    // ── Tree construction ───────────────────────────────────────────────────

    private void BuildTree()
    {
        var root = ExampleTree.CreateItem();   // the tree's hidden root

        foreach (var directory in SortedDirectories("res://Example/"))
            AddGroup(root, directory);
    }

    private void AddGroup(TreeItem parent, string directory)
    {
        using var dir = DirAccess.Open(directory);
        if (dir is null) return;

        var group = ExampleTree.CreateItem(parent);
        group.SetText(0, GroupTitle(directory));
        group.SetSelectable(0, false);      // a group only expands; its scenes are what you open
        group.Collapsed = false;

        var (_, author, groupDescription) = ReadComponentInfo(directory);
        var listed = new HashSet<string>(StringComparer.Ordinal);

        // 1. The manifest decides order and wording.
        foreach (var (file, title, description) in ReadDemoManifest(directory))
        {
            var scenePath = $"{directory.TrimEnd('/')}/{file}";
            if (!FileAccess.FileExists(scenePath)) continue;

            AddLeaf(group, scenePath, title, description.Length > 0 ? description : groupDescription, author);
            listed.Add(file);
        }

        // 2. Everything else in the folder, plus nested groups for sub-directories.
        dir.ListDirBegin();
        for (var entry = dir.GetNext(); entry != ""; entry = dir.GetNext())
        {
            if (dir.CurrentIsDir())
            {
                if (!entry.StartsWith('.') && !entry.StartsWith('_'))
                    AddGroup(group, $"{directory.TrimEnd('/')}/{entry}");
                continue;
            }

            if (!entry.EndsWith(".tscn", StringComparison.Ordinal) || listed.Contains(entry)) continue;
            AddLeaf(group, $"{directory.TrimEnd('/')}/{entry}", entry.GetBaseName(), groupDescription, author);
        }
        dir.ListDirEnd();

        // A group with nothing under it is noise.
        if (group.GetChildCount() == 0) group.Free();
    }

    private void AddLeaf(TreeItem parent, string scenePath, string title, string description, string author)
    {
        var item = ExampleTree.CreateItem(parent);
        item.SetText(0, title);
        item.SetTooltipText(0, scenePath);
        _entries[item.GetInstanceId()] = new DemoEntry(title, description, scenePath, author);
        _firstLeaf ??= item;
    }

    private static string[] SortedDirectories(string path)
    {
        using var dir = DirAccess.Open(path);
        if (dir is null)
        {
            GD.PushError($"Failed to open {path}");
            return [];
        }

        var directories = new List<string>();
        dir.ListDirBegin();
        for (var entry = dir.GetNext(); entry != ""; entry = dir.GetNext())
        {
            if (!dir.CurrentIsDir() || entry.StartsWith('.') || entry.StartsWith('_')) continue;
            directories.Add($"{path.TrimEnd('/')}/{entry}");
        }
        dir.ListDirEnd();

        directories.Sort(StringComparer.Ordinal);
        return [.. directories];
    }

    // ── Metadata ────────────────────────────────────────────────────────────

    private static string GroupTitle(string directory)
    {
        var (name, _, _) = ReadComponentInfo(directory);
        return name.Length > 0 ? name : directory.GetFile();
    }

    /// <summary>Reads <c>Component/&lt;name&gt;/component_info.json</c> for the group's name/author/description.</summary>
    private static (string Name, string Author, string Description) ReadComponentInfo(string directory)
    {
        var path = directory.Replace("Example", "Component") + "/component_info.json";
        if (!FileAccess.FileExists(path)) return ("", "Unknown", "");

        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null) return ("", "Unknown", "");

        var parsed = Json.ParseString(file.GetAsText());
        var info = parsed.VariantType == Variant.Type.Dictionary
            ? parsed.AsGodotDictionary()
            : [];

        return (
            Text(info, "name"),
            Text(info, "author", "Unknown"),
            Text(info, "description"));
    }

    /// <summary>
    /// Reads the optional <c>demos.json</c> of a group: a list of
    /// <c>{ "scene": ..., "title": ..., "description": ... }</c> entries.
    /// </summary>
    private static List<(string File, string Title, string Description)> ReadDemoManifest(string directory)
    {
        var result = new List<(string, string, string)>();
        var path = $"{directory.TrimEnd('/')}/demos.json";
        if (!FileAccess.FileExists(path)) return result;

        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null) return result;

        var parsed = Json.ParseString(file.GetAsText());
        if (parsed.VariantType != Variant.Type.Dictionary) return result;

        if (!parsed.AsGodotDictionary().TryGetValue("demos", out var demos) ||
            demos.VariantType != Variant.Type.Array)
            return result;

        foreach (var demo in demos.AsGodotArray())
        {
            if (demo.VariantType != Variant.Type.Dictionary) continue;
            var entry = demo.AsGodotDictionary();
            var scene = Text(entry, "scene");
            if (scene.Length == 0) continue;
            result.Add((scene, Text(entry, "title", scene.GetBaseName()), Text(entry, "description")));
        }
        return result;
    }

    private static string Text(Godot.Collections.Dictionary dictionary, string key, string fallback = "")
        => dictionary.TryGetValue(key, out var value) && value.VariantType == Variant.Type.String
            ? value.AsString()
            : fallback;

    // ── Selection ───────────────────────────────────────────────────────────

    private void OnItemSelected()
    {
        var item = ExampleTree.GetSelected();
        if (item is null) return;
        if (!_entries.TryGetValue(item.GetInstanceId(), out var entry)) return;

        ExampleTitleLabel.Text = entry.Title;
        ExampleAuthorLabel.Text = $"Author: {entry.Author}";
        ExampleDescriptionLabel.Text = entry.Description.Length > 0
            ? entry.Description
            : "No description available.";

        var note = LoadExample(entry);
        if (note.Length > 0) ExampleDescriptionLabel.Text += $"\n\n{note}";
    }

    /// <summary>
    /// Loads a demo scene into the panel. Returns a note to add to the description when the scene has
    /// nothing to show, which is the case for the plain-<see cref="Node"/> scenes some folders ship
    /// (self-running test suites) - a <see cref="Container"/> only arranges <see cref="Control"/>s.
    /// </summary>
    private string LoadExample(DemoEntry entry)
    {
        if (_currentExample is not null)
        {
            ExampleViewport.Resized -= OnExampleViewportResized;
            ExampleViewport.RemoveChild(_currentExample);
            _currentExample.QueueFree();
            _currentExample = null;
        }

        var scene = GD.Load<PackedScene>(entry.ScenePath);
        if (scene is null)
        {
            GD.PushError($"Could not load {entry.ScenePath}");
            return "";
        }

        var instance = scene.Instantiate();
        if (instance is not Control control)
        {
            instance.QueueFree();
            return "This scene has no visual output: open it on its own (its script reports to the console).";
        }

        // Inside a ScrollContainer the scene keeps its own size (that is what makes the panel scroll), so fill
        // the width and let the content decide how tall it needs to be - except that the page is given at least
        // the panel's height, so a short page never leaves a band of panel background under itself. The height
        // the scene declares is kept as the floor: a page that needs more room than the panel keeps it and
        // scrolls.
        _exampleMinimumHeight = control.CustomMinimumSize.Y;
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ExampleViewport.AddChild(control);
        FitExampleToPanel(control);
        ExampleViewport.Resized += OnExampleViewportResized;
        _currentExample = control;
        return "";
    }

    /// <summary>
    /// Give the loaded page at least the panel's height. The ScrollContainer lays a child out at its minimum
    /// height, so without this a page shorter than the panel sits at the top with a band of panel background
    /// under it, which reads as a layout mistake rather than as a page that ended.
    /// </summary>
    private void FitExampleToPanel(Control? example)
    {
        if (example is null) return;

        example.CustomMinimumSize = new Vector2(example.CustomMinimumSize.X,
            Mathf.Max(_exampleMinimumHeight, ExampleViewport.Size.Y));
    }

    private void OnExampleViewportResized() => FitExampleToPanel(_currentExample);
}
