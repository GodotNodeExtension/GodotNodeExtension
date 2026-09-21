[英文](rendering.md) | **中文**

# 渲染排版结果

布局交给你的是一份算好的几何：一串按绘制次序排好的元素，每个都带着要画的已塑形字形、以及它们被测量时的位置。
所以渲染就是遍历这串元素——**不再测量，也不再塑形**。每个字段的含义见 [output-format.cn.md](output-format.cn.md)；
本文讲的是怎么*用*它们来绘制、命中测试和调试。

示例用 SkiaSharp，因为它能按字体面在指定位置画一个字形；但输出里没有任何东西是 Skia 专有的：只要能按位置放
字形的绘图 API 都能用，下面这些规则讲的是几何，不是某个库。

## 你会拿到什么

- `Elements` —— 要画的元素流，次序与位置都已排好。一个源元素通常会变成多个元素，一个簇一个。
- `Lines` —— 同一批元素按行分组，并带行级几何。行盒、命中测试与段落归组用它；里面的元素与 `Elements` 里的是
  同一批对象。
- `ContentSize` —— 内容的包围盒，用来定滚动区域大小。
- `LayoutLine.Spans` 与元素的 `SpanIndex` —— 元素落在行的哪一个可用区间里。

## 坐标与轴

- **内容原点**在内容框的左上角，也就是已经包含请求的 `Padding` 之后。无论哪种书写模式，X 向右、Y 向下，输出
  里的一切都相对这个原点；你自己的内边距、滚动偏移与缩放叠加在它之上。
- **行内与块向是两条轴，哪条 Godot 轴承载哪一条取决于书写模式。** 行内轴横排是 `+X`、竖排是 `+Y`；块向轴
  横排是 `+Y`、`VerticalRl` 是 `-X`（列向左推进）。`LayoutAxes` 把内容空间的点或尺寸换算成行内与块向标量，
  所以渲染代码很少需要问自己是哪种模式：

```csharp
LayoutAxes axes = new(WritingMode.VerticalRl, BlockExtentLimit: 800f);

float inlineStart = axes.Inline(element.Position);          // where the box starts along the column
float blockStart = axes.Block(element.Position);            // which column the box sits on
float blockExtent = axes.BlockExtent(element.Size);         // how thick the box is across it
```

- `Position` 是盒子**起始**的那个角，盒子先沿行内单位向量展开、再沿块向单位向量展开。因此在右到左的列里它是
  向**左**展开的：绝不要把它当成一个向右向下延伸的矩形来画或做命中测试。
- `BaselineY` 是**块向**轴上的坐标，等于 `Block(Position) + ascent`：横排下就是基线的 Y，竖排下是该列自身的
  坐标。横排的行把字形画在它**上面**，而且绝不要自己从字体度量推一个基线——这个字段存在的意义正是取代那件事，
  否则换一次字体整行都会移位。
- `GlyphRun.Origin` 是第一个字形的笔位。横排下它是内容空间里的一个点——从那里开始。竖排下它不是点：笔位改从
  元素的**行内起点**、落在列的**中线**上开始（见下）。
- `Glyph.Advance` 在两种书写模式下都是正数，所以用它推进笔位时，笔永远沿着该串自己的阅读方向走。
- `GlyphRun.Rotation` 说明这一串是否被旋转过：`None` 表示字形正立地跟着行或列，`ClockwiseQuarter` 表示每个
  字形绕笔位顺时针转四分之一圈——列里的拉丁词或数字就是这样排的。

## 绘制字形串

整份契约一句话说完：笔位从该串的起点开始，逐个走字形，每次加上 `Advance`，画之前再加上字形自己的
`OffsetX`/`OffsetY`。按 `FontId` 把字形分组能减少绘制调用，因为一个串未必只用一种字体面。

### 横排

```csharp
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using SkiaSharp;

// One laid-out text element: draw the glyphs it carries, in the order it carries them.
static void DrawRun(SKCanvas canvas, in GlyphRun run, SKPaint paint)
{
    float baseline = run.Origin.Y;   // the pen on the baseline of the first glyph
    float pen = run.Origin.X;

    var builder = new SKTextBlobBuilder();
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
        var positions = new SKPoint[count];
        float x = pen;

        for (int i = 0; i < count; i++)
        {
            Glyph glyph = run.Glyphs[start + i];
            positions[i] = new SKPoint(x + glyph.OffsetX, baseline + glyph.OffsetY);

            // Drawing APIs are usually 16-bit: a glyph index beyond that still advances the pen.
            glyphs[i] = glyph.Id <= ushort.MaxValue ? (ushort)glyph.Id : (ushort)0;
            x += glyph.Advance;
        }

        if (FontCatalog.Shared.TryGetTypeface(fontId, out SKTypeface typeface) && typeface is not null)
        {
            using var font = new SKFont(typeface, run.FontSize);
            builder.AddPositionedRun(glyphs, font, positions);
        }

        pen = x;
    }

    SKTextBlob blob = builder.Build();
    if (blob is not null)
        canvas.DrawText(blob, 0f, 0f, paint);
}
```

`DisplayText` 非空时，就是布局真正测量并替换过的那份文本（引号、省略号、镜像括号）——字形串本身已经反映了它，
所以这里没有别的事要做。完全没有字形串的时候（文字特效、用字符串绘制的语法高亮片段），就用你自己的文本 API
去画 `DisplayText ?? Text`；那条路是例外，不是常规。

### 列

竖排下笔位沿列向**下**走：`Advance` 让它沿 +Y 推进，`OffsetX` 是一个字形唯一的横向位移，而 `OffsetY` 是到字形
竖排原点的距离、塑形层报告的是取反的值——所以可画的基线是 `pen - OffsetY`，不是 `pen + OffsetY`。列坐在元素的
块起始边上、厚度等于盒子：它的中线在盒子内侧半个块向跨度处，而笔位从元素的行内起点（盒子顶端）开始，
**不**加任何字体度量。

```csharp
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using SkiaSharp;

// One laid-out text element of a vertical layout.
static void DrawColumn(SKCanvas canvas, LayoutElement element, in GlyphRun run, SKPaint paint)
{
    // The box grows leftward from its position, so the centre line is half its block extent inside it.
    float centre = element.Position.X - (element.Size.X * 0.5f);
    float pen = element.Position.Y;   // the element's own inline start: no ascent is added here

    using var builder = new SKTextBlobBuilder();
    int index = 0;

    while (index < run.Glyphs.Length)
    {
        ulong fontId = run.Glyphs[index].FontId;
        int start = index;
        while (index < run.Glyphs.Length && run.Glyphs[index].FontId == fontId)
            index++;

        int count = index - start;
        var glyphs = new ushort[count];
        var positions = new SKPoint[count];
        float y = pen;

        for (int i = 0; i < count; i++)
        {
            Glyph glyph = run.Glyphs[start + i];
            positions[i] = new SKPoint(centre + glyph.OffsetX, y - glyph.OffsetY);
            glyphs[i] = glyph.Id <= ushort.MaxValue ? (ushort)glyph.Id : (ushort)0;
            y += glyph.Advance;
        }

        if (FontCatalog.Shared.TryGetTypeface(fontId, out SKTypeface typeface) && typeface is not null)
        {
            using var font = new SKFont(typeface, run.FontSize);
            builder.AddPositionedRun(glyphs, font, positions);
        }

        pen = y;
    }

    SKTextBlob blob = builder.Build();
    if (blob is not null)
        canvas.DrawText(blob, 0f, 0f, paint);
}
```

### 旋转的字形串

`Rotation = ClockwiseQuarter` 的串是列里的拉丁词或数字：每个字形保留它沿行时本该有的基线，整体顺时针转四分之一
圈，于是歪着头读就能顺着列读下去。文本 blob 不能逐个旋转字形，所以这条路一个字形一个字形地画——而且因为转过
的字形从笔位朝 +X 生长，笔位要先退回约 `0.375 × FontSize`，字形才留在列内。

```csharp
// Inside the loop above, for a run whose Rotation is ClockwiseQuarter.
using SKPath path = font.GetGlyphPath((ushort)glyph.Id);

if (path is not null)
{
    canvas.Save();
    canvas.Translate(centre + glyph.OffsetX, pen - glyph.OffsetY);
    canvas.RotateDegrees(90f);
    canvas.DrawPath(path, paint);
    canvas.Restore();
}
```

## 元素次序

按拿到的次序画。这串元素的排序已经保证了在下面的先出现：背景或块装饰在它托着的文字之前，而一个文字元素的叠加层
跟在它后面。

```text
one line of the stream, in drawing order

  Rect / Line                 a merged inline background, a block's background or border
  Text, with a GlyphRun       the line's clusters: draw each one at its own origin
  Text, with a HyphenRun      the hyphen the line broke with, drawn after that element's glyphs
  Text, with Emphasis         the emphasis mark, on its own side of the character
  Text, with Ruby             the annotation, in its band or beside its base character
  Image / ExtensionRegion     the inline objects the caller owns
  Action                      nothing: a trigger point, not ink
```

按 `Type` 分派，并且把不可见的元素当作不存在：尺寸为零的 `Rect`、`Line`、`Image` 或 `ExtensionRegion` 什么都
不画，`Action` 永远不画，而没有字形串的文字元素在字形这条路上也什么都不画（它就是上面说的字符串路径例外）。

```csharp compile
static bool NeedsDrawing(LayoutElement element) => element.Type switch
{
    DrawElement.ElementType.Action => false,                        // a trigger point, not ink
    DrawElement.ElementType.Text => element.GlyphRun is not null,   // text with no run draws nothing
    _ => element.Size.X > 0f || element.Size.Y > 0f,                // a box needs a size to be visible
};
```

`Reason` 非空的元素是装配行时合成或展开出来的——合并后的背景、块的装饰与标记、断词处的连字符、块被展平的内容。
像其它元素一样画它们；知道它们存在的理由有两个：调试叠加正是靠它们解释自己，以及 `Reason` 非空**且**
`SourceRange` 为空的元素不覆盖任何源文本，因此必须排除在选区、复制与命中测试之外。

`BackgroundFillLine` 是唯一一个对次序敏感的细节：这种背景填满整行行盒而不是文字本身，所以要沿块向轴画在
`LayoutLine.Y .. Y + Height` 上，而不是画在元素自己的盒子上。`SyntaxSpans` 非空时按片段覆盖元素的 `Color`；
而 `Hanging` 的标记按约定就是要探出行末边缘的文字：画在它该在的地方，不要把它夹回行内。

## 注音与着重号

两者出来时都已经放好、也已经塑形或量好，并且带着绘制它们所需的几何：注音自带字形，着重号是一个字符、自带字号
与中心点。从语言重新推一遍，只会得到第二个、而且可能不一致的答案。

```csharp
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using SkiaSharp;

// An annotation: its glyphs, walked along its own reading direction.
static void DrawAnnotation(SKCanvas canvas, in RubyAnnotation ruby, SKPaint paint)
{
    if (ruby.Glyphs is null || ruby.Glyphs.Length == 0) return;

    if (!FontCatalog.Shared.TryGetTypeface(ruby.FontId, out SKTypeface typeface) || typeface is null) return;

    using var font = new SKFont(typeface, ruby.FontSize);

    // Orientation is the annotation's own direction, independent of the writing mode: a horizontal
    // paragraph can carry a column of symbols (Bopomofo, clreq §5.5.3.1).
    bool down = ruby.Orientation == RubyOrientation.Vertical;
    float pen = down ? ruby.BaselineY : ruby.X;

    foreach (Glyph glyph in ruby.Glyphs)
    {
        SKPoint position = down
            ? new SKPoint(ruby.X + glyph.OffsetX, pen - glyph.OffsetY)          // down its own column
            : new SKPoint(pen + glyph.OffsetX, ruby.BaselineY + glyph.OffsetY); // along the line
        pen += glyph.Advance;

        using var one = new SKTextBlobBuilder();
        one.AddPositionedRun([(ushort)glyph.Id], font, [position]);
        SKTextBlob blob = one.Build();
        if (blob is not null)
            canvas.DrawText(blob, 0f, 0f, paint);
    }

    // ruby.Width is the annotation's extent along that direction and ruby.BandWidth the room the layout
    // reserved for it across that direction: centre it in that room instead of inventing a band when it is 0.
}

// An emphasis mark: one character, centred on the point the layout gave.
static void DrawEmphasis(SKCanvas canvas, in EmphasisMarkGeometry mark, SKFont markFont, SKPaint paint)
{
    float halfWidth = markFont.MeasureText(mark.Mark) * 0.5f;
    SKFontMetrics metrics = markFont.Metrics;

    // CentreY is the centre of the mark on the block axis; a drawing API takes a baseline, so the mark
    // moves up by half its own height. Mark.Side is the side of the character the mark belongs on.
    float baseline = mark.CenterY - ((metrics.Ascent + metrics.Descent) * 0.5f);

    canvas.DrawText(mark.Mark, mark.X - halfWidth, baseline, markFont, paint);
}
```

`HyphenRun` 和别的字形串一样：用同一次行走、从它自己的起点开始、在该元素自己的字形之后画出来。连字符也可能以
`Reason = HyphenationBreak` 的独立元素形式出现——那时画的是那个元素，就不要再把这个串画一遍。承载这些叠加层的
行会因为它们变高（`LayoutLine.ExtraAbove` / `ExtraBelow` 已经计在 `Height` 里），所以按 `LayoutLine` 画行盒时
不需要再加任何东西。

## 命中测试与选区

两个方向，输出都支持。

**从一个点到元素。** 遍历各行，找到块向范围包含该点的行，再找到盒子包含它的元素——要用元素自己报告的区间，因为
一行可能有不止一个区间，而元素未必在第一个里：

```csharp compile
static bool Contains(LayoutAxes axes, LayoutElement element, Vector2 point)
{
    // The box starts at Position and grows along the inline axis first, then along the block axis.
    float inline = axes.Inline(point - element.Position);
    float block = axes.BlockDelta(point - element.Position);

    return inline >= 0f && inline <= axes.Inline(element.Size)
        && block >= 0f && block <= axes.BlockExtent(element.Size);
}
```

```csharp compile
static float InlineStart(LayoutLine line, LayoutElement element)
{
    // The element sits in the interval it was placed in; LineLeft describes the first interval only.
    LineSpan span = line.Spans[element.SpanIndex];

    return span.Left + (element.SpanIndex == 0 ? line.LineIndent : 0f);
}
```

**从一个元素回到文本。** `SourceIndex` 指出这块排版结果来自哪个源元素，而 `SourceRange` 是相对*那个*元素文本的
**半开**区间 `[Start, End)`，单位是 UTF-16 code unit——不是全文档坐标，也不是字符数：长度 *N* 覆盖的可见字符
可能比 *N* 少。`ClusterStart`/`ClusterEnd` 是簇级的映射，凡是与单个字形有关的问题（插入点位置、这个字符由哪个
字形覆盖）都该用它，因为塑形会把多个字符合成一个字形、也会把一个字符合成多个字形。
`CharacterIndex`/`CharacterCount` 是用于选区与打字机播放的全局单调坐标：把 `CharacterCount` 与
`Glyph.ClusterStart` 相比，就能只显示一段前缀。

```csharp compile
static TextRange UnionOfOneSourceElement(LayoutLine line)
{
    int start = int.MaxValue;
    int end = 0;

    foreach (LayoutElement element in line.Elements)
    {
        // Synthesised pieces (an inserted hyphen, a decoration) cover no source text of their own.
        if (element.Reason is not null || element.SourceRange.IsEmpty) continue;

        start = Math.Min(start, element.SourceRange.Start);
        end = Math.Max(end, element.SourceRange.End);
    }

    return start == int.MaxValue ? TextRange.Empty : new TextRange(start, end);
}
```

两条规则让这份映射保持可靠：元素次序是**绘制**次序（右到左文本与混排内容会被排成视觉序），所以绝不要用元素在
`Elements` 里的位置去映射文本；而 `Reason` 非空、`SourceRange` 为空的元素没有自己的文本，不属于任何选区。

## 调试叠加

排版结果不对，几乎总是几何问题，而输出直接回答了它——每个元素都说明自己在哪、属于哪一行、以及为什么在那儿。
值得做的叠加，按收益排序：

```text
line boxes        LayoutLine.Y .. Y + Height, along the block axis
baselines         BaselineY of every element that has one, and a column's centre line across it
element boxes     Position, then Size along the inline axis and the block axis
line intervals    Spans, plus LineLeft / LineRight / LineIndent for the first one
labels            CharClass, SpanIndex, LineIndex and Reason on each box
counters          LineCount, ProhibitedBreakSkips and Timings for the frame
```

其中计数器最便宜、也解释得最多：`ProhibitedBreakSkips` 说明某一行结束得比它的宽度允许的更早，因为禁则拒绝了
那个断点；`Timings.CompileMs` 说明一次重排到底有没有复用已准备的内容（为 `0` 就是复用了）。

## 必须与不得

**必须**

- 画出 `GlyphRun` 携带的字形，按次序，用 `Advance` 推进笔位，并加上 `OffsetX`/`OffsetY`。
- 横排的行把字形放在 `BaselineY` 上；竖排的列从元素的行内起点、落在列中线上取笔位。
- 通过 `FontId` 解析字形所属的字体面并按它分批：一个串可能混用多种字体面。
- 按 `Elements` 的次序画，并且每个元素都要画——包括 `Reason` 非空的那些。
- 注音、着重号与连字符都按它们各自的几何来画。
- 通过 `SourceIndex`、`SourceRange` 与簇下标把排版映射回文本。
- 一行有不止一个区间时，用 `Spans` 与元素的 `SpanIndex`。
- `Hanging` 的标记画在行盒外，并且不要为叠加层预留空间：行盒已经为它长过了。

**不得**

- 重新塑形，也不要重新测量。已经有了字形串，`Text` 就只是信息。
- 在竖排下把 `BaselineY` 当作 Y 读：它是块向轴上的坐标。
- 给 `Position.Y` 加字体 ascent 来凑基线，也不要给列的笔位加字体度量。
- 重新分类字符——`CharClass` 就是语言规则给出的分类。
- 把 `Reason` 非空且 `SourceRange` 为空的元素放进选区或命中测试。
- 假设一个字符一个元素、或一个元素一个字形。
- 假设每个元素都有基线：`NaN` 表示布局从未算过，此时以字体度量为兜底。
- 在 `DisplayText` 非空时改画 `Text`；也不要给 `BandWidth` 为 `0` 的注音凭空发明一条带。
