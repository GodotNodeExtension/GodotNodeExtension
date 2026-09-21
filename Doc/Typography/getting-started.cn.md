[英文](getting-started.md) | **中文**

# 快速上手

从空工程到画出第一段文字。路很短，因为分工很短：你描述文字，服务器决定它放在哪里，渲染器把拿回来的东西画
出来——不再测量、也不再塑形。

## 开始之前

- Godot 4.7 或更新、.NET SDK 10.0 或更新，以及 [README.cn.md](README.cn.md) 里列出的那些包。
- 下面用到的类型分布在五个命名空间里：

```csharp
using Godot;
using GodotNodeExtension.Component.Typography.Core;        // FontCatalog
using GodotNodeExtension.Component.Typography.Core.Model;   // DrawElement, LayoutElement, ...
using GodotNodeExtension.Component.Typography.Languages;    // LanguageProfile, ...
using GodotNodeExtension.Component.Typography.Server;       // TypographyServer, LayoutHandle, ...
```

## 拿到服务器与 handle

`TypographyServer.Instance` 是进程里唯一的服务器。`LayoutHandle` 是它的一个客户端：一块画布、一个文档
视图、一次排版会话。handle 持有已准备的内容，所以用同一个 handle 重新请求排版时，没变的文字可以直接复用
测量结果——这也正是改变尺寸很便宜的原因。

```csharp compile
TypographyServer server = TypographyServer.Instance;
LayoutHandle handle = server.CreateHandle();
```

每个客户端创建一个 handle，然后一直用它。`ReleaseHandle` 把状态交还；服务器关闭后 handle 就不再可用：
`TypographyServer.IsHandleValid` 回答这个问题，而落在过期 handle 上的请求会被拒绝，返回
`TypographyServer.NotSubmitted`（`0`），而不是一个请求 id。

## 构造元素流

输入是一个 `DrawElement` 数组。文字元素需要 `Text`、`Font` 与 `FontSize`，而 `CharacterIndex`/`CharacterCount`
说明它在你自己那份文本里的位置，单位是 UTF-16 code unit——之后的选择与打字机播放靠的就是它们。段落由文本里的
换行切开，或由 `IsParagraphBreak = true` 的元素切开。**开启**一个段落的元素，就是携带该段落
`ParagraphSettings`（包括它的语言）的那个元素，因此一份文档可以混用不同约定。

```csharp compile
Font font = GD.Load<Font>("res://fonts/YourFont.ttf");

string chinese = "排版";
string english = "Typography is layout.";

var elements = new[]
{
    new DrawElement
    {
        Type = DrawElement.ElementType.Text,
        Text = chinese,
        Font = font,
        FontSize = 32,                       // pixels; 0 means "not set" and the engine uses 16
        CharacterIndex = 0,
        CharacterCount = chinese.Length,     // UTF-16 code units, not characters on screen
    },
    new DrawElement { IsParagraphBreak = true },
    new DrawElement
    {
        Type = DrawElement.ElementType.Text,
        Text = english,
        Font = font,
        FontSize = 24,
        CharacterIndex = chinese.Length + 1, // the break counts as one position in your text
        CharacterCount = english.Length,
        ParagraphSettings = new ParagraphSettings { LanguageTag = "en" },
    },
};
```

图片、矩形、线段、扩展区与动作也都是元素，而 `BlockInfo`/`IsBlockEnd` 包住一个由你自己摆放内部内容的区域。
逐字段的参考见 [input-format.cn.md](input-format.cn.md)。

## 提交请求

`TypographySettings` 携带几何——行在多宽处断开、内边距、行距——以及可选的覆盖项，用来覆盖语言本来会做的事。
由语言治理的字段留 `null` 表示"语言怎么说就怎么办"，所以请求绝不会被误当成一次刻意的规则选择。

```csharp compile
TypographyServer server = TypographyServer.Instance;
LayoutHandle handle = server.CreateHandle();
Font font = GD.Load<Font>("res://fonts/YourFont.ttf");

var elements = new[]
{
    new DrawElement
    {
        Type = DrawElement.ElementType.Text,
        Text = "排版",
        Font = font,
        FontSize = 32,
    },
};

long requestId = server.RequestFullLayout(handle, elements, new TypographySettings
{
    LanguageTag = "zh-Hans",   // BCP-47; null means "no language assumption"
    MaxWidth = 320f,           // pixels: how far a line may run on the inline axis
    LineSpacing = 4f,          // pixels, added to the text's own line box
    Padding = 8f,              // pixels, applied around the whole content area
});
```

返回值就是这次请求的 id。在同一个 handle 上提交新请求会取消上一个，所以只有最新的 id 可能产出结果。另外三个
入口：

- `RequestRelayout` 只重跑与宽度相关的那一半；它需要同一 handle 上先有一次 `RequestFullLayout`，缓存为空时
  它会报错。
- `RequestStreamAppend` 追加一个元素，`RequestStreamFlush` 收尾整份文档。它们的结果是渐进式的
  （`IsProgressive`）：渐进结果的尾部还可能再长出几行。

## 取结果

`TryGetResult` 从不阻塞。有结果在等这个 handle，它就把那一个结果交给你。

### 每帧轮询

最常用的用法：文字或尺寸变化时提交，然后在主线程上每帧轮询一次。拿到结果可能属于一个你早已替换掉的请求，
所以用之前先比对它的 `RequestId`。

```csharp compile
TypographyServer server = TypographyServer.Instance;
LayoutHandle handle = server.CreateHandle();
long requestId = 0;                        // whatever RequestFullLayout returned

// Once per frame, on the main thread:
if (server.TryGetResult(handle, out LayoutResult result)
    && result.RequestId == requestId
    && !result.IsCancelled
    && result.Error is null)
{
    foreach (LayoutElement element in result.Elements ?? [])
        Paint(element);
}

static void Paint(LayoutElement element)
{
    // Draw element.GlyphRun here; the rendering recipe walks the stream.
}
```

### 等结果

不在帧循环里的时候——一次性的预览、一个工具、一张缩略图——就自己等结果。在编辑器里队列是在调用线程上排空的，
所以提交调用返回时结果通常已经就绪，等待不花什么代价；而运行中的游戏不得这样阻塞主线程。

```csharp compile
static LayoutResult WaitFor(TypographyServer server, LayoutHandle handle, long requestId)
{
    while (true)
    {
        if (server.TryGetResult(handle, out LayoutResult result)
            && result.RequestId == requestId
            && !result.IsCancelled)
        {
            return result;
        }

        System.Threading.Thread.Sleep(1);   // never do this on the main thread inside a frame loop
    }
}
```

`LayoutResult.Error` 非空时表示排版失败，此时没有可画的元素。否则 `Elements` 就是要画的元素流，按绘制次序；
`Lines` 回答行级问题与命中测试（里面的元素是同一批对象）；`ContentSize` 是内容包围盒，用来定滚动区域大小；
`Timings`、`LineCount`、`ElementCount` 与 `ProhibitedBreakSkips` 是诊断信息。

## 画出结果

下面是一个自包含的类型：它提交一个段落、轮询结果，并用 SkiaSharp 把字形串画出来。
`FontCatalog.Shared.TryGetTypeface` 把字形串携带的 id 还原成字体面，而绘制路径上没有任何东西在塑形文字。

```csharp compile-class
using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Server;

/// <summary>Lays out one paragraph and draws it: build, request, poll, glyphs.</summary>
public sealed class OneParagraph : IDisposable
{
    private readonly TypographyServer _server = TypographyServer.Instance;
    private readonly LayoutHandle _handle;
    private long _requestId = TypographyServer.NotSubmitted;

    public OneParagraph()
    {
        _handle = _server.CreateHandle();
    }

    /// <summary>Submits one paragraph of text.</summary>
    /// <param name="text">The text to lay out.</param>
    /// <param name="font">Font the text is shaped with.</param>
    /// <param name="maxWidth">Line length in pixels.</param>
    public void Submit(string text, Font font, float maxWidth)
    {
        var elements = new[]
        {
            new DrawElement
            {
                Type = DrawElement.ElementType.Text,
                Text = text,
                Font = font,
                FontSize = 32,
                CharacterIndex = 0,
                CharacterCount = text.Length,
            },
        };

        _requestId = _server.RequestFullLayout(_handle, elements, new TypographySettings
        {
            LanguageTag = "ja",     // BCP-47; null means "no language assumption"
            MaxWidth = maxWidth,
            LineSpacing = 4f,       // pixels, on top of the text's own line box
        });
    }

    /// <summary>Polls once; returns the elements when the current request has a finished layout.</summary>
    /// <returns>The element stream, or null while the layout is still running.</returns>
    public List<LayoutElement> Poll()
    {
        if (!_server.TryGetResult(_handle, out LayoutResult result)) return null;

        // A superseded request never produces the result this caller is waiting for.
        if (result.IsCancelled || result.RequestId != _requestId) return null;

        if (result.Error is not null)
        {
            GD.PushError($"layout failed: {result.Error}");
            return null;
        }

        return result.Elements;
    }

    /// <summary>Draws a result: every glyph run, in the order the layout produced them.</summary>
    /// <param name="canvas">Canvas to draw onto.</param>
    /// <param name="elements">The result's element stream.</param>
    /// <param name="paint">Paint carrying the colour.</param>
    public static void Draw(SkiaSharp.SKCanvas canvas, List<LayoutElement> elements, SkiaSharp.SKPaint paint)
    {
        foreach (LayoutElement element in elements)
        {
            if (element.GlyphRun is { Glyphs.Length: > 0 } run)
                DrawRun(canvas, run, paint);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _server.ReleaseHandle(_handle);

    /// <summary>Draws one run: walk its advances, draw its glyphs, shape nothing again.</summary>
    private static void DrawRun(SkiaSharp.SKCanvas canvas, GlyphRun run, SkiaSharp.SKPaint paint)
    {
        // Horizontal writing: the run's origin is the pen on the baseline of its first glyph.
        float baseline = run.Origin.Y;
        float pen = run.Origin.X;

        var builder = new SkiaSharp.SKTextBlobBuilder();
        int index = 0;

        while (index < run.Glyphs.Length)
        {
            // One positioned run per face: shaping fallback can mix faces inside one glyph run.
            ulong fontId = run.Glyphs[index].FontId;
            int start = index;
            while (index < run.Glyphs.Length && run.Glyphs[index].FontId == fontId)
                index++;

            int count = index - start;
            var glyphs = new ushort[count];
            var positions = new SkiaSharp.SKPoint[count];
            float x = pen;

            for (int i = 0; i < count; i++)
            {
                Glyph glyph = run.Glyphs[start + i];
                positions[i] = new SkiaSharp.SKPoint(x + glyph.OffsetX, baseline + glyph.OffsetY);
                glyphs[i] = glyph.Id <= ushort.MaxValue ? (ushort)glyph.Id : (ushort)0;
                x += glyph.Advance;
            }

            if (FontCatalog.Shared.TryGetTypeface(fontId, out SkiaSharp.SKTypeface typeface)
                && typeface is not null)
            {
                using var font = new SkiaSharp.SKFont(typeface, run.FontSize);
                builder.AddPositionedRun(glyphs, font, positions);
            }

            pen = x;
        }

        SkiaSharp.SKTextBlob blob = builder.Build();
        if (blob is not null)
            canvas.DrawText(blob, 0f, 0f, paint);
    }
}
```

## 放进工程

在工程里，这个模式就是一个持有单个 handle 的 `Control`：内容或尺寸变化时提交，每帧轮询一次，然后把结果交给
负责画它的那个东西。负责画的东西就是上面那个类型——这个宿主并不关心渲染器是哪一个。

```csharp compile-members
// One layout session per host: the handle is created once, polled every frame, released on exit.
private readonly TypographyServer _server = TypographyServer.Instance;
private LayoutHandle _handle;
private long _requestId = TypographyServer.NotSubmitted;

/// <summary>Where the host paints a result; the type above is one such painter.</summary>
public Action<LayoutResult> PaintResult { get; set; } = _ => { };

/// <summary>Font the text is shaped with.</summary>
public Font BodyFont { get; set; }

public override void _Ready()
{
    _handle = _server.CreateHandle();
    Resized += OnResized;
    Submit();
}

public override void _Process(double delta)
{
    if (!_server.IsHandleValid(_handle))
    {
        // A shutdown invalidated the handle; requests on a stale one are only rejected.
        _handle = _server.CreateHandle();
        Submit();
        return;
    }

    if (!_server.TryGetResult(_handle, out LayoutResult result)) return;      // nothing yet
    if (result.IsCancelled || result.RequestId != _requestId) return;        // superseded
    if (result.Error is not null)
    {
        GD.PushError(result.Error);
        return;
    }

    PaintResult(result);
    QueueRedraw();
}

public override void _ExitTree()
{
    Resized -= OnResized;
    _server.ReleaseHandle(_handle);
}

private void Submit()
{
    _requestId = _server.RequestFullLayout(_handle, BuildElements(), Settings());
}

private void OnResized()
{
    // The same text at another width: reuse the preparation, re-run the cheap half.
    _requestId = _server.RequestRelayout(_handle, Settings());
}

private TypographySettings Settings() => new()
{
    LanguageTag = "zh-Hans",
    MaxWidth = (float)Size.X,   // the node's own width is the line length
    Padding = 8f,
};

private DrawElement[] BuildElements()
{
    string body = "排版是把文字放好的手艺。";

    return
    [
        new DrawElement
        {
            Type = DrawElement.ElementType.Text,
            Text = body,
            Font = BodyFont,
            FontSize = 24,
            CharacterIndex = 0,
            CharacterCount = body.Length,
        },
    ];
}
```

这个循环里有两个细节。`Resized` 可能在还没有任何排版之前就到了（节点在第一次排版之前被改了尺寸），此时
`RequestRelayout` 会因为无内容可复用而报错：这种情况就提交一次 `RequestFullLayout`，或者用一个标志记住是否
跑过一次完整排版。另外，渐进式结果（`IsProgressive`）的尾部还在生长：想画就画，但不要把它当成最终结果缓存
起来。第一次请求也可能在节点有尺寸之前就发出去了——若结果回来的内容是空的，下一次 `Resized` 再提交一遍即可。

## 常见坑

- **不要在渲染器里重新整形。** `GlyphRun` 就是测量结果；用文本 API 把字符串再画一遍，是第二次独立的塑形，
  它可能与排版不一致。一次塑形，多次绘制。
- **必须用布局给的基线。** 横排的行画在 `BaselineY` 上，竖排的列从元素的行内起点取笔位。自己给
  `Position.Y` 加上 ascent 会重复计算，而且换一次字体整行就会移位。见 [rendering.cn.md](rendering.cn.md)。
- **在主线程轮询。** Godot 资源（`Font`、`Texture2D`）属于主线程：在那里解析、在那里提交、也在那里绘制。
  结果本身是纯数据，任何线程读都安全。
- **从后台线程提交时要先解析字体。** `FontCatalog.Shared.EnsureResolved(elements)` 会在调用线程上完成这件事；
  服务器对每次完整排版也会做，但生产者在主线程之外提交时得自己先做（`ResolvedFontId` 才是排版线程可以碰的
  东西）。
- **用当前有效的 handle。** 关闭之后旧 handle 完全不会产出结果；检查 `IsHandleValid` 并新建一个。被拒绝的
  请求返回 `TypographyServer.NotSubmitted`，这与"还在处理中"是可区分的。
- **绝不要指望一个字符一个元素。** 一个源元素会变成一个簇一个元素，而一个簇可能包含多个 code unit。映射要
  走 `ClusterStart`/`ClusterEnd` 与 `SourceRange`，不要走元素次序。
