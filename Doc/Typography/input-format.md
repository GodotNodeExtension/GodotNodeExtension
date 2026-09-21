# Typography input format

This is the **input** side of the contract: the `DrawElement` stream and the `TypographySettings` object a caller
hands to the typography engine, field by field — type, unit, default, meaning and what each value must satisfy.
The laid-out elements that come back are described in [`output-format.md`](output-format.md).

## Units and coordinate space

- **Every length is pixels, as a `float`.** No input value is expressed in any other unit.
- **`em` appears only as a parameter unit and as the unit a specification states a value in.** A first-line indent
  and the automatic tab-stop spacing are given in ems (character widths); the engine turns them into pixels with the
  font size of the run they belong to. Every geometric value that comes back is pixels.
- **Font size is an `int` in pixels** (`DrawElement.FontSize`); a value of `0` means "not set" and the engine
  substitutes `16` px.
- **The content origin is the top-left corner of the content box**, X to the right and Y down; `Padding` is applied
  inside that box, so laid-out content starts at `(Padding, Padding)` and `ContentSize` covers the padding as well.
  `Position`, `Size` and `BaselineY` are all expressed relative to that origin.
- **The writing mode decides which axis carries the line and which one stacks the lines.** The request's
  `WritingMode` is resolved through the axis abstraction, so a stage never asks "am I vertical?":

| Writing mode | Inline axis (along a line) | Block axis (line stacking) | Block coordinate 0 |
|---|---|---|---|
| `HorizontalTb` | `+X` | `+Y` | left edge of the content box |
| `VerticalRl` | `+Y` | `-X` (columns advance leftward) | right edge of the content box |
| `VerticalLr` | `+Y` | `+X` | reserved; refused as an input value |

  In vertical writing the `Size` of a **laid-out** box is `(block extent, inline extent)`, i.e. `Size.X` is the extent
  across the column and `Size.Y` the extent along it — the two axes are simply exchanged, and the value is still
  expressed in content space. An input `Size` for a non-text object is read the other way round: `Size.X` is its
  extent along the inline axis and `Size.Y` its extent along the block axis, in either mode, because that is how the
  layout measures the box before it maps it into content space.
- **A text interval is a half-open range of UTF-16 code units**: `[Start, End)`. `TextRange.End` is exclusive, and
  a range of length *N* does not mean *N* visible characters (a combining mark, a CRLF pair and a ZWJ emoji sequence
  are indivisible units). Ranges carried by an element are relative to that element's own `Text`.

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

## Element kinds

`DrawElement.Type` says what the element is, and two boolean markers cut the stream into paragraphs and blocks.

| Kind | Meaning | How the layout treats it |
|---|---|---|
| `ElementType.Text` | Renderable text span. | Segmented by character class, shaped and measured; the source of every glyph run. |
| `ElementType.Rect` | Filled rectangle. | An inline non-text object: an atomic box of `Size`, placed at the pen like an image. |
| `ElementType.Line` | Line segment. | An inline non-text object, treated exactly like `Rect` (`Size` is its box). |
| `ElementType.Image` | Image / texture. | An inline non-text object; `Size` is its box and `Texture` what to draw. |
| `ElementType.ExtensionRegion` | Custom draw region owned by the caller. | An inline non-text object of `Size`; `ExtensionId`/`ExtensionContent` are carried to the output untouched. |
| `ElementType.Action` | Non-visual trigger point (a game action). | A zero-width, non-breakable marker that occupies no room and is carried through. |
| `IsParagraphBreak == true` | Paragraph separator. | Ends the current paragraph and finalizes its line; the element draws nothing and needs no text. |
| `IsBlockEnd == true` | End of a block region opened by the preceding element with `BlockInfo` set. | Closes the block; elements after it continue in normal flow. |

## `DrawElement` fields

Every field of `DrawElement`, including the ones the layout writes back and a producer must therefore leave alone.
The rows whose Required? column says *Layout output — ignored as input* are filled by the engine so that a consumer
can draw without re-deriving geometry; setting them on the way in changes nothing.

| Field | Type | Unit | Default | Meaning | Required? |
|---|---|---|---|---|---|
| `Type` | `ElementType` | — | `Text` | What the element is (see the table above). | Yes |
| `Position` | `Vector2` | px | `(0, 0)` | Box origin relative to the content origin. Ignored for elements in normal flow (the layout places them); it is an offset for elements inside a fixed-size block. | For fixed-size block content; otherwise ignored |
| `Size` | `Vector2` | px | `(0, 0)` | Box size. For a non-text object it is the intrinsic size: the inline extent and the block extent. | Yes for `Image`, `Rect`, `Line`, `ExtensionRegion` |
| `BaselineY` | `float` | px (block axis) | `NaN` | Coordinate of the text baseline along the block axis. `NaN` means "never computed". | Layout output — ignored as input |
| `SourceRange` | `TextRange` | UTF-16 code units | `[0, 0)` | Source text range this element covers. | Layout output — ignored as input |
| `GlyphRun` | `GlyphRun?` | — | `null` | The shaped glyphs to draw. | Layout output — ignored as input |
| `CharClass` | `CharacterClass` | — | `Ideograph` (enum 0) | Character class of the cluster. | Layout output — ignored as input |
| `SpanIndex` | `int` | — | `0` | Index of the line interval the element sits in. | Layout output — ignored as input |
| `ClusterStart` | `int` | — | `0` | Index of the first layout cluster. | Layout output — ignored as input |
| `ClusterEnd` | `int` | — | `0` | Index past the last layout cluster. | Layout output — ignored as input |
| `LineIndex` | `int` | — | `0` | Index of the line the element was placed on. | Layout output — ignored as input |
| `Direction` | `TextDirection` | — | `LeftToRight` | Base text direction in effect. | Layout output — ignored as input |
| `DisplayText` | `string?` | — | `null` | Text actually drawn when it differs from the source text. | Layout output — ignored as input |
| `Reason` | `string?` | — | `null` | Why the geometry is what it is. | Layout output — ignored as input |
| `Color` | `Color` | — | transparent black `(0,0,0,0)` | Color the element is drawn in. | No |
| `Text` | `string?` | — | `null` | Text content. A null or empty string produces no segment. | Yes for `Type.Text` |
| `Font` | `Font?` | — | `null` | Godot font the text is shaped with. | Yes for text, unless `ResolvedFontId` is set |
| `ResolvedFontId` | `ulong` | — | `0` | Id of the typeface `Font` resolved to, as handed out by the font catalog. `0` means "not resolved yet"; the engine then resolves `Font` itself, which is only sound on the main thread. | Recommended for a threaded request |
| `FontSize` | `int` | px | `0` (the engine substitutes `16`) | Font size the text is shaped at. | Yes for text |
| `Texture` | `Texture2D?` | — | `null` | Image to draw. | Yes for `Type.Image` |
| `LinkUrl` | `string?` | — | `null` | URL for link hit testing; carried through untouched. | No |
| `ElementId` | `int` | — | `0` | Caller-defined id for selection tracking; carried through. | No |
| `ExtensionId` | `int` | — | `0` (`-1` = not an extension) | Index into the caller's active extension regions. | Yes for `Type.ExtensionRegion` |
| `ExtensionContent` | `string?` | — | `null` | Raw content passed to the block extension (for example the code inside a fenced block). | No |
| `ActionTag` | `string?` | — | `null` | Action identifier (for example `"shake"`, `"sfx"`). | Yes for `Type.Action` |
| `ActionPayload` | `Variant` | — | default `Variant` (nil) | Optional data passed with the action. | No |
| `CharacterIndex` | `int` | UTF-16 code units | `0` | Index of the element's first character in the caller's global order, used for selection and typewriter progress. | No — but it must be consistent with the text (see *Input invariants*) |
| `CharacterCount` | `int` | UTF-16 code units | `0` | Number of characters the element covers (`0` for non-text). | No |
| `TextEffectId` | `int` | — | `0` (`-1` = no effect) | Index into the caller's text-effect registry. The layout never applies it; the consumer draws such text from the string. | No |
| `IsBold` | `bool` | — | `false` | Draw this text bold. Does not select a face — shape with a bold `Font` instead. | No |
| `IsItalic` | `bool` | — | `false` | Draw this text italic. Does not select a face either. | No |
| `IsStrikethrough` | `bool` | — | `false` | Strikethrough decoration. | No |
| `IsUnderline` | `bool` | — | `false` | Underline decoration; the part of it that reaches below the descender is added to the line's box. | No |
| `IsSubscript` | `bool` | — | `false` | Render as subscript (smaller, lowered baseline). | No |
| `IsSuperscript` | `bool` | — | `false` | Render as superscript (smaller, raised baseline). | No |
| `RubyText` | `string?` | — | `null` | Shorthand for an annotation over the whole element's text: sets `Ruby` with `Distribution = Group`. Reading it returns `Ruby.Text`. | No |
| `Ruby` | `RubySpec?` | — | `null` | Annotation to place over this element's text; see *Inline objects*. | No |
| `EmphasisMark` | `EmphasisMarkStyle` | — | `None` | Emphasis mark over the element's characters (着重点 / 圏点). The mark's shape and side come from the language. | No |
| `LaidOutRuby` | `RubyAnnotation?` | — | `null` | Where the layout placed the element's annotation. | Layout output — ignored as input |
| `LaidOutEmphasis` | `EmphasisMarkGeometry?` | — | `null` | Where the layout placed the element's emphasis mark. | Layout output — ignored as input |
| `SyntaxSpans` | `List<ColoredSpan>?` | — | `null` | Per-span syntax coloring. Null means "use the element's `Color`". The layout carries it but never shapes from it. | No |
| `CornerRadius` | `float` | px | `0` | Corner radius for rounded-rectangle rendering (`0` = sharp corners). | No |
| `IsParagraphBreak` | `bool` | — | `false` | Marks the element as a paragraph separator. | No (a marker) |
| `ParagraphSettings` | `ParagraphSettings?` | — | `null` | Paragraph-level overrides for the paragraph this element **begins**; null means "use the request's settings". Ignored on elements inside the paragraph. | No |
| `BlockInfo` | `BlockLayout?` | — | `null` | Marks the start of a block region. | No (a marker) |
| `IsBlockEnd` | `bool` | — | `false` | Marks the end of the block region opened by the preceding `BlockInfo` element. | No (a marker) |
| `VerticalAlignment` | `InlineVerticalAlignment` | — | `Baseline` (enum 0) | How a non-text element's height is distributed between ascent and descent: `Baseline` puts its bottom on the text baseline, `Top` aligns its top with the top of the line, `Middle` centres it on the baseline (half the height on each side), `Bottom` aligns its bottom with the bottom of the line. | No |
| `BackgroundColor` | `Color?` | — | `null` | Background behind the text (inline code, highlight). Non-null makes the renderer draw a background rectangle. | No |
| `BackgroundPadding` | `Vector2` | px | `(0, 0)` | Extra padding for the background (X = inline, Y = block), applied symmetrically; the inline component also widens the measured box. | No |
| `BackgroundCornerRadius` | `float` | px | `0` | Corner radius of the background rectangle (`0` = sharp). | No |
| `BackgroundFillLine` | `bool` | — | `false` | The background expands to the full line height instead of fitting the text. | No |

```csharp
using GodotNodeExtension.Component.Typography.Core.Model;

// A paragraph break is its own element: it separates paragraphs and draws nothing.
var breakElement = new DrawElement { IsParagraphBreak = true };
```

## Paragraphs and language

**How a paragraph is split.** A paragraph ends at the first of:

- an element whose `IsParagraphBreak` is `true` — it finalizes the current line and starts the next paragraph;
- a line-break character (`\n`, `\r`, or the pair `\r\n`) inside a `Text` element's `Text` — it counts as one break,
  ends the paragraph and is not drawn.

The **element that begins a paragraph** is the one that opens it: the first element of the document, or the first
element after a paragraph break. Only that element's `ParagraphSettings` is read; the field is ignored on every
element inside the paragraph, so a paragraph's overrides always live on its first element.

| Field | Type | Unit | Default | Meaning | Overrides |
|---|---|---|---|---|---|
| `LanguageTag` | `string?` | BCP-47 tag | `null` | Language of this paragraph. | `null` → the request's `LanguageTag` |
| `FirstLineIndent` | `int?` | character widths (ems) | `null` | First-line indent: `0` = off, `2` = the standard CJK indent. | `null` → the resolved language's indent |
| `SpacingBefore` | `float?` | px | `null` | Space before the paragraph. | `null` → no space before it (paragraph spacing is applied as the space *after* the preceding paragraph) |
| `SpacingAfter` | `float?` | px | `null` | Space after the paragraph. | `null` → the request's `ParagraphSpacing` |
| `Alignment` | `TextAlignment?` | — | `null` | Alignment of this paragraph's lines. | `null` → the resolved language's alignment |
| `LeftIndent` | `float` | px | `0` | Left indent for the whole paragraph (block quotes, list indents). | Always applies — it is an absolute value, not an override |
| `RightIndent` | `float` | px | `0` | Right indent for the whole paragraph. | Always applies — absolute value |

**How a language is declared.**

- **Request level**: `TypographySettings.LanguageTag` (BCP-47, for example `zh-Hans` or `en`). `null` means "not
  specified" and resolves to the profile that makes no language assumption.
- **Paragraph level**: `ParagraphSettings.LanguageTag` on the element that begins the paragraph. This is the
  language declaration's real granularity: a paragraph is the smallest unit a language attaches to, because indent,
  line-start rules and script spacing are paragraph properties rather than run properties — a mixed-language
  document normally switches at paragraph boundaries.
- A paragraph that declares its own language keeps the rest of the request as it stands: the request's explicit
  overrides still win over the language's defaults.

**When no language is declared** the engine reads the paragraph's own text and infers what the characters say:

- a **kana** character (hiragana, katakana, half-width katakana, including the prolonged sound mark) → the
  paragraph is laid out as `ja`;
- a **Hangul** character (syllables, Jamo, compatibility Jamo) → `ko`;
- **Han-only text infers nothing**: Simplified and Traditional Chinese share one script, so a Han-only paragraph
  keeps whatever the request declared (or the no-language-assumption profile).

The first character that names a script decides for the whole paragraph, and a **declared language is never
overridden** by inference.

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

## Request settings (`TypographySettings`)

`TypographySettings` is one object per layout request. **Geometry is request-owned and never comes from a language
profile** — a language supplies behaviour, not the box it is laid out in. The language-governed fields are nullable
on purpose: `null` means "whatever the language profile says", so a request-level default can never be mistaken for
a deliberate choice.

| Field | Type | Unit | Default | Meaning | Null means |
|---|---|---|---|---|---|
| `LanguageTag` | `string?` | BCP-47 tag | `null` | Language the whole request is laid out in. | "Not specified": the profile that makes no language assumption |
| `WritingMode` | `WritingMode?` | — | `null` | Writing mode of this request: `HorizontalTb` or `VerticalRl` (`VerticalLr` is reserved and refused). A writing mode is a layout decision, not a property of a language, so the caller chooses it. | The language profile's mode |
| `MaxWidth` | `float` | px | `0` | Maximum line width: the extent of a line along the inline axis in horizontal writing. In vertical writing it bounds the block axis (how far the columns may run). | Not nullable |
| `MaxHeight` | `float` | px | `0` | Maximum column height: the inline limit in vertical writing. Ignored in horizontal writing, where `MaxWidth` is the inline limit and the block axis is unbounded. Zero or less means "no limit". | Not nullable |
| `LineSpacing` | `float` | px | `0` | Extra line spacing (leading), added to the text's own line box; it stays a floor, because the engine adds the room the content asks for. | Not nullable |
| `ParagraphSpacing` | `float` | px | `0` | Space after a paragraph, used where `ParagraphSettings.SpacingAfter` is null. | Not nullable |
| `EnableLineProhibition` | `bool?` | — | `null` | Whether CJK line-start/line-end prohibition rules apply. | The language profile decides |
| `ProhibitionLevel` | `ProhibitionLevel?` | — | `null` | Prohibition strictness: `None`, `Basic`, `Gb` (GB/T 15834), `Strict` (dash and ellipsis also prohibited at line start). | The language profile decides |
| `EnableCjkLatinSpacing` | `bool?` | — | `null` | Whether spacing is inserted between CJK and Latin runs. | The language profile decides |
| `CjkLatinSpacingEm` | `float?` | em | `null` | Width of that gap (the CJK profiles use `0.25`). | The language profile decides |
| `EnableHyphenation` | `bool?` | — | `null` | Whether automatic hyphenation is applied (a language with break patterns hyphenates, one without does not). | The language profile decides |
| `EnablePunctuationCompression` | `bool?` | — | `null` | Whether punctuation may be squeezed. | The language profile decides |
| `FirstLineIndent` | `int?` | character widths (ems) | `null` | First-line indent for paragraphs that do not override it: `0` = off, `2` = the CJK convention. | The language profile decides |
| `Alignment` | `TextAlignment?` | — | `null` | Paragraph alignment: `Left`, `Center`, `Right`, `Justify`. | The language profile decides |
| `Direction` | `TextDirection?` | — | `null` | Base direction of the text: `LeftToRight` or `RightToLeft` (`RightToLeft` is produced by the RTL language profiles; no API forces it per range yet). | The language profile decides |
| `EnableLetterformSubstitution` | `bool?` | — | `null` | Whether the language's display forms (quotation marks, ellipsis, sentence marks) are applied. | The language profile decides |
| `GridStep` | `float?` | px | `null` | Grid step: every element's inline start is snapped to a multiple of this step from `GridOrigin`, which is what makes a page of text line up column by column and gives CJK/Latin mixing integral character cells (clreq §6.2.4). | No grid |
| `GridOrigin` | `float` | px | `0` | Where the grid starts, relative to the content origin. Only used when `GridStep` is set. | Not nullable |
| `TabStops` | `List<TabStop>` | px | empty list | Explicit tab stops, in ascending order; a tabulation character sends what follows it to the next one. | Not nullable (a list) |
| `DefaultTabStopEm` | `float?` | em | `4` | Spacing of the automatic tab stops, used where `TabStops` has no stop past the pen. | The automatic stops are off, so a tab past the last explicit stop does nothing |
| `WrapRegions` | `List<WrapRegion>` | px | empty list | Exclusion regions for image-text mixed layout. When non-empty, the layout uses per-line available-span queries instead of a fixed width. | Not nullable (a list) |
| `Padding` | `float` | px | `0` | Padding applied around the entire content area (all four sides). | Not nullable |

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

## Inline objects in a paragraph

A text element may carry an annotation and an emphasis mark, and any element may carry a background. These are
properties of one paragraph's inline content, not of the request.

**Ruby annotation (`RubySpec`).** The input says what the annotation is and how it should be distributed; the
geometry it ends up with is the layout's business. An annotation is an inline annotation, not a separate line: it
stays with its base text when a line breaks.

| Field | Type | Unit | Default | Meaning | Required? |
|---|---|---|---|---|---|
| `Text` | `string` | — | `""` (empty) | The annotation text. | Yes, and non-empty |
| `Distribution` | `RubyDistribution` | — | `Mono` | How the annotation is distributed over the base text (see below). | No |
| `SizeRatio` | `float` | fraction of the base font size | `0.5` | Annotation font size as a fraction of the base font size; the default is jlreq §3.3.3's half size. A value of `0` or less is treated as `0.5`. | No |

| `RubyDistribution` value | Meaning |
|---|---|
| `Mono` | One annotation piece per base character, centred on that character (jlreq's モノルビ). If the annotation has fewer characters than the base run, it is measured as one group piece instead. |
| `Group` | One annotation for the whole base run, centred over the run (jlreq's グループルビ). |
| `Jukugo` | Per-character annotations whose run is kept together and laid out as a group, so an annotation wider than its character does not collide with its neighbour (jlreq's 熟語ルビ, §3.3.7). The base run widens to fit the annotations it carries, up to a per-position cap that is an engine value. |

`RubyText` is the shorthand for the common case: setting it to a non-empty string is the same as setting `Ruby` to a
`RubySpec` with that text and `Distribution = Group`; setting it to null removes the annotation.

**Emphasis mark (`EmphasisMark`).**

| `EmphasisMarkStyle` value | Meaning |
|---|---|
| `None` | No emphasis mark (the default). |
| `Dot` | A dot: `●` (U+25CF) in Chinese, `•` (U+2022) in horizontal Japanese. |
| `SesameDot` | A sesame dot (`﹅`), the vertical-writing form in jlreq. |

The mark's **side** is not an input value: horizontal Chinese puts it below the characters (clreq §5.3.1),
horizontal Japanese above them (jlreq §3.3.9), and vertical writing to the right of the column. A mark grows the
line's box, because it sits outside the character's own box by convention — as does an underline.

**Background decoration.** These four fields decorate a text element; their inline padding also widens the measured
box, so neighbouring text is pushed away from it.

| Field | Type | Unit | Default | Meaning | Required? |
|---|---|---|---|---|---|
| `BackgroundColor` | `Color?` | — | `null` | Background color; non-null draws a rectangle behind the text. | No |
| `BackgroundPadding` | `Vector2` | px | `(0, 0)` | Extra padding, X along the inline axis and Y across it, applied symmetrically; the inline component participates in line breaking. | No |
| `BackgroundCornerRadius` | `float` | px | `0` | Corner radius of the rectangle. | No |
| `BackgroundFillLine` | `bool` | — | `false` | The rectangle expands to the full line height instead of fitting the text. | No |

**Images, extension regions and actions.** An image is a text element's inline counterpart: `Type = Image` with
`Size` (the box it occupies) and `Texture` (what to draw). `VerticalAlignment` decides where that box sits relative
to the baseline. `Type = ExtensionRegion` is the same kind of box whose drawing the caller owns, described by
`ExtensionId` (the index of the region the caller registered) and `ExtensionContent`. `Type = Action` is a
zero-width marker carrying `ActionTag` and `ActionPayload`; it takes no room and nothing may break after it.

| Kind | Fields that matter | Unit | Default | Meaning |
|---|---|---|---|---|
| `ElementType.Image` | `Size`, `Texture`, `VerticalAlignment` | px / — | `(0,0)`, `null`, `Baseline` | Inline image of that box. |
| `ElementType.ExtensionRegion` | `Size`, `ExtensionId`, `ExtensionContent`, `VerticalAlignment` | px / — / text / — | `(0,0)`, `0`, `null`, `Baseline` | Inline extension region of that box. |
| `ElementType.Action` | `ActionTag`, `ActionPayload` | — / — | `null`, nil | Zero-width trigger point. |

**Blocks (`BlockLayout` + `IsBlockEnd`).** A block is a region whose internal layout is managed by the caller. The
element carrying `BlockInfo` opens it, the matching element carrying `IsBlockEnd` closes it, and the elements in
between belong to the block. A fixed-size block (`Size` not zero) is placed as **one unit** and its internal
elements keep their own positions relative to the block origin; an auto-size block (`Size == Vector2.Zero`) is laid
out by the engine, which computes the block's height.

| Field | Type | Unit | Default | Meaning | Required? |
|---|---|---|---|---|---|
| `Size` | `Vector2` | px | `(0, 0)` | Total measured size of the block. `Vector2.Zero` means "auto-size": the engine lays the content out and computes the size. | Yes for a fixed-size block; zero for an auto-size one |
| `FullWidth` | `bool` | — | `false` | The block spans the full content width and always starts on a new line. When `false` it may be placed inline if it fits in the remaining width. | No |
| `LeftIndent` | `float` | px | `0` | Left indent for the block's content. | Auto-size blocks only |
| `RightIndent` | `float` | px | `0` | Right indent for the block's content (its max width is reduced by this). | Auto-size blocks only |
| `Padding` | `Vector2` | px | `(0, 0)` | Internal padding (X = inline, Y = block); the content is offset by it and the block height includes it on both sides. | Auto-size blocks only |
| `BackgroundColor` | `Color?` | — | `null` | Background color of the block; non-null emits a filled rectangle behind all its content. | No |
| `BackgroundCornerRadius` | `float` | px | `0` | Corner radius of that rectangle. | No |
| `LeftBorderColor` | `Color?` | — | `null` | Left border color; non-null emits a vertical line at the left edge. | No |
| `LeftBorderWidth` | `float` | px | `0` | Left border width. | Required for the border to appear |
| `LeftBorderOffset` | `float` | px | `0` | Left border X offset from the block's left edge. | No |
| `MarkerText` | `string?` | — | `null` | Marker text drawn at the block's left edge, outside the content area (list bullets, ordered numbers), aligned with the first content line. | No |
| `MarkerFont` | `Font?` | — | `null` | Font of the marker text. | Required for `MarkerText` to be emitted |
| `MarkerFontSize` | `int` | px | `0` | Font size of the marker text. | No |
| `MarkerColor` | `Color` | — | transparent black `(0,0,0,0)` | Color of the marker text. | No |
| `MarkerElements` | `List<DrawElement>?` | — | `null` | Custom marker elements drawn at the left edge (non-text markers such as checkboxes), positioned relative to the marker area's origin. Used only when `MarkerText` is not set. | No |
| `WrapRegions` | `List<WrapRegion>?` | — | `null` | Exclusion regions for the block's internal layout. | Auto-size blocks only |
| `ContentAlignment` | `TextAlignment` | — | `Left` | Horizontal alignment of the block's content within the available width; affects fixed-size blocks narrower than the parent width. | No |
| `IsAutoSize` (read-only) | `bool` | — | `true` when `Size == Vector2.Zero` | Whether the block's height is computed automatically. | — |

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

## Wrap regions and tab stops

A wrap region is a positioned exclusion the text flows around; a tab stop is where a tabulation character sends
what follows it. Both are request-level lists.

| `WrapRegion` field | Type | Unit | Default | Meaning |
|---|---|---|---|---|
| `Shape` | `WrapShape` | — | a `RectWrapShape` of width `0` and height `0` | The shape of the exclusion zone. |
| `Position` | `Vector2` | px | `(0, 0)` | Position of the shape's origin in content space. |
| `WrapMode` | `WrapFloat` | — | `Left` | How the region is anchored in the text flow. |
| `Margin` | `float` | px | `0` | Margin around the shape; the text keeps this distance from it. |
| `FirstLine` | `int` | line index | `0` | First line the region belongs to (`0` = the first line of the layout); lines before it are not affected. |
| `LastLine` | `int` | line index | `-1` | Last line the region belongs to; `-1` means "to the end of the layout". |

| `WrapShape` | Fields | Unit | Default | Meaning |
|---|---|---|---|---|
| `RectWrapShape` | `Width`, `Height` | px | `0`, `0` | An axis-aligned rectangle; its origin is the region's `Position`. |
| `PolygonWrapShape` | `ScanlineExtents`, `ScanlineRuns`, `ScanlineStep`, `TotalWidth`, `TotalHeight` | px / px / px / px / px | `[]`, `[]`, `1`, `0`, `0` | A polygon with a per-scanline extent table, or one built from vertices (even-odd rule). A shape may occupy several intervals on one line, so text can flow on both sides of a narrowing shape. |

| `WrapFloat` value | Meaning |
|---|---|
| `Left` | Float to the left; text wraps on the right. |
| `Right` | Float to the right; text wraps on the left. |
| `Inline` | No float: an inline block that breaks the line. |

| `TabStop` field | Type | Unit | Default | Meaning |
|---|---|---|---|---|
| `Position` | `float` | px | — (positional, required) | Distance from the content origin. A tab is an alignment instruction, not a character with a width of its own, so the stop must be given. |
| `Alignment` | `TabAlignment` | — | `Left` | How the content after the tab lines up at that position. |
| `Leader` | `char?` | — | `null` | Character that fills the gap between the text before the tab and the content at the stop (a table of contents writes dots there). Null leaves the gap empty. |

Stops must be listed in **ascending** order; a tab sends what follows it to the first stop past the pen, and past the
last stop the automatic stops apply (`DefaultTabStopEm`) or the tab does nothing.

| `TabAlignment` value | Meaning |
|---|---|
| `Left` | The content starts at the stop. |
| `Center` | The content is centred on the stop. |
| `Right` | The content ends at the stop. |
| `DecimalPoint` | The content's decimal separator sits at the stop, which lines a column of numbers up; content without a separator falls back to `Left`. |

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

## Input invariants

**Ignored when the element is used as input.** `BaselineY`, `SourceRange`, `GlyphRun`, `CharClass`, `SpanIndex`,
`ClusterStart`, `ClusterEnd`, `LineIndex`, `Direction`, `DisplayText`, `Reason`, `LaidOutRuby` and `LaidOutEmphasis`
are filled by the layout so that a consumer can draw without re-deriving geometry. A producer leaves them at their
defaults; setting them changes nothing.

**What must be self-consistent.**

- **`CharacterIndex` and `CharacterCount` describe UTF-16 code units of the element's own text.** They are what
  selection and typewriter playback fall back on, so the element's declared extent has to match its text length and
  tile with its neighbours: each laid-out piece reports `CharacterIndex = CharacterIndex + offset-in-text` and
  `CharacterCount = length of that piece`. An element whose declaration does not match its text makes a selection
  land on the wrong characters. (`CharacterCount` is also taken verbatim for elements expanded out of a fixed-size
  block, so declare it there too.)
- **`Font` and `FontSize` decide the shaping.** Text is shaped with the font on its own element and the size on it
  (a non-positive size is substituted by `16` px). An element with neither a `Font` nor a `ResolvedFontId` measures
  nothing, and an element whose font cannot be resolved shapes nothing — it is not an error, it is simply empty.
  `IsBold` and `IsItalic` do **not** select a face: shape with a bold or italic font instead.
- **`ResolvedFontId` belongs to the main thread.** It is the typeface id the font catalog handed out. A producer
  that lays out from a background thread must fill it before submitting; the id `0` means "not resolved" and the
  engine then resolves `Font` itself, which is only legitimate on the main thread.
- **A block must be balanced.** Every element carrying `BlockInfo` needs a later element carrying `IsBlockEnd` before
  the stream ends. The elements in between belong to the block: a fixed-size block's internal elements are positioned
  by the caller (`Position` is their offset from the block origin) and are not part of the surrounding flow.
- **`Ruby` needs a font and a non-empty annotation.** An annotation is measured with the base element's `Font` and
  `FontSize`; a `RubySpec` whose `Text` is empty, or an element that carries no font, carries no annotation.
  `Mono` with fewer annotation characters than base characters is measured as a group, because it cannot be cut per
  character without inventing text.
- **`TextEffectId` and `SyntaxSpans` go through the string path.** They are carried to the output untouched and
  never take part in shaping, measurement or line breaking; a consumer that renders them draws from the element's
  text rather than from the glyph run.
- **A tab binds what follows it.** A line may not break between a tabulation character and the content that lines up
  at its stop, and the tab itself has no width of its own — the gap is decided by the stops.
- **A range is half-open and local.** Every `TextRange` is `[Start, End)` in UTF-16 code units, relative to the text
  of the element it belongs to. Length is not a character count.
- **Ascending lists.** `TypographySettings.TabStops` must be ascending by `Position`. `LastLine = -1` is the "to the
  end" sentinel; any other negative value has no meaning.
- **Non-text elements carry a size.** `Image`, `Rect`, `Line` and `ExtensionRegion` are boxes: `Size.X` is their
  extent along the inline axis and `Size.Y` their extent along the block axis. `Size = (0, 0)` makes them invisible
  rather than absent, and a non-text element consumes no text characters.

## Where the values end up on the output

Every input value either reaches the output as it was given, decides geometry, or decides which language rule runs.

| Input field or semantics | What it determines on the output |
|---|---|
| `DrawElement.Type` | `LayoutElement.Type` (unchanged). |
| `DrawElement.Text` | `LayoutElement.Text`; `LayoutElement.DisplayText` when the language substitutes a display form. |
| `DrawElement.Font`, `ResolvedFontId`, `FontSize` | `GlyphRun` (`FontId`, `FontSize`, `Glyphs`) and `LayoutElement.FontSize`; the run's `Width` is the sum of the glyph advances. |
| `DrawElement.Color` | `LayoutElement.Color`. |
| `IsBold`, `IsItalic`, `IsStrikethrough`, `IsUnderline`, `IsSubscript`, `IsSuperscript` | The same-named output fields; `IsUnderline` and `EmphasisMark` also grow the line's box. |
| `Ruby` / `RubyText` | `LayoutElement.RubyText` and `LayoutElement.Ruby` (a `RubyAnnotation` with the shaped annotation glyphs, position, orientation and the reserved band width). |
| `EmphasisMark` | `LayoutElement.EmphasisMark` and `LayoutElement.Emphasis` (an `EmphasisMarkGeometry` with the mark, its size, centre and side). |
| `BackgroundColor`, `BackgroundPadding`, `BackgroundCornerRadius`, `BackgroundFillLine` | `LayoutElement.Background*`; adjacent pieces of one element are merged into a single background rectangle, which becomes a `Rect` element whose `Color`, `CornerRadius` and `Size` come from these fields. |
| `DrawElement.Size` (non-text) | `LayoutElement.Size`, and with `VerticalAlignment` the element's `BaselineY` and box position. |
| `VerticalAlignment` | Where a non-text box sits relative to the baseline (its `BaselineY`). |
| `Texture`, `LinkUrl`, `ElementId`, `ExtensionId`, `ExtensionContent`, `ActionTag`, `ActionPayload`, `SyntaxSpans`, `CornerRadius`, `TextEffectId` | The same-named output fields, unchanged. |
| `CharacterIndex`, `CharacterCount` | `LayoutElement.CharacterIndex` (base index plus the piece's offset) and `CharacterCount` (the piece's length). |
| `IsParagraphBreak` | Not an output element: it finalizes the current line and starts a new paragraph, which the output reports as a line/paragraph structure. |
| `ParagraphSettings` | The geometry of that paragraph's lines: their indents, their spacing before and after, the elements' `Position`, and the line's alignment. |
| `BlockInfo` | `LayoutElement.SubLines` / `BlockInfo` for an auto-size block, plus the block's decorations and marker, emitted as synthesized `Rect`, `Line` and `Text` elements. |
| `IsBlockEnd` | Closes the block; the block's content is emitted between the two markers. |
| `DrawElement.Position` (inside a fixed-size block) | `LayoutElement.Position`, offset by the block's placement. |
| `TypographySettings.MaxWidth`, `MaxHeight` | Line breaking: where each line ends, hence every element's `Position`/`Size` and `LayoutResult.ContentSize`. |
| `TypographySettings.Padding` | The offset of the content origin (content starts at `(Padding, Padding)`) and the content size. |
| `LineSpacing`, `ParagraphSpacing`, `FirstLineIndent`, `Alignment`, `Direction`, `GridStep`, `GridOrigin` | Line boxes and element positions: the baseline coordinates, the line pitch, the first-line indent, how a line fills its interval, and where an element is allowed to stand on the grid. |
| `WrapRegions` | The available intervals of each line, hence element `Position`s and `SpanIndex`. |
| `TabStops`, `DefaultTabStopEm` | The tab element's `Size` (the gap up to the stop) and `Text` (the leader character). |
| `TypographySettings.LanguageTag` and each language-governed override | Which language rules run: the resolved profile decides prohibition, script spacing, hyphenation, display forms, ruby placement and emphasis side, all of which show up in `DisplayText`, `Hanging`, `Reason` and the annotations' geometry. |

The output side — every `LayoutElement` field, the line and diagnostics structures — is described in
[`output-format.md`](output-format.md).
