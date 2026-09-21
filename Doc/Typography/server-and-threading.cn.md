# Typography 服务器与线程

排版引擎只通过一个对象对外——`TypographyServer.Instance`——而每个使用者都通过属于自己的**句柄**与它打交道。
本页就是这个对象的契约：句柄是什么、请求怎么提交、结果怎么取回、活儿在哪个线程上干、一次排版要花多少、失败
如何送到你手上，以及编辑器重载程序集时这一切会怎样。

两份格式文档各归各位：你交进去的东西是 [`input-format.cn.md`](input-format.cn.md)，拿回来的东西是
[`output-format.cn.md`](output-format.cn.md)，服务器分开维护的那两半的设计在 [`design.cn.md`](design.cn.md)。
引擎**不做**什么，列在 [`limitations.cn.md`](limitations.cn.md) 里。

## 为什么是服务器

排版可以拆成两半，而这两半的代价毫无共同之处：

- **编译半程**把元素流变成塑形后的簇以及簇与簇之间的边界决策（切分、归类、度量、边界规则）。它取决于文本、
  字体和语言——但**不取决于宽度**，所以只要文本没变，它每一帧产出的结果都一样。
- **执行半程**负责摆放已经编译好的东西：每一行在哪里结束、怎样调整（间距、挤压、对齐、缩进）、元素怎样装配
  出来交给绘制。它取决于宽度，所以缩放窗口时必须重跑的是它。

服务器为每个使用者同时持有这两半，于是有三件事成立：

- 文本只**编译一次**，而不是每帧一次；
- 缩放窗口只花便宜的半程，而且结果自己会说——`Timings.CompileMs == 0`（见
  [*一次排版的代价*](#一次排版的代价)）；
- 活儿不在帧路径上做，所以一段很长的文字不会把你的 `_Process` 卡住。

产出的东西已经摆好位置、带着塑形后的字形，所以服务器和渲染器都不会为了绘制重新排一次
（[`output-format.cn.md`](output-format.cn.md)）。

## 句柄与服务器世代

`CreateHandle()` 发给你一个 `LayoutHandle`：使用者提交请求时使用的身份。一个句柄拥有一套引擎、一份编译产物
缓存和一个结果队列，所以两个使用者不会共用排版状态，而你提交的一切都从提交时用的那个句柄里取回。

| 调用 | 作用 |
|---|---|
| `CreateHandle()` | 分配一个句柄及其专有状态。首次使用时（编辑器之外）启动排版线程。 |
| `IsHandleValid(handle)` | 这个句柄是否仍然可用：属于当前服务器世代，且仍然登记在册。 |
| `ReleaseHandle(handle)` | 取消该句柄手上的在途工作并释放其状态。此后不得再使用该句柄。 |
| `ClearCache(handle)` | 丢弃编译产物、保留句柄：下一次全量排版会重新编译。 |
| `Shutdown()` | 停掉一切并使所有句柄失效（见 [*生命周期与热重载*](#生命周期与热重载)）。 |
| `RejectedRequests` | 有多少请求因为无人能接收而被拒绝。只要不为零，就说明有使用者让句柄活过了它的寿命。 |

**句柄带着它被创建时的世代**（`LayoutHandle.Generation`），而每一次 `Shutdown()` 都会把服务器的世代加一。
`LayoutHandle.IsValid` 只说明"曾有服务器给它发过号"；它是否还属于此刻在跑的那个服务器，才是决定一个请求究竟
会不会产出结果的关键，而这个问题只有服务器能回答。`TypographyServer.IsHandleValid(handle)` 就是这个答案。

一个**过期**句柄——已被释放的，或停机之前创建的——会被明确拒绝，而不是无声无息：

- 提交返回 `TypographyServer.NotSubmitted`（`0`），并记一条警告，写明句柄号和世代差；
- `RejectedRequests` 把它计入，所以即使使用者不看返回值，也会留下痕迹。

`0` 永远不是合法的请求号，因此比较请求号的调用方可以区分"被拒"与"还在排队"——这两者以前根本无法区分，因为
过期句柄上的请求根本不产出结果。

## 发起排版请求

| 请求 | 实际跑什么 | 什么时候用它 |
|---|---|---|
| `RequestFullLayout(handle, elements, settings)` | 编译提交的元素，然后排版 | 文本本身变了的一切情况：新文档、改过的文字、换了字体或语言 |
| `RequestRelayout(handle, settings)` | 用**缓存的**编译产物排版 | 宽度变了（窗口缩放、换了 `MaxWidth`/`MaxHeight`），或改了设置但文本没动 |
| `RequestStreamAppend(handle, element, sourceIndex)` | 编译并摆放多出来的一个元素 | 边产出边到达的文本：打字机效果、流式日志 |
| `RequestStreamFlush(handle)` | 定稿已经排好的行 | 这种流的结尾 |

每个调用都返回一个 `long` 请求号；`NotSubmitted` 表示被拒（过期句柄）。**同一句柄上的新请求会取消前一个请求**：
被顶掉的请求直接丢弃而不入队，它的结果是被替换而不是被留着，所以缩放的连击不会把队列塞满没人会读的几何。
只有句柄上最新的那个请求号值得理会。

`RequestFullLayout` 在**调用方线程**上解析每个元素的平台字体，然后请求才进入排版线程。这是刻意的：它是每一个
生产者都必经的唯一入口，所以没有哪个生产者需要记得做这件事，而排版线程永远不碰字体资源。一个元素数为零的
元素流不算错误——结果就是空的，没有元素，也没有错误消息。

`RequestRelayout` 变不出它要复用的内容。什么都没缓存时它照样作答，只是结果的 `Error` 写着
`No PreparedContent cached. Call RequestFullLayout first.`；所以还没排过版就先缩放窗口的调用方，应该先提交一次
全量排版。

`RequestStreamAppend` 交出的是**流式**结果（`LayoutResult.IsProgressive` 为 `true`）：这些行在末尾还可能继续增长，
使用者不应把它们冻结。`RequestStreamFlush` 交出的才是最终结果。

## 取回结果

每帧一次，在主线程上：

```csharp
if (server.TryGetResult(handle, out LayoutResult result) && result.RequestId == requestId)
```

- `TryGetResult` 不阻塞：它取出该句柄最新的一份结果，或者告诉你没有。
- 把 `result.RequestId` 与你提交的请求号比对。队列里永远只有该句柄最新的那份结果，但在一串请求之后，请求号是
  你判断手上这份几何**回答的是哪一个**请求、因而是按哪个宽度排出来的唯一依据。
- `result.Elements` 是要按顺序绘制的元素流；`result.Lines` 把同一批元素按行分组，供行级几何与命中测试使用；
  `result.ContentSize` 用来给滚动区域定尺寸。
- `result.Timings`、`LineCount`、`ElementCount`、`ProhibitedBreakSkips` 都是诊断信息，永远不是排版的输入。
  `ProhibitedBreakSkips` 统计被禁则或不可断开对拒绝的断点，它是"一行为什么远没到宽度就结束了"最快的解释。
- `result.Describe()` 给出一行日志摘要：请求号、行数与元素数、内容尺寸、各相位耗时和被跳过的断点。

逐字段的参考在 [`output-format.cn.md`](output-format.cn.md)。

## 一次排版的代价

每份结果都带着各相位的墙上时钟耗时，而这个拆分才是重点：

| 耗时 | 覆盖什么 |
|---|---|
| `PrepareMs` | 切分、归类、塑形、与边界无关的度量——昂贵的那半 |
| `BoundaryMs` | 建立相邻簇之间的边界决策 |
| `BreakMs` | 断行 |
| `AdjustMs` | 行调整（挤压、拉伸、对齐、制表位、网格） |
| `FlattenMs` | 把行摊平成输出元素流 |
| `CompileMs` | 派生值：`PrepareMs + BoundaryMs`——与宽度无关的那半 |
| `TotalMs` | 派生值：五个相位之和 |

要盯的数字是 **`CompileMs`**。只改了宽度的重排必须报 `0`：编译产物与边界决策被复用了，这正是缓存承诺的东西。
重排却报出非零的 `CompileMs`，说明编译又跑了一遍——文本被重新提交了、缓存被清掉了（`ClearCache`）、或者这是
一次全量排版。缓存属于句柄，所以"文本变了"与"只有宽度变了"之间只差一次调用，而不是差一份文档。

## 错误与取消

- **失败是一份结果，不是异常。** 请求执行中出错会被捕获，并以 `LayoutResult.Error` 交付，不带元素、不带行：
  报告它，而不是画半张排版图。
- **缓存是唯一可预期的错误。** 没有编译产物却要重排，就在 `Error` 里说明，而不是让调用方一直等一个永远不会
  到来的结果。
- **被顶掉的请求会被丢弃。** 同一句柄上的新请求取消前一个，等着被读的结果则被最新的替换。`IsCancelled` 标记
  的是"完成前已被取消"的结果，但被顶掉的请求通常根本不会产出结果，因为调用方早已换掉了它本该匹配的请求号。
  请把"没有我当前请求号的结果"理解为"还没完成"，而不是失败。
- **被拒不等于在排队。** 过期句柄会让提交返回 `NotSubmitted`（`0`）并记警告；`RejectedRequests` 计入每一个无法
  送达的请求——过期句柄，或在提交与执行之间被释放的句柄。

## 线程模型

| 场景 | 在哪个线程排版 | 对调用方意味着什么 |
|---|---|---|
| 编辑器之外（运行中的游戏） | 一条专用后台线程，由第一次 `CreateHandle()` 启动，所有句柄共用 | 在主线程提交、每帧轮询一次 `TryGetResult`；排版在帧继续往下走的同时进行 |
| 编辑器之内 | 没有排版线程——请求队列在提交它的线程上直接排干 | 同样的调用、同样的轮询；提交的请求通常在调用返回时就已经完成 |

- **一条线程，服务所有句柄。** 请求按提交顺序处理，两个线程唯一共享的东西就是每个句柄各自的结果队列。
- **线程属于组件，不属于句柄。** 释放句柄并不会停掉它；停掉它的是 `Shutdown()`。
- **排版线程只读普通数据。** 平台字体在提交时、在调用方线程上解析，所以后台线程永远不碰字体资源。如果生产者
  在自己的线程上构造元素，就应该在那里填好 `DrawElement.ResolvedFontId`；带着已解析 id 的元素不再需要主线程
  解析。
- **先提交，再通过下一个请求与引擎打交道。** 句柄背后的引擎不是那种"在请求在途时还能随手改"的共享对象。结果
  是普通数据，任何线程都可以读。
- **取消是按句柄、不是按请求。** 没有 cancel 调用：句柄上的下一个请求，就是对上一个请求的取消。

## 生命周期与热重载

`Shutdown()` 一步结束一个服务器的寿命：它把世代加一（**此刻起所有现存句柄都过期**），唤醒线程并等它结束
（有超时上限），然后释放每一个句柄的状态。`Dispose()` 做的是同一件事。它返回之后，旧服务器不可能再产出任何
结果——被留下的句柄会被拒绝，而不是一直悬着。

服务器会自己再站起来：下一次 `CreateHandle()` 会再次启动线程（编辑器之外），并发给你一个属于新世代的句柄。
停机之前的句柄不会复活，手里还攥着一个的使用者必须重新创建一个。

**在编辑器里，每次改 C# 都会重建项目程序集**，而编辑器只有在程序集之外再没有任何东西引用它时才换得掉旧的。
本组件里有两处状态本会让旧程序集一直活着，而它们都会自己松手：

- 塑形器缓存（每种字型一个），它持有由字体数据创建的本地对象；
- 排版线程，它的入口点属于启动它的那个程序集。这正是编辑器里根本不跑排版线程的原因：在那里队列在提交它的
  线程上排干，于是程序集得以卸载。

不需要手动关掉任何东西，也不会有东西意外地活下来。句柄同样活不下来：它属于签发它的那个程序集，所以重载之后
全新的服务器里没有这个句柄，下一个请求会被拒（`NotSubmitted`）。跨着重载一直攥着句柄的使用者，必须先查
`IsHandleValid`、再创建一个新的——这也是任何时候遇到拒绝时该做的反应。

如果改过本组件后，编辑器会话仍然报告程序集无法卸载，请重载一次编辑器：旧程序集在这套机制有机会运行之前就已经
被钉住了。

## 最小用法

```csharp compile
TypographyServer server = TypographyServer.Instance;
LayoutHandle handle = server.CreateHandle();

DrawElement[] elements =
[
    new DrawElement
    {
        Type = DrawElement.ElementType.Text,
        Text = "排版引擎",
        FontSize = 16,
        CharacterCount = 4,
    },
];

// 这段文本只编译一次；之后宽度变了也不必重新编译。
long requestId = server.RequestFullLayout(handle, elements, new TypographySettings
{
    MaxWidth = 320f,
    LanguageTag = "zh-Hans",
});

// 每帧一次，在主线程上：
if (server.TryGetResult(handle, out LayoutResult result) && result.RequestId == requestId)
{
    foreach (LayoutElement element in result.Elements ?? [])
    {
        _ = element.GlyphRun;        // 画它：这里的东西都不必再量一遍、再塑形一遍
    }

    _ = result.Timings.CompileMs;    // 重排复用了编译产物时为 0
}

// 改宽度只花便宜的那半。
long resized = server.RequestRelayout(handle, new TypographySettings
{
    MaxWidth = 480f,
    LanguageTag = "zh-Hans",
});
```
