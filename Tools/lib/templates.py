"""Text templates for `components.py create`: the files a new component is scaffolded from.

Kept as Python strings on purpose. A `*.cs` template file would be picked up as real source by the
analyzers (ReSharper inspects the solution directory) and by the documentation tools, and every one of
them would report the `{{Name}}` placeholders as syntax errors.

Every template is written to satisfy the repository's guards on the first try: `components.py verify`
(README with a usage section, a source file, an example, a test suite), `check_doc_coverage.py` (an XML
comment on every public member - a new file has no baseline entry, so a single missing comment is a
regression), `check_doc_parity.py` (the English/Chinese README pair has to match structurally) and the
build's analyzer set (0 warnings).
"""

from __future__ import annotations


def render(template: str, **values: str) -> str:
    """Fill `{{Key}}` placeholders; a placeholder without a value is a programming error."""
    text = template
    for key, value in values.items():
        text = text.replace("{{" + key + "}}", value)
    assert "{{" not in text, f"unfilled placeholder in template: {text[:120]!r}"
    return text


# ── component metadata ──────────────────────────────────────────────────────

COMPONENT_INFO = """{
  "name": "{{Name}}",
  "version": "{{Version}}",
  "description": "{{Description}}",
  "author": "{{Author}}",
  "license": "{{License}}",
  "requirements": {
    "godot": ">=4.7.0",
    "dotnet": ">=10.0"
  },
  "dependencies": {
    "nuget": [],
    "components": []
  }
}
"""


# ── component sources ───────────────────────────────────────────────────────

SOURCE_DRAWABLE = """using Godot;

namespace GodotNodeExtension.Component.{{Name}};

/// <summary>
/// {{Description}}
/// </summary>
[Tool]
[GlobalClass]
public partial class {{Name}} : {{Base}}
{
    /// <summary>Text the component shows. Set it in a scene or from code; changing it redraws.</summary>
    [Export]
    public string Message
    {
        get => _message;
        set
        {
            _message = value;
            QueueRedraw();
        }
    }

    private string _message = "{{Name}}";

    /// <inheritdoc/>
    public override void _Ready()
    {
        base._Ready();
        QueueRedraw();
    }

    /// <inheritdoc/>
    public override void _Draw()
    {
        DrawString(ThemeDB.FallbackFont, new Vector2(8, 28), _message);
    }
}
"""

SOURCE_RESOURCE = """using Godot;

namespace GodotNodeExtension.Component.{{Name}};

/// <summary>
/// {{Description}}
/// </summary>
[Tool]
[GlobalClass]
public partial class {{Name}} : Resource
{
    /// <summary>Text the resource carries; the scaffold's default makes a fresh instance identifiable.</summary>
    [Export]
    public string Message { get; set; } = "{{Name}}";
}
"""

SOURCE_LIBRARY = """namespace GodotNodeExtension.Component.{{Name}};

/// <summary>
/// {{Description}}
/// </summary>
public sealed class {{Name}}
{
    /// <summary>Text the type carries; the scaffold's default makes a fresh instance identifiable.</summary>
    public string Message { get; set; } = "{{Name}}";
}
"""


# ── example ─────────────────────────────────────────────────────────────────

EXAMPLE_SCRIPT_NODE = """using Godot;

namespace GodotNodeExtension.Example.{{Name}};

/// <summary>
/// Demo scene for <see cref="Component.{{Name}}.{{Name}}"/>: the component node is placed by
/// {{Name}}Demo.tscn and reached through the exported reference below - never through a hard-coded
/// node path, so renaming the node in the scene cannot break the demo silently.
/// </summary>
public partial class {{Name}}Demo : Control
{
    /// <summary>The component node the scene contains.</summary>
    [Export]
    public Component.{{Name}}.{{Name}} ComponentNode { get; set; } = null!;

    /// <inheritdoc/>
    public override void _Ready()
    {
        if (ComponentNode is null)
        {
            GD.PushError("{{Name}}Demo: ComponentNode is not wired in {{Name}}Demo.tscn");
            return;
        }

        ComponentNode.Message = "Hello from {{Name}}";
    }
}
"""

EXAMPLE_SCENE_NODE = """[gd_scene load_steps=3 format=3]

[ext_resource type="Script" path="res://Example/{{Name}}/{{Name}}Demo.cs" id="1_demo"]
[ext_resource type="Script" path="res://Component/{{Name}}/{{Name}}.cs" id="2_component"]

[node name="{{Name}}Demo" type="Control" node_paths=PackedStringArray("ComponentNode")]
layout_mode = 3
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
script = ExtResource("1_demo")
ComponentNode = NodePath("Center/{{Name}}")

[node name="Center" type="CenterContainer" parent="."]
layout_mode = 1
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2

[node name="{{Name}}" type="{{Base}}" parent="Center"]
custom_minimum_size = Vector2(240, 120)
layout_mode = 2
script = ExtResource("2_component")
Message = "Hello from {{Name}}"
"""

EXAMPLE_SCRIPT_PLAIN = """using Godot;

namespace GodotNodeExtension.Example.{{Name}};

/// <summary>
/// Demo scene for <see cref="Component.{{Name}}.{{Name}}"/>: the type has no scene presence of its own,
/// so the demo creates an instance, uses it and reports the result in the editor console.
/// </summary>
public partial class {{Name}}Demo : Control
{
    /// <inheritdoc/>
    public override void _Ready()
    {
        var component = new Component.{{Name}}.{{Name}}();
        GD.Print($"{component.GetType().Name}.Message = {component.Message}");
    }
}
"""


# ── tests ───────────────────────────────────────────────────────────────────

TEST_NODE = """namespace GodotNodeExtension.Tests.{{Name}};

using GdUnit4;
using static GdUnit4.Assertions;
using ComponentNode = GodotNodeExtension.Component.{{Name}}.{{Name}};

/// <summary>
/// Specification for <see cref="ComponentNode"/>. Replace the scaffold's cases with the behaviour this
/// component promises: what is drawn, what the exports do, and what happens at the edges.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class {{Name}}Test
{
    /// <summary>A fresh instance carries the documented default <c>Message</c>.</summary>
    [TestCase]
    public void AFreshInstanceCarriesTheDefaultMessage()
    {
        var node = AutoFree(new ComponentNode());

        AssertThat(node.Message).IsEqual("{{Name}}");
    }

    /// <summary>Assigning <c>Message</c> stores it, so the redraw draws the new text.</summary>
    [TestCase]
    public void AssigningMessageStoresIt()
    {
        var node = AutoFree(new ComponentNode()) as ComponentNode;

        node.Message = "changed";

        AssertThat(node.Message).IsEqual("changed");
    }
}
"""

TEST_PLAIN = """namespace GodotNodeExtension.Tests.{{Name}};

using GdUnit4;
using static GdUnit4.Assertions;
using ComponentType = GodotNodeExtension.Component.{{Name}}.{{Name}};

/// <summary>
/// Specification for <see cref="ComponentType"/>. Replace the scaffold's cases with the behaviour this
/// type promises: what it computes, what it rejects and how its edges behave.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class {{Name}}Test
{
    /// <summary>A fresh instance carries the documented default <c>Message</c>.</summary>
    [TestCase]
    public void AFreshInstanceCarriesTheDefaultMessage()
    {
        var component = new ComponentType();

        AssertThat(component.Message).IsEqual("{{Name}}");
    }

    /// <summary>Assigning <c>Message</c> stores it.</summary>
    [TestCase]
    public void AssigningMessageStoresIt()
    {
        var component = new ComponentType { Message = "changed" };

        AssertThat(component.Message).IsEqual("changed");
    }
}
"""

INTEGRATION_TEST = """namespace GodotNodeExtension.Tests.{{Name}}.Integration;

using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using ComponentNode = GodotNodeExtension.Component.{{Name}}.{{Name}};

/// <summary>
/// Rendering check for <see cref="ComponentNode"/> through the engine's real rendering device: the
/// scaffold only proves that the component draws a frame without an engine error, inside a viewport it is
/// really attached to. Replace the assertions with what this component's picture is supposed to show, and
/// record a baseline image next to this suite when geometry alone cannot express it.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class {{Name}}RenderIntegrationTest
{
    /// <summary>Viewport the component is drawn into, so the device actually rasterises it.</summary>
    private static readonly Vector2I ViewSize = new(240, 120);

    /// <summary>True when this run has no rendering device; the case then reports itself skipped.</summary>
    private static bool NoRenderingDevice(string caseName)
    {
        if (RenderingServer.GetRenderingDevice() is not null)
            return false;

        GD.Print($"[skip] {caseName}: no rendering device in this run");
        return true;
    }

    /// <summary>True when this run has no SceneTree; without one no viewport can be attached.</summary>
    private static bool NoSceneTree(string caseName)
    {
        if (Engine.GetMainLoop() is SceneTree)
            return false;

        GD.Print($"[skip] {caseName}: no SceneTree in this run");
        return true;
    }

    /// <summary>The component is attached, visible and as large as the viewport asked it to be.</summary>
    [TestCase]
    public void TheComponentDrawsThroughTheRealDevice()
    {
        const string caseName = nameof(TheComponentDrawsThroughTheRealDevice);
        if (NoRenderingDevice(caseName) || NoSceneTree(caseName)) return;

        var tree = (SceneTree)Engine.GetMainLoop();
        var viewport = AutoFree(new SubViewport
        {
            Size = ViewSize,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        });
        var node = AutoFree(new ComponentNode
        {
            Size = ViewSize,
            Message = "Hello from {{Name}}",
        });
        viewport.AddChild(node);
        tree.Root.AddChild(viewport);

        AssertThat(node.IsVisibleInTree()).IsTrue();
        AssertThat(node.Size).IsEqual((Vector2)ViewSize);
    }
}
"""


# ── documentation ───────────────────────────────────────────────────────────

DOC_EN = """**English** | [中文](README.cn.md)

# {{Name}}

{{Description}}

## Features

- **Scaffolded, not finished**: this page, the example and the test suite describe the component as it
  should behave; replace them as the implementation grows.
- **Scene friendly**: every setting is an exported property, so a scene can configure the component
  without any code.

## Installation

```bash
dotnet tool install -g GodotNodeExtensionInstaller
godotNodeInstall install {{Name}}
```

The component is installed into `addons/GodotNodeExtension/{{Name}}/` of the consuming project.

## Usage

```csharp compile
var component = new {{Name}}();
component.Message = "Hello";
```

Add the node to a scene (or create it in code) and set `Message`; the text is drawn where the node
covers the canvas.

## Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Message` | `string` | `"{{Name}}"` | Text the component shows. Assigning it redraws. |

## Limitations

- The scaffold ships one exported property and one drawing call: it is a starting point, not a feature.
- The output is drawn with the theme's fallback font; a component that needs a specific face has to
  resolve the font itself.
"""

DOC_CN = """[English](README.md) | **中文**

# {{Name}}

{{Description}}

## 功能特性

- **这是脚手架，不是成品**：本页、示例与测试套件描述的是组件*应该*有的行为，随实现推进替换掉它们。
- **对场景友好**：所有设置都是 `[Export]` 属性，场景里不写代码也能配置该组件。

## 安装

```bash
dotnet tool install -g GodotNodeExtensionInstaller
godotNodeInstall install {{Name}}
```

组件会被装进使用方工程的 `addons/GodotNodeExtension/{{Name}}/`。

## 用法

```csharp compile
var component = new {{Name}}();
component.Message = "Hello";
```

把节点放进场景（或在代码里创建）并设置 `Message`，文本会画在节点覆盖的画布上。

## 属性

| 属性 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `Message` | `string` | `"{{Name}}"` | 组件显示的文字，赋值后会重绘。 |

## 限制

- 脚手架只带一个导出属性和一次绘制调用：它是起点，不是功能。
- 绘制使用主题的回退字体；需要指定字体的组件得自己解析字体。
"""


# ── generated project files ─────────────────────────────────────────────────

#: The types `components.py create --type` accepts, with the base class, the attributes that register the
#: type with Godot and whether the type can live in an example scene.
COMPONENT_TYPES: dict[str, dict[str, str | bool]] = {
    "control": {"base": "Control", "attributes": "[Tool]\n[GlobalClass]", "scene": True},
    "node": {"base": "Node2D", "attributes": "[Tool]\n[GlobalClass]", "scene": True},
    "resource": {"base": "Resource", "attributes": "[Tool]\n[GlobalClass]", "scene": False},
    "library": {"base": "", "attributes": "", "scene": False},
}
