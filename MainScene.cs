using System;
using System.Collections.Generic;
using System.IO;
using Godot;
using FileAccess = Godot.FileAccess;

namespace GodotNodeExtension;

public partial class MainScene : Control
{
    // Export references to UI controls from tscn file
    [Export] public ItemList ExampleList { get; set; } = null!;
    [Export] public Label ExampleTitleLabel { get; set; } = null!;
    [Export] public Label ExampleAuthorLabel { get; set; } = null!;
    [Export] public RichTextLabel ExampleDescriptionLabel { get; set; } = null!;
    [Export] public Container ExampleViewport { get; set; } = null!;

    private readonly List<ExampleInfo> _examples = new();
    private Control? _currentExample;

    private struct ExampleInfo
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string Author { get; set; }
        public string Description { get; set; }
        public string ScenePath { get; set; }
        public PackedScene Scene { get; set; }
    }

    public override void _Ready()
    {
        LoadExamples();
        ConnectSignals();
        
        if (_examples.Count > 0)
        {
            ExampleList.Select(0);
            OnExampleSelected(0);
        }
    }

    private void LoadExamples()
    {
        var exampleDir = "res://Example/";
        var dir = DirAccess.Open(exampleDir);
        
        if (dir == null)
        {
            GD.PrintErr("Failed to open Example directory");
            return;
        }

        dir.ListDirBegin();
        string fileName = dir.GetNext();
        
        while (fileName != "")
        {
            if (dir.CurrentIsDir() && !fileName.StartsWith("."))
            {
                LoadExampleFromDirectory(Path.Combine(exampleDir, fileName));
            }
            fileName = dir.GetNext();
        }
        
        dir.ListDirEnd();
        
        _examples.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.Ordinal));
        
        foreach (var example in _examples)
        {
            ExampleList.AddItem(example.Title);
        }
    }

    private void LoadExampleFromDirectory(string dirPath)
    {
        var dir = DirAccess.Open(dirPath);
        if (dir == null) return;
        
        // Look for .tscn files
        dir.ListDirBegin();
        string fileName = dir.GetNext();
        PackedScene? scene = null;
        string scenePath = "";
        
        while (fileName != "")
        {
            if (fileName.EndsWith(".tscn") && fileName.Contains("Demo"))
            {
                scenePath = Path.Combine(dirPath, fileName);
                scene = GD.Load<PackedScene>(scenePath);
                break;
            }
            fileName = dir.GetNext();
        }
        
        dir.ListDirEnd();
        
        if (scene == null) return;
        
        // Extract directory name for display
        var dirName = Path.GetFileName(dirPath);
        
        // Try to get metadata from component_info.json if exists
        var componentInfoPath = Path.Combine(dirPath.Replace("Example", "Component"), "component_info.json");
        string title = dirName;
        string author = "Unknown";
        string description = "No description available.";
        
        var componentInfoFile = FileAccess.Open(componentInfoPath, FileAccess.ModeFlags.Read);
        if (componentInfoFile != null)
        {
            try
            {
                var json = componentInfoFile.GetAsText();
                var jsonDict = Json.ParseString(json).AsGodotDictionary();
                
                title = jsonDict.TryGetValue("name", out var nameVar) ? nameVar.AsString() : dirName;
                author = jsonDict.TryGetValue("author", out var authorVar) ? authorVar.AsString() : "Unknown";
                description = jsonDict.TryGetValue("description", out var descVar) ? descVar.AsString() : "No description available.";
            }
            catch (Exception ex)
            {
                GD.PrintErr($"Failed to parse component_info.json for {dirName}: {ex.Message}");
            }
            finally
            {
                componentInfoFile.Close();
            }
        }
        
        _examples.Add(new ExampleInfo
        {
            Name = dirName,
            Title = title,
            Author = author,
            Description = description,
            ScenePath = scenePath,
            Scene = scene
        });
    }

    private void ConnectSignals()
    {
        ExampleList.ItemSelected += OnExampleSelected;
    }

    private void OnExampleSelected(long index)
    {
        if (index < 0 || index >= _examples.Count) return;
        
        var example = _examples[(int)index];
        
        // Update info panel
        ExampleTitleLabel.Text = example.Title;
        ExampleAuthorLabel.Text = $"Author: {example.Author}";
        ExampleDescriptionLabel.Text = example.Description;
        
        // Load example scene
        LoadExampleScene(example);
    }

    private void LoadExampleScene(ExampleInfo example)
    {
        // Clear current example
        if (_currentExample != null)
        {
            _currentExample.QueueFree();
            _currentExample = null;
        }
        
        try
        {
            // Instantiate new example
            _currentExample = example.Scene.Instantiate<Control>();
            
            if (_currentExample == null)
            {
                GD.PrintErr($"Failed to instantiate example scene: {example.ScenePath}");
                return;
            }
            
            // Add to viewport
            ExampleViewport.AddChild(_currentExample);
            
            GD.Print($"Loaded example: {example.Title}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Error loading example {example.Title}: {ex.Message}");
        }
    }

    public override void _ExitTree()
    {
        if (_currentExample != null)
        {
            _currentExample?.QueueFree();
        }
    }
}
