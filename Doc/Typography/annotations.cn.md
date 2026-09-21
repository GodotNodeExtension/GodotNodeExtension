# Typography 注音与着重号

注音——排在基文之上（或之侧）的读音，注音符号、振假名——是**排版出来的**，不是装饰上去的：由排版对注音塑形、决定它放在哪里、朝哪个方向读，并把成品几何连同它自己的字形交给渲染器。它是行内内容，不是单独的一行：它跟着自己所属的基文，二者不会被分开。

本文覆盖三种分布、注音的放置侧别与走向、尺寸、它从行里取走的空间，以及着重号——引擎放置的另一种行内记号。涉及到的字段见 [`input-format.cn.md`](input-format.cn.md)（`RubySpec`、`EmphasisMark`）与 [`output-format.cn.md`](output-format.cn.md)（`RubyAnnotation`、`EmphasisMarkGeometry`）；哪种语言用哪一侧、哪个走向，见 [`languages.cn.md`](languages.cn.md)。

## 三种分布

`RubySpec.Distribution` 说明注音怎么摊到基文上。

| 分布 | 在基文上的方式 | 可断行性 |
|---|---|---|
| `Mono` | 每个基字一个注音片段，居中于该字（jlreq 的モノルビ）。 | 被注音的整段是原子的。 |
| `Group` | 整段基文一个片段，居中于整段（jlreq 的グループルビ）。 | 同上。 |
| `Jukugo` | 每字一个片段，按整段成组排出（jlreq 的熟語ルビ，§3.3.7）。 | 同上。 |

- **被注音的段落永远不跨行断开。** 注音基文内部的每个位置都被拒绝作为断点，输出会说明原因：本该在此断开的边界报出注音组。一行装不下这段时，整段一起移到下一行，而不是把注音和基文拆开。
- **注音字数少于基文字数的 `Mono` 会按整段一个片段来度量**，因为逐字切开意味着凭空编造文字。任何分布下，注音都居中于它覆盖的内容：`Mono` 与 `Jukugo` 居中于单字，`Group`——以及被拓宽成单个片段的 `Mono`——居中于整段。
- **`Jukugo` 会撑宽基文。** 当某个字上的注音比该字更宽时，差额被加进基文的字间间距，好让相邻注音互不挨挤（jlreq §3.3.7）；每个位置有一个上限，它是引擎的取值——规范陈述的是原则，不是一个数字。加出来的空间属于基文自身的步进，因此断行、元素盒与笔位都能看到它，却不必知道注音的存在。上限的存在，使极宽的注音仍可能与其邻居挨上；无上限的拉伸则必然挨上。
- **每个注音由哪种分布放置，会被报出来**：注音元素把分布记在自己的原因里。

```text
            東 京                  base text, one annotated run
Mono        と う | き ょ う        one piece per character, centred on it
Group       と う き ょ う          one piece, centred over the two characters
Jukugo      と う | き ょ う        per character, base run widened between the pieces
```

## 注音放在哪里

这两半都归语言管：注音放在哪一侧，以及它朝哪个方向读。二者是彼此独立的值，一个剖面会把它们都说清楚。

### 侧别：RubyPlacement

| 放置方式 | 注音所在的位置 | 它占的空间从哪来 |
|---|---|---|
| `ReserveAbove` | 文本盒上方，在行内。 | 行按这条带增长，行内基线一起下移。 |
| `OverflowBetweenLines` | 同样在文本盒上方，因为空间就在那儿：jlreq 的行間処理把注音放在行与行之间。 | 同一条带：由引擎预留，而不是指望作者给的行距——注音压住上一行的排版不是可用的排版。 |
| `ReserveBeside` | 基字旁边。 | 基文自身的步进：每个被注音的字增长半个基字号（clreq §5.5.3.2），注音被放在这段空间之内、居中于它。除非列比它的基字更高，行高不变。 |

侧别是沿**块轴**表达的，因此在竖排里「文本盒上方」指的是列的块起始侧——即右侧（clreq §5.5.3.1、jlreq §3.3.9）。旁置的注音符号正是为这种方式准备的（clreq §5.5.3.1），横排竖排皆然。

### 走向：RubyOrientation

- **`Horizontal`**：注音沿行排列，与基文的走向一致。
- **`Vertical`**：注音沿自己的一列向下排，一个符号接一个符号，在基字旁边。

二者彼此独立，因此**横排段落也可以带一列**：繁体中文在横排里也把注音符号排成基字旁边的一列，与竖排无别（clreq §5.5.3.1）；而日文与简体中文的注音沿行排列。这就是走向不由书写模式推导的原因。

依赖这一点之前，有两处后果值得先知道：

- **注音按它自己的阅读方向塑形。** 列是自上而下塑形的，因此它的字形带竖排原点；若改按行塑形，渲染器拿到的是没有竖排原点的字形，整列会画在排版放置位置的右上方。
- **行上方的一条带装不下一列**，因此这个组合在语言注册处就被拒绝：带的深度等于注音的高度，而列沿基字延伸、需要它旁边的空间。同时要求两者的剖面会大声失败，而不是被排成别的什么东西。

输出会说明它拿到的是哪一种，以及在那个方向上有多宽：注音的 `Width` 是沿行读时的带宽、沿列读时的列高；跨该方向预留的空间另行报出，因此渲染器可以把注音居中在自己的带里，而不是贴到某一侧边缘。

## 注音尺寸

- **默认是基字号的半字。** `RubySpec.SizeRatio` 默认为 `0.5`：jlreq §3.3.3 给日文注音的尺寸，也是 CSS Ruby 的用户代理样式表给每个注音的尺寸。`0` 或更小的值按默认值处理。
- **clreq §5.5.3.2 的 3:10 不由引擎强制。** 那个比例属于注音符号，因此引擎不会把它套给没提出要求的调用方：繁体中文的注音与其它一样拿半字，想要注音符号那个比例的调用方用 `SizeRatio = 0.3f` 自己提出。
- 注音以最终尺寸塑形，该尺寸会写在注音几何上，渲染器按它光栅化字面，不必重新度量。

## 注音占用的空间

- **带的深度按字形真正留下的「墨迹」量，而不是按注音字体的行度量。** 声调符号会升到字体上缘之上，高个字形也会超过它，因此只按行度量出来的带会切掉自己存在所为了承载的那部分注音。行度量仍是地板——注音终究还是一段文字——所以带取「墨迹范围」与「字体自身上下缘之和」中较大的那个。
- **一行会为自己承载的东西增长。** 行会报出它在文本盒之外的两侧各需要多少空间，行高就是这些空间加上文本盒、再加上请求要求的行距。因此带注音的行内基线一起下移，带注音的两行不会重叠，即使作者没有给行距；请求声明的行距仍是地板，引擎是把内容要的空间加上去，而不是从作者的行距里扣。
- **比基字更高的旁置列会补上自己的超出量**，在两侧均分：列居中于自己的基字，因此越过基字盒的那部分就是差额各分一半。（这是横排的情形；竖排时这同一份空间改在列的块起始侧取得。）注音**沿行**占的空间已经属于基文步进，所以这是它唯一向行高索取的空间。
- **在行的开头或结尾，行只让出真正会越界的部分**（jlreq §3.3.9）。比它所注基文更宽的注音是居中的，只有会越出行边缘的那部分才把行内容推进来，推这么多，不多推。行内容之前的空间并不浪费——内边距、首行缩进、绕排区留下的区间都待在那里——因此已经缩进一个字的段落、或带内边距的排版，会保持原位而注音仍然留在版面内。行为此让出的空间会写进行几何，需要重新摆放一行的消费者从那里起算。
- **同一个字上的两种记号共用空间，不会互相叠加。** 一个字同时带注音与着重号时，二者在共同需要的那一侧取两者中较大的那份空间，而不是相加；两样都有的字是「排得下」，不是「叠起来」。

## 注音调号

注音调号不是读法里的又一个符号：它比符号的格子窄，贴在末符号的角上，读法本身的长度也不随它变化（clreq §5.5.3.3、§5.5.3.2）。只要注文是注音（含该区块的任一符号），引擎就按这个规格摆放调号：

- **平上去与方言非入声**（`ˊˇˋˉ`、`˪˫`）不占自己的格：墨迹挂在符号列之外，跨末符号格的上缘——即「最后一个注音符号的右上角外侧」，半个调号在该符号之上，另一半在整列之外。
- **入声**（`ㆴㆵㆶㆷ`）挂在同侧，但在列的末端：调号墨迹的底缘就是读法的末端，即约定的「右下方」。
- **轻声**（`˙`）在读法之前，确实占一格——不过是个薄格：沿读法方向为基字的 1/15，圆点在该格中居中。

依赖它之前有两点值得知道：

- **基字右侧预留的空间与有无调号无关。** clreq §5.5.3.2 规定右侧注文占基字一半的空间，且明说该空间包含调号、无调号的读法与有调号者占位一致。因此 `ㄏㄠ` 与 `ㄏㄠˋ` 占的空间相同，段落里的字因此对得齐。
- **读法不再可能比它标注的汉字更高。** 把调号当符号读时，三个符号加一个调号在 3:10 的比例下是第四格，比基字本身还高；现在读法就是它各符号的格长。

语言把注文设在行内时（注音置于基字上方的另一种标准形式，clreq §5.5.3.1）同样处理：调号仍留半个自己在末符号之外，墨迹顶到或底到注文自身的框。只有注音的调号这样摆放——罗马拼音的附加符号属于它的字母，随字母一起塑形。

## 着重号与下划线

- **记号的侧别来自语言，不来自输入。** `DrawElement.EmphasisMark` 选择样式（`None`、`Dot` 或 `SesameDot`）；记号放在哪里属于语言：横排中文在字符下方（clreq §5.3.1），横排日文在字符上方（jlreq §3.3.9），竖排则在列的右侧，两套约定都把它算作块起始侧。
- **记号的字符跟随样式与侧别。** 中文形态、以及凡在下方的情况画实心圆点，在上方画圆点符号，元素明确要求 `SesameDot` 时画芝麻点——jlreq 的竖排形态，由元素点名，因此该选择不会因为它所在语言会做什么而改变。
- **尺寸。** 记号默认按基字号四分之一排。这是约定取值，不是哪份规范给出的数字；它集中在一处，语言可以声明自己的值。
- **位置。** 记号沿行居中于自己的字符，并按侧别待在行间空隙里——在字符自身盒子之外，这正是「行间空隙」的意思。几何报出中心点与侧别，渲染器按给定尺寸画那一个字符，不再从语言重新推导侧别。
- **行会为记号增长。** 记号需要「一个记号大小的偏移」加上「自身半个记号的宽度」，因此行在它所在的那一侧预留一个半记号尺寸。带着重号的段落因此排在自己的盒子里，而不是把记号丢到上一行去。
- **下划线**以同样的方式增长行，在块末侧：字体自带下划线越过下缘的那部分，会加进行在下方报出的空间里。两种装饰都按自身几何绘制，逐字段说明见 [`output-format.cn.md`](output-format.cn.md)。

## 让它动起来

注音与着重号是输入值；关于它们的一切其余内容，都是排版产出的几何。

```csharp
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Server;

// One source element: a base run, its annotation, and an emphasis mark over the same characters.
var source = new DrawElement
{
    Type = DrawElement.ElementType.Text,
    Text = "東京",
    Font = font,                              // the base font; the annotation is measured with it too
    FontSize = 18,
    Ruby = new RubySpec
    {
        Text = "とうきょう",
        Distribution = RubyDistribution.Group, // one piece, centred over the two characters
        SizeRatio = 0.5f,                      // half the base size (the default)
    },
    EmphasisMark = EmphasisMarkStyle.Dot,
};

// The language decides where the annotation goes and which way it reads; "ja" puts it above, along the line.
var settings = new TypographySettings { LanguageTag = "ja", MaxWidth = 320f };

TypographyServer server = TypographyServer.Instance;
LayoutHandle handle = server.CreateHandle();
long requestId = server.RequestFullLayout(handle, [source], settings);

if (server.TryGetResult(handle, out LayoutResult result) && result.RequestId == requestId)
{
    foreach (LayoutElement element in result.Elements!)
    {
        // The annotation comes back shaped, measured and placed: draw its glyphs, never measure them again.
        if (element.Ruby is { } annotation)
        {
            DrawAnnotation(annotation.Glyphs!, annotation.X, annotation.BaselineY,
                annotation.FontSize, annotation.Width, annotation.Orientation);
        }

        // The mark is a character, centred on its character, on the side the language put it.
        if (element.Emphasis is { } mark)
            DrawMark(mark.Mark, mark.X, mark.CenterY, mark.Size);
    }
}
```
