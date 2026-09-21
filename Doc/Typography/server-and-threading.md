# Typography server and threading

The layout engine is reached through one object — `TypographyServer.Instance` — and every consumer works through a
**handle** of its own. This page is the contract around that object: what a handle is, how a request is submitted
and a result collected, where the work runs, what it costs, how a failure reaches you, and what happens to all of
it when the editor reloads the assembly.

The formats stay where they are described: what you hand in is [`input-format.md`](input-format.md), what comes
back is [`output-format.md`](output-format.md), and the two halves the server keeps apart are laid out in
[`design.md`](design.md). What the engine does *not* do is listed in [`limitations.md`](limitations.md).

## Why a server

Layout splits into two halves whose costs have nothing in common:

- the **compile half** turns a stream of elements into shaped clusters and the boundary decisions between them
  (segmentation, classification, measurement, boundary rules). It depends on the text, the font and the language —
  but not on the width, so it produces the same result frame after frame until the text changes.
- the **execute half** places what was compiled: where each line ends, how it is adjusted (spacing, squeeze,
  alignment, indents) and how the elements are assembled for drawing. It depends on the width, so it is the half a
  resize has to run again.

A server owns both halves per consumer, and three things follow:

- text is compiled **once**, not once per frame;
- a resize costs only the cheap half, and the result says so — `Timings.CompileMs == 0` (see
  [*What a layout costs*](#what-a-layout-costs));
- the work happens off the frame path, so a long paragraph does not hold up your `_Process`.

What comes out is already positioned, with shaped glyphs, so neither the server nor the renderer lays anything out
again for drawing ([`output-format.md`](output-format.md)).

## Handles and generations

`CreateHandle()` hands out a `LayoutHandle`: the identity a consumer submits under. A handle owns one engine, one
cache of compiled content and one result queue, so two consumers never share layout state, and everything a
consumer submits is read back through the handle it submitted on.

| Call | What it does |
|---|---|
| `CreateHandle()` | Allocate a handle and its per-handle state. Starts the layout thread on first use, outside the editor. |
| `IsHandleValid(handle)` | Whether this handle is still usable: current server generation, still registered. |
| `ReleaseHandle(handle)` | Cancel this handle's in-flight work and dispose its state. The handle must not be used afterwards. |
| `ClearCache(handle)` | Drop the compiled content and keep the handle: the next full layout compiles again. |
| `Shutdown()` | Stop everything and invalidate every handle (see [*Lifetime and hot reload*](#lifetime-and-hot-reload)). |
| `RejectedRequests` | How many requests were refused because nobody could receive them. Non-zero means a consumer kept using a handle past its life. |

**A handle carries the generation it was created in** (`LayoutHandle.Generation`), and every `Shutdown()` bumps the
server's generation. `LayoutHandle.IsValid` says only that a server once assigned the handle; whether it still
belongs to the server that is running now is what decides whether a request will ever produce a result, and only the
server can answer that. `TypographyServer.IsHandleValid(handle)` is that answer.

A **stale** handle — released, or created before a shutdown — is refused loudly instead of silently:

- the submission returns `TypographyServer.NotSubmitted` (`0`) and logs a warning naming the handle and the
  generation gap;
- `RejectedRequests` counts it, so a consumer that ignores the return value still leaves a trace.

`0` is never a valid request id, so a caller that compares ids can tell "refused" from "still pending" — the two
states a stale handle used to make indistinguishable by producing no result at all.

## Requesting a layout

| Request | What it runs | Use it for |
|---|---|---|
| `RequestFullLayout(handle, elements, settings)` | Compile the submitted elements, then lay them out | Everything the text itself changes: a new document, edited text, another font or language |
| `RequestRelayout(handle, settings)` | Lay out the **cached** compiled content | A width change (a window resize, a different `MaxWidth`/`MaxHeight`) or a settings change that leaves the text alone |
| `RequestStreamAppend(handle, element, sourceIndex)` | Compile and place one more element | Text that arrives as it is produced: a typewriter, a streaming log |
| `RequestStreamFlush(handle)` | Finalize the lines built so far | The end of such a stream |

Every call returns a `long` request id; `NotSubmitted` means it was refused (a stale handle). **A new request on a
handle cancels the one before it**: the superseded request is dropped rather than queued and its result is replaced
rather than kept, so a burst of resizes cannot fill a queue with geometry nobody will read. Only the newest id on a
handle is worth acting on.

`RequestFullLayout` resolves the platform font of every element **on the calling thread**, before the request
reaches the layout thread. That is deliberate: it is the single entry point every producer goes through, so no
producer has to remember it, and the layout thread never touches a font resource. An element stream with no
elements at all is not an error — the result comes back empty, with no elements and no error message.

`RequestRelayout` cannot invent the content it reuses. With nothing cached it still answers, with a result whose
`Error` says `No PreparedContent cached. Call RequestFullLayout first.`, so a caller that resizes before it ever
laid out should submit a full layout first.

`RequestStreamAppend` delivers **progressive** results (`LayoutResult.IsProgressive` is `true`): the lines may still
gain elements at their end, so a consumer should not freeze them. `RequestStreamFlush` delivers the final one.

## Reading the results back

Once per frame, on the main thread:

```csharp
if (server.TryGetResult(handle, out LayoutResult result) && result.RequestId == requestId)
```

- `TryGetResult` never blocks: it dequeues the newest result waiting for that handle, or reports that there is none.
- Compare `result.RequestId` with the id you submitted. The queue only ever holds the newest result of a handle, but
  after a burst of requests the id is how you know **which** request the geometry in front of you answers — and
  therefore which width it was laid out for.
- `result.Elements` is the stream to draw, in order; `result.Lines` groups the same elements per line for
  line-level geometry and hit testing; `result.ContentSize` sizes a scroll region.
- `result.Timings`, `LineCount`, `ElementCount` and `ProhibitedBreakSkips` are diagnostics, never an input to a
  layout. `ProhibitedBreakSkips` counts the break candidates a prohibition or an unbreakable pair rejected, which is
  the quickest explanation of a line that ends well before its width would allow.
- `result.Describe()` gives a one-line summary for a log: id, line and element counts, content size, per-phase
  timings and the skipped breaks.

The field-by-field reference is [`output-format.md`](output-format.md).

## What a layout costs

Every result carries the wall-clock time of each phase, and the split is the point:

| Timing | What it covers |
|---|---|
| `PrepareMs` | Segmentation, classification, shaping, boundary-independent measurement — the expensive half |
| `BoundaryMs` | Building the boundary decisions between neighbouring clusters |
| `BreakMs` | Line breaking |
| `AdjustMs` | Line adjustment (squeeze, stretch, alignment, tabs, grid) |
| `FlattenMs` | Flattening the lines into the output element stream |
| `CompileMs` | Derived: `PrepareMs + BoundaryMs` — the width-independent half |
| `TotalMs` | Derived: the sum of the five phases |

The number to watch is **`CompileMs`**. A relayout that only changed the width must report `0`: the compiled content
and the boundary decisions were reused, which is exactly what the cache promises. A non-zero `CompileMs` on a
relayout means the compile ran again — the text was resubmitted, the content was cleared (`ClearCache`), or the
request was a full layout. The cache belongs to the handle, so "the text changed" and "only the width changed" are
one call apart rather than one document apart.

## Errors and cancellation

- **A failure is a result, not an exception.** An error while a request runs is caught and delivered as
  `LayoutResult.Error`, with no elements and no lines: report it and draw nothing rather than draw half a layout.
- **The cache is the one expected error.** A relayout without compiled content says so in `Error` instead of
  leaving the caller waiting for a result that will never come.
- **A superseded request is dropped.** A new request on a handle cancels the previous one, and results waiting to
  be read are replaced by the newest. `IsCancelled` marks a result whose request was cancelled before it completed,
  but a superseded request normally never produces one, because the caller has already replaced the id it would
  have matched. Treat "no result with my current id" as "not finished yet", not as a failure.
- **Refused is not pending.** A stale handle makes the submission return `NotSubmitted` (`0`) and warns;
  `RejectedRequests` counts every request that could not be delivered — a stale handle, or a handle released
  between submission and execution.

## The thread model

| Where | Which thread lays out | What that means for a caller |
|---|---|---|
| Outside the editor (a running game) | One dedicated background thread, started by the first `CreateHandle()` and shared by every handle | Submit on the main thread and poll `TryGetResult` once per frame; the layout runs while the frame goes on |
| Inside the editor | None — the request queue is drained on the thread that submits | The same calls and the same polling; a submitted request is normally already finished by the time the call returns |

- **One thread, every handle.** Requests are processed in submission order, and each handle's result queue is the
  only thing the two threads share.
- **The thread belongs to the component, not to a handle.** Releasing handles does not stop it; `Shutdown()` does.
- **The layout thread reads plain data only.** Platform fonts are resolved at submission, on the caller's thread, so
  the background thread never touches a font resource. A producer that builds elements on a thread of its own
  should fill `DrawElement.ResolvedFontId` there; an element that carries a resolved id needs no main-thread
  resolution.
- **Submit, then talk to the engine through the next request.** The engine behind a handle is not a shared object
  to poke while a request is in flight. Results are plain data and can be read from any thread.
- **Cancellation is per handle, not per request.** There is no cancel call: the next request on the handle is the
  cancellation of the one before it.

## Lifetime and hot reload

`Shutdown()` ends a server's life in one step: it bumps the generation (**every existing handle is stale from that
moment**), signals the thread and waits for it to end (with a time limit), then disposes the state of every handle.
`Dispose()` does the same. After it returns, nothing of the old server can produce a result — handles that were
left are refused rather than left hanging.

A server starts again by itself: the next `CreateHandle()` starts the thread once more (outside the editor) and
hands out a handle in the new generation. Handles from before the shutdown do not come back to life, and a consumer
holding one must create a new one.

**In the editor the project assembly is rebuilt on every C# change**, and the editor can only replace the old one
once nothing outside it still references it. Two pieces of state in this component would otherwise keep the old
assembly alive, and both let go on their own:

- the cache of shapers (one per typeface), which owns native objects created from the font data;
- the layout thread, whose entry point belongs to the assembly that started it. This is why the editor runs no
  layout thread at all: there the queue is drained on the thread that submits, and the assembly can be unloaded.

Nothing has to be turned off by hand, and nothing survives by accident. A handle does not survive either: it
belongs to the assembly that issued it, so after a reload the fresh server has no such handle and the next request
is refused (`NotSubmitted`). A consumer that keeps a handle across a reload must check `IsHandleValid` and create a
new one — which is the sane reaction to a refusal at any time.

If an editor session still reports that assemblies cannot be unloaded after a change in this component, reload the
editor once: the old assembly was already pinned before this could run.

## A minimal request

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

// Compile this text once; the width may change later without compiling again.
long requestId = server.RequestFullLayout(handle, elements, new TypographySettings
{
    MaxWidth = 320f,
    LanguageTag = "zh-Hans",
});

// Once per frame, on the main thread:
if (server.TryGetResult(handle, out LayoutResult result) && result.RequestId == requestId)
{
    foreach (LayoutElement element in result.Elements ?? [])
    {
        _ = element.GlyphRun;        // draw it: nothing here has to be measured or shaped again
    }

    _ = result.Timings.CompileMs;    // 0 when a relayout reused the compiled content
}

// A width change costs the cheap half only.
long resized = server.RequestRelayout(handle, new TypographySettings
{
    MaxWidth = 480f,
    LanguageTag = "zh-Hans",
});
```
