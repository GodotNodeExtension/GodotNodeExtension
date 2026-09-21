# Typography 书写模式

书写模式说明一行朝哪个方向跑、行朝哪个方向推进，而这是**请求**的决定而不是语言的决定：同一种语言在书里排成
列、在页面上排成行，所以由调用方选模式，语言剖面只提供某种语言在该模式**内部**所规定的东西。本文说明横排与
竖排共有什么、一列改变了什么，以及画布宿主如何绘制一列。字段本身见 [`input-format.cn.md`](input-format.cn.md)
与 [`output-format.cn.md`](output-format.cn.md)；流水线为何这样切分见 [`design.cn.md`](design.cn.md)。以下示例使用
本组件的命名空间：

```csharp
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;   // WritingMode, TypographySettings, LayoutElement, Glyph
```

## 横排与竖排

每一次排版都表达在两条轴上：**inline** 轴（一行沿它跑）与 **block** 轴（行沿它推进）。书写模式不过是为每条轴
选择朝哪个方向增长，外加块坐标零点在哪里。

| 模式 | 一行是 | inline 轴 | block 轴 | 块坐标零点 |
|---|---|---|---|---|
| `HorizontalTb` | 一行向右增长，从左到右读 | `+X` | `+Y` | 内容框左缘 |
| `VerticalRl` | 一列向下增长，列向左推进，从右到左读 | `+Y` | `-X` | 内容框右缘 |
| `VerticalLr` | 预留：词汇存在，几何尚不遵循它 | `+Y` | `+X` | — |

内容空间本身从不旋转：任何模式下 X 都向右、Y 都向下，因此排除区、内边距、滚动偏移与命中测试都仍是页面几何。
模式改变的是这两个坐标各自承载哪个量，而这正是两种模式唯一分歧的地方。

## 如何请求一种书写模式

`TypographySettings.WritingMode` 可为空，`null` 表示"语言剖面声明的那一种"。没有任何内置语言剖面声明竖排——
模式是排版决定，不是语言的属性——因此不带模式的请求就是横排，而列永远是被显式请求出来的。

`VerticalRl` 会被接受。`VerticalLr` 不会：请求它的那次解析会直接失败并抛出 `NotSupportedException`，因为改用横排
排出去只会回传一份自称竖排的文档。清楚的拒绝比安静的错答有用。

竖排请求应当同时给出两个上界，因为它们在那里约束的是不同的东西：

- `MaxHeight` 约束一行——一列可以有多长。它是竖排下的行内上限。
- `MaxWidth` 约束块向轴——列可以铺开多远。它同时锚定块坐标零点。请求没有给出列高（为零或更小）时，行内上限
  退回以 `MaxWidth` 为准。

```csharp compile
// Columns: the caller asks for them, and a request that wants them sets both bounds.
var settings = new TypographySettings
{
    WritingMode = WritingMode.VerticalRl,  // null = whatever the language profile declares (horizontal today)
    LanguageTag = "ja",
    MaxHeight = 480f,                      // px: how long a column may be (the inline limit)
    MaxWidth = 320f,                       // px: how far the columns may run (the block extent)
};
```

## 竖排下什么会变

引擎决定的一切都是用 inline 与 block 两个标量决定的，因此模式不是第二条流水线——它是同一批数字的不同映射。
这个映射中有七件事是调用方可见的。

### 列与列的起点

一行是一列，向下增长；行则向**左**推进：第一列站在内容框的右缘，下一列在它左边，依此类推。块坐标零点就落在
那条右缘上，因此在 `VerticalRl` 里块坐标是向左增长的——与横排下的块向轴方向相反，而这就是读一个位置或一行
自身坐标时要记住的事。

```text
一次 VerticalRl 请求的内容框

   block 轴向左增长   <--------------------------------
   +-------------+-------------+-------------+
   |  第 2 列    |  第 1 列    |  块坐标 0   |   inline 轴向下增长：
   | （第 2 行） | （第 1 行） |  在这里     |   一行沿自己的列向下跑
   +-------------+-------------+-------------+
                               ^
                               内容框右缘
```

### 一行有多长

一行的长度由 `MaxHeight` 约束，而 `MaxWidth` 约束列可以铺开多远：与横排是同样两个数字，只是角色互换。断行、
禁则、挤压与两端对齐都沿 inline 轴度量，所以决定本身没有任何变化——变的只是得出的长度朝哪个方向延展。

### 盒子、基线与尺寸

已排版的盒子是按两条轴描述的，而不是按 X/Y；竖排下这一点体现在两个字段上：

- `Size` 是**先块向后**量出的盒子：`Size.X` 是跨列的跨度，`Size.Y` 是沿列向下的长度。盒子从 `Position` 出发，
  沿 inline 单位向量、再沿 block 单位向量延展，在 `VerticalRl` 里也就是向左延展——绘制或命中测试它时，绝不能
  假定一个向右、向下的矩形。
- `BaselineY` 仍是 **block** 轴上的坐标（`Block(Position) + ascent`）：横排下它就是 `Position.Y + ascent`，竖排下
  它是该列自己的坐标而不是一个 Y。一列没有水平基线可以摆放，因此绘制列时不得把这个字段当作页面上的纵向位置；
  列的笔位来自元素的行内起点与字形自身的偏移。

### 行高与行距

行几何仍是沿块向轴的一个标量：行的 `Y` 是它在这条轴上的坐标，`Height` 是
`ExtraAbove + Ascent + Descent + ExtraBelow + LineSpacing`，下一行从沿块向轴再进一个 `Height` 处开始，也就是
更靠左。`Ascent` 与 `Descent` 是该行元素沿这条轴的最大值，因此让两条横排行分开的一切，也出于同样的理由让两列
分开：一行会为自己承载的东西长高，而注音带、着重号或下划线所需的余地表现为列的厚度。

### 欧文与数字会被旋转

自带方向的文字保留自己的方向：欧文 run——一个词或一个数字，因为拉丁字母、数字与其它西文字符同属一类——按它
自己阅读的方式塑形，用自己的方向与自己需要的度量，然后被顺时针转四分之一圈，使人在歪头顺列读下去时能读它。
run 会用 `GlyphRun.Rotation = ClockwiseQuarter` 报告这件事；它的字形仍沿列逐个向下推进，只是每一个都绕自己的
笔位被旋转绘制。

旋转正是让一个词还是一个词、一个数字还是一个数字的原因。若按列的方向塑形，run 的前进量是它的 em 框，那会把
字母摊成整格、把词挤出列外；按自身方向塑形，前进量则是字母真正需要的宽度。竖排中站立的字符——汉字、假名——
带 `Rotation = None`，沿列塑形。

### 注音与着重号

注音与着重号落在列的块起始侧，对从右到左的列就是列的**右侧**（clreq §5.3.1、jlreq §3.3.9）。余地仍归排版：
注音带在块起始侧被预留，因此它让列变厚，而不是在最后一刻把邻列挤开。

关于它们有两件事完全不跟随书写模式。注音自己的方向是它自己的：`RubyOrientation` 说明注音是沿基文走还是沿自己
那一列向下，而注音符号（Bopomofo）即便在横排段落里也是基字旁边的一列（clreq §5.5.3.1）。着重号的侧别则由语言
在该模式内选定，因此横排中文段落里落在字下方的那种标记，在这里落在列的右侧。

### 列首

一行的起点会让位于否则会越出区域的注音。当开行那个 run 的注音比 run 本身更宽时，行的内容会按差额向内让开，
于是注音自己的起点与行首对齐，没有任何东西画在区域之外（jlreq §3.3.9）。这段余地记在行的缩进里，而缩进是一个
inline 轴量——所以竖排下让位的是**列**的首端，量则是一段沿列向下的距离。本身就带缩进的行保持原位：它的第一个
字符已经站得够靠里，足以容下注音。

## 自己写一个绘制列的画布宿主

结果的消费者需要三样东西，而其中没有一样是字体度量。

- **什么在哪里。** `Lines` 带着各行及其块向几何、以及每行提供的区间；`Elements` 带着同样的元素，按绘制顺序
  铺平，每个都有自己的 `Position`、`Size` 与字形 run。
- **沿列的笔位。** 从元素的行内起点开始，也就是它盒子的顶端，然后逐个字形走：把每个画在 `pen - OffsetY`，并
  让笔位前进它的 `Advance`。不加 ascent——在这个方向上，塑形器报告的偏移是从字形垂直原点量起的距离并已取负——
  而把行盒的 ascent 加下去，正是画出来的文字比排版回传的盒子高出约半字的原因。
- **跨列的位置。** 字形在列的厚度上居中，字形的 `OffsetX` 是它唯一的横向位移。被旋转的 run 从笔位向 +X 生长，
  因此旋转 run 的笔位要从列中线退回大约 `0.375 * FontSize`，好把转过一圈的字形留在列内。

排版决定好的其余部分都按它们各自的几何绘制：注音、着重号与连字符各自带着字形与位置到达；装饰线沿列向下而不是
横跨页面；悬挂的标点悬在一行的末端边缘之外，也就是列的底部。`ContentSize` 是按内容空间的 X/Y 组成的，因此
在竖排下用它决定滚动区域时，它还不是一个 (inline, block) 跨度。

```csharp compile
// A canvas host: one element of a vertical layout, drawn from the geometry the layout returned.
static void DrawColumn(in LayoutElement element, Action<Glyph, float, float> drawGlyph)
{
    if (element.GlyphRun is not { } run)
        return;

    // Size.X is the column's thickness and Size.Y how far down it runs, so this is the column's centre line.
    float column = element.Position.X - (element.Size.X * 0.5f);
    float pen = element.Position.Y;

    // A turned glyph grows toward +X from the pen, so a rotated run's pen stands back from the centre line.
    if (run.Rotation == GlyphRotation.ClockwiseQuarter)
        column -= run.FontSize * 0.375f;

    foreach (Glyph glyph in run.Glyphs)
    {
        drawGlyph(glyph, column + glyph.OffsetX, pen - glyph.OffsetY);
        pen += glyph.Advance;
    }
}
```

## 两种模式共享的规则

竖排是同一个引擎跑在另一条轴上，这是刻意的：为横排一行写下的规则在列里继续有效，因为规则从来不是关于 X 或 Y。

- **断行与禁则**——同一套断行搜索、同一批边界：基础是 Unicode 断行算法（UAX #14），其上是同样的禁则类集
  （clreq、jlreq、klreq）。不可起一行的 cluster 也不可起一列。
- **间距、挤压与拉伸**——同一批边界上的同一批区间。一个间隙的上下界是沿 inline 轴的长度，因此无需重新推导就
  成为沿列的距离；两端对齐、缩进与制表位同理。
- **断词、注音与着重号**——同样的语言参数决定一个词能否断词、注音落在哪里、朝哪个方向读、着重号在哪一侧。模式
  决定这些几何最终落在内容空间的哪里，而不是规则是什么。
- **输出契约**——同样的字段，只是有两处按轴向去读：盒子尺寸的两个分量是块向与行内，而基线是块向坐标。

引擎不做的是：在规范另有陈述之处自创一条只属于竖排的规则。一列是用排一行时那套规则沿轴排出来的，而规范为竖排
单独陈述的规则不在这里声称已实现。
