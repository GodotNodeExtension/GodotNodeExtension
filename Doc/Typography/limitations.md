# Typography limitations

No typography engine implements every convention, and the honest thing to do with the difference is to write it
down. This page is that record: what this component does not do, what it does instead, and — where one exists — the
way around it. Each entry is a decision rather than a defect, so the behaviour you meet in the output is the
behaviour described here.

## Line breaking and squeezing

- **Automatic hyphenation.** *Now*: a long word in a narrow column breaks inside itself in the languages that ship
  patterns (`en`, `en-GB`, `de`, `fr`), and `EnableHyphenation` turns the behaviour off per request. A word that
  could be hyphenated reserves the hyphen's advance while it is measured, and a line that really breaks there ends
  with the hyphen the line assembler places; its glyphs were shaped during the compile phase, so a renderer still
  draws nothing it did not receive. *Missing*: a language without patterns hyphenates not at all. *Workaround*:
  author-placed soft hyphens (U+00AD) break a word wherever you put them, in any language.
- **Justification on a line an exclusion splits.** *Now*: a line cut into two intervals falls back to left
  alignment per interval. *Missing*: distributing the space across the gaps of what is really one line, so such a
  line is not justified. *Workaround*: keep a paragraph that carries an exclusion left-aligned, so the block does
  not mix two alignment behaviours.
- **Hanging punctuation.** *Now*: implemented where the conventions allow it — a mark that would start the next
  line stays at the end of the current one and sticks out past its edge (clreq §6.1.3 allows this for simplified
  Chinese; traditional Chinese gets it only in vertical writing; jlreq §3.8.2 refuses it in mixed text). The
  content size includes the overhang, which is what a hanging mark is, and the element reports
  `Reason=HangingPunctuation`. *Missing*: nothing inside those rules — a language the conventions refuse it for
  does not get it.
- **The empty leading half of an opening bracket at the head of a line.** *Now*: trimmed when the language asks
  for it (jlreq §3.1.5's 折り返し天付き, clreq §6.3.2.3; Korean states no such adjustment): the line gains half an
  em and the element's `Reason` says `OpeningBracketHalfWidth`. *Missing*: an adjustment a convention does not ask
  for is not invented, so a language that states none keeps the bracket's full leading half.
- **The compressible range of a gap.** *Now*: the CJK/Latin gap carries both bounds (clreq §6.3.3, jlreq §3.2.6:
  1/8 to 1/2 em) and both are consumed — stretching stops at the upper bound, squeezing at the lower. *Missing*:
  the range of every other kind of gap is not read from the boundary; the adjustment stage handles those with its
  own priorities, which are engine values rather than figures a convention states.

## Wrapping around images

- **Exclusions.** *Now*: rectangles work, and so do polygons that narrow: a shape may occupy several intervals of
  one line, so text flows on both sides of it, and each element reports which interval it landed in (`SpanIndex`).
  *Missing*: a float's lifecycle (a shape that moves or resizes between lines), a hole that comes from a second
  contour, and what a self-intersecting contour means — the even-odd rule makes it alternate rather than fill.
  *Workaround*: give each state of a moving shape its own line range (`WrapRegion.FirstLine`/`LastLine`) and lay
  the paragraph out again for that state.

## Annotations

- **Mono, group and jukugo.** *Now*: all three are laid out; the annotation is shaped at the ratio the caller asks
  for (`RubySpec.SizeRatio`, half the base size by default), the run it annotates never breaks across lines, and
  where it goes and which way it reads are the language's decision. Jukugo ruby widens the base run's
  inter-character spacing when a character's annotation is wider than the character, so neighbouring annotations
  keep apart (jlreq §3.3.7). *Missing*: that widening's per-gap cap is an engine value, because the convention
  states the principle rather than a number.
- **The room an annotation needs is the engine's.** *Now*: the annotation band is reserved by the layout and added
  to the line's own box, so an annotated line grows instead of letting its annotation overlap the line before it —
  even when the author asked for no line spacing. A Beside annotation's column overhang (half an em of the base
  size per annotated character, clreq §5.5.3.2) grows the line the same way. *Missing*: this is a deliberate
  departure from the conventions. jlreq leaves the band to the author's line spacing and clreq asks for a line gap
  of one and a half times the base size for a right-hand annotation in vertical writing; neither lays out a
  document whose author gave none. The engine reserves the band itself rather than assuming the author did.
- **The size ratio, and who gets the reserved half em.** *Now*: the default annotation size is half the base size
  (jlreq's figure, and what the CSS Ruby UA stylesheet gives every annotation), the annotation's direction is its
  own — `Vertical` sets it down a column of its own, one symbol under the next, beside the base character, in a
  horizontal paragraph as much as in a vertical one (clreq §5.5.3.1) — and its room is measured from the ink the
  glyphs actually leave, not from the annotation font's line metrics, so a band can never be shorter than the mark
  it exists for. *Missing*: clreq §5.5.3.2's 3:10 ratio is not enforced, the figure belongs to the annotation
  system that needs it; and the half em a Beside annotation takes is added to the characters that carry an
  annotation, not to the characters that carry none, which the same note asks for in a horizontal paragraph (the
  engine implements the smallest reading of the stated purpose). *Workaround*: ask for the ratio you want —
  `SizeRatio = 0.3f` for Bopomofo.

## Emphasis marks

- **Placement.** *Now*: a mark is placed on the side its language puts it on — under the characters in horizontal
  Chinese, over them in horizontal Japanese, to the right of the column in vertical writing (clreq §5.3.1,
  jlreq §3.3.9) — centred on each character, at a size that is a convention value (1/4 em) rather than a figure
  the specifications state. A mark and an underline both grow the line (see [`output-format.md`](output-format.md)
  on the line's two extra sides). *Missing*: an emphasis mark and an annotation on the same character compete for
  the same room, and nothing is reserved in the mark's favour — the mark sits outside the character's box and the
  band is measured for the annotation.

## Writing modes

- **Vertical writing.** *Now*: implemented — columns, the column's own line length, rotated Latin runs, annotations
  and marks on the column's side, and the line heights that follow from them. *Missing*: no vertical-specific
  kerning or inter-character adjustments beyond the horizontal rules the same conventions define; no right-to-left
  columns, because `VerticalLr` is refused rather than laid out horizontally; and no automatic default, because a
  request has to ask for columns.

## Baseline model

- **The ink position of a full stop inside its box** (clreq §6.1.3, jlreq §3.1.2). *Now*: what is drawn is the
  placement the font's own glyph carries. *Missing*: the language-specific position inside the em box is not
  modelled, because it needs font variants the engine does not read.
- **Baseline kinds.** *Now*: a line carries one baseline model — the ascent and descent the resolved font reports.
  *Missing*: ideographic versus alphabetic baselines are not modelled, for the same reason: the variants that would
  say which one a character wants are not read, so neither is claimed.

## Content size

## Text effects and syntax highlighting

- *Now*: both are carried through untouched — `TextEffectId` names an effect in the consumer's registry, and
  `SyntaxSpans` colors spans of a text element. *Missing*: neither takes part in shaping, measurement or line
  breaking, and text drawn with an effect goes through the string path, so a consumer that draws it from the text
  shapes it a second time. *Workaround*: where an effect can be applied to already-shaped glyphs, apply it to the
  element's glyph run; that keeps what was measured and what is drawn the same.

## Right-to-left text

- *Now*: laid out — a paragraph whose language is right to left fills from the right edge, each run is shaped in
  its own direction, the runs of a mixed line are put in visual order, and paired characters (brackets, quotation
  marks) are drawn as their mirror image. The levels and the visual order come from a UAX #9 implementation; the
  paragraph direction, the line-level rules and the mirroring are this component's. *Missing*: no API forces a
  direction onto a range — a caller cannot say "this span is right to left" the way a document format's own bidi
  override can. *Workaround*: let the language carry the direction (a paragraph's declared language decides it), or
  split the text so that the paragraphs separate the two directions.

## Languages and undeclared text

- *Now*: a paragraph that names no language is read for what it is — text carrying kana is laid out as Japanese and
  text carrying Hangul as Korean, whatever the request declared for the document, because everything that follows
  the language (where an annotation goes, how it is set, line breaking, prohibition, hyphenation) would otherwise
  answer with the document's convention rather than the text's. The inference is a *default*: a declared language
  always wins. *Missing*: Han-only text infers nothing, because simplified and traditional Chinese share one
  script, so a Han-only paragraph with no declaration keeps the request as it stands. *Workaround*: declare the
  language per paragraph wherever the text alone cannot say it.

## Documentation consistency

The pages of this documentation are published together and kept in step:

- **One page, one question.** Every page answers one kind of question — the two formats, the design, the languages,
  line breaking, the layout rules, rendering, the server, and the gaps listed here — so a page can be read on its
  own. The whole set is mapped on the overview page ([`README.md`](README.md)).
- **Each page exists twice, with the same structure.** An English page and a Chinese page carry the same sections
  in the same order, so a section exists in both editions or in neither, and the two cannot drift apart.
- **Links stay inside this set.** A page links the other pages of this component, the English edition linking the
  English pages and the Chinese edition the Chinese ones, so no link leads out of the documentation you installed.
- **Units and terms mean the same thing everywhere.** A length is pixels, `em` appears only where a rule states a
  value in ems, and a term means on every page what it means on the page that introduces it.
- **A gap is described where it belongs or listed here.** Nothing is left unmentioned on the assumption that a
  reader will not notice it.

These limitations are known and written down, not assumed not to exist.
