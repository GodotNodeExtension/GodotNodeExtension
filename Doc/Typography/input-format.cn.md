# Typography 输入格式

这是契约的**输入**侧：调用方交给排版引擎的 `DrawElement` 元素流与 `TypographySettings` 对象，逐字段说明
类型、单位、默认值、含义，以及每个取值必须满足什么。回传的排版结果见 [`output-format.cn.md`](output-format.cn.md)。

## 单位与坐标空间

- **所有长度都是像素（px），类型为 `float`。** 输入值不用任何其它单位。
- **`em` 只作为参数单位、以及规范陈述量值时使用的单位。** 首行缩进与自动制表位间距以 em（字宽）给出，引擎用
  该 run 的字号把它们换算成像素。回传的一切几何量都是像素。
- **字号是像素单位的 `int`**（`DrawElement.FontSize`）；取值 `0` 表示"未设置"，引擎代之以 `16` px。
- **内容空间原点在内容框的左上角**，X 向右、Y 向下；`Padding` 在这一框内生效，因此排版后的内容从
  `(Padding, Padding)` 开始，`ContentSize` 也把 Padding 计在内。`Position`、`Size` 与 `BaselineY` 都相对于
  该原点。
- **书写模式决定哪条轴承载行、哪条轴堆叠行。** 请求的 `WritingMode` 经轴抽象解析，所以任何阶段都不必问
  "我是不是竖排"：

| 书写模式 | 行内轴（沿一行） | 块向轴（行堆叠） | 块坐标 0 的位置 |
|---|---|---|---|
| `HorizontalTb` | `+X` | `+Y` | 内容框左缘 |
| `VerticalRl` | `+Y` | `-X`（列向左推进） | 内容框右缘 |
| `VerticalLr` | `+Y` | `+X` | 预留；作为输入取值会被拒绝 |

  在竖排下，**排版结果**盒子的 `Size` 是 `(块向跨度, 行内跨度)`，即 `Size.X` 是跨列的跨度、`Size.Y` 是沿列的
  跨度——两条轴只是互换，取值仍以内容空间表达。作为输入的非文字对象 `Size` 则相反：`Size.X` 是它沿行内轴的
  跨度、`Size.Y` 是沿块向轴的跨度，两种模式下都一样，因为布局在把它映射进内容空间之前就是这样量它的。
- **文本区间是 UTF-16 code unit 的**半开**区间**：`[Start, End)`。`TextRange.End` 不含在内，且长度为 *N* 的
  区间并不意味着 *N* 个可见字符（组合标记、CRLF 对、ZWJ emoji 序列都是不可分的单位）。元素携带的区间相对于
  该元素自身的 `Text`。

```csharp
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;

// A run of text: the size is in pixels, the position is relative to the content origin,
// and the character indices are UTF-16 code units.
var element = new DrawElement
{
    Type = DrawElement.ElementType.Text,
    Text = "排版",
    FontSize = 16,
    CharacterIndex = 0,
    CharacterCount = 2,
};
```

## 元素种类

`DrawElement.Type` 说明元素是什么，两个布尔标记则把元素流切成段落与块。

| 种类 | 含义 | 布局如何对待它 |
|---|---|---|
| `ElementType.Text` | 可绘制的文本片段。 | 按字符类切分、塑形并测量；所有 glyph run 的来源。 |
| `ElementType.Rect` | 填充矩形。 | 行内非文字对象：`Size` 大小的原子盒子，像图片一样放在笔位上。 |
| `ElementType.Line` | 线段。 | 行内非文字对象，与 `Rect` 完全相同地处理（`Size` 就是它的盒子）。 |
| `ElementType.Image` | 图片 / 纹理。 | 行内非文字对象；`Size` 是它的盒子，`Texture` 是要绘制的图。 |
| `ElementType.ExtensionRegion` | 由调用方自行绘制的区域。 | `Size` 大小的行内非文字对象；`ExtensionId`/`ExtensionContent` 原样传到输出。 |
| `ElementType.Action` | 非视觉触发点（游戏动作）。 | 零宽、不可在其后断行的标记，不占空间并被原样带过。 |
| `IsParagraphBreak == true` | 段落分隔符。 | 结束当前段落并收束其行；该元素不绘制任何内容，也不需要文本。 |
| `IsBlockEnd == true` | 结束由前一个带 `BlockInfo` 的元素开启的块区域。 | 关闭该块；其后的元素回到正常流中。 |

## `DrawElement` 字段

`DrawElement` 的全部字段，包括由布局写回、因此**生产者必须留空**的那些。Required? 列为 *Layout output —
ignored as input* 的行由引擎填充，好让消费者不必重新推导几何就能绘制；在输入端设置它们不会改变任何行为。

| Field | Type | Unit | Default | Meaning | Required? |
|---|---|---|---|---|---|
| `Type` | `ElementType` | — | `Text` | 元素是什么（见上表）。 | Yes |
| `Position` | `Vector2` | px | `(0, 0)` | 盒原点，相对于内容原点。正常流中的元素忽略该值（由布局放置）；对定尺块内部的元素则是一个偏移。 | For fixed-size block content; otherwise ignored |
| `Size` | `Vector2` | px | `(0, 0)` | 盒子尺寸。对非文字对象即其固有尺寸：行内跨度与块向跨度。 | Yes for `Image`, `Rect`, `Line`, `ExtensionRegion` |
| `BaselineY` | `float` | px (block axis) | `NaN` | 文本基线沿块向轴的坐标。`NaN` 表示"从未计算过"。 | Layout output — ignored as input |
| `SourceRange` | `TextRange` | UTF-16 code units | `[0, 0)` | 该元素覆盖的源文本区间。 | Layout output — ignored as input |
| `GlyphRun` | `GlyphRun?` | — | `null` | 要绘制的已塑形 glyph。 | Layout output — ignored as input |
| `CharClass` | `CharacterClass` | — | `Ideograph`（枚举 0） | 该 cluster 的字符类。 | Layout output — ignored as input |
| `SpanIndex` | `int` | — | `0` | 元素所在行内区间的下标。 | Layout output — ignored as input |
| `ClusterStart` | `int` | — | `0` | 第一个布局 cluster 的下标。 | Layout output — ignored as input |
| `ClusterEnd` | `int` | — | `0` | 最后一个布局 cluster 之后的下标。 | Layout output — ignored as input |
| `LineIndex` | `int` | — | `0` | 元素被放在哪一行。 | Layout output — ignored as input |
| `Direction` | `TextDirection` | — | `LeftToRight` | 生效的基准文向。 | Layout output — ignored as input |
| `DisplayText` | `string?` | — | `null` | 与源文本不同时实际绘制的文本。 | Layout output — ignored as input |
| `Reason` | `string?` | — | `null` | 几何为何如此。 | Layout output — ignored as input |
| `Color` | `Color` | — | 透明黑 `(0,0,0,0)` | 元素的绘制颜色。 | No |
| `Text` | `string?` | — | `null` | 文本内容。null 或空串不产生任何 segment。 | Yes for `Type.Text` |
| `Font` | `Font?` | — | `null` | 用于塑形该文本的 Godot 字体。 | Yes for text, unless `ResolvedFontId` is set |
| `ResolvedFontId` | `ulong` | — | `0` | `Font` 解析出的字体 id，由字体目录发放。`0` 表示"尚未解析"；此时引擎自己解析 `Font`，而这只在主线程上才成立。 | Recommended for a threaded request |
| `FontSize` | `int` | px | `0`（引擎代之以 `16`） | 塑形该文本的字号。 | Yes for text |
| `Texture` | `Texture2D?` | — | `null` | 要绘制的图片。 | Yes for `Type.Image` |
| `LinkUrl` | `string?` | — | `null` | 用于链接命中测试的 URL；原样带过。 | No |
| `ElementId` | `int` | — | `0` | 调用方为选区跟踪定义的 id；原样带过。 | No |
| `ExtensionId` | `int` | — | `0`（`-1` = 非扩展区） | 调用方**现役**扩展区列表中的下标。 | Yes for `Type.ExtensionRegion` |
| `ExtensionContent` | `string?` | — | `null` | 传给块扩展的原始内容（例如围栏块里的代码）。 | No |
| `ActionTag` | `string?` | — | `null` | 动作标识（例如 `"shake"`、`"sfx"`）。 | Yes for `Type.Action` |
| `ActionPayload` | `Variant` | — | 默认 `Variant`（nil） | 随动作一起传出的可选数据。 | No |
| `CharacterIndex` | `int` | UTF-16 code units | `0` | 元素首字符在调用方全局次序中的下标，供选区与打字机进度使用。 | No — but it must be consistent with the text（见*输入不变量*） |
| `CharacterCount` | `int` | UTF-16 code units | `0` | 该元素覆盖的字符数（非文字为 `0`）。 | No |
| `TextEffectId` | `int` | — | `0`（`-1` = 无效果） | 调用方文字特效注册表的下标。布局从不应用它；消费者对这类文本走字符串路径绘制。 | No |
| `IsBold` | `bool` | — | `false` | 以粗体绘制。**不**用于选字面——请改用粗体 `Font` 塑形。 | No |
| `IsItalic` | `bool` | — | `false` | 以斜体绘制。同样不用于选字面。 | No |
| `IsStrikethrough` | `bool` | — | `false` | 删除线装饰。 | No |
| `IsUnderline` | `bool` | — | `false` | 下划线装饰；其超出下伸部的部分计入行的盒子。 | No |
| `IsSubscript` | `bool` | — | `false` | 作为下标绘制（更小、基线更低）。 | No |
| `IsSuperscript` | `bool` | — | `false` | 作为上标绘制（更小、基线更高）。 | No |
| `RubyText` | `string?` | — | `null` | 对整个元素文本加注的简写：等价于设置 `Ruby` 且 `Distribution = Group`。读取时返回 `Ruby.Text`。 | No |
| `Ruby` | `RubySpec?` | — | `null` | 置于该元素文本之上的注音；见*行内对象*。 | No |
| `EmphasisMark` | `EmphasisMarkStyle` | — | `None` | 逐字的着重号（着重点 / 圏点）。其字形与侧别由语言决定。 | No |
| `LaidOutRuby` | `RubyAnnotation?` | — | `null` | 布局把该元素的注音放在了哪里。 | Layout output — ignored as input |
| `LaidOutEmphasis` | `EmphasisMarkGeometry?` | — | `null` | 布局把该元素的着重号放在了哪里。 | Layout output — ignored as input |
| `SyntaxSpans` | `List<ColoredSpan>?` | — | `null` | 逐 span 的语法着色。null 表示"用元素的 `Color`"。布局原样携带，但从不据此塑形。 | No |
| `CornerRadius` | `float` | px | `0` | 圆角矩形的圆角半径（`0` = 直角）。 | No |
| `IsParagraphBreak` | `bool` | — | `false` | 把该元素标为段落分隔符。 | No（标记） |
| `ParagraphSettings` | `ParagraphSettings?` | — | `null` | 该元素**开启**的那个段落的段落级覆盖；null 表示"用请求的设置"。段落内部元素上的该字段被忽略。 | No |
| `BlockInfo` | `BlockLayout?` | — | `null` | 标记一个块区域的开始。 | No（标记） |
| `IsBlockEnd` | `bool` | — | `false` | 标记由前一个 `BlockInfo` 元素开启的块区域到此结束。 | No（标记） |
| `VerticalAlignment` | `InlineVerticalAlignment` | — | `Baseline`（枚举 0） | 非文字元素的高度如何在 ascent 与 descent 之间分配：`Baseline` 让盒底落在文本基线上，`Top` 让盒顶对齐行顶，`Middle` 以基线为中心（上下各一半），`Bottom` 让盒底对齐行底。 | No |
| `BackgroundColor` | `Color?` | — | `null` | 文本背后的底色（行内代码、高亮）。非 null 时渲染方在文本背后画一个矩形。 | No |
| `BackgroundPadding` | `Vector2` | px | `(0, 0)` | 背景的额外内边距（X = 行内，Y = 块向），四边对称；行内分量还会加宽测量出的盒子。 | No |
| `BackgroundCornerRadius` | `float` | px | `0` | 背景矩形的圆角半径（`0` = 直角）。 | No |
| `BackgroundFillLine` | `bool` | — | `false` | 背景撑满整行高度，而不是贴合文本自身。 | No |

```csharp
using GodotNodeExtension.Component.Typography.Core.Model;

// A paragraph break is its own element: it separates paragraphs and draws nothing.
var breakElement = new DrawElement { IsParagraphBreak = true };
```

## 段落与语言

**段落如何划分。** 段落终止于两者之一：

- `IsParagraphBreak` 为 `true` 的元素——它收束当前行并开启下一个段落；
- `Text` 元素的 `Text` 中的换行字符（`\n`、`\r`，或 `\r\n` 这一对）——它算一次断行，结束该段落且不绘制。

**开启段落的元素**就是打开它的那个：文档的第一个元素，或段落分隔之后的第一个元素。只有该元素的
`ParagraphSettings` 会被读取；段落内部元素上的该字段被忽略，因此一个段落的覆盖值总是写在它的首个元素上。

| Field | Type | Unit | Default | Meaning | Overrides |
|---|---|---|---|---|---|
| `LanguageTag` | `string?` | BCP-47 tag | `null` | 本段落的语言。 | `null` → 用请求的 `LanguageTag` |
| `FirstLineIndent` | `int?` | 字宽（em） | `null` | 首行缩进：`0` = 关闭，`2` = 标准 CJK 缩进。 | `null` → 用解析出的语言缩进值 |
| `SpacingBefore` | `float?` | px | `null` | 段落前的间距。 | `null` → 段前无间距（段间距是作为**前一段之后**的间距施加的） |
| `SpacingAfter` | `float?` | px | `null` | 段落后的间距。 | `null` → 用请求的 `ParagraphSpacing` |
| `Alignment` | `TextAlignment?` | — | `null` | 本段落各行的对齐方式。 | `null` → 用解析出的语言对齐方式 |
| `LeftIndent` | `float` | px | `0` | 整段的左缩进（引用块、列表缩进）。 | 始终生效——它是绝对值，而非覆盖 |
| `RightIndent` | `float` | px | `0` | 整段的右缩进。 | 始终生效——绝对值 |

**语言如何声明。**

- **请求级**：`TypographySettings.LanguageTag`（BCP-47，例如 `zh-Hans` 或 `en`）。`null` 表示"未指定"，落到
  那份不做任何语言假设的剖面。
- **段落级**：开启段落的元素上的 `ParagraphSettings.LanguageTag`。这才是语言标注的真实粒度：段落是语言能
  附着的最小单位，因为缩进、行首禁则与中西间距是**段落**属性而非 run 属性——混语文档通常在段落边界切换。
- 声明了自己语言的段落，请求的其余部分照旧生效：请求中显式给出的覆盖仍然胜过该语言的默认值。

**没有任何语言声明时**，引擎阅读段落自身的文字，按字符推断：

- **假名**字符（平假名、片假名、半角片假名，含长音符）→ 该段按 `ja` 排版；
- **谚文**字符（音节、Jamo、兼容 Jamo）→ `ko`；
- **纯汉字不推断**：简繁中文共用同一文字，因此只有汉字的段落保持请求声明的结果（或那份无语言假设的剖面）。

第一个点出文字的字符为整个段落作数；且**已声明的语言永不会被推断覆盖**。

```csharp
using GodotNodeExtension.Component.Typography.Core.Model;

// The element that opens a paragraph carries that paragraph's settings and language.
var paragraph = new DrawElement { Type = DrawElement.ElementType.Text, Text = "Hello" };
paragraph.ParagraphSettings = new ParagraphSettings
{
    LanguageTag = "en",   // BCP-47; null keeps the request's language
    FirstLineIndent = 0,  // character widths; null keeps the language default
    SpacingAfter = 12f,   // px
    LeftIndent = 24f,     // px
};
```

## 请求设置（`TypographySettings`）

`TypographySettings` 是每个排版请求一个的对象。**几何是请求自己的，永不来自语言剖面**——语言提供的是行为，
而不是它所排的那个盒子。语言治理的字段刻意可为 null：`null` 表示"语言剖面怎么说就怎么做"，这样请求级的默认值
就永远不会被误当成一个刻意的选择。

| Field | Type | Unit | Default | Meaning | Null means |
|---|---|---|---|---|---|
| `LanguageTag` | `string?` | BCP-47 tag | `null` | 整个请求所用的语言。 | "未指定"：落在不做语言假设的剖面 |
| `WritingMode` | `WritingMode?` | — | `null` | 本请求的书写模式：`HorizontalTb` 或 `VerticalRl`（`VerticalLr` 预留、会被拒绝）。书写模式是排版决定而非语言属性，所以由调用方选择。 | 用语言剖面的模式 |
| `MaxWidth` | `float` | px | `0` | 最大行宽：横排下即一行沿行内轴的跨度。竖排下它界定块向轴（列可以推进多远）。 | 不可为 null |
| `MaxHeight` | `float` | px | `0` | 最大列高：竖排下的行内上限。横排下被忽略（此时行内上限是 `MaxWidth`，块向不设限）。小于等于零表示"不设限"。 | 不可为 null |
| `LineSpacing` | `float` | px | `0` | 额外的行间距（行距），加在文本自身的行盒之上；它是地板值，因为引擎只会补上内容所需的余量。 | 不可为 null |
| `ParagraphSpacing` | `float` | px | `0` | 段落之后的间距，在 `ParagraphSettings.SpacingAfter` 为 null 时使用。 | 不可为 null |
| `EnableLineProhibition` | `bool?` | — | `null` | 是否施加 CJK 行首/行末禁则。 | 由语言剖面决定 |
| `ProhibitionLevel` | `ProhibitionLevel?` | — | `null` | 禁则严格度：`None`、`Basic`、`Gb`（GB/T 15834）、`Strict`（破折号与省略号也禁止出现在行首）。 | 由语言剖面决定 |
| `EnableCjkLatinSpacing` | `bool?` | — | `null` | 是否在 CJK 与拉丁 run 之间插入间距。 | 由语言剖面决定 |
| `CjkLatinSpacingEm` | `float?` | em | `null` | 该间隙的宽度（各 CJK 剖面取 `0.25`）。 | 由语言剖面决定 |
| `EnableHyphenation` | `bool?` | — | `null` | 是否施加自动断词（有断词模式的语言断，没有的不必）。 | 由语言剖面决定 |
| `EnablePunctuationCompression` | `bool?` | — | `null` | 标点是否可以被挤压。 | 由语言剖面决定 |
| `FirstLineIndent` | `int?` | 字宽（em） | `null` | 未被段落覆盖时的首行缩进：`0` = 关闭，`2` = CJK 惯例。 | 由语言剖面决定 |
| `Alignment` | `TextAlignment?` | — | `null` | 段落对齐：`Left`、`Center`、`Right`、`Justify`。 | 由语言剖面决定 |
| `Direction` | `TextDirection?` | — | `null` | 文本基准文向：`LeftToRight` 或 `RightToLeft`（后者由 RTL 语言剖面产生；目前没有按区间强制方向的 API）。 | 由语言剖面决定 |
| `EnableLetterformSubstitution` | `bool?` | — | `null` | 是否施加该语言的显示形态（引号、省略号、句末符号）。 | 由语言剖面决定 |
| `GridStep` | `float?` | px | `null` | 网格步进：每个元素的行内起点被吸附到距 `GridOrigin` 的该步进整数倍上，这正是让整页文字逐列对齐、并让中西混排拥有整数字元的原因（clreq §6.2.4）。 | 无网格 |
| `GridOrigin` | `float` | px | `0` | 网格从哪里开始，相对于内容原点。仅在设置了 `GridStep` 时使用。 | 不可为 null |
| `TabStops` | `List<TabStop>` | px | 空列表 | 显式制表位，按升序排列；制表字符把其后的内容送到下一个制表位。 | 不可为 null（列表） |
| `DefaultTabStopEm` | `float?` | em | `4` | 自动制表位的间距，在 `TabStops` 中笔位之后没有制表位时使用。 | 关闭自动制表位：越过最后一个显式制表位后的制表符什么也不做 |
| `WrapRegions` | `List<WrapRegion>` | px | 空列表 | 图文混排的排除区。非空时布局改用逐行可用区间查询，而不用固定宽度。 | 不可为 null（列表） |
| `Padding` | `float` | px | `0` | 施加在整个内容区四周（四边）的内边距。 | 不可为 null |

```csharp
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;

var settings = new TypographySettings
{
    LanguageTag = "zh-Hans",              // null = no language assumption
    WritingMode = WritingMode.HorizontalTb,
    MaxWidth = 320f,                      // px
    Padding = 8f,                         // px
    Alignment = TextAlignment.Justify,
    FirstLineIndent = 2,                  // ems
    LineSpacing = 2f,                     // px
    ParagraphSpacing = 8f,                // px
};
```

## 段落内的行内对象

一个文本元素可以携带注音与着重号，任何元素都可以携带背景。这些是一个段落行内内容的属性，而不是请求的属性。

**注音（`RubySpec`）。** 输入说的是注音是什么、该如何分布；它最终落在什么几何位置上，是布局的事。注音是
行内标注而非独立一行：断行时它随基文本一起走。

| Field | Type | Unit | Default | Meaning | Required? |
|---|---|---|---|---|---|
| `Text` | `string` | — | `""`（空串） | 注音文本。 | Yes，且非空 |
| `Distribution` | `RubyDistribution` | — | `Mono` | 注音如何在基文本上分布（见下表）。 | No |
| `SizeRatio` | `float` | 基字号的分数 | `0.5` | 注音字号相对基字号的比例；默认值即 jlreq §3.3.3 的半字号。取值小于等于 `0` 时按 `0.5` 处理。 | No |

| `RubyDistribution` 取值 | 含义 |
|---|---|
| `Mono` | 每个基字一份注音，居中于该字（jlreq 的モノルビ）。若注音字符数少于基文本，则改为按整组一份来测量。 |
| `Group` | 整个基文本一份注音，居中于该 run（jlreq 的グループルビ）。 |
| `Jukugo` | 逐字注音，但整个 run 作为一组保持在一起排布，使宽于本字的注音不会与邻居相撞（jlreq 的熟語ルビ，§3.3.7）。基文本会为它所带的注音加宽，每处有一个上限（该上限是引擎取值）。 |

`RubyText` 是常见情形的简写：把它设为非空字符串，等价于把 `Ruby` 设为一个取该文本、`Distribution = Group`
的 `RubySpec`；设为 null 则移除注音。

**着重号（`EmphasisMark`）。**

| `EmphasisMarkStyle` 取值 | 含义 |
|---|---|
| `None` | 无着重号（默认）。 |
| `Dot` | 点：中文用 `●`（U+25CF），横排日文用 `•`（U+2022）。 |
| `SesameDot` | 胡麻点（`﹅`），jlreq 中竖排所用的形态。 |

着重号的**侧别不是输入值**：横排中文放在字下（clreq §5.3.1），横排日文放在字上（jlreq §3.3.9），竖排则放在列
的右侧。着重号会撑大行的盒子，因为按惯例它位于字符自身盒子之外——下划线同理。

**背景装饰。** 这四个字段装饰一个文本元素；其中的行内内边距还会加宽测量出的盒子，从而把相邻文本推开。

| Field | Type | Unit | Default | Meaning | Required? |
|---|---|---|---|---|---|
| `BackgroundColor` | `Color?` | — | `null` | 背景色；非 null 时在文本背后画一个矩形。 | No |
| `BackgroundPadding` | `Vector2` | px | `(0, 0)` | 额外内边距，X 沿行内轴、Y 跨行内轴，四边对称；行内分量参与断行。 | No |
| `BackgroundCornerRadius` | `float` | px | `0` | 矩形的圆角半径。 | No |
| `BackgroundFillLine` | `bool` | — | `false` | 矩形撑满整行高度，而不是贴合文本。 | No |

**图片、扩展区与动作。** 图片是文本元素的行内对应物：`Type = Image`，配 `Size`（它占据的盒子）与 `Texture`
（要画的图）。`VerticalAlignment` 决定该盒子相对基线落在哪里。`Type = ExtensionRegion` 是同一类盒子，只是绘制
归调用方所有，由 `ExtensionId`（调用方注册的扩展区下标）与 `ExtensionContent` 描述。`Type = Action` 是携带
`ActionTag` 与 `ActionPayload` 的零宽标记；它不占空间，且其后不允许断行。

| 种类 | 起作用的字段 | Unit | Default | Meaning |
|---|---|---|---|---|
| `ElementType.Image` | `Size`、`Texture`、`VerticalAlignment` | px / — | `(0,0)`、`null`、`Baseline` | 该盒子大小的行内图片。 |
| `ElementType.ExtensionRegion` | `Size`、`ExtensionId`、`ExtensionContent`、`VerticalAlignment` | px / — / text / — | `(0,0)`、`0`、`null`、`Baseline` | 该盒子大小的行内扩展区。 |
| `ElementType.Action` | `ActionTag`、`ActionPayload` | — / — | `null`、nil | 零宽触发点。 |

**块（`BlockLayout` + `IsBlockEnd`）。** 块是一块内部布局由调用方管理的区域。携带 `BlockInfo` 的元素开启它，
携带 `IsBlockEnd` 的配对元素关闭它，两者之间的元素属于该块。定尺块（`Size` 非零）作为**一个单元**被放置，
其内部元素各自保持相对于块原点的位置；自动尺寸块（`Size == Vector2.Zero`）由引擎排布，并自行算出块高。

| Field | Type | Unit | Default | Meaning | Required? |
|---|---|---|---|---|---|
| `Size` | `Vector2` | px | `(0, 0)` | 块的总测量尺寸。`Vector2.Zero` 表示"自动尺寸"：引擎排布其内容并算出尺寸。 | Yes for a fixed-size block; zero for an auto-size one |
| `FullWidth` | `bool` | — | `false` | 块占满整个内容宽度并总是另起一行。为 `false` 时，若剩余宽度放得下，可以作为行内块放置。 | No |
| `LeftIndent` | `float` | px | `0` | 块内容的左缩进。 | 仅自动尺寸块 |
| `RightIndent` | `float` | px | `0` | 块内容的右缩进（其最大宽度相应减少）。 | 仅自动尺寸块 |
| `Padding` | `Vector2` | px | `(0, 0)` | 块的内边距（X = 行内，Y = 块向）；内容按其偏移，块高两侧都把它计入。 | 仅自动尺寸块 |
| `BackgroundColor` | `Color?` | — | `null` | 块的背景色；非 null 时在其全部内容背后发出一个填充矩形。 | No |
| `BackgroundCornerRadius` | `float` | px | `0` | 该矩形的圆角半径。 | No |
| `LeftBorderColor` | `Color?` | — | `null` | 左边框颜色；非 null 时在左缘画一条竖线。 | No |
| `LeftBorderWidth` | `float` | px | `0` | 左边框宽度。 | 边框要出现则必填 |
| `LeftBorderOffset` | `float` | px | `0` | 左边框相对块左缘的 X 偏移。 | No |
| `MarkerText` | `string?` | — | `null` | 画在块左缘、内容区之外的标记文本（列表项目符号、有序编号），与内容首行对齐。 | No |
| `MarkerFont` | `Font?` | — | `null` | 标记文本的字体。 | `MarkerText` 要发出则必填 |
| `MarkerFontSize` | `int` | px | `0` | 标记文本的字号。 | No |
| `MarkerColor` | `Color` | — | 透明黑 `(0,0,0,0)` | 标记文本的颜色。 | No |
| `MarkerElements` | `List<DrawElement>?` | — | `null` | 画在左缘的自定义标记元素（复选框等非文字标记），位置相对于标记区原点。仅在未设置 `MarkerText` 时使用。 | No |
| `WrapRegions` | `List<WrapRegion>?` | — | `null` | 供块内部布局使用的排除区。 | 仅自动尺寸块 |
| `ContentAlignment` | `TextAlignment` | — | `Left` | 块内容在可用宽度内的水平对齐；影响比父宽度窄的定尺块。 | No |
| `IsAutoSize`（只读） | `bool` | — | `Size == Vector2.Zero` 时为 `true` | 块高是否自动计算。 | — |

```csharp
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;

var ruby = new DrawElement
{
    Type = DrawElement.ElementType.Text,
    Text = "東京",
    FontSize = 18,
    Ruby = new RubySpec
    {
        Text = "とうきょう",
        Distribution = RubyDistribution.Group,
        SizeRatio = 0.5f,
    },
    EmphasisMark = EmphasisMarkStyle.Dot,
    BackgroundColor = new Color(1f, 0.9f, 0.2f),
    BackgroundPadding = new Vector2(2f, 1f),
    BackgroundCornerRadius = 3f,
};
```

## 绕排区与制表位

绕排区是文本绕行的一块有位置的排除区；制表位是制表字符把其后内容送去的地方。两者都是请求级的列表。

| `WrapRegion` 字段 | Type | Unit | Default | Meaning |
|---|---|---|---|---|
| `Shape` | `WrapShape` | — | 宽 `0`、高 `0` 的 `RectWrapShape` | 排除区的形状。 |
| `Position` | `Vector2` | px | `(0, 0)` | 形状原点在内容空间中的位置。 |
| `WrapMode` | `WrapFloat` | — | `Left` | 该区域在文本流中的锚定方式。 |
| `Margin` | `float` | px | `0` | 形状四周的外边距；文本与它保持这个距离。 |
| `FirstLine` | `int` | 行下标 | `0` | 该区域归属的第一行（`0` = 布局的第一行）；在此之前的行不受影响。 |
| `LastLine` | `int` | 行下标 | `-1` | 该区域归属的最后一行；`-1` 表示"到布局结束"。 |

| `WrapShape` | 字段 | Unit | Default | Meaning |
|---|---|---|---|---|
| `RectWrapShape` | `Width`、`Height` | px | `0`、`0` | 轴对齐矩形；其原点即区域的 `Position`。 |
| `PolygonWrapShape` | `ScanlineExtents`、`ScanlineRuns`、`ScanlineStep`、`TotalWidth`、`TotalHeight` | px / px / px / px / px | `[]`、`[]`、`1`、`0`、`0` | 带逐扫描线跨度表的多边形，或由顶点构建的多边形（偶奇规则）。一个形状在一行上可以占据多个区间，文本因此可以在收窄形状的两侧流动。 |

| `WrapFloat` 取值 | 含义 |
|---|---|
| `Left` | 左浮动；文本在右侧绕排。 |
| `Right` | 右浮动；文本在左侧绕排。 |
| `Inline` | 不浮动：一个会断行的行内块。 |

| `TabStop` 字段 | Type | Unit | Default | Meaning |
|---|---|---|---|---|
| `Position` | `float` | px | —（位置参数，必填） | 距内容原点的距离。制表符是一条对齐指令而不是自带宽度的字符，所以必须给出制表位。 |
| `Alignment` | `TabAlignment` | — | `Left` | 制表符之后的内容在该位置如何对齐。 |
| `Leader` | `char?` | — | `null` | 填充"制表符之前的文本"与"制表位处内容"之间空隙的字符（目录会写点线）。null 则留空。 |

制表位必须按 **升序** 列出；制表符把其后的内容送到笔位之后的第一个制表位，越过最后一个制表位后则使用自动制表位
（`DefaultTabStopEm`），否则制表符什么也不做。

| `TabAlignment` 取值 | 含义 |
|---|---|
| `Left` | 内容从制表位开始。 |
| `Center` | 内容以制表位为中心。 |
| `Right` | 内容在制表位结束。 |
| `DecimalPoint` | 内容的小数点落在制表位上，这正是让一列数字对齐的方式；没有小数点的内容退回 `Left`。 |

```csharp
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;

var settings = new TypographySettings
{
    MaxWidth = 360f,
    WrapRegions =
    [
        new WrapRegion
        {
            Shape = new RectWrapShape { Width = 96f, Height = 72f },
            Position = new Vector2(0f, 0f),
            WrapMode = WrapFloat.Left,
            Margin = 6f,
            FirstLine = 0,
            LastLine = -1,     // to the end of the layout
        },
    ],
    TabStops =
    [
        new TabStop(120f, TabAlignment.Right, '.'),
        new TabStop(240f),
    ],
    DefaultTabStopEm = 4f,
};
```

## 输入不变量

**作为输入时被忽略的字段。** `BaselineY`、`SourceRange`、`GlyphRun`、`CharClass`、`SpanIndex`、`ClusterStart`、
`ClusterEnd`、`LineIndex`、`Direction`、`DisplayText`、`Reason`、`LaidOutRuby` 与 `LaidOutEmphasis` 由布局填充，
好让消费者不必重新推导几何就能绘制。生产者让它们保持默认值即可；设置它们不会改变任何行为。

**必须自洽的部分。**

- **`CharacterIndex` 与 `CharacterCount` 描述的是该元素自身文本的 UTF-16 code unit。** 选区与打字机播放正是回落到
  它们，所以元素声明的跨度必须与它的文本长度一致、并与相邻元素首尾相接：每个排版后的片段报告
  `CharacterIndex = CharacterIndex + 文本内偏移`、`CharacterCount = 该片段的长度`。声明与文本不符的元素会让选区
  落在错误的字符上。（定尺块展开出的元素还会原样沿用 `CharacterCount`，所以在那里也要写对。）
- **`Font` 与 `FontSize` 决定塑形。** 文本用**自身元素上的**字体、**自身元素上的**字号塑形（非正字号由 `16` px
  代替）。既没有 `Font` 也没有 `ResolvedFontId` 的元素量不出任何宽度；字体解析不出来的元素塑不出任何字形——
  这不是错误，只是空的。`IsBold` 与 `IsItalic` **不**用于选字面：请改用粗体或斜体字体来塑形。
- **`ResolvedFontId` 属于主线程。** 它是字体目录发放的字体 id。要在线程上排版的调用方必须在提交前填好它；
  id `0` 表示"尚未解析"，此时引擎自己解析 `Font`，而这只在主线程上才成立。
- **块必须成对。** 每个携带 `BlockInfo` 的元素都需要在其后、元素流结束之前有一个携带 `IsBlockEnd` 的元素。
  两者之间的元素属于该块：定尺块的内部元素由调用方定位（`Position` 是它们相对块原点的偏移），不属于外围文本流。
- **`Ruby` 需要字体与非空注音。** 注音用基元素的 `Font` 与 `FontSize` 测量；`Text` 为空的 `RubySpec`、或未携带
  字体的元素，都不带注音。注音字符数少于基字数的 `Mono` 会按整组测量，因为逐字切开就得凭空造字。
- **`TextEffectId` 与 `SyntaxSpans` 走字符串路径。** 它们被原样带到输出，从不参与塑形、测量或断行；实现它们的
  消费者从元素的文本绘制，而不是从 glyph run 绘制。
- **制表符绑定其后的内容。** 行不得在制表字符与对齐到其制表位的内容之间断开；而制表符自身没有宽度——间隙由
  制表位决定。
- **区间是半开的、且是局部的。** 每个 `TextRange` 都是 UTF-16 code unit 的 `[Start, End)`，相对于它所从属的那个
  元素的文本。长度不等于字符数。
- **列表按升序。** `TypographySettings.TabStops` 必须按 `Position` 升序。`LastLine = -1` 是"直到结束"的哨兵值；
  其它负值没有含义。
- **非文字元素要带尺寸。** `Image`、`Rect`、`Line` 与 `ExtensionRegion` 都是盒子：`Size.X` 是它们沿行内轴的跨度，
  `Size.Y` 是它们沿块向轴的跨度。`Size = (0, 0)` 让它们不可见（而非不存在），且非文字元素不消耗任何文本字符。

## 这些值最终落在输出的哪里

每个输入值要么原样到达输出，要么决定几何，要么决定哪条语言规则生效。

| 输入字段或语义 | 它在输出上决定了什么 |
|---|---|
| `DrawElement.Type` | `LayoutElement.Type`（原样）。 |
| `DrawElement.Text` | `LayoutElement.Text`；当语言替换显示形态时为 `LayoutElement.DisplayText`。 |
| `DrawElement.Font`、`ResolvedFontId`、`FontSize` | `GlyphRun`（`FontId`、`FontSize`、`Glyphs`）与 `LayoutElement.FontSize`；该 run 的 `Width` 是各字形 advance 之和。 |
| `DrawElement.Color` | `LayoutElement.Color`。 |
| `IsBold`、`IsItalic`、`IsStrikethrough`、`IsUnderline`、`IsSubscript`、`IsSuperscript` | 同名的输出字段；`IsUnderline` 与 `EmphasisMark` 还会撑大行的盒子。 |
| `Ruby` / `RubyText` | `LayoutElement.RubyText` 与 `LayoutElement.Ruby`（一个 `RubyAnnotation`，带已塑形的注音字形、位置、走向与预留的带宽）。 |
| `EmphasisMark` | `LayoutElement.EmphasisMark` 与 `LayoutElement.Emphasis`（一个 `EmphasisMarkGeometry`，带该标记、字号、中心与侧别）。 |
| `BackgroundColor`、`BackgroundPadding`、`BackgroundCornerRadius`、`BackgroundFillLine` | `LayoutElement.Background*`；同一元素的相邻片段会被合并成单个背景矩形，成为一个 `Rect` 元素，其 `Color`、`CornerRadius`、`Size` 均来自这些字段。 |
| `DrawElement.Size`（非文字） | `LayoutElement.Size`，并与 `VerticalAlignment` 一起决定元素的 `BaselineY` 与盒子位置。 |
| `VerticalAlignment` | 非文字盒子相对基线的位置（它的 `BaselineY`）。 |
| `Texture`、`LinkUrl`、`ElementId`、`ExtensionId`、`ExtensionContent`、`ActionTag`、`ActionPayload`、`SyntaxSpans`、`CornerRadius`、`TextEffectId` | 同名的输出字段，原样不变。 |
| `CharacterIndex`、`CharacterCount` | `LayoutElement.CharacterIndex`（起始下标加上片段内偏移）与 `CharacterCount`（该片段的长度）。 |
| `IsParagraphBreak` | 不是输出元素：它收束当前行并开启新段落，输出以行/段落结构体现。 |
| `ParagraphSettings` | 该段落各行的几何：缩进、段前段后间距、各元素的 `Position`，以及该行的对齐方式。 |
| `BlockInfo` | 自动尺寸块的 `LayoutElement.SubLines` / `BlockInfo`，外加块的装饰与标记——它们以合成的 `Rect`、`Line` 与 `Text` 元素发出。 |
| `IsBlockEnd` | 关闭该块；块内容在两枚标记之间发出。 |
| `DrawElement.Position`（定尺块内部） | `LayoutElement.Position`，并按块的放置位置偏移。 |
| `TypographySettings.MaxWidth`、`MaxHeight` | 断行：每行在哪里结束，进而决定每个元素的 `Position`/`Size` 与 `LayoutResult.ContentSize`。 |
| `TypographySettings.Padding` | 内容原点的偏移（内容从 `(Padding, Padding)` 开始）与内容尺寸。 |
| `LineSpacing`、`ParagraphSpacing`、`FirstLineIndent`、`Alignment`、`Direction`、`GridStep`、`GridOrigin` | 行盒与元素位置：基线坐标、行距、首行缩进、一行如何填满它的区间，以及元素允许站在网格的什么位置。 |
| `WrapRegions` | 每一行的可用区间，进而决定各元素的 `Position` 与 `SpanIndex`。 |
| `TabStops`、`DefaultTabStopEm` | 制表元素的 `Size`（到制表位为止的间隙）与 `Text`（引导字符）。 |
| `TypographySettings.LanguageTag` 及每个受语言治理的覆盖项 | 哪些语言规则生效：解析出的剖面决定禁则、脚本间距、断词、显示形态、注音落位与着重号侧别，它们都体现在 `DisplayText`、`Hanging`、`Reason` 以及注音的几何上。 |

输出侧——`LayoutElement` 的每个字段、行结构与诊断信息——见 [`output-format.cn.md`](output-format.cn.md)。
