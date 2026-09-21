# Typography line breaking and wrapping

Where a line ends is a decision, and the layout makes it once: the intervals a line offers (`LayoutLine.Spans`), the
reasons behind them, and the marks the layout placed itself (`Hanging`, `HyphenRun`) all come back in the output, which
is described field by field in [`output-format.md`](output-format.md). This page is the behaviour behind those numbers:
which rules refuse a break, how a justified line gains or gives up room, when a mark may sit outside the line, how a
long word is broken inside itself, how text flows around a shape, and where a tabulation character sends what follows
it. The request fields named here are in [`input-format.md`](input-format.md), and the values each language chooses are
in [`languages.md`](languages.md).

## What Unicode decides, and what a language adds

Two Unicode algorithms supply the baseline every language shares:

- **Line breaking (UAX #14)** answers one question — may these two characters be separated at all? Its answer is the
  default for every language, and for Western and right-to-left text it is the whole rule.
- **Segmentation (UAX #29)** supplies the units. A grapheme cluster is indivisible, so a line never breaks inside one
  (a combining mark, a CRLF pair or a ZWJ emoji sequence is not two characters as far as breaking is concerned), and
  word boundaries are what a word-level rule — hyphenation — starts from.

Both algorithms are checked against Unicode's own conformance suites on every change, and the property tables they
compile against are generated from a recorded Unicode version, so a version change is a reviewable change rather than
a silent one.

A language never reimplements that baseline; it tailors it. **Prohibition** (禁则, *kinsoku*) is the tailoring the CJK
conventions add: the baseline may permit a break between two characters while the convention refuses it at a line start
or at a line end. A language profile names the rule set it runs — `line-breaking.unicode` for the plain baseline,
`line-breaking.kinsoku` for Chinese, Japanese and Korean — and a host may register a rule set of its own and name it
from a profile, so a convention the engine has never seen is a data change rather than a framework change.

Every adjacent cluster pair becomes one **boundary**, decided once during the compile phase and reused by every later
step. A boundary carries whether a line may start or end there, whether the two clusters may be separated at all,
whether space may be added there, the gap between them and the range it may be squeezed or stretched within, which side
owns that gap, and a machine-readable reason. The break search reads those decisions instead of asking the character
classifier again, which is what keeps a rule in one place: the rules live in the language, and both the stage that
breaks lines and the stage that adjusts them merely consume them.

## Line-start and line-end prohibition

Two request fields decide whether prohibition applies and how strict it is; both are nullable, and `null` means
"whatever the language says":

| Field | Values | What it decides |
|---|---|---|
| `EnableLineProhibition` | `true`, `false` | whether the language's prohibition rules run at all |
| `ProhibitionLevel` | `None`, `Basic`, `Gb`, `Strict` | how strict they are — which classes of character are affected |

| Level | What it refuses |
|---|---|
| `None` | nothing: text breaks wherever the Unicode baseline allows |
| `Basic` | the set's basic level: pause and stop marks, closing marks, connector marks, the interpunct and the solidus may not start a line, and opening marks may not end one |
| `Gb` | the GB/T 15834 shape of the same set; for Chinese it adds the solidus at line end, while the Japanese and Korean sets define the same characters for it as for `Basic` |
| `Strict` | the strictest: the basic set plus two-em dashes and ellipses at a line start |

**Which characters count is language data, not algorithm.** The rule is always the same — "do not start a line with a
closing bracket" — and what differs is the class set and the strictness. The shipped sets are the engine's historical
one, Chinese (clreq §6.1.1's pause and stop marks, closing marks, connector marks, interpunct and solidus at line start
and opening marks at line end), Japanese (jlreq §3.1.7/§3.9: closing brackets, hyphens, dividing marks, middle dots,
stops, commas, iteration marks, prolonged sound marks and small kana may not start a line, opening brackets may not end
one — jlreq's `cl-01`…`cl-30` rows that are about line edges) and Korean (klreq §7.1.2/§7.1.3's own set). A host
registers its own set in the same shape: a line-start set and a line-end set per strictness level.

**The stricter side wins.** A pair is refused when either side's convention refuses it: prohibition is a prohibition the
moment one side states it, never a preference to be balanced against the other side's silence.

**Some pairs may not be separated at all**, whatever the width: the two halves of a two-em dash, the halves of an
ellipsis, a number with its affix, unit or currency symbol, an annotated run (an annotation never breaks away from the
text it annotates), a tabulation character and the content that lines up at its stop, and a zero-width action marker
with whatever follows it. These arrive at the break search as "the two clusters must stay on the same line", so nothing
in the search has to know why.

When a line runs out of room and no candidate is left, it breaks where it must: a box too narrow for one unbreakable
run, or a stretch of text whose every break point is forbidden, ends a line short of its width or lets a single piece
overflow. That is a layout-quality signal rather than a normal outcome, and `LayoutResult.ProhibitedBreakSkips` counts
the candidates that prohibition rejected, which is the cheapest explanation for a line that ends earlier than its width
allows.

## Squeezing and stretching a line

Justification asks every line but the last of a paragraph to fill the room it was given, and a line can move in two
directions to do it. The order of the two lists below is the whole policy: a line takes room from the first place that
can give it, and gives it back to the first place that can take it.

**A line gains room in this order:**

| Priority | Where the room comes from | Bound |
|---|---|---|
| 1 | the word spaces of Latin text | none stated — this is where a Western paragraph's elasticity lives |
| 2 | the gap between a CJK run and a Latin one | the upper bound the convention states (below) |
| 3 | the gap between two CJK characters | none stated |

**A line gives up room in this order:**

| Priority | Where the room comes from | Bound |
|---|---|---|
| 1 | the punctuation that ends the line | trimmed to half width, and only where the language trims it (see below) |
| 2 | the word spaces of Latin text | a bounded amount per space, an engine value rather than a figure a specification states |
| 3 | the gap between a CJK run and a Latin one | the lower bound the convention states (below) |
| 4 | the other punctuation on the line | each mark proportionally, up to half of its own width |

The first priority of squeezing is a language decision, not an engine one. Chinese trims the trailing half em of a
full-width mark that ends a line when the line needs the room (clreq §6.2.2.3); Japanese keeps the half em after a stop
uncompressible, because that half em is part of the mark, so a Japanese line that does not fit breaks earlier instead
(jlreq §3.1.9); Korean has already chosen a narrower character for the sentence marks of horizontal writing, which is a
display form and not a width rule. `EnablePunctuationCompression` switches the punctuation half of squeezing off
entirely, for a caller who wants a line to break rather than give up a mark's width.

**The one gap whose range the conventions state** is the gap between CJK and Latin text: a quarter em by default
(clreq §6.3.3, jlreq §3.2.6), compressible to an eighth of an em and stretchable to half an em. The layout inserts the
gap where CJK and Latin were written next to each other without one — `EnableCjkLatinSpacing` decides whether it is
inserted at all, and `CjkLatinSpacingEm` how wide it is.

| Bound | Consumed by | What happens when it runs out |
|---|---|---|
| 1/8 em — the lower bound | squeezing | the gap stops giving up room and the squeeze continues with the next priority |
| 1/4 em — the default | — | the room the layout inserts between the two scripts |
| 1/2 em — the upper bound | stretching | the gap stops growing and the remaining slack goes to the next priority |

Both ends are therefore consumed, and neither is exceeded: a justified line stops widening a gap at what the convention
permits and takes the rest from the inter-character gaps of CJK text instead, and a squeezed line stops shortening a gap
at the same convention's floor instead of closing it. This is the only gap whose range is read from the convention; for
every other kind of gap the adjustment stage has its own priorities and no stated range.

A pair the convention may not separate further — the separation prohibition, jlreq §3.1.11 — is not a candidate for
stretching at all, whatever the line would like to do with it.

Two cases are deliberately not stretched: **the last line of a paragraph**, which ends where it ends, and **a line that
offers more than one interval**, which is left-aligned per interval — distributing space across the gaps of what is
really two lines side by side is a separate problem, and pretending otherwise would move text across the obstacle in
between. A line whose deficit or slack is below half a pixel is left at its natural position, because a rounding-level
difference should not move glyphs.

## Hanging punctuation

A mark that may not start a line need not move to the next one: where the convention allows it, it stays at the end of
the current line and sticks out past its edge — clreq §6.1.3's hanging punctuation, jlreq's ぶら下げ. The line then ends
after it, and what follows it starts the next line, so the line's content is one mark wider than the interval it was
given. That overhang is the point of the behaviour, not an accident: the content size includes it, and a consumer draws
the mark outside the line box.

Three things have to agree before a mark may hang:

- **the mark itself** must be one the language refuses at a line start — a pause or stop mark, a closing mark or an
  ellipsis; an ordinary character has nothing to hang from;
- **the language** must allow hanging at all;
- **the line** must not already carry Latin text where the language refuses hanging in mixed writing (jlreq §3.8.2, which
  argues that a hanging mark reads badly next to Latin text).

| Language | Hanging punctuation | Why |
|---|---|---|
| Simplified Chinese | allowed (clreq §6.1.3) | the convention that asks for it |
| Traditional Chinese | only in vertical writing (clreq §6.1.3) | so in horizontal writing the mark moves to the next line as any other character would |
| Japanese | not in mixed text (jlreq §3.8.2) | a line that already carries Latin letters refuses the mark |
| Korean | not applied | no shipped Korean convention asks for it |
| Western and right-to-left | not applicable | a language without prohibition rules has no mark that may not start a line |

A line that offers more than one interval never hangs a mark, because the end of its first interval is the edge of an
exclusion: a mark that hung there would be printed on the obstacle. The element reports the outcome as `Hanging = true`,
and the layout placed it past the interval's edge, so the consumer draws it there and does not clip it.

## Breaking inside a word

A long word in a narrow column need not overflow the line or leave a gap before it: for the languages that ship
break patterns, the layout cuts the word at the places its language allows and breaks there.

| Language tag | Patterns |
|---|---|
| `en` | American English |
| `en-GB` | British English |
| `de` | German, 1996 orthography |
| `fr` | French |

The patterns are the hyph-utf8 TeX ones, and every table records its source, version, checksum and licence.
`EnableHyphenation` switches the behaviour off per request; `null` leaves it to the language. A language with no patterns
is untouched, and so is a language whose convention breaks between characters or at word boundaries without hyphenating
anything — Chinese, Japanese and Korean ship none, because a word-level break is not a rule their conventions have.

**How the break becomes an ordinary break.** During the compile phase a run of letters that is a word (at least four
characters, no digit, hyphen or slash in it, and not rewritten by a display form) is cut at the positions its language
allows. Each piece becomes a segment with a break opportunity after it, which is the same shape the Unicode algorithm
produces for punctuation and hyphens, so the break search needs no word knowledge at all: it sees a legal break inside
the word and takes it if the line needs it. A piece that could end a line reserves the hyphen's advance — the hyphen is
shaped once per word, during the compile phase — so the fit test pays for it, and a word the breaker never broke is not
shortened by a hyphen's width it never used.

**Where the hyphen itself comes from.** The line assembler places it, as a synthetic element whose `Reason` is
`HyphenationBreak`: an empty source range, no cluster, and the glyph run that was shaped in the compile phase (the piece
that broke reports it as `HyphenRun`). The consumer draws the hyphen it was handed and never appends one itself, because
a renderer that had to append it would have to know hyphenation exists — and the whole point of shaping during the
compile phase is that nobody else has to.

**Author-placed soft hyphens (U+00AD)** need no patterns: the Unicode line breaking class of that character already
permits a break after it, so the layout has a legal break there for free. The layout adds no hyphen of its own at such a
break: the character is the caller's, and what is drawn is what the caller wrote.

## Wrapping around shapes

A wrap region is a positioned exclusion the text flows around — an image, a rule, a custom region — and the request's
`WrapRegions` list is where the layout is told about it. A region names a **shape**, a **position** in content space, a
**margin** the text keeps away from it, and the lines it belongs to (`FirstLine`/`LastLine`, with `-1` meaning "to the
end of the layout"). The shape is a rectangle when that is all it is, and a polygon otherwise; a polygon carries a
per-scanline extent table, or is built from its vertices under the even-odd rule, which is what lets a concave shape
narrow in the middle.

**A line asks what the shape occupies at its own Y** — the line's height counts, so a shape that begins halfway down a
line still covers it — takes the complement inside the content width, and works out the room it has. That is what
`LayoutLine.Spans` reports, and a line has one interval or several:

- **one interval** for an ordinary line;
- **two or more** when an exclusion covers only part of the line: a float on the left and one on the right leave an
  interval in the middle, and a shape narrower in the middle than at its edges leaves one on each side;
- **none** when the shape covers the whole line, in which case the layout moves past the exclusion to the first block
  coordinate that is clear of it and lays the line out there.

When the pen reaches the end of an interval, it continues in the next one instead of ending the line early: a line is as
full as the room it has, and breaking at the obstacle's edge would leave the rest of that line empty. `LineLeft` and
`LineRight` describe the **first** interval only, so anything that positions elements must use the interval the element
was placed in — `LayoutElement.SpanIndex` says which one that is, and using the first interval for an element that sits
in the second moves text onto the obstacle.

Paragraph indents narrow the outer edges of the line, not each interval: the left indent comes off the start of the
first interval and the right indent off the end of the last, so an exclusion in the middle of a line never indents what
sits to its right.

What is not modelled yet: the **lifecycle of a float** (a shape that moves or resizes between lines, rather than one
whose belonging is stated up front), a **hole** produced by a second contour, and what a **self-intersecting contour**
means — the even-odd rule makes it alternate rather than fill, which is a different shape from a hole.

## Tab stops and leaders

A tabulation character is an alignment instruction, not a character with a width of its own: it has no ink that decides
how wide the gap is, so the layout has to be told where the tab sends what follows it. `TabStops` is the author's list
of stops — positions from the content origin, in ascending order — and each stop says how content lines up there:

| `TabAlignment` | What happens at the stop |
|---|---|
| `Left` (the default) | the content starts at the stop |
| `Center` | the content is centred on the stop |
| `Right` | the content ends at the stop |
| `DecimalPoint` | the content's decimal separator sits at the stop, which is how a column of numbers lines up; content without a separator falls back to `Left` |

A tab moves what follows it to the **first stop past the pen**. Past the last stop the automatic stops take over:
`DefaultTabStopEm` (four ems by default) measured from the line's own start, the em being the font size the line is set
in. Where the automatic stops are switched off, a tab past the last stop does nothing — which is what a tab with nowhere
to go should do.

**The leader is recorded, not shaped.** The layout does not fill the gap with glyphs; it says which character belongs
there and how wide the gap is. The tab's own element carries the leader in its `Text` (empty when the author asked for
none) and the gap up to the stop in its width, so a consumer repeats the character across the gap without shaping
anything — a table of contents' dots cost the layout nothing.

A tab binds what follows it: no line may break between a tabulation character and the content that lines up at its stop,
so a column's heading and its cells stay together.

Tab stops have the last word on placement: they are applied after the alignment has filled the interval and after the
grid has had its say, because a stop is a position the author named while a grid is a global rule, and the two only
disagree when both are switched on. The hyphen that ends a line broken inside a word is placed after even that.

```csharp
using GodotNodeExtension.Component.Typography.Core.Model;

// A line offers one interval for an ordinary line and more when text flows around an exclusion;
// every element reports which interval it landed in.
static LineSpan IntervalOf(LayoutLine line, in LayoutElement element)
{
    LineSpan[] spans = line.Spans.Length > 0 ? line.Spans : [new LineSpan(line.LineLeft, line.LineRight)];
    return spans[element.SpanIndex];
}

// A hanging mark is ordinary text the layout placed past the line's edge: draw it there, do not clip it.
static bool DrawsOutsideTheLine(in LayoutElement element) => element.Hanging;
```
