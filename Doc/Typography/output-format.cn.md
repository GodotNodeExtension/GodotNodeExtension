# Typography 输出格式

排版引擎交给渲染器的是一串扁平化的排版元素、这些元素所在的行，以及它们几何背后的决策。本文是这份输出的
格式说明：每个字段的含义、单位、哪个取值是哨兵，以及消费者必须或不得做什么。本文只描述格式——几何是怎么
算出来的不属于这里的契约。

## 1. 单位与坐标空间

| 项 | 约定 |
|---|---|
| 长度 | 像素（`float`）：`Position`、`Size`、`BaselineY`、`Advance`、`Ascent`、`Descent`、各种缩进与带宽。em 只存在于语言参数里，绝不出现在输出上。 |
| 内容原点 | 内容框的左上角，即已经包含请求的 `Padding` 之后。所有位置都相对它；渲染器在此基础上再叠加自己的内边距与滚动偏移。 |
| 内容空间的轴 | 无论哪种书写模式，都是 X 向右、Y 向下。 |
| 行内轴（inline） | 横排为 `+X`，竖排为 `+Y`。 |
| 块向轴（block） | 横排为 `+Y`；`VerticalRl` 为 `-X`（列向左推进），`VerticalLr` 为 `+X`。 |
| 文本区间 | UTF-16 code unit 下标，半开区间 `[Start, End)`（`TextRange`），相对 `LayoutElement.SourceIndex` 所指的源元素文本，不是全文档坐标。 |
| 簇（cluster）下标 | 半开，且按产生它的那次排版上下文编号：嵌套的块排版会从 0 重新编号。 |
| 基线 | `BaselineY` 是**块向**轴上的坐标，等于 `Block(Position) + ascent`。横排下即 `Position.Y + ascent`；`VerticalRl` 下即 `BlockExtentLimit - Position.X`，也就是该列自身的坐标。 |

`Position` 是元素盒子**起始**的那个角；盒子从这里沿行内单位向量、再沿块向单位向量展开。因此竖排下盒子是向
**左**展开的，画盒子或做命中测试时不得假设它是一个向右/向下延伸的矩形。

画**行**的渲染器把字形放在 `BaselineY` 上，而不是从字体度量重新推一个基线：否则换一次字体整行都会移位。画
**列**的渲染器没有水平基线可放——笔位从元素的行内起点沿列下走，字形自带偏移（§5）——所以竖排路径不得把
`BaselineY` 当作 Y 使用。

```csharp
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;

// One laid-out element plus the writing mode of the request that produced it.
LayoutElement element = default;
var axes = new LayoutAxes(WritingMode.VerticalRl, BlockExtentLimit: 800f);

// Both axes of the box, as scalars, in the mode the layout used.
float inlineStart = axes.Inline(element.Position);   // pen start along the column
float blockStart = axes.Block(element.Position);     // which column the box sits in

// BaselineY is that block coordinate plus the ascent, not a Y.
float baseline = axes.Block(element.Position) + 12f;
```

## 2. `LayoutResult`

| 字段 | 类型 | 含义 | 消费规则 |
|---|---|---|---|
| `Handle` | `LayoutHandle` | 本结果所属的客户端句柄。 | 与提交请求时用的句柄比对。被关停作废的句柄根本不会产生结果，所以不要在上面无限等待。 |
| `RequestId` | `long` | 提交请求时返回的编号。 | 只对最新一次请求的编号作出响应；被取代的结果是直接丢弃，而不是排队。 |
| `IsProgressive` | `bool` | 流式追加的结果（部分结果）为 `true`，最终结果为 `false`。 | 部分结果结尾还可能继续增长，不要把它冻结。 |
| `Elements` | `List<LayoutElement>?` | 扁平化后的元素流，按绘制顺序——背景在它所衬的文字之前。 | 主要输出，按顺序绘制。只有在 `Error` 有值时才是 `null`。 |
| `Lines` | `IReadOnlyList<LayoutLine>?` | 排版好的行，每行持有落在它上面的元素。 | 用于行级几何与命中测试；元素位置与 `Elements` 里是同一批对象。 |
| `ContentSize` | `Vector2` | 内容在各轴上的范围，映射到内容空间：inline 范围（最长行伸到多远）与 block 范围（最后一行的末端），各加末尾的 `Padding`。它覆盖的是**渲染器实际绘制的东西**，不只是元素自己的盒子：注音可以比它标的字更宽、着重号则居中在盒子之外，两者都算在范围内。 | 用它给滚动区域或绘制表面定尺寸。横排下这一对读作宽与高；竖排下第一个分量是列延伸到多远、第二个是最长列的长度。 |
| `Error` | `string?` | 失败信息（重排时没有可复用的缓存内容也走这里）。 | 非空表示 `Elements`/`Lines` 缺失：应上报而不是绘制。 |
| `IsCancelled` | `bool` | 请求在完成前被取代。 | 丢弃该结果。 |
| `LineCount` | `int` | 结果中的行数。 | 诊断用。 |
| `ElementCount` | `int` | 结果中的元素数。 | 诊断用。 |
| `ProhibitedBreakSkips` | `int` | 因行首/行末禁则或不可拆对而被否决的断点候选数。 | 诊断用：数值偏高能解释“这一行远没排满就换行了”。 |
| `Timings` | `LayoutTimings` | 各相位的墙钟耗时（见下）。 | 诊断用，永不作为排版输入。 |

结果上刻意没有边界数组：边界模型（§8）是编译期输出，其决策已经体现在几何里。结果只报告它的代价
（`Timings.BoundaryMs`）与被否决的候选数（`ProhibitedBreakSkips`）。

### 2.1 `LayoutTimings`

| 相位 | 含义 | 说明 |
|---|---|---|
| `PrepareMs` | 切分、分类、塑形、与宽度无关的度量。 | 昂贵且可缓存的那一半。复用了缓存内容的重排为 0。 |
| `BoundaryMs` | 构建边界决策（§8）。 | 重排为 0：边界与宽度无关，会被复用。 |
| `BreakMs` | 断行。 | |
| `AdjustMs` | 行调整（挤压、拉伸、对齐、网格、制表位）。 | |
| `FlattenMs` | 把行摊平成输出元素流。 | |
| `CompileMs` | 派生值：`PrepareMs + BoundaryMs`。 | 与宽度无关的那一半：只改宽度的重排必须报告 0。 |
| `TotalMs` | 派生值：五个相位之和。 | |

```text
LayoutResult
├── Elements[0..n)     LayoutElement      flat, drawing order, already positioned
├── Lines[0..n)        LayoutLine         Elements grouped per line, line box metrics
│   ├── Spans[0..n)    LineSpan           usable intervals along the inline axis
│   └── Elements[0..n) LayoutElement      the same elements as above
└── ContentSize        Vector2            content-space bounding box
```

## 3. `LayoutElement` 字段

结构体的全部字段，按类型声明顺序。下标、计数、枚举、布尔和颜色行不写单位（`--`）；没有特殊取值时哨兵列写
`--`。

| 字段 | 类型 | 单位 | 哨兵 | 含义 | 消费规则 |
|---|---|---|---|---|---|
| `SourceIndex` | `int` | -- | -- | 本元素派生自哪个源元素。 | `SourceRange` 相对的是**那个**元素的文本；把排版映射回源文本要靠这一对，而不是元素顺序。 |
| `Position` | `Vector2` | px | -- | 元素盒子的起始角，相对内容原点。盒子沿行内单位向量展开，再沿块向单位向量展开（右向左的列中即向左）。 | 不要假设它是向右/向下的矩形：绘制与命中测试都要按书写模式处理。 |
| `BaselineY` | `float` | px，块向轴 | `NaN` = 未计算 | 文本基线的块向坐标：`Block(Position) + ascent`。 | 横排：把字形放在它上面，不要重新推导。竖排：不得当作 Y 使用。`NaN` 表示该元素没有经过断行器，此时按字体度量回退，与这个字段出现之前完全一致。 |
| `Size` | `Vector2` | px | 标记文本元素由渲染器定尺寸时是 `Vector2.Zero` | 排版后的最终盒子尺寸，内容空间口径。 | 这是盒子而不是墨迹：对齐/两端对齐会加宽盒子而不移动字形，需要字形自然宽度时用 `GlyphRun.Width`。 |
| `CharClass` | `CharacterClass` | -- | 装配阶段合成的元素为 `NonText` | 该簇排版时所依据的字符类别。 | 直接读它，不要重新分类文本；分类是语言规则。 |
| `SourceRange` | `TextRange` | UTF-16 code unit，半开 | 空区间 = 不覆盖任何源文本 | 本元素覆盖的源文本，相对 `SourceIndex` 元素的文本。 | 用于选择、复制与打字机进度。不要假设 `Length` 等于可见字符数。 |
| `ClusterStart` | `int` | 下标，上下文局部 | `-1` = 未知 | 覆盖的第一个排版簇的下标。簇是最小的不可拆排版单位，永不跨换行。 | 通过簇下标把排版映射回源文本，而不是通过元素顺序。该下标有序但非全文档唯一：嵌套块排版会从 0 重新编号，没上过行的段会留下空隙。 |
| `ClusterEnd` | `int` | 下标，上下文局部 | `-1` = 未知 | 覆盖的最后一个簇之后一位；目前等于 `ClusterStart + 1`。 | 覆盖检查时保留为区间；将来的簇模型可能一次覆盖多个簇。 |
| `SpanIndex` | `int` | 下标 | 普通单区间行上为 `0` | 本元素被放进的区间在行的 `Spans` 中的下标。 | 把元素放在它被放进的那个区间里；`LineLeft`/`LineRight` 只描述第一个区间。 |
| `LineIndex` | `int` | 下标 | `-1` = 未知 | 本元素所在 `LayoutLine` 的下标。由块自身子排版展开出来的元素带的却是块**内部**行的下标，并带有 `Reason` 标记。 | 先看 `Reason` 区分文档行与块内部行，再决定是否相信这个下标。 |
| `Direction` | `TextDirection` | -- | -- | 本元素生效的基础文本方向。 | 告诉消费者元素内容从区间的哪一侧开始。目前这里只写 `LeftToRight`；右向左 run 的视觉顺序体现为行内元素的先后顺序。 |
| `DisplayText` | `string?` | -- | `null` = 与源文本一致 | 发生显示形态替换时（引号体系、省略号、句点、镜像括号）实际要画的文本。 | 非空时画它。替换永不改变 `SourceRange`。 |
| `GlyphRun` | `GlyphRun?` | -- | `null` = 该元素自身没有文本（背景、线、标记） | 从 run 自身原点定位的已塑形字形。 | 画这些字形，绝不再塑形一次——这正是“画出来的”与“量出来的”不会漂移的原因。 |
| `Reason` | `string?` | -- | `null` = 断行器产出的普通簇；非空 = 被行首行末规则标注，或由装配阶段合成/展开 | 该元素几何如此的原因。**合成**的取值：`MergedInlineBackground`、`BlockDecoration:Background`、`BlockDecoration:LeftBorder`、`BlockDecoration:Marker`、`BlockDecoration:MarkerElement`、`FixedSizeBlockContent`、`AutoSizeBlockContent`、`HyphenationBreak`。**行首行末规则标注的真文本**：`HangingPunctuation`（标点悬出行末边缘）、`OpeningBracketHalfWidth`（行首开括号让出始侧半字）。 | 合成的那批自身不携带源文本（断行处插入的连字符 `SourceRange` 为空、`ClusterStart == -1`），在选择、复制、命中测试里应当跳过。**后两个是真文本**：text、range、cluster 索引都是真的，跳过它们会丢掉读者看得见的字符。它们也是 dump 能自我解释的原因。 |
| `Type` | `DrawElement.ElementType` | -- | -- | 元素类型：`Text`、`Rect`、`Line`、`Image`、`ExtensionRegion`、`Action`。 | 据此分派绘制。 |
| `Color` | `Color` | -- | -- | 绘制用颜色。 | 设置了 `SyntaxSpans` 时按 span 覆盖。 |
| `Text` | `string?` | -- | 非文本元素为 `null` | 要渲染的文本内容。 | 只要有 `GlyphRun` 就只是参考信息；设置了 `DisplayText` 时画的是 `DisplayText`。 |
| `Font` | `Font?` | -- | 非文本元素为 `null` | 文本渲染用字体资源。 | 只在回退路径（没有 `GlyphRun`）和装饰上需要；已塑形的 run 用自己的 id 指认字体。 |
| `FontSize` | `int` | px | -- | 文本渲染字号。 | 仅用于度量回退：不要拿它重新塑形。 |
| `IsBold` | `bool` | -- | -- | 请求了粗体。 | 已经烙进塑形结果。 |
| `IsItalic` | `bool` | -- | -- | 请求了斜体。 | 同上。 |
| `IsStrikethrough` | `bool` | -- | -- | 文本带删除线装饰。 | 横排沿线画、竖排沿列画这条装饰。 |
| `IsUnderline` | `bool` | -- | -- | 文本带下划线装饰。 | 同上；下划线超出降部所需的空间已经计入行的 `ExtraBelow`。 |
| `IsSubscript` | `bool` | -- | -- | 文本按下标排。 | 按语言的量做位移；盒子和基线已经反映了它。 |
| `IsSuperscript` | `bool` | -- | -- | 文本按上标排。 | 同上。 |
| `RubyText` | `string?` | -- | `null` = 无注音 | 源形态的注音文本。 | 绘制用 `Ruby`；这里只是调用方请求的文本，不含几何。 |
| `Ruby` | `RubyAnnotation?` | -- | `null` = 无注音 | 本元素的注音被放在哪里，连同它自己的已塑形字形。 | 按这份几何绘制；不再度量或塑形注音（§6）。 |
| `HyphenRun` | `GlyphRun?` | -- | `null` = 此处无连字符 | 当行在词内断开时，本元素结尾的那个连字符。 | 在本元素自己的字形之后、按它自己的原点绘制；一个带 `Reason = HyphenationBreak` 的独立元素可能已经把它呈现出来。 |
| `Hanging` | `bool` | -- | `false` | 该元素的标记悬挂在行尾边缘之外，这是规范允许的。 | 画在行盒之外，不要裁回；它是普通文本，不是排版错误。 |
| `EmphasisMark` | `EmphasisMarkStyle` | -- | `None` = 无标记 | 从源元素带过来的着重号样式：`None`、`Dot`、`SesameDot`。 | 参考信息：几何在 `Emphasis` 里。 |
| `Emphasis` | `EmphasisMarkGeometry?` | -- | `null` = 无 | 本元素的着重号被放在哪里。 | 按那个中心点与字号居中绘制（§7）；不要按语言重新推导侧别。 |
| `SyntaxSpans` | `List<ColoredSpan>?` | -- | `null` = 用 `Color` | 文本元素内部的逐 span 语法着色。 | 设置时按各 span 自己的颜色绘制，而不是元素颜色。 |
| `Texture` | `Texture2D?` | -- | `null` = 无 | 要绘制的贴图。 | 用于 `Image` 元素。 |
| `LinkUrl` | `string?` | -- | `null` = 不是链接 | 链接命中测试用的 URL。 | 设置时把元素盒子当作链接做命中测试。 |
| `ElementId` | `int` | -- | -- | 选择跟踪用的唯一元素 id。 | 跨帧的选择身份；与 `SourceRange` 合起来标识一段文本。 |
| `ExtensionId` | `int` | 下标 | `-1` = 不是扩展 | 生效的扩展区域下标。 | 据此分派 `ExtensionRegion` 的绘制。 |
| `ExtensionContent` | `string?` | -- | `null` = 无 | 传给块扩展的原始内容。 | |
| `ActionTag` | `string?` | -- | `null` = 无动作 | 动作标识。 | 元素被激活时触发该动作。 |
| `ActionPayload` | `Variant` | -- | 默认 `Variant` = 无载荷 | 随动作信号传出的可选数据。 | |
| `CharacterIndex` | `int` | 字符下标 | -- | 打字机进度用的全局字符下标。 | 逐簇重算（源下标 + 簇内偏移），于是同一个源元素变出的多个元素之间仍然单调。合成元素可能留在 `0`。 |
| `CharacterCount` | `int` | 字符数 | 非文本元素为 `0` | 本元素的字符数。 | 与 `Glyph.ClusterStart` 比较来揭示文本前缀；带 `Reason` 的合成元素不计入。 |
| `TextEffectId` | `int` | 下标 | `-1` = 无效果 | 文本效果注册表下标。 | 把该效果作用于元素的字形。 |
| `CornerRadius` | `float` | px | `0` = 直角 | 圆角矩形渲染的圆角半径。 | |
| `BackgroundColor` | `Color?` | -- | `null` = 无背景 | 带行内装饰（行内代码、高亮）的文本元素的背景色。 | 在文字后面画一个填充矩形。被合并成一个 `Rect`（`Reason = MergedInlineBackground`）的那些文本元素上，此字段会被清空。 |
| `BackgroundPadding` | `Vector2` | px | `Vector2.Zero` | 背景渲染用的额外内边距，对称施加（X、Y）。 | 按它撑大背景矩形；排版宽度里已经预留过。 |
| `BackgroundCornerRadius` | `float` | px | `0` = 直角 | 背景矩形的圆角半径。 | |
| `BackgroundFillLine` | `bool` | -- | `false` | 背景矩形填满整个行盒，而不是贴合文本。 | 用行的块向范围，而不是元素盒子。 |
| `SubLines` | `List<LayoutLine>?` | -- | `null` = 不是自动尺寸块 | 自动尺寸块的子排版行；其位置相对块的内容区（已经偏移了左缩进与内边距）。 | 为了块展开而携带；块内容已经以普通元素的形式摊平交给渲染器，所以不要再把这些子行也画一遍。 |
| `BlockInfo` | `BlockLayout?` | -- | `null` = 不是块 | 块排版信息，同样为了展开而携带。 | 同上：在输出上只是参考信息。 |

```csharp
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;

// 装配阶段的 reason 表示"这段文本不属于本文档"；行首行末规则标注的则是真文本，
// 所以这两个取值是源字符本身，不能一并过滤掉。
static readonly HashSet<string> SourceTextReasons = ["HangingPunctuation", "OpeningBracketHalfWidth"];

static bool IsSynthesised(LayoutElement element) =>
    element.Reason is { } reason && !SourceTextReasons.Contains(reason);

static bool CoversSource(LayoutElement element) => !element.SourceRange.IsEmpty;

static float BaselineOrFallback(LayoutElement element, float ascentFromFont) =>
    float.IsNaN(element.BaselineY) ? element.Position.Y + ascentFromFont : element.BaselineY;
```

## 4. `LayoutLine` 与 `LineSpan`

行盒是沿块向轴表达的，它的度量也都是块向标量。`Ascent` 与 `Descent` 是行内各元素在该轴上的最大值，行上每条
基线都向内伸进 `ExtraAbove`，所以带注音的一行即便作者没给行距也不会与邻行重叠。

| 字段 | 类型 | 单位 | 含义 | 消费规则 |
|---|---|---|---|---|
| `Y` | `float` | px，块向轴 | 该行起始边在块向轴上的坐标：横排即 Y，竖排即该列自身的坐标。 | 与块向坐标（`BaselineY`、`Block(Position)`）比较，竖排下永远不要与 Y 比较。 |
| `Height` | `float` | px，块向范围 | 行盒：`ExtraAbove + Ascent + Descent + ExtraBelow + LineSpacing`（正的行距在这之后再加上）。 | 用于行级命中测试与 `BackgroundFillLine`；不要用字体度量重算。 |
| `ExtraAbove` | `float` | px | 文本盒在块向**起始**侧之外需要的空间：注音带，或旁置注音列伸出盒子之外的那部分。 | 属于 `Height`；带注音的行因此更高。渲染器只需通过 `Height` 感知它。 |
| `ExtraBelow` | `float` | px | 块向**末端**侧在 `Descent` 之外需要的空间：字下的着重号、下划线，或注音列的伸出部分。 | 同上。 |
| `Ascent` | `float` | px，块向轴 | 行内所有元素的最大升部，沿块向轴（盒子在基线之前贡献的部分）。 | 块向量；竖排下不要把它加到 Y 上。 |
| `Descent` | `float` | px，块向轴 | 行内所有元素的最大降部。 | 同上。 |
| `Elements` | `List<LayoutElement>` | -- | 落在这一行上的元素，按显示顺序。 | 已经定位好了：按原位置绘制。 |
| `IsFrozen` | `bool` | -- | 该行已定稿、不再变化（流式模式）。 | 冻结的行可以被消费者缓存。 |
| `ParagraphIndex` | `int` | 下标 | 该行所属段落的下标。 | 据此把行归入段落。 |
| `IsFirstLineOfParagraph` | `bool` | -- | 这是其段落的第一行。 | 诊断用；它蕴含的缩进已经在 `LineIndent` 里。 |
| `LineLeft` | `float` | px，行内轴 | 该行可用区间在行内轴上的起点，相对内容原点。已经包含内边距与段落缩进，并被绕排区域收窄。 | 只描述**第一个**区间。定位任何元素之前先看元素的 `SpanIndex`。 |
| `LineRight` | `float` | px，行内轴 | 该区间在行内轴上的终点（不含）。 | 同样只描述第一个区间。 |
| `Spans` | `LineSpan[]` | px | 该行提供的区间，自左向右、互不重叠——普通行一个，绕排只遮住行的一部分时会多于一个。它是该块向坐标上可用空间的穷尽有序划分。 | 每个元素都要放进它被放进的区间，否则第二个区间的文字会被画到第一个区间里。 |
| `LineIndent` | `float` | px | 该行内容相对 `LineLeft` 额外被推开的距离：段落的行首缩进，加上行首注音比它注的字更宽时该行让出的空间。 | 内容从 `LineLeft + LineIndent` 开始。它是行几何的一部分，重新排布一行的消费者必须从这里起算，而不是从 `LineLeft`。 |

| `LineSpan` 字段 | 类型 | 单位 | 含义 |
|---|---|---|---|
| `Left` | `float` | px，行内轴 | 区间左边界，包含。 |
| `Right` | `float` | px，行内轴 | 右边界，不含。 |
| `Width` | `float` | px | 派生值：`Right - Left`。 |

```text
line box along the block axis
  ExtraAbove        annotation band above the text box (block-start side)
  Ascent            max ascent of the line's elements
  ----------------  every baseline on the line sits on this line
  Descent           max descent of the line's elements
  ExtraBelow        emphasis mark below, underline overhang, annotation column overhang
  LineSpacing       extra spacing the request asked for
  ---------------------------------------------------------------
  Height = ExtraAbove + Ascent + Descent + ExtraBelow + LineSpacing

inline extent
  Spans[0]           the first interval: LineLeft .. LineRight
  Spans[1..n)        further intervals, for text flowing around an exclusion
  LineIndent         offset from LineLeft at which the content of the first interval starts
```

## 5. `GlyphRun` 与 `Glyph`

| `GlyphRun` 字段 | 类型 | 单位 | 含义 | 消费规则 |
|---|---|---|---|---|
| `FontId` | `ulong` | 字体目录 id | 这些字形所属的已解析字体，由字体目录发放。 | 通过目录把它解析成平台字体。一个 run 不一定只用一张字面：回退路径会用别的字面塑形一部分文本，所以每个字形都自带 `FontId`。 |
| `FontSize` | `float` | px | 塑形该 run 时使用的字号。 | 按这个尺寸光栅化字面；不要重新塑形。 |
| `Glyphs` | `Glyph[]` | -- | 按绘制顺序排列的字形。 | 按顺序走；绘制接口要求每批同一张字面时按 `FontId` 分组。 |
| `Origin` | `Vector2` | px | run 的原点，相对内容原点。横排：第一个字形基线上的笔位，是内容空间的一个点——它是权威的，因为布局在移动盒子的同时也移动了它。 | 横排从这里开始画。竖排：它不是内容空间意义上的点（两个分量分别配对盒子的块向起始边与块向基线）；改从元素的行内起点、落在列中线处起画。 |
| `Rotation` | `GlyphRotation` | -- | run 在行内被转成什么样：`None`，或竖排中拉丁 run 的 `ClockwiseQuarter`。 | `None`：随列正立绘制。`ClockwiseQuarter`：每个字形绕笔位顺时针转四分之一圈，于是 run 歪着头读得下去；转过之后字形从笔位向 +X 生长，所以笔位要相对列中线往回让出大约这个 run 的墨迹高度（约 `0.375 * FontSize`），字形才留在列内。 |
| `Width` | `float` | px | run 的自然宽度：各行 advance 之和，也就是任何行调整之前的宽度。 | 布局量宽、断行时用的就是这个宽度。调整改的是元素盒子而不是字形，这正是“自然几何”与“调整后几何”可分离的原因。 |

| `Glyph` 字段 | 类型 | 单位 | 含义 | 消费规则 |
|---|---|---|---|---|
| `Id` | `uint` | 字形索引 | 要绘制的字形索引，只有与 `FontId` 一起才有意义。 | 用那张字面绘制。只支持 16 位索引的绘制接口无法寻址更大的索引；排版仍然照记它的 advance。 |
| `FontId` | `ulong` | 字体目录 id | 该字形来自哪张字面。 | 按它把相邻字形并成一批。 |
| `Advance` | `float` | px | 前进量，已含 kerning 与连字效果。两种书写模式下都是正数；塑形器在竖排下给出的负值已被布局翻正。 | 在 run 的阅读方向上把笔位推进这么多。 |
| `OffsetX` | `float` | px | 笔位到字形原点的水平偏移（标记定位）。 | 加到笔位上；沿列方向时这是字形唯一的横向移动。 |
| `OffsetY` | `float` | px | 横排 run 中从基线、竖排 run 中从字形自身的纵向原点算起的纵向偏移。 | 横排：加到基线上。竖排：把字形放在 `pen - OffsetY`，因为该方向上塑形器给出的这个距离是取反的。 |
| `ClusterStart` | `int` | 字符下标 | 该字形覆盖的第一个源字符，相对元素的 `Text`。 | 簇下标是唯一可靠的“回到文本”的映射：塑形会把多个字符合成一个字形（连字），也会把一个字拆成多个。也可用作打字机揭示判据。 |
| `ClusterEnd` | `int` | 字符下标 | 覆盖的最后一个源字符之后一位。 | 与 `ClusterStart` 一起覆盖连字/多字形这些情形。 |

```csharp
using System;
using GodotNodeExtension.Component.Typography.Core.Model;

// A horizontal run: the pen walks the advances on the baseline the layout reported.
static void DrawLineRun(in GlyphRun run, Action<Glyph, float, float> drawGlyph)
{
    float pen = run.Origin.X;
    float baseline = run.Origin.Y;

    foreach (Glyph glyph in run.Glyphs)
    {
        drawGlyph(glyph, pen + glyph.OffsetX, baseline + glyph.OffsetY);
        pen += glyph.Advance;
    }
}
```

## 6. `RubyAnnotation`

注音出来时就已经量好、塑好、放好了：画它的人不再度量一次，它的位置也不依赖渲染器的字体度量。

| 字段 | 类型 | 单位 | 含义 | 消费规则 |
|---|---|---|---|---|
| `Text` | `string` | -- | 注音文本。 | 参考信息；画的是字形。 |
| `Glyphs` | `Glyph[]?` | -- | 从（`X`, `BaselineY`）定位的已塑形注音字形。 | 画它们；绝不再塑形或度量注音。 |
| `FontId` | `ulong` | 字体目录 id | 注音字形所属字面。 | 通过目录解析。 |
| `FontSize` | `float` | px | 注音字号（基字字号乘以请求的比例；一半即 jlreq §3.3.3 的默认值）。 | 按这个尺寸光栅化。 |
| `X` | `float` | px | 注音在内容空间的 X。沿行时：它的左边缘。沿列时：该列的**中线**。 | 含义由 `Orientation` 选择；两种情况不是同一条边。 |
| `BaselineY` | `float` | px | 沿行时：注音自己的基线。沿列时：该列起始处的坐标（块向轴上的位置；注音墨迹从这里沿列的走向生长）。 | `Orientation` 为 `Vertical` 时不要把它当 Y 读。 |
| `Width` | `float` | px | 注音沿**它自己**的阅读方向的延伸：各字形 advance 之和。沿行时是带的宽度，沿列时是列的高度。 | 按 `Orientation` 指的方向走 advance，而不是沿行方向走。 |
| `BandWidth` | `float` | px | 排版为这条注音在**垂直**于它阅读方向上预留的空间；没有预留时为 `0`。行间注音拿到的是该行的注音带；旁置注音拿到的是每个被注之字加进基文 advance 的半个 em（clreq §5.5.3.2）。 | 注音是放在这块空间**之内**的，所以知道它的消费者可以把注音在自己的带里居中（即 CSS Ruby 的初始值 `ruby-align: space-around`）。为 `0` 时没有预留空间：按给定几何锚定，不要自己臆造一条带。 |
| `Orientation` | `RubyOrientation` | -- | 注音自身怎么排：`Horizontal` 沿行，`Vertical` 沿它自己的一列向下。 | 与书写模式无关：横排段落也可以带一列（注音符号，clreq §5.5.3.1）。 |
| `Reason` | `string?` | -- | 由哪种分布放置：`Ruby:Mono`、`Ruby:Jukugo`、`Ruby:Group`。 | 诊断与 dump 用。 |

```text
along the line (RubyOrientation.Horizontal)
   X ........................ X + Width
   +--------------------------+   band, BandWidth deep
   |          注 文            |   BaselineY = the annotation's baseline
   +--------------------------+
              基 文

down a column (RubyOrientation.Vertical)
   X = the column's centre line
        |  BandWidth across
        |  注  }  the ink grows from BaselineY
        |  文  }  in the column's own direction
        |      }  Width = the sum of the advances
        基 文
```

## 7. `EmphasisMarkGeometry`

| 字段 | 类型 | 单位 | 含义 | 消费规则 |
|---|---|---|---|---|
| `Mark` | `string` | -- | 标记字符：中文为 `U+25CF`，日文横排为 `U+2022`，`SesameDot` 为芝麻点。 | 标记是字符而不是已塑形 run：用 `Size` 尺寸的字体画这一个字符。这是输出中唯一要求“字符”而非字形的地方。 |
| `Size` | `float` | px | 标记字号（基字字号乘以语言的着重号 em 值）。 | 按这个尺寸光栅化。 |
| `X` | `float` | px | 标记在**行内**轴上的中心。 | 让标记在水平方向居中于此（若绘制接口取左边缘，则减去自身宽度的一半）。 |
| `CenterY` | `float` | px | 标记在**块向**轴上的中心。 | 让标记在该坐标上居中。配合 `Side` 可知道标记在字符的哪一侧。 |
| `Side` | `EmphasisSide` | -- | 在哪一侧：`Below`（中文横排，clreq §5.3.1）、`Above`（日文横排，jlreq §3.3.9）、`Right`（竖排）。 | 照给的值绘制；不要按语言重新推导侧别。标记以其字符为中心、位于字符盒子之外的行间隙里，不需要占用额外行高。 |
| `Reason` | `string?` | -- | `Emphasis:Below`、`Emphasis:Above` 或 `Emphasis:Right`。 | 诊断与 dump 用。 |

## 8. `Boundary`

边界是两个相邻簇之间的决策点：断行与调整阶段关于这一对需要知道的一切集中在一处。它之所以是输出的一部分，
是因为这些决策最终以几何的形式被消费者看到——两个元素之间多出来的间隙，一行比宽度允许的更早结束。消费者
不会为了绘制去读边界：它不得从字符类别重新推导禁则或中西间距，而这正是该模型存在的意义。该数组是编译期输
出，按左簇索引（簇 `i` 与 `i + 1` 之间的边界是第 `i` 项）；结果报告的是它的代价，而不是它的内容。

| 字段 | 类型 | 单位 | 含义 | 消费规则 |
|---|---|---|---|---|
| `LeftCluster` | `int` | 下标 | 左侧簇的下标。 | 数组以它为索引；这一对标识了决策涉及的两个元素。 |
| `RightCluster` | `int` | 下标 | 右侧簇的下标（目前为 `LeftCluster + 1`）。 | |
| `Kind` | `BoundaryKind` | -- | 这一对是关于什么的：`Plain`、`SpaceRun`、`ScriptChange`、`Punctuation`、`Numeric`、`InlineObject`、`HardBreak`。 | 诊断用。`Plain` 占多数且不携带任何东西。 |
| `LeftScript` | `ScriptRole` | -- | 左簇的脚本角色：`Neutral`、`Han`、`LatinLetter`、`Digit`、`Space`、`Punctuation`、`InlineObject`、`WesternPunctuation`。 | 诊断用；这些角色刻意比 Unicode script 更粗。 |
| `RightScript` | `ScriptRole` | -- | 右簇的脚本角色。 | |
| `Owner` | `BoundaryOwner` | -- | 此处调整的归属侧：`Left`（左簇的尾边）、`Right`、`Both`。 | 告诉调整阶段一段间隙属于谁，于是间隙可度量、可移动，而不是被记在某个字形头上。 |
| `BaseSpacing` | `float` | px | 两簇之间的自然间距，叠加在各自 advance 之上。两簇自带分离时（例如空格簇）为 0。 | 这是读者看到的那段间隙；它已经体现在元素位置上。 |
| `MinSpacing` | `float` | px | 此处可被挤压到的最小间距。语言没有给出区间（即不可调）时等于 `BaseSpacing`。 | 可动间隙的下界。 |
| `MaxSpacing` | `float` | px | 可被拉伸到的最大间距。没有区间时等于 `BaseSpacing`。 | 上界。中西间距是唯一有规范给出区间的边界（clreq §6.3.3、jlreq §3.2.6：1/8 到 1/2 em）。 |
| `ForbiddenAtLineStart` | `bool` | -- | 本对的右簇不得作为行首。 | 这是禁则而不是偏好：此处不得断行。 |
| `ForbiddenAtLineEnd` | `bool` | -- | 本对的左簇不得作为行尾。 | 同上。 |
| `ForbiddenToBreak` | `bool` | -- | 两簇必须同行（不可拆对、数字与单位、注音组、制表符与它引导的内容）。 | 同上。 |
| `ForbiddenToStretch` | `bool` | -- | 调整阶段不得在此加空间（分離禁止）。 | 同上，针对拉伸。 |
| `Reason` | `string` | -- | 机器可读的解释，例如 `Plain`、`UnbreakablePair`、`ScriptChange:CjkLatinGap`、`ScriptChange:GapDisabled`、`ScriptChange:Other`、`Punctuation`、`SpaceRun`、`InlineObject`、`HardBreak`、`RubyGroup`、`TabStop`；由 Unicode 规则裁决时还会带 `:UAX14` 后缀。 | 诊断与 golden diff；这里点出了做出该决策的模块。 |
| `IsNotable` | `bool` | -- | 派生值：该边界携带了 dump 读者会在意的东西——某条禁则、某个间距，或非 `Plain` 的 `Kind`。 | 用它过滤；其余都是噪声。 |

```csharp
using System.Collections.Generic;
using GodotNodeExtension.Component.Typography.Core.Model;

// Only the notable boundaries carry a decision; the plain majority is noise.
static IEnumerable<Boundary> Notable(IReadOnlyList<Boundary> boundaries)
{
    foreach (Boundary boundary in boundaries)
    {
        if (boundary.IsNotable)
            yield return boundary;
    }
}
```

## 9. 输入到输出的映射

输入模型及其字段见 [input-format.cn.md](input-format.cn.md)。一个源元素通常变成多个排版元素——每簇一个
——所以下表中的“透传”一律指**逐簇**透传。

| 输入 | 输出 | 性质 | 说明 |
|---|---|---|---|
| `Type` | `LayoutElement.Type` | 透传 | 原样，包括各种非文本类型。 |
| `Text` | `LayoutElement.Text` | 逐簇透传 | 每个输出元素带的是它覆盖的那个簇的文本。 |
| `Font`、`FontSize` | `LayoutElement.Font`、`LayoutElement.FontSize` | 透传 | 为无字形回退路径与装饰保留；已塑形的 run 自带字体 id 与字号。 |
| `ResolvedFontId` | `GlyphRun.FontId` | 被消费，之后由排版决定 | 生产者解析平台字体；字形用那张字面塑形并把 id 报回来。 |
| `IsBold`、`IsItalic`、`IsStrikethrough`、`IsUnderline`、`IsSubscript`、`IsSuperscript` | 同名 | 透传 | 样式已经烙进塑形结果；这些标志留给装饰，以及需要样式而非形状的消费者。 |
| `Color` | `LayoutElement.Color` | 透传 | `SyntaxSpans` 会按 span 覆盖。 |
| `SyntaxSpans` | `LayoutElement.SyntaxSpans` | 透传 | |
| `Texture`、`LinkUrl`、`ElementId`、`ExtensionId`、`ExtensionContent`、`ActionTag`、`ActionPayload`、`TextEffectId`、`CornerRadius` | 同名 | 透传 | 交互、扩展与效果元数据原样穿过排版。 |
| `BackgroundColor`、`BackgroundPadding`、`BackgroundCornerRadius`、`BackgroundFillLine` | 同名，或一个 `Rect` | 先透传，后被合并 | 同一行上属于同一段背景的多个片段会被折成一个矩形（`Reason = MergedInlineBackground`），那些文本元素的背景字段同时被清空。 |
| `Ruby`（注音规格：文本、分布、字号比例） | `RubyText` 与 `Ruby` | 文本透传，几何由排版决定 | 注音文本在输出上出现两次（作为源文本与在注音里）；它的字形、位置、带宽与走向都由排版决定。 |
| `EmphasisMark`（样式） | `EmphasisMark` 与 `Emphasis` | 样式透传，几何由排版决定 | 样式保留下来，让消费者知道请求的是什么；标记字符、字号、中心点与侧别来自排版。 |
| `VerticalAlignment` | `Ascent`、`BaselineY`、`Position` | 被消费 | 它决定元素高度如何在升部与降部之间切分，从而决定盒子与基线落在哪里。 |
| 由断行器负责的元素的 `Position`、`Size` | `Position`、`Size` | 由排版决定 | 行上的位置是算出来的；输入的坐标被忽略。 |
| 定尺块内部元素的 `Position` | `Position` | 由排版决定（偏移） | 块内位置相对块原点，输出时已经按它偏移。 |
| `IsParagraphBreak`、`ParagraphSettings` | 没有自己的字段 | 被消费 | 它们把内容切成段落并选出逐段规则；结果是行几何与 `ParagraphIndex`。 |
| `BlockInfo`、`IsBlockEnd` | `BlockInfo`、`SubLines`，以及若干合成元素 | 被消费并展开 | 块变成一串元素：内容被偏移摊平，它的装饰（背景、边框、标记）作为额外元素出来并带 `Reason`。 |
| `CharacterIndex`、`CharacterCount` | 同名 | 重算 | `CharacterIndex` 变成源下标加簇内偏移，`CharacterCount` 变成该簇的长度，于是打字机可以按簇推进。 |
| 排版设置：语言、书写模式、`MaxWidth` / `MaxHeight`、`LineSpacing`、`Padding`、对齐、缩进、间距 | `LayoutLine` 的度量、`LineLeft` / `LineRight` / `LineIndent` / `Spans`、`ContentSize` | 由排版决定 | 从不回写成字段；它们只以几何的形式可见。 |
| `WrapRegions` | `LayoutLine.Spans` 出现多于一个区间 | 由排版决定 | 只遮住一行一部分的排除区，使该行多出一个可用区间。 |
| `TabStops`、`DefaultTabStopEm` | 元素位置；制表元素自身不占宽 | 由排版决定 | 制表符之后的内容落在哪里，由制表位及其对齐方式决定。 |
| 输入上的“仅排版输出”字段（`BaselineY`、`SourceRange`、`GlyphRun`、`CharClass`、`SpanIndex`、`ClusterStart`、`ClusterEnd`、`LineIndex`、`Direction`、`DisplayText`、`Reason`） | 同名 | 仅输出 | 排版输入的产出方让它们保持默认值；排版是唯一的写入方。 |

## 10. 渲染器检查清单

实际怎么消费这份输出见随本文档一起提供的渲染配方；下面这些不变量是本文格式所要求的那一部分。

必须：

- 按顺序画 `GlyphRun` 给出的字形，用 `Advance` 推进笔位，并施加 `OffsetX`/`OffsetY`。
- 横排用排版给的基线（`BaselineY`），竖排用元素的行内起点，这样换字体或换一个排版后端都不会让文字移位。
- 通过字形的 `FontId` 解析字面并按它分批；一个 run 可能因为回退而混用多张字面。
- 按 `Elements` 的顺序绘制：背景或装饰在它所属的文字之前。
- 注音、着重号与连字符都按它们自己的几何绘制（`Ruby`、`Emphasis`、`HyphenRun`）。
- 用 `SourceRange` 与 `ClusterStart`/`ClusterEnd` 把文本位置映射回源文本。
- 一行提供多于一个区间时，使用 `LayoutLine.Spans` 与元素的 `SpanIndex`。
- 带 `Hanging` 的标记要画在行盒之外：那是排版记录下来的规范行为。
- 用 `ContentSize` 决定滚动与内边距区域，并把它当作内容空间里的一个界。

不得：

- 重新塑形文本。只要有 `GlyphRun`，`Text` 就只是参考信息。
- 在竖排下把 `BaselineY` 当 Y 读：它是块向坐标。
- 用 `Position.Y` 加字体升部来推导基线。这正是该字段要替代的做法，而且换字体后会重复计算一次。
- 重新对字符分类：`CharClass` 就是分类结果。
- 把 `Reason` 非空的元素放进选择、复制或命中测试：它由装配阶段合成，自身不覆盖任何源文本。
- 假设“一个元素一个字符”或“一个元素一个字形”。簇是区间，一个源元素会映射成多个输出元素。
- 指望每条基线都有值：`NaN` 表示排版没算过，字体度量就是回退方案。
- 在设置了 `DisplayText` 时画 `Text`：排版量的是替换后的形态。
- 在 `BandWidth` 为 `0` 时为注音臆造一条带，或为了给它找个位置而重新度量注音。
