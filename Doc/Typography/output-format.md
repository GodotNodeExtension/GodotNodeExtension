# Typography output format

The typography layout engine hands a renderer a flat stream of laid-out elements, the lines those elements
were placed on, and the decisions behind their geometry. This document is the format of that output: what each
field means, in which unit, which value is a sentinel, and what a consumer must or must not do with it. It
describes the format only — how the geometry was arrived at is not part of the contract.

## 1. Units and coordinate space

| Item | Convention |
|---|---|
| Length | pixels, `float`: `Position`, `Size`, `BaselineY`, `Advance`, `Ascent`, `Descent`, indents, band widths. em exists only inside the language parameters and never appears in the output. |
| Content origin | the top-left corner of the content box, i.e. after the request's `Padding`. Every position is relative to it; a renderer adds its own padding and scroll offset on top. |
| Axes in content space | X to the right, Y down, in every writing mode. |
| Inline axis | `+X` in horizontal writing, `+Y` in vertical writing. |
| Block axis | `+Y` in horizontal writing; `-X` for `VerticalRl` (columns advance leftward) and `+X` for `VerticalLr`. |
| Text ranges | UTF-16 code unit indices, half-open `[Start, End)` (`TextRange`), relative to the text of the source element named by `LayoutElement.SourceIndex` — not document-global. |
| Cluster indices | half-open, numbered inside the layout context that produced them: a nested block layout numbers its own clusters from zero. |
| Baseline | `BaselineY` is a position on the **block** axis, equal to `Block(Position) + ascent`. In horizontal writing that is `Position.Y + ascent`; in `VerticalRl` it is `BlockExtentLimit - Position.X`, i.e. the column's own coordinate. |

`Position` is the corner the element's box *starts* at; the box extends from there along the inline unit vector,
then along the block unit vector. In vertical writing the box therefore runs **leftward**, and a consumer that
draws or hit-tests it must not assume a rightward or downward rectangle.

A renderer drawing a *line* places its glyphs on `BaselineY` instead of re-deriving a baseline from font metrics:
a font swap would otherwise shift the whole line. A renderer drawing a *column* has no horizontal baseline to
place — the pen runs down the column from the element's inline start and the shaped glyphs carry their own offsets
(§5) — so the vertical path must not read `BaselineY` as a Y.

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

| Field | Type | Meaning | Consumer rule |
|---|---|---|---|
| `Handle` | `LayoutHandle` | The client handle this result belongs to. | Compare it with the handle the request was submitted on. A handle invalidated by a shutdown yields no result at all, so never wait forever on one. |
| `RequestId` | `long` | The id returned when the request was submitted. | Act on the id of the latest request only; a superseded result is dropped rather than queued. |
| `IsProgressive` | `bool` | `true` for a stream-append result (partial), `false` for a final one. | A progressive result may still gain lines at its end; do not freeze it. |
| `Elements` | `List<LayoutElement>?` | The flattened element stream, in drawing order — a background precedes the text it backs. | The primary output: draw it in order. `null` only when `Error` is set. |
| `Lines` | `IReadOnlyList<LayoutLine>?` | The laid-out lines, each holding the elements placed on it. | Use it for line-level geometry and hit testing; element positions are the same objects as in `Elements`. |
| `ContentSize` | `Vector2` | Extent of the content along each axis, mapped into content space: the inline extent (how far the longest line reaches) and the block extent (the last line's end), each plus the trailing `Padding`. It covers what a renderer draws, not only the elements' boxes: an annotation can be wider than the character it annotates and an emphasis mark is centred outside its box, so both are part of the extent. | Size a scroll region or a drawing surface with it. In horizontal writing the pair reads as width and height; in vertical writing the first component is how far the columns run and the second the longest column. |
| `Error` | `string?` | Failure message (also used when a relayout has no cached content to reuse). | Non-null means `Elements`/`Lines` are absent: report it instead of drawing. |
| `IsCancelled` | `bool` | The request was superseded before it completed. | Discard the result. |
| `LineCount` | `int` | Number of lines in the result. | Diagnostics. |
| `ElementCount` | `int` | Number of elements in the result. | Diagnostics. |
| `ProhibitedBreakSkips` | `int` | Break candidates rejected by a line-start/line-end prohibition or by an unbreakable pair. | Diagnostics: a high count explains a line that ends well before its width would allow. |
| `Timings` | `LayoutTimings` | Wall-clock duration of each phase (see below). | Diagnostics; never a layout input. |

The result carries no boundary array: the boundary model (§8) is compile-phase output whose decisions are already
applied to the geometry. The result reports its cost through `Timings.BoundaryMs` and the rejected candidates
through `ProhibitedBreakSkips`.

### 2.1 `LayoutTimings`

| Phase | Meaning | Note |
|---|---|---|
| `PrepareMs` | Segmentation, classification, shaping, boundary-independent measurement. | The expensive, cacheable half. Zero on a relayout that reused cached content. |
| `BoundaryMs` | Building the boundary decisions (§8). | Zero on a relayout: boundaries are width-independent and are reused. |
| `BreakMs` | Line breaking. | |
| `AdjustMs` | Line adjustment (squeeze, stretch, alignment, grid, tabs). | |
| `FlattenMs` | Flattening lines into the output element stream. | |
| `CompileMs` | Derived: `PrepareMs + BoundaryMs`. | The width-independent half: a relayout that only changes the width must report zero. |
| `TotalMs` | Derived: the sum of all five phases. | |

```text
LayoutResult
├── Elements[0..n)     LayoutElement      flat, drawing order, already positioned
├── Lines[0..n)        LayoutLine         Elements grouped per line, line box metrics
│   ├── Spans[0..n)    LineSpan           usable intervals along the inline axis
│   └── Elements[0..n) LayoutElement      the same elements as above
└── ContentSize        Vector2            content-space bounding box
```

## 3. `LayoutElement` fields

Every field of the struct, in the order the type declares them. `Unit` is `--` for indices, counts, enums, booleans
and colors. `Sentinel` is `--` when no value is special.

| Field | Type | Unit | Sentinel | Meaning | Consumer rule |
|---|---|---|---|---|---|
| `SourceIndex` | `int` | -- | -- | Index of the source element this one was derived from. | `SourceRange` is relative to the text of *that* element; map layout back to source through this pair, never through element order. |
| `Position` | `Vector2` | px | -- | Corner the element's box starts at, relative to the content origin. The box extends along the inline unit, then the block unit (leftward in right-to-left columns). | Do not assume a rightward/downward rectangle: draw and hit-test with the writing mode in mind. |
| `BaselineY` | `float` | px, block axis | `NaN` = not computed | Block-axis coordinate of the text baseline: `Block(Position) + ascent`. | Horizontal: place the glyphs on it, do not re-derive it. Vertical: must not be read as a Y. `NaN` means the element never passed through the line breaker, so fall back to font metrics exactly as before the field existed. |
| `Size` | `Vector2` | px | `Vector2.Zero` for a marker text element the renderer sizes | Final box size after layout, in content space. | This is the box, not the ink: justification widens boxes without moving the glyphs, so use `GlyphRun.Width` for the natural run width. |
| `CharClass` | `CharacterClass` | -- | `NonText` on elements the assembly stage synthesised | Character class the cluster was laid out with. | Read it instead of re-classifying the text; the classification is a language rule. |
| `SourceRange` | `TextRange` | UTF-16 code units, half-open | empty range = covers no source text | Source text this element covers, relative to the `SourceIndex` element's text. | Use it for selection, copying and playback. Never assume `Length` equals a number of visible characters. |
| `ClusterStart` | `int` | index, context-local | `-1` = unknown | Index of the first layout cluster covered. A cluster is the smallest indivisible layout unit and never spans a line break. | Map layout to source through cluster indices, not through element order. The index is ordered but not document-global: a nested block layout renumbers from zero and segments that never reached a line leave gaps. |
| `ClusterEnd` | `int` | index, context-local | `-1` = unknown | One past the last cluster covered; `ClusterStart + 1` today. | Keep it a range in coverage checks; a later cluster model may cover several clusters. |
| `SpanIndex` | `int` | index | `0` on an ordinary single-interval line | Index into the line's `Spans` of the interval this element was placed in. | Position the element in the interval it was placed in; `LineLeft`/`LineRight` only describe the first one. |
| `LineIndex` | `int` | index | `-1` = unknown | Index of the `LayoutLine` this element was placed on. Elements expanded from a block's own sub-layout carry the index of the block's *internal* line instead, and are marked with a `Reason`. | Use `Reason` to tell document lines from block-internal ones before trusting this index. |
| `Direction` | `TextDirection` | -- | -- | Base text direction in effect for this element. | Tells a consumer which edge the element's content starts at. Only `LeftToRight` is written here today; a right-to-left run's visual order shows up as the order of the elements within the line. |
| `DisplayText` | `string?` | -- | `null` = identical to the source text | Text actually drawn when display-form substitution applies (quotation marks, ellipsis, sentence marks, mirrored brackets). | Draw this when it is non-null. The substitution never changes `SourceRange`. |
| `GlyphRun` | `GlyphRun?` | -- | `null` = the element has no text of its own (background, rule, marker) | Shaped glyphs positioned from the run's own origin. | Draw these glyphs and never shape the text again — that is what keeps the drawing identical to the measurement. |
| `Reason` | `string?` | -- | `null` = an ordinary cluster from the line breaker; non-null = annotated by the line edge rules, or synthesised or expanded by the assembly stage | Why the element's geometry is what it is. Synthesised: `MergedInlineBackground`, `BlockDecoration:Background`, `BlockDecoration:LeftBorder`, `BlockDecoration:Marker`, `BlockDecoration:MarkerElement`, `FixedSizeBlockContent`, `AutoSizeBlockContent`, `HyphenationBreak`. **Real text the line edge rules annotated**: `HangingPunctuation` (the mark hangs past the line's end edge), `OpeningBracketHalfWidth` (a head bracket gave up its leading half). | The synthesised ones carry no source text of their own (a hyphen inserted at a break has an empty `SourceRange` and `ClusterStart == -1`), so skip them in selection, copying and hit testing. The two annotations are the source's own characters: their text, range and cluster index are real, and dropping them loses text a reader can see. They are the reason a dump can explain itself. |
| `Type` | `DrawElement.ElementType` | -- | -- | Element type: `Text`, `Rect`, `Line`, `Image`, `ExtensionRegion`, `Action`. | Dispatch drawing on it. |
| `Color` | `Color` | -- | -- | Color to render with. | Overridden per span by `SyntaxSpans` when that is set. |
| `Text` | `string?` | -- | `null` for a non-text element | Text content to render. | Informational whenever `GlyphRun` is present; the drawn text is `DisplayText` when that is set. |
| `Font` | `Font?` | -- | `null` for a non-text element | Font resource for text rendering. | Needed only on the fallback path (no `GlyphRun`) and for decorations; the shaped run identifies its face by id. |
| `FontSize` | `int` | px | -- | Font size for text rendering. | Metrics fallback only: do not re-shape with it. |
| `IsBold` | `bool` | -- | -- | Bold was requested. | Already baked into the shaped glyphs. |
| `IsItalic` | `bool` | -- | -- | Italic was requested. | Same. |
| `IsStrikethrough` | `bool` | -- | -- | The text carries a strikethrough decoration. | Draw the decoration along the line (horizontal) or along the column (vertical). |
| `IsUnderline` | `bool` | -- | -- | The text carries an underline decoration. | Same; the room an underline needs below the descender is already in the line's `ExtraBelow`. |
| `IsSubscript` | `bool` | -- | -- | The text is set as a subscript. | Shift by the language's amount; already reflected in the box and the baseline. |
| `IsSuperscript` | `bool` | -- | -- | The text is set as a superscript. | Same. |
| `RubyText` | `string?` | -- | `null` = no annotation | Annotation text in source form. | Use `Ruby` to draw; this is the text the caller asked for, without geometry. |
| `Ruby` | `RubyAnnotation?` | -- | `null` = no annotation | Where this element's annotation was placed, with its own shaped glyphs. | Draw it from this geometry; never measure or shape the annotation again (§6). |
| `HyphenRun` | `GlyphRun?` | -- | `null` = no hyphen here | The hyphen this element ends its line with, when the line broke inside a word. | Draw it after the element's own glyphs, from its own origin; a separate element carrying `Reason = HyphenationBreak` may already present it. |
| `Hanging` | `bool` | -- | `false` | The element's mark hangs past the line's end edge, which the convention allows. | Draw it outside the line box and do not clamp it; it is ordinary text, not a layout error. |
| `EmphasisMark` | `EmphasisMarkStyle` | -- | `None` = no mark | Emphasis mark style carried over from the source: `None`, `Dot`, `SesameDot`. | Informational: the geometry is in `Emphasis`. |
| `Emphasis` | `EmphasisMarkGeometry?` | -- | `null` = none | Where the element's emphasis mark was placed. | Draw the mark centred on that point with that size (§7); do not re-derive the side from the language. |
| `SyntaxSpans` | `List<ColoredSpan>?` | -- | `null` = use `Color` | Per-span syntax coloring inside this text element. | When set, draw each span with its own color instead of the element color. |
| `Texture` | `Texture2D?` | -- | `null` = none | Texture to draw. | For `Image` elements. |
| `LinkUrl` | `string?` | -- | `null` = not a link | URL for link hit testing. | Hit-test the element's box as a link when set. |
| `ElementId` | `int` | -- | -- | Unique element id for selection tracking. | Selection identity across frames; combined with `SourceRange` it identifies a range of text. |
| `ExtensionId` | `int` | index | `-1` = not an extension | Index into the active extension regions. | Route `ExtensionRegion` drawing with it. |
| `ExtensionContent` | `string?` | -- | `null` = none | Raw content passed to the block extension. | |
| `ActionTag` | `string?` | -- | `null` = no action | Action identifier. | Fire the action when the element is activated. |
| `ActionPayload` | `Variant` | -- | default `Variant` = no payload | Optional data passed with the action signal. | |
| `CharacterIndex` | `int` | character index | -- | Global character index for typewriter progress. | Recomputed per cluster (`source.CharacterIndex + cluster offset`), so it stays monotonic across the elements one source element became. A synthesised element may leave it at `0`. |
| `CharacterCount` | `int` | character count | `0` for non-text elements | Number of characters in this element. | Reveal a prefix of the text by comparing it with `Glyph.ClusterStart`; count nothing a `Reason` marks as synthesised. |
| `TextEffectId` | `int` | index | `-1` = no effect | Index into the text-effect registry. | Apply the effect to the element's glyphs. |
| `CornerRadius` | `float` | px | `0` = sharp corners | Corner radius for rounded-rectangle rendering. | |
| `BackgroundColor` | `Color?` | -- | `null` = no background | Background color for a text element with inline decoration (inline code, highlight). | Draw a filled rectangle behind the text. The field is cleared on the text elements whose backgrounds were merged into one `Rect` with `Reason = MergedInlineBackground`. |
| `BackgroundPadding` | `Vector2` | px | `Vector2.Zero` | Extra padding around the text for that background, applied symmetrically (X, Y). | Grow the background rectangle by it; it was already reserved in the layout width. |
| `BackgroundCornerRadius` | `float` | px | `0` = sharp corners | Corner radius of the background rectangle. | |
| `BackgroundFillLine` | `bool` | -- | `false` | The background rectangle fills the full line box instead of fitting the text. | Use the line's block extent, not the element's box. |
| `SubLines` | `List<LayoutLine>?` | -- | `null` = not an auto-size block | Sub-layout lines of an auto-size block; their positions are relative to the block's content area (offset by the left indent and padding). | Carried through for block expansion; the flattened content already reaches the renderer as ordinary elements, so do not draw the sub-lines as well. |
| `BlockInfo` | `BlockLayout?` | -- | `null` = not a block | Block layout information, carried for the same expansion. | Same: informational on the output. |

```csharp
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;

// The assembly stage's reasons mean "this text is not the document's"; the line edge rules annotate real
// text instead, so those two values are the source's own characters and must not be filtered out.
static readonly HashSet<string> SourceTextReasons = ["HangingPunctuation", "OpeningBracketHalfWidth"];

static bool IsSynthesised(LayoutElement element) =>
    element.Reason is { } reason && !SourceTextReasons.Contains(reason);

static bool CoversSource(LayoutElement element) => !element.SourceRange.IsEmpty;

static float BaselineOrFallback(LayoutElement element, float ascentFromFont) =>
    float.IsNaN(element.BaselineY) ? element.Position.Y + ascentFromFont : element.BaselineY;
```

## 4. `LayoutLine` and `LineSpan`

The line box is expressed along the block axis, and its metrics are block-axis quantities. `Ascent` and `Descent`
are the maxima over the line's elements along that axis, and every baseline on the line sits `ExtraAbove` into the
box, so one annotated line cannot overlap its neighbour even when no line spacing was asked for.

| Field | Type | Unit | Meaning | Consumer rule |
|---|---|---|---|---|
| `Y` | `float` | px, block axis | Coordinate of the line's start edge along the block axis: Y in horizontal writing, the column's own coordinate in vertical writing. | Compare it against block coordinates (a `BaselineY`, `Block(Position)`), never against a Y in vertical writing. |
| `Height` | `float` | px, block extent | The line box: `ExtraAbove + Ascent + Descent + ExtraBelow + LineSpacing` (a positive line spacing is added after that). | Use it for line-level hit testing and for `BackgroundFillLine`; do not recompute it from font metrics. |
| `ExtraAbove` | `float` | px | Room the line needs on the block-start side of its text box beyond `Ascent`: an annotation band, or the part of a beside-set annotation column that reaches past the box. | Part of `Height`; it is why an annotated line is taller. A renderer needs it only through `Height`. |
| `ExtraBelow` | `float` | px | Room needed on the block-end side beyond `Descent`: an emphasis mark below the characters, an underline, or an annotation column overhang. | Same. |
| `Ascent` | `float` | px, block axis | Largest ascent among the line's elements, along the block axis (what the box contributes before the baseline). | Block-axis quantity; do not add it to a Y in vertical writing. |
| `Descent` | `float` | px, block axis | Largest descent among the line's elements. | Same. |
| `Elements` | `List<LayoutElement>` | -- | The elements placed on this line, in display order. | Already positioned: draw them where they are. |
| `IsFrozen` | `bool` | -- | The line is finalised and will not change (streaming mode). | A frozen line may be cached by the consumer. |
| `ParagraphIndex` | `int` | index | Index of the paragraph this line belongs to. | Group lines into paragraphs with it. |
| `IsFirstLineOfParagraph` | `bool` | -- | This is its paragraph's first line. | Diagnostics; the indent it implies is already in `LineIndent`. |
| `LineLeft` | `float` | px, inline axis | Start of the usable span along the inline axis, relative to the content origin. Includes padding and paragraph indent and is narrowed by wrap regions. | Describes the **first** interval only. Use the element's `SpanIndex` before positioning anything. |
| `LineRight` | `float` | px, inline axis | End (exclusive) of that span, along the inline axis. | Same restriction. |
| `Spans` | `LineSpan[]` | px | The intervals this line offers, left to right and non-overlapping — one for an ordinary line, more when text flows around an exclusion that covers part of it. An exhaustive, ordered partition of what is usable at that block coordinate. | Position every element in the interval it was placed in, or text of the second interval is drawn in the first one. |
| `LineIndent` | `float` | px | Extra space the line's content is pushed by, relative to `LineLeft`: the paragraph's first-line indent, plus the room the line gave way for at its start when a leading annotation is wider than the text it annotates. | Content starts at `LineLeft + LineIndent`. It is part of the line geometry, so a consumer repositioning a line must start from there, not from `LineLeft`. |

| `LineSpan` field | Type | Unit | Meaning |
|---|---|---|---|
| `Left` | `float` | px, inline axis | Left edge of the interval, inclusive. |
| `Right` | `float` | px, inline axis | Right edge, exclusive. |
| `Width` | `float` | px | Derived: `Right - Left`. |

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

## 5. `GlyphRun` and `Glyph`

| `GlyphRun` field | Type | Unit | Meaning | Consumer rule |
|---|---|---|---|---|
| `FontId` | `ulong` | catalog id | The resolved font the glyphs belong to, as handed out by the font catalog. | Resolve it to a platform font through the catalog. A run is not necessarily one face: the fallback path draws parts of the text with another face, which is why every glyph carries its own `FontId`. |
| `FontSize` | `float` | px | Font size the run was shaped at. | Rasterize the face at this size; do not re-shape. |
| `Glyphs` | `Glyph[]` | -- | The glyphs in drawing order. | Walk them in order; group by `FontId` when a drawing API needs one face per batch. |
| `Origin` | `Vector2` | px | Origin of the run, relative to the content origin. Horizontal writing: the pen position on the baseline of the first glyph, a point in content space — authoritative, because the layout moved it whenever it moved the box. | Start drawing here in horizontal writing. Vertical writing: not a point in content space (its components pair the box' block-start edge with a block baseline); start at the element's inline start on the column's centre line instead. |
| `Rotation` | `GlyphRotation` | -- | How the run is turned in its line: `None`, or `ClockwiseQuarter` for a Latin run set in vertical writing. | `None`: draw the glyphs upright with the column. `ClockwiseQuarter`: turn each glyph a quarter turn clockwise about the pen, so the run reads down the column with the head tilted; a turned glyph extends toward +X from the pen, so place the pen back from the column's centre line by roughly the run's ink height (about `0.375 * FontSize`) to keep it inside the column. |
| `Width` | `float` | px | Natural width of the run: the sum of the advances, before any line adjustment. | The width the layout measured and broke lines with. Adjustment changes the element's box, not the glyphs, which is what keeps natural and adjusted geometry separable. |

| `Glyph` field | Type | Unit | Meaning | Consumer rule |
|---|---|---|---|---|
| `Id` | `uint` | font glyph index | Glyph index to draw, meaningful only together with `FontId`. | Draw it from that face. A drawing API limited to 16-bit indices cannot address a higher index; the layout still counts its advance. |
| `FontId` | `ulong` | catalog id | The face this glyph came from. | Batch consecutive glyphs by it. |
| `Advance` | `float` | px | Horizontal advance including kerning and ligature effects. Positive in both writing modes; the layout already flipped the shaper's negative vertical advance. | Move the pen by it in the run's reading direction. |
| `OffsetX` | `float` | px | Offset from the pen to the glyph origin (mark positioning). | Add it to the pen; across a column it is the only sideways movement a glyph has. |
| `OffsetY` | `float` | px | Vertical offset from the baseline to the glyph origin in a horizontal run, from the glyph's vertical origin in a vertical run. | Horizontal: add it to the baseline. Vertical: place the glyph at `pen - OffsetY`, because the shaper reports this distance negated in that direction. |
| `ClusterStart` | `int` | character index | First source character this glyph covers, relative to the element's `Text`. | Cluster indices are the only sound mapping back to text: shaping merges characters into one glyph (a ligature) and splits one character into several. Also usable as the playback reveal test. |
| `ClusterEnd` | `int` | character index | One past the last source character covered. | Together with `ClusterStart`, covers the ligature/multi-glyph cases. |

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

An annotation comes out measured, shaped and placed: whoever draws it does not measure it again, and its position
does not depend on the renderer's font metrics.

| Field | Type | Unit | Meaning | Consumer rule |
|---|---|---|---|---|
| `Text` | `string` | -- | The annotation text. | Informational; the glyphs are what is drawn. |
| `Glyphs` | `Glyph[]?` | -- | The shaped annotation glyphs, positioned from (`X`, `BaselineY`). | Draw them; never shape or measure the annotation again. |
| `FontId` | `ulong` | catalog id | Face the annotation glyphs belong to. | Resolve it through the catalog. |
| `FontSize` | `float` | px | Annotation font size (base size times the requested ratio; half the base size is the jlreq §3.3.3 default). | Rasterize at this size. |
| `X` | `float` | px | Content-space X of the annotation. Along the line: its left edge. Down a column: the column's **centre line**. | The meaning is chosen by `Orientation`; the two cases are not the same edge. |
| `BaselineY` | `float` | px | Along the line: the annotation's own baseline. Down a column: the coordinate where the column starts (a position on the block axis; the annotation's ink grows from it in the direction the column runs). | Do not read it as a Y when `Orientation` is `Vertical`. |
| `Width` | `float` | px | The annotation's extent along its own reading direction: the sum of the glyph advances. Along the line: the band's width. Down a column: the column's height. | Walk the advances along the direction `Orientation` names, not along the line. |
| `BandWidth` | `float` | px | The room the layout reserved for this annotation *across* its reading direction, or `0` when it reserved none. An interlinear annotation gets the line's annotation band; a beside-set one gets the half em of base advance added per annotated character (clreq §5.5.3.2). | The annotation is placed inside that room, so a consumer that knows it can centre the annotation in its own band (CSS Ruby's initial `ruby-align: space-around`). With `0` there is no reserved room: anchor the annotation at the geometry given instead of inventing a band. |
| `Orientation` | `RubyOrientation` | -- | Which way the annotation itself is set: `Horizontal` along the line, `Vertical` down a column of its own. | Independent of the writing mode: a horizontal paragraph can carry a column (Bopomofo, clreq §5.5.3.1). |
| `Reason` | `string?` | -- | Which distribution placed it: `Ruby:Mono`, `Ruby:Jukugo`, `Ruby:Group`. | Diagnostics and dumps. |

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

| Field | Type | Unit | Meaning | Consumer rule |
|---|---|---|---|---|
| `Mark` | `string` | -- | The mark's character: `U+25CF` in Chinese, `U+2022` in horizontal Japanese, the sesame dot for `SesameDot`. | The mark is a character, not a shaped run: draw that one character with a font at `Size`. This is the one place the output asks for a character rather than glyphs. |
| `Size` | `float` | px | Mark font size (base font size times the language's emphasis size in em). | Rasterize the mark at this size. |
| `X` | `float` | px | Centre of the mark on the **inline** axis. | Centre the mark horizontally there (offset by half its measured width if the drawing API takes a left edge). |
| `CenterY` | `float` | px | Centre of the mark on the **block** axis. | Centre the mark on that coordinate. With `Side` this says which side of the character the mark is on. |
| `Side` | `EmphasisSide` | -- | Which side: `Below` (horizontal Chinese, clreq §5.3.1), `Above` (horizontal Japanese, jlreq §3.3.9), `Right` (vertical writing). | Draw it as given; do not re-derive the side from the language. The mark is centred on its character and sits outside the character's box, in the line gap, so it needs no room of its own. |
| `Reason` | `string?` | -- | `Emphasis:Below`, `Emphasis:Above` or `Emphasis:Right`. | Diagnostics and dumps. |

## 8. `Boundary`

A boundary is the decision point between two adjacent clusters: everything the break and adjustment stages need to
know about that pair, in one place. It is output because those decisions are what the consumer sees as geometry —
an extra gap between two elements, a line that ends earlier than its width would allow. A consumer never reads a
boundary to draw: it must not re-derive prohibition or script spacing from character classes, which is exactly what
the model exists to remove. The array is compile-phase output indexed by the left cluster (the boundary between
clusters `i` and `i + 1` is entry `i`); a result reports its cost, not its content.

| Field | Type | Unit | Meaning | Consumer rule |
|---|---|---|---|---|
| `LeftCluster` | `int` | index | Index of the cluster on the left. | The array is indexed by it; the pair identifies the two elements the decision was about. |
| `RightCluster` | `int` | index | Index of the cluster on the right (`LeftCluster + 1` today). | |
| `Kind` | `BoundaryKind` | -- | What the pair is about: `Plain`, `SpaceRun`, `ScriptChange`, `Punctuation`, `Numeric`, `InlineObject`, `HardBreak`. | Diagnostics. `Plain` is the majority and carries nothing. |
| `LeftScript` | `ScriptRole` | -- | Script role of the left cluster: `Neutral`, `Han`, `LatinLetter`, `Digit`, `Space`, `Punctuation`, `InlineObject`, `WesternPunctuation`. | Diagnostics; the roles are deliberately coarser than Unicode script. |
| `RightScript` | `ScriptRole` | -- | Script role of the right cluster. | |
| `Owner` | `BoundaryOwner` | -- | Which side owns any adjustment applied here: `Left` (the left cluster's trailing edge), `Right`, `Both`. | Tells the adjustment stages where a gap belongs, so a gap can be measured and moved instead of being attributed to a glyph. |
| `BaseSpacing` | `float` | px | Natural spacing between the two clusters, on top of their advances. Zero when the clusters already carry their own separation (a space cluster). | This is the gap the reader sees; it is already applied to the element positions. |
| `MinSpacing` | `float` | px | Smallest spacing this boundary may be squeezed to. Equal to `BaseSpacing` when the language states no range (i.e. not adjustable). | The lower bound of a movable gap. |
| `MaxSpacing` | `float` | px | Largest spacing it may be stretched to. Equal to `BaseSpacing` when there is no range. | The upper bound. The CJK/Latin gap is the one boundary a convention gives a range (clreq §6.3.3, jlreq §3.2.6: 1/8 to 1/2 em). |
| `ForbiddenAtLineStart` | `bool` | -- | A line may not start at the right cluster of this pair. | Prohibition, not a preference: a break here must not be taken. |
| `ForbiddenAtLineEnd` | `bool` | -- | A line may not end at the left cluster of this pair. | Same. |
| `ForbiddenToBreak` | `bool` | -- | The two clusters must stay on the same line (an unbreakable pair, a number with its unit, an annotation group, a tab and what it leads to). | Same. |
| `ForbiddenToStretch` | `bool` | -- | The adjustment stages may not add space here (separation prohibition). | Same, for stretching. |
| `Reason` | `string` | -- | Machine-readable explanation, e.g. `Plain`, `UnbreakablePair`, `ScriptChange:CjkLatinGap`, `ScriptChange:GapDisabled`, `ScriptChange:Other`, `Punctuation`, `SpaceRun`, `InlineObject`, `HardBreak`, `RubyGroup`, `TabStop`, plus a `:UAX14` suffix when the Unicode rule decided it. | Diagnostics and golden diffs; the module that produced the decision is named here. |
| `IsNotable` | `bool` | -- | Derived: the boundary carries something a reader of a dump would care about — a prohibition, a spacing, or a kind other than `Plain`. | Filter with it; the rest is noise. |

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

## 9. Input to output mapping

The input model and its fields are described in [input-format.md](input-format.md). One source element normally
becomes many layout elements — one per cluster — so "pass-through" below always means pass-through *per cluster*.

| Input | Output | Nature | Note |
|---|---|---|---|
| `Type` | `LayoutElement.Type` | pass-through | Unchanged, including the non-text types. |
| `Text` | `LayoutElement.Text` | pass-through, per cluster | Each output element carries the text of the cluster it covers. |
| `Font`, `FontSize` | `LayoutElement.Font`, `LayoutElement.FontSize` | pass-through | Kept for the no-glyphs fallback path and for decorations; the shaped run carries its own font id and size. |
| `ResolvedFontId` | `GlyphRun.FontId` | consumed, then layout-decided | The producer resolves the platform font; the glyphs are shaped with that face and report it back. |
| `IsBold`, `IsItalic`, `IsStrikethrough`, `IsUnderline`, `IsSubscript`, `IsSuperscript` | Same names | pass-through | Style is already baked into the shaped glyphs; the flags stay for decorations and for consumers that need the style, not the shapes. |
| `Color` | `LayoutElement.Color` | pass-through | `SyntaxSpans` overrides it per span. |
| `SyntaxSpans` | `LayoutElement.SyntaxSpans` | pass-through | |
| `Texture`, `LinkUrl`, `ElementId`, `ExtensionId`, `ExtensionContent`, `ActionTag`, `ActionPayload`, `TextEffectId`, `CornerRadius` | Same names | pass-through | Interaction, extension and effect metadata survive the layout untouched. |
| `BackgroundColor`, `BackgroundPadding`, `BackgroundCornerRadius`, `BackgroundFillLine` | Same names, or a single `Rect` | pass-through, then merged | Segments of one background run on a line are collapsed into one rectangle with `Reason = MergedInlineBackground`, and the text elements' background fields are cleared. |
| `Ruby` (an annotation spec: text, distribution, size ratio) | `RubyText` and `Ruby` | text passes through, geometry is layout-decided | The annotation's text reaches the output twice (as source text and inside the annotation); its glyphs, position, band width and orientation are decided by the layout. |
| `EmphasisMark` (style) | `EmphasisMark` and `Emphasis` | style passes through, geometry is layout-decided | The style survives so a consumer can tell what was asked for; the mark's character, size, centre point and side come from the layout. |
| `VerticalAlignment` | `Ascent`, `BaselineY`, `Position` | consumed | It decides how the element's height splits into ascent and descent, and therefore where its box and baseline land. |
| `Position`, `Size` of an element the line breaker owns | `Position`, `Size` | layout-decided | Positions on a line are computed; an input position is ignored. |
| `Position` of an element inside a fixed-size block | `Position` | layout-decided (offset) | Block-internal positions are relative to the block origin and come out offset by it. |
| `IsParagraphBreak`, `ParagraphSettings` | No field of their own | consumed | They cut the content into paragraphs and select per-paragraph rules; the result is line geometry and `ParagraphIndex`. |
| `BlockInfo`, `IsBlockEnd` | `BlockInfo`, `SubLines`, plus synthesised elements | consumed and expanded | The block becomes one element stream: its content is offset and flattened, and its decorations (background, border, marker) come out as extra elements carrying a `Reason`. |
| `CharacterIndex`, `CharacterCount` | Same names | recomputed | `CharacterIndex` becomes the source index plus the cluster's offset, and `CharacterCount` the cluster's length, so playback can walk clusters. |
| Typography settings: language, writing mode, `MaxWidth` / `MaxHeight`, `LineSpacing`, `Padding`, alignment, indents, spacing | `LayoutLine` metrics, `LineLeft` / `LineRight` / `LineIndent` / `Spans`, `ContentSize` | layout-decided | Never echoed as fields; they are visible only as geometry. |
| `WrapRegions` | `LayoutLine.Spans` with more than one interval | layout-decided | An exclusion that covers only part of a line leaves the line with a second interval. |
| `TabStops`, `DefaultTabStopEm` | Element positions; a tab element carries no width of its own | layout-decided | Where the content after a tab lands is decided by the stop and its alignment. |
| Layout-output-only fields on the input (`BaselineY`, `SourceRange`, `GlyphRun`, `CharClass`, `SpanIndex`, `ClusterStart`, `ClusterEnd`, `LineIndex`, `Direction`, `DisplayText`, `Reason`) | Same names | output-only | A producer of layout input leaves them at their defaults; the layout is the only writer. |

## 10. Renderer checklist

How the output is consumed in practice is described in the rendering recipe that accompanies this reference; the invariants below are the
part of it that this format imposes.

Must:

- Draw the glyphs the `GlyphRun` gives, in order, advancing the pen by `Advance` and applying `OffsetX`/`OffsetY`.
- Use the layout's baseline for a horizontal line (`BaselineY`), and the element's inline start for a column, so a
  font swap or a second layout backend cannot shift the text.
- Resolve a glyph's face through its `FontId` and batch by it; a run may mix faces through fallback.
- Draw in `Elements` order: a background or a decoration comes before the text it belongs to.
- Draw the annotation, the emphasis mark and the hyphen from their own geometry (`Ruby`, `Emphasis`,
  `HyphenRun`).
- Map text positions back to source through `SourceRange` and `ClusterStart`/`ClusterEnd`.
- Use `LayoutLine.Spans` and the element's `SpanIndex` when a line offers more than one interval.
- Keep a `Hanging` mark outside the line box: that is the convention the layout recorded.
- Size scroll and padding from `ContentSize`, and treat it as a bound in content space.

Must not:

- Shape the text again. Once `GlyphRun` is present, `Text` is informational.
- Read `BaselineY` as a Y in vertical writing: it is a block-axis coordinate.
- Add a font ascent to `Position.Y` to derive a baseline. That is what the field replaces, and it double-counts
  after a font swap.
- Re-classify characters: `CharClass` is the classification.
- Put an element with a non-null `Reason` into selection, copying or hit testing: it is synthesised by the
  assembly stage and covers no source text of its own.
- Assume one element per character, or one glyph per element. Clusters are ranges, and one source element maps to
  many output elements.
- Expect every baseline: `NaN` means the layout never computed one, and the font metrics are the fallback.
- Draw `Text` where `DisplayText` is set: the substituted form is the one the layout measured.
- Invent a band for an annotation when `BandWidth` is `0`, or re-measure an annotation to find its own place.
