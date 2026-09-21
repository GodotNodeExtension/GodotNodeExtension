[英文](README.md) | **中文**

# Typography

Typography 是 Godot 的多语言排版服务器：它把一串语义元素变成带位置的、**字形级**的输出，渲染器拿到后不必再
测量或塑形任何东西。

文字里有两半是绘图 API 决定不了的，都由它负责：**文字放在哪里**（断行、对齐、间距、绕排），以及**字符级上
它长什么样**（塑形、字形选择、字形替换、行首行末规则）。它不做渲染：不碰画布、不碰纹理、不碰像素。回传的是
一串扁平的 `LayoutElement`，每个都带着已塑形的字形和它们被测量时的几何，另外还有它们所在的行，所以绘制就是
遍历这串元素——见 [rendering.cn.md](rendering.cn.md)。

页面级排版同样不在范围内：没有页面、没有杂志式分栏、没有页眉页脚、没有表格插图。一份文档就是内容框里的段落流、
行内对象与块。

## 能力总览

服务器自带的语言，以及跟随语言的那些行为。每个格子里是剖面给出的默认值——请求可以覆盖其中任何一项。

| 行为 | `zh-Hans` | `zh-Hant` | `ja` | `ko` | `en` | `ar`, `he` | `und` | 其它未注册的标签 |
|---|---|---|---|---|---|---|---|---|
| **断行** | clreq 类集，建立在 Unicode 基线之上 | 与简体同一套类集 | jlreq 类集（小假名、长音符等） | klreq 类集 | Unicode 断行（UAX #14） | UAX #14 | UAX #14 加上引擎历史遗留的中日韩调整 | 同 `und` |
| **禁则** | clreq 类集，级别 `Basic` | clreq 类集，级别 `Basic` | jlreq 类集，级别 `Basic` | klreq 类集，级别 `Basic` | — | — | 历史遗留类集，级别 `Basic` | 同 `und` |
| **中西间距** | 1/4 em，1/8–1/2 | 1/4 em，1/8–1/2 | 1/4 em，1/8–1/2 | 1/4 em，1/8–1/2 | — | — | 1/4 em | 同 `und` |
| **缩进 / 对齐** | 2 字 / 两端对齐 | 2 字 / 两端对齐 | 1 字 / 两端对齐 | 1 字 / 两端对齐 | 0 / 左对齐 | 0 / 左对齐，行从右缘开始填满 | 0 / 左对齐 | 同 `und` |
| **标点宽度** | 行末剪掉半 em；行首开括号收窄 | 同简体 | 行末句点后的半 em 保留下（jlreq §3.1.9）；行首开括号收窄 | 行末不做调整 | — | — | 只做挤压 | 同 `und` |
| **字形替换** | — | 直角引号与居中省略号 | 日文形态（引号用 `「」`，省略号用 `……`） | 窄形句读（`。`、`、` 写作 `.`、`,`，klreq §6.1.2） | — | — | — | 同 `und` |
| **注音** | 读音排在字的上方、沿行排（拼音） | 注音符号排在基字右侧、竖排成列（clreq §5.5.3.1） | 振假名排在字的上方、行与行之间的带里（jlreq §3.3.9） | 没有声明（klreq 把注音交给 ruby 规范） | 没有声明 | 没有声明 | 预留文字盒上方的带 | 同 `und` |
| **默认书写模式** | `HorizontalTb` | `HorizontalTb` | `HorizontalTb` | `HorizontalTb` | `HorizontalTb` | `HorizontalTb` | `HorizontalTb` | `HorizontalTb` |

- **未注册的标签解析为 `und`**，也就是明确的"不做语言假设"这条路径——绝不会悄悄变成中文。`en-GB`、`de`
  与 `fr` 也已注册：形状与 `en` 相同，只是各自带自己的断词模式。
- **语言是段落属性**，所以一份文档可以混用：中文文档里引用一段英文，两个段落各按自己的约定排版。几何始终
  属于请求。
- **书写模式是请求的选择**，不是语言的：无论声明哪种语言，请求都可以要 `VerticalRl` 的列；没有任何剖面默认
  声明竖排，而 `VerticalLr` 会被拒绝。
- **着重号**（`着重点` / `圏点`）排在语言放它的那一侧：中文横排在字下、日文横排在字上、竖排在列的右侧
  （clreq §5.3.1、jlreq §3.3.9）。

## 三分钟上手

[getting-started.cn.md](getting-started.cn.md) 带你从空工程走到画出第一段文字：怎么拿到服务器与 handle、
怎么构造元素流、怎么提交请求并取结果（每帧轮询或等待）、一个自包含的示例把回传的字形串画出来、一个放在工程里的
`Control` 子类，以及动手写渲染代码之前值得知道的那些坑。

## 文档地图

| 文档 | 内容 |
|---|---|
| [getting-started.cn.md](getting-started.cn.md) | 从零到画出第一段文字：handle、元素、请求、结果、绘制 |
| [design.cn.md](design.cn.md) | 引擎背后的模型：阶段、边界模型、剖面、缓存 |
| [input-format.cn.md](input-format.cn.md) | 输入侧逐字段说明：`DrawElement`、`TypographySettings`、段落与语言 |
| [output-format.cn.md](output-format.cn.md) | 输出侧逐字段说明：`LayoutElement`、`LayoutLine`、`GlyphRun`、`Boundary` |
| [rendering.cn.md](rendering.cn.md) | 怎么画一份结果：坐标轴、字形串、元素次序、注音、命中测试、调试叠加 |
| [languages.cn.md](languages.cn.md) | 语言剖面、标签解析、段落级声明与文字推断 |
| [annotations.cn.md](annotations.cn.md) | 注音与着重号：调用方要声明什么，布局把它放在哪里 |
| [writing-modes.cn.md](writing-modes.cn.md) | 横排与竖排：列、旋转的字形串、书写模式改变了什么 |
| [line-breaking.cn.md](line-breaking.cn.md) | 断点、禁则、断词、悬挂标点 |
| [layout-and-spacing.cn.md](layout-and-spacing.cn.md) | 行、区间、缩进、对齐、两端对齐、网格、制表位、绕排区 |
| [server-and-threading.cn.md](server-and-threading.cn.md) | handle、请求种类、结果、线程与生命周期 |
| [limitations.cn.md](limitations.cn.md) | 没有实现、也没有声称支持的东西，照实写 |

## 依赖与运行环境

- **Godot 4.7 或更新** —— 组件就是对着它开发和测试的。
- **.NET SDK 10.0 或更新** —— 源码面向 `net10.0` 框架。
- **三个 NuGet 包**：`HarfBuzzSharp`（≥ 8.3.1.2），一次塑形多次绘制所需的字形下标；`SkiaSharp.HarfBuzz`
  （≥ 3.119.1），文字是用它塑形的；`Unicode.Bidi`（≥ 0.3.18），右到左文字背后的 UAX #9 实现。

## 设计上的三件事

- **边界是决策，不是空隙。** 每一对相邻簇都会产生一个决策，它一次带齐断行与调整阶段需要的东西：行首禁止、
  行末禁止、不可断、可挤压或可拉伸到的间距、间隙归属，以及原因。规则只表达一次，谁需要谁去读，而不是从字符
  类别里重新推一遍。
- **语言是数据，不是代码路径。** `LanguageProfile` 只提供参数、并指明它用哪套规则，所以新增一种语言就是新增
  数据。验收判据是：**新增一种语言不得改框架**。
- **规则差异是特性。** 断行策略是一个已注册的特性（`line-breaking.unicode`、`line-breaking.kinsoku`），宿主
  可以注册自己的那套，剖面再指明用哪个。剖面在注册时就被校验：缺依赖、声明了冲突，都会大声失败，而不是用
  兜底规则把文字排下去。
