# Typography design

This document is about **why** the typography pipeline is cut the way it is: the two phases it is split into, the four
objects a layout is carried by, the four principles everything else follows from, and the one abstraction that lets a
line run somewhere other than to the right. It is a design document rather than a recipe — the field-level truth of
what a caller submits and what a renderer receives lives in [`input-format.md`](input-format.md) and
[`output-format.md`](output-format.md), and what a writing mode changes at the value level lives in
[`writing-modes.md`](writing-modes.md). The examples below use the component's namespaces:

```csharp
using Godot;
using GodotNodeExtension.Component.Typography.Core;         // TypographyFeatureRegistry
using GodotNodeExtension.Component.Typography.Core.Model;   // WritingMode, TextAlignment, LayoutAxes
using GodotNodeExtension.Component.Typography.Languages;    // LanguageProfile, LanguageProfileRegistry, TypographyParameters
```

## Two phases

The pipeline is split where the width enters. Everything that can be decided from the text, the styles and the
resolved language alone belongs to the **compile phase**; everything that needs the box, the alignment and the
exclusions belongs to the **execute phase**.

- **Compile phase** — runs once per paragraph, is width-independent, and is cached. Its product is a *prepared
  paragraph*: runs, clusters with their character properties, the boundary array, and the shaped glyphs.
- **Execute phase** — runs once per layout, is width-dependent, and is what a relayout repeats. Its product is the
  output: lines with their intervals, positioned elements, glyph runs, backgrounds, annotations and marks.

The split is not an implementation detail, it is the contract behind the request kinds. A request that only changes
the width reuses the prepared paragraphs, and the result reports the width-independent half as its own timing
(`Timings.CompileMs`), which must read zero on such a relayout. The same split is what makes a rule change
reviewable: a rule is applied in the compile phase, so changing it cannot silently re-decide a break that the execute
phase reached on its own.

### What each stage owns

| Stage | Phase | Decides | Product |
|---|---|---|---|
| `S1` itemize | compile | what the runs of text are, which language and direction each one belongs to, which of them are atomic | runs with their script, language, direction and source range |
| `S2` cluster | compile | what the smallest indivisible units are, how each one is classified, and what decision applies between two neighbours | clusters carrying a character class and a source range, plus the boundary array |
| `S3` measure | compile | what the text looks like at the character level: which face, which glyphs, how wide | shaped glyphs with advances and offsets, the cluster-to-glyph mapping, the font decision |
| `S4` break | execute | where a line may end, given the width and the exclusions | lines: which clusters each one holds, and the intervals it offers |
| `S5` adjust | execute | how the room inside a line is used: squeeze, stretch, alignment, indents | adjusted positions, and the amounts that were applied |
| `S6` assemble | execute | which elements the layout becomes: text, backgrounds, annotations, marks, decorations | the output element stream, each element carrying its geometry and its reason |

```text
the caller's element stream
        │
        ├── compile phase  (once per paragraph, width-independent, cached)
        │     S1 itemize   runs: script, language, direction, source range
        │     S2 cluster   grapheme clusters, character properties, boundaries
        │     S3 measure   shaping: glyph ids, advances, cluster mapping
        │
        └── execute phase  (once per layout, width-dependent)
              S4 break     boundary decisions become lines
              S5 adjust    spacing, squeeze/stretch, alignment, indents
              S6 assemble  glyph runs, backgrounds, annotations, marks
        │
the layout the renderer draws
```

## Four models

A layout is carried by four objects, and each one answers a different question. Keeping them apart is what lets one
of them change without the others having to be re-derived.

### The element stream: what the caller means

The input is a stream of semantic elements: what is being said (text), how it should look (font, size, decorations),
where it is an annotation rather than a line of text, and where it carries an action, an extension region or a
non-text box. It also carries the one language hint per paragraph, because a paragraph is the smallest unit a
language attaches to.

Geometry is deliberately absent from it. A flow element's `Position` is ignored — the layout owns it — and the fields
the layout writes back (the baseline, the source range, the glyph run, the reason) mean nothing on the way in: a
producer that fills them changes nothing, which is what stops a half-laid-out element from being mistaken for an
input. The stream is described field by field in [`input-format.md`](input-format.md).

### Boundaries: what a rule decided between two clusters

Every adjacent pair of clusters produces one `Boundary`: a decision point that carries what the break search and the
adjustment stages need about that pair in a single place. It names the kind of contact (an ordinary neighbour, a
space, a script change, punctuation, a number with its unit, an inline object, a hard break), the script role of each
side, which side owns any adjustment applied there, the natural spacing and the range it may be squeezed or stretched
within, and four prohibitions — no break here, no line start on the right cluster, no line end on the left one, no
stretching. It carries a machine-readable `Reason`, so a dump of a layout can explain itself, and a derived
`IsNotable` so the plain majority can be filtered out.

Boundaries are compile-phase output, indexed by the left cluster of the pair, and width-independent. They are *not*
part of the result: a result reports what the boundaries cost to build and how many break candidates they rejected,
not their content. A consumer never reads one to draw — that would be re-deriving the language's rules from character
classes, which is exactly what the model exists to remove.

### Lines and intervals: where text may stand

A line is a box along the block axis whose height is what it carries: the room an annotation band, a mark, an
underline or an overhanging annotation column needs is added to it (`ExtraAbove` and `ExtraBelow` on the block-start
and block-end sides), so the baselines on a line move together and two neighbouring lines cannot overlap even when the
request declares no line spacing. `Ascent` and `Descent` are the maxima over the line's elements along that axis.

A line offers **intervals** rather than one span: an exclusion that covers only part of a line leaves the line with
the room on both sides of it, and the text flows on. `LineSpan` is the interval type, `LineLeft`/`LineRight` are the
convenient way to ask about the first one, and an element reports which interval it landed in (`SpanIndex`) — a
consumer that positions elements from the line's first interval would draw the second interval's text on top of the
first's. `LineIndent` is the extra offset the content of the first interval starts at, relative to `LineLeft`.

### Glyphs: what to draw

The output is a flat stream of positioned elements, each carrying the shaped glyphs it draws (`GlyphRun`), the
baseline the line put it on, the source text it covers, the clusters it holds, and the reason its geometry is what it
is. Annotations and emphasis marks come with their own geometry and their own glyphs, so they are not measured twice
either. Mapping back to the text goes through the source range and the cluster range, never through element order:
one source element becomes many output elements, and right-to-left text or a block's own sub-layout reorders them.
[`output-format.md`](output-format.md) is the contract for every one of those fields.

## A boundary is a decision, not a gap

The obvious place to keep inter-character space is on the character: measure a punctuation mark a little wider and
let the rest of the layout cope. That model cannot say what a reader actually needs to know — whether the space after
a full stop belongs to the stop or to the character behind it, whether a line may break there at all, whether that
space may be squeezed when the line is one character too long, and whether stretching it is allowed.

So the space is not attached to either side; it is a decision *between* them. That is what makes the adjustment stage
possible at all: a gap with an owner can be measured and moved, a gap with a range (`MinSpacing`/`MaxSpacing`) can be
squeezed to its lower bound and stretched to its upper one and no further, and a prohibition is a property of the
position rather than of a character, so the same character can be allowed to end a line in one context and refused in
another. It is also what keeps a rule in one place: the break search walks candidates and reads decisions; it never
re-asks the classifier what it is looking at, so adding or changing a rule is a compile-phase change and cannot drift
between the two stages that consume the boundary.

## A language is a profile, not a code path

A language is not a branch in the engine. It is a `LanguageProfile`: a parameter set (the strictness of line
prohibition and which class set it uses, whether script spacing applies and over which range, the first-line indent,
the default alignment, how a display form replaces a source character, where an annotation sits and which way it
reads, which side an emphasis mark goes) plus the ids of the rule features it wants. Resolution is a tag lookup that
walks from the most specific tag to the least and ends at the profile that makes no language assumption, and nothing
falls back to a language nobody asked for.

The consequences are the point of the design:

- **A language pack is data.** Adding one means adding a profile (and, at most, a rule set the engine has not seen
  before). The acceptance criterion the framework is held to is exactly that: *adding a language must not require
  touching the framework*.
- **A profile is validated when it is registered, not when it is used.** A profile that names a rule nobody
  registered, or a class set the engine cannot find, or a combination that contradicts itself, fails loudly at that
  point instead of quietly laying text out with a fallback rule.
- **The request still wins.** A profile supplies defaults for the language-governed half; everything the caller
  states explicitly is applied on top of it, so a profile can never override a deliberate choice.
- **The granularity is the paragraph.** Geometry (the box, the padding, the exclusions) belongs to the request,
  because a paragraph does not own the box it sits in; the language-governed half belongs to the paragraph, which is
  why a document can quote another language and each paragraph is laid out by its own convention.

```csharp compile
// A language is a set of values plus the ids of the rules it wants - no algorithm of its own.
LanguageProfileRegistry.Shared.Register(new LanguageProfile(
    id: "nl",
    fallback: ["und"],
    parameters: new TypographyParameters
    {
        WritingMode = WritingMode.HorizontalTb,
        BoundaryRuleFeature = TypographyFeatureRegistry.UnicodeBoundaryRuleId,
        EnableLineProhibition = false,
        EnableCjkLatinSpacing = false,
        FirstLineIndent = 0,
        Alignment = TextAlignment.Left,
    },
    description: "Dutch: the Unicode baseline, no prohibition, no script spacing"));
```

## Rule differences are features

Two languages rarely differ by having a rule or not having it; they differ by *which* rule, and a host may need a
rule the engine has never heard of. So a rule set is a registered feature rather than a branch: a feature declares an
id, the scope it answers for (a paragraph, a run, a boundary, a cluster), what it requires and what it conflicts with,
and the engine ships two boundary rules — the Unicode line breaking algorithm and the CJK prohibition rules tailored
on top of it. A profile names the id it wants, and the break search is unaware of which one it got.

Registration is where the combinations are checked. A profile whose feature needs something the profile does not
include, or whose features declare a conflict, is rejected with a message at that moment — a broken language pack
fails loudly rather than doing something else quietly. The same mechanism is how a host extends the engine: implement
the feature, register it, let a profile name it.

## A line has intervals, plural

Text that flows around an image does not only get narrower: a shape that narrows in the middle leaves a line with
usable room on **both** sides of it. A line modelled as one left and one right edge cannot express that, and the
stages that position elements cannot know where the second piece of the line starts.

So a line offers an ordered, non-overlapping, exhaustive list of intervals, an element says which interval it was
placed in, and the alignment of each interval is its own (distributing space across the gap of what is really two
lines is a different problem, and is not what the model claims). The rest of the line geometry stays what it was: the
first interval is the common case, and asking for it stays a one-liner.

## Axes: inline, block and the writing mode

A writing mode decides two things and nothing else: which direction a line runs in (the **inline** axis) and which
direction lines advance in (the **block** axis). Horizontal writing is a line growing rightward with lines stacking
downward; right-to-left vertical writing is a line growing downward with columns advancing leftward. Modelling that
as "X or Y" inside each stage would put a branch in every place that computes a coordinate, and every additional mode
would multiply them.

So every stage expresses geometry as inline and block scalars, and the mapping into content space happens in one
place. `LayoutAxes` is that place: it knows the inline unit vector, the block unit vector, where block coordinate
zero sits (the block-start edge of the content box — its *right* edge for right-to-left columns), and how to turn a
pair of scalars into a point, a content-space vector into an inline or block coordinate, and a pair of extents into a
box size. Content space itself never changes: X is still to the right and Y still down, so exclusions, hit testing,
padding and scroll offsets keep working in page coordinates, and a mode is a table entry rather than a second
geometry.

```csharp compile
// Inline/block scalars in, content space out: no stage has to ask which mode it is in.
var axes = new LayoutAxes(WritingMode.VerticalRl, BlockExtentLimit: 800f);

Vector2 boxStart = axes.Point(inlinePos: 0f, blockPos: 0f);    // block coordinate 0 at the box' top: its right corner
Vector2 boxSize = axes.Size(inlineExtent: 160f, blockExtent: 24f);
float baseline = axes.Block(boxStart) + 12f;                   // a block-axis coordinate, not a Y
```

Two output fields inherit the abstraction, and both are easy to read the old way: the baseline is a coordinate on the
block axis rather than a Y, and the size of a laid-out box is its block extent first and its inline extent second.
What a mode does to the values an application sees is described in [`writing-modes.md`](writing-modes.md).

## Why the renderer does not measure

A text engine could hand a renderer the string and let it shape, measure and draw. It would also reintroduce the
failure the design exists to remove: the layout breaks lines with the advances *it* measured, and a second shaping in
the renderer is a second opinion about the same text. Different font resolution, a different feature set, a font the
engine could not see — any of them changes the drawn width, and the line that was measured to fit now overlaps its
neighbour or falls short of the margin. The disagreement is silent, and it appears exactly when the fonts change,
which is the one moment a document must not reflow by itself.

So measurement is not delegated: the compile phase shapes once and the output carries the result — glyph ids, their
advances and offsets, the face each one belongs to, the cluster each one came from. Drawing is then translation and
nothing else, which is why a renderer needs no font metrics at all on the glyph path, and why a font swap cannot move
a line. The same rule covers the things a renderer would otherwise have to invent: an automatic hyphen is shaped in
the compile phase and only placed by the assembler, an annotation and an emphasis mark arrive with their own glyphs
and geometry, and a display form is substituted before shaping, so the drawn glyphs are the ones that were measured.

The property is enforced as an invariant rather than intended: every cluster reaches the output at most once, the
source text is covered exactly once, the glyph advances sum to the measured width, and only text elements carry glyph
runs. A renderer that still shapes the string itself cannot satisfy the last of these, which is the point.

## Where the two format documents sit

The pipeline has two ends, and each end has one document that is true at that end.

| Document | Is about | Is read |
|---|---|---|
| [`input-format.md`](input-format.md) | the element stream and the request settings a caller submits: types, units, defaults, sentinels, what each value must satisfy | while a request is being built, and by the compile phase, which consumes exactly those fields |
| [`output-format.md`](output-format.md) | the stream, line, glyph, annotation and boundary structures that come back, field by field, with the rule a consumer must follow for each | after a result arrives and before anything is drawn, mapped back or hit-tested |
| this document | why the pipeline is cut this way, and what a change to it has to respect | when a decision has to be made: adding a language, a rule, a mode, or a consumer |

[`writing-modes.md`](writing-modes.md) is the fourth piece: what a chosen writing mode changes about the geometry
those two contracts describe. The three of them deliberately do not repeat each other — the field-level truth is in
the two format documents, and this one explains the shape they describe.
