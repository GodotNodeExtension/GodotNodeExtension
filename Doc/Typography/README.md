**English** | [中文](README.cn.md)

# Typography

Typography is a multi-language typography server for Godot: it turns a stream of semantic elements into
positioned, **glyph-level** output that a renderer can draw without measuring or shaping anything again.

It owns the two halves of text that a drawing API cannot decide: **where text goes** (line breaking,
alignment, spacing, wrapping around exclusions) and **what it looks like at the character level** (shaping,
glyph selection, display forms, line-start and line-end rules). It does not render: it never touches a
canvas, a texture or a pixel. What comes back is a flat stream of `LayoutElement`s carrying the shaped
glyphs and the geometry they were measured at, plus the lines they were placed on, so drawing is a walk
over that stream — see [rendering.md](rendering.md).

Page-level layout is out of scope as well: no pages, no magazine columns, no headers, footers, tables or
figures. A document is a stream of paragraphs, inline objects and blocks inside one content box.

## What it can do

The languages the server ships with, and the behaviours that follow the language. Every cell is what the
profile supplies by default — a request may override any of them.

| Behaviour | `zh-Hans` | `zh-Hant` | `ja` | `ko` | `en` | `ar`, `he` | `und` | Any other tag |
|---|---|---|---|---|---|---|---|---|
| **Line breaking** | clreq classes, on the Unicode baseline | the same classes as simplified | jlreq classes (small kana, prolonged sound marks) | klreq classes | Unicode line breaking (UAX #14) | UAX #14 | UAX #14 plus the engine's historical CJK tailoring | same as `und` |
| **Prohibition** | clreq classes, level `Basic` | clreq classes, level `Basic` | jlreq classes, level `Basic` | klreq classes, level `Basic` | — | — | the historical set, level `Basic` | same as `und` |
| **CJK/Latin spacing** | 1/4 em, 1/8–1/2 | 1/4 em, 1/8–1/2 | 1/4 em, 1/8–1/2 | 1/4 em, 1/8–1/2 | — | — | 1/4 em | same as `und` |
| **Indent / alignment** | 2 characters / justified | 2 characters / justified | 1 character / justified | 1 character / justified | 0 / left | 0 / left, the line filling from its right edge | 0 / left | same as `und` |
| **Punctuation width** | half em trimmed at the line end; opening bracket trimmed at the line head | same as simplified | the half em after a line-ending stop is preserved (jlreq §3.1.9); opening bracket trimmed | no line-end adjustment | — | — | compression only | same as `und` |
| **Display forms** | — | corner brackets and the centred ellipsis | the Japanese forms (`「」` for quotation marks, `……`) | narrow sentence marks (`.` and `,` for `。` and `、`, klreq §6.1.2) | — | — | — | same as `und` |
| **Annotations** | a reading set above the character, along the line (pinyin) | Bopomofo beside the base character, set down a column (clreq §5.5.3.1) | furigana above the character, in the band between the lines (jlreq §3.3.9) | none declared (klreq leaves annotations to the ruby specification) | none declared | none declared | the band above the text box is reserved | same as `und` |
| **Default writing mode** | `HorizontalTb` | `HorizontalTb` | `HorizontalTb` | `HorizontalTb` | `HorizontalTb` | `HorizontalTb` | `HorizontalTb` | `HorizontalTb` |

- **An unregistered tag resolves to `und`**, which is the explicit no-language-assumption path — never
  silently to Chinese. `en-GB`, `de` and `fr` are registered too: the same shape as `en`, with their own
  hyphenation patterns.
- **Language is a paragraph property**, so one document can mix them: a Chinese document may quote a block
  of English and each paragraph is laid out by its own convention. Geometry stays with the request.
- **Writing mode is the request's choice**, not the language's: a request may ask for `VerticalRl`
  columns whichever language it declares, no profile declares vertical text by default, and `VerticalLr`
  is refused.
- **Emphasis marks** (`着重点` / `圏点`) are placed on the side the language puts them: below the
  characters in horizontal Chinese, above them in horizontal Japanese, and to the right of a column in
  vertical writing (clreq §5.3.1, jlreq §3.3.9).

## Three-minute start

[getting-started.md](getting-started.md) takes you from an empty project to a drawn paragraph: how to get
the server and a handle, how to build the element stream, how to submit a request and read the result
(once per frame or by waiting), a self-contained example that draws the returned glyph runs, a `Control`
subclass that lays text out inside a project, and the pitfalls worth knowing before you write the first
line of renderer code.

## Documentation map

| Document | Content |
|---|---|
| [getting-started.md](getting-started.md) | From zero to the first drawn paragraph: handle, elements, request, result, drawing |
| [design.md](design.md) | The model behind the engine: phases, the boundary model, profiles, caches |
| [input-format.md](input-format.md) | The input side, field by field: `DrawElement`, `TypographySettings`, paragraphs and languages |
| [output-format.md](output-format.md) | The output side, field by field: `LayoutElement`, `LayoutLine`, `GlyphRun`, `Boundary` |
| [rendering.md](rendering.md) | How to draw a result: axes, glyph runs, element order, annotations, hit testing, debug overlays |
| [languages.md](languages.md) | Language profiles, tag resolution, paragraph-level declaration and text inference |
| [annotations.md](annotations.md) | Ruby and emphasis marks: what to ask for, and where the layout puts them |
| [writing-modes.md](writing-modes.md) | Horizontal and vertical writing: columns, rotated runs, what a mode changes |
| [line-breaking.md](line-breaking.md) | Break opportunities, prohibition, hyphenation, hanging punctuation |
| [layout-and-spacing.md](layout-and-spacing.md) | Lines, intervals, indents, alignment, justification, grids, tabs, wrap regions |
| [server-and-threading.md](server-and-threading.md) | Handles, request kinds, results, threading and lifetimes |
| [limitations.md](limitations.md) | What is not implemented or not claimed, stated plainly |

## Requirements

- **Godot 4.7 or newer** — the component is developed and tested against it.
- **.NET SDK 10.0 or newer** — the sources target the `net10.0` framework.
- **Three NuGet packages**: `HarfBuzzSharp` (≥ 8.3.1.2), the glyph indices a shape-once pipeline draws
  with; `SkiaSharp.HarfBuzz` (≥ 3.119.1), the shaper the text is shaped with; `Unicode.Bidi` (≥ 0.3.18),
  the UAX #9 implementation behind right-to-left text.

## Three decisions the design rests on

- **Boundaries are decisions, not gaps.** Every adjacent pair of clusters produces a decision that carries
  what the break and adjustment stages need: forbidden at line start, forbidden at line end, forbidden to
  break, the spacing it may be squeezed or stretched to, who owns it, and the reason for it. A rule is
  expressed once and read by whoever needs it, instead of being re-derived from character classes.
- **A language is data, not a code path.** A `LanguageProfile` supplies parameters and names the rule set
  it runs with, so adding a language is adding data. The acceptance criterion is that adding a language
  must not require touching the framework.
- **Rule differences are features.** A line-break policy is a registered feature
  (`line-breaking.unicode`, `line-breaking.kinsoku`), so a host can register its own and a profile can name
  it. Profiles are validated when they are registered: a missing rule set or a declared conflict fails
  loudly instead of laying text out with a fallback rule.
