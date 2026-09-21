# Typography languages

A language here is **data, not a code path**. A `LanguageProfile` names the parameters a script convention
prescribes — where a line may break, how much room goes between scripts, what an indent and an alignment default
to, which display forms replace the characters that were written, where an annotation goes — and a request or a
paragraph names a language to select them. Adding a language means adding a profile; no stage branches on which
language is in force.

This document covers that layer: how a language is declared, what each shipped language does, what happens when
nothing (or nothing known) is declared, and how a new language is added. The fields a language reaches the engine
through, and the fields its decisions show up in, are listed in [`input-format.md`](input-format.md) and
[`output-format.md`](output-format.md); the annotation conventions have their own document,
[`annotations.md`](annotations.md).

## How a language is declared

A language is declared in one of two places, and the paragraph is the finer of them.

| Where | Field | Scope |
|---|---|---|
| The request | `TypographySettings.LanguageTag` | Every paragraph of the layout |
| A paragraph | `ParagraphSettings.LanguageTag` | The paragraph whose first element carries it |

- **The request** declares the document's language. `null`, or an empty string, means "not specified" and resolves
  to the profile that makes no language assumption. Geometry stays request-owned: a language never supplies the box
  it is laid out in, only the rules applied inside it.
- **A paragraph** declares its own language on the element that *begins* it; the field is ignored on every element
  inside the paragraph. Because a line's indent, its prohibition rules and the room between scripts are paragraph
  properties, a mixed-language document normally switches at paragraph boundaries: a Chinese paragraph followed by
  an English one puts each convention on the paragraph it belongs to.
- **A paragraph's language does not undo the request's explicit overrides.** The language-governed fields of a
  request are nullable, and a non-null value is the caller's deliberate choice; a paragraph's language supplies the
  rule set and every default the caller left open, not a replacement for what was asked for.

```csharp
using GodotNodeExtension.Component.Typography.Core.Model;

// The document is Chinese; this one paragraph is English and is laid out by English rules.
var request = new TypographySettings { LanguageTag = "zh-Hans", MaxWidth = 320f };

var paragraph = new DrawElement { Type = DrawElement.ElementType.Text, Text = "Typography" };
paragraph.ParagraphSettings = new ParagraphSettings { LanguageTag = "en" };
```

### Inference when nothing is declared

A paragraph that declares no language of its own is read for what it is. Characters say which language the text is
written in only in one respect, and that is the one the engine reads:

| The text carries | Inferred language |
|---|---|
| Kana — hiragana, katakana, half-width katakana, the prolonged sound mark included | `ja` |
| Hangul — syllables, Jamo, compatibility Jamo | `ko` |
| Han characters only | nothing |

- **The first character that names a script decides for the whole paragraph**, so a paragraph whose kana appear in
  its third sentence is Japanese from its first character on.
- **Han-only text infers nothing.** Simplified and Traditional Chinese share one script, so nothing in the
  characters distinguishes them; a Han-only paragraph keeps whatever the request declared, or the profile that
  makes no language assumption when the request declared nothing either.
- **No other script infers anything.** The question the characters answer is only "does this text carry kana or
  Hangul", which separates Japanese and Korean from Han-only text and is not enough to recognise a Western or a
  right-to-left paragraph. A Latin paragraph inside a Chinese document has to be declared, not guessed.
- **Inference ranks above the request.** It answers what the paragraph actually is, and a Japanese sentence inside
  a Chinese document is ordinary: laid out with the document's language it would take the document's line breaking,
  prohibition, annotation side and display forms, all of which belong to the wrong text's convention.
- **A declared language is never overridden by inference.** Once a paragraph names a language, its characters are
  not read for one; the inference fills a gap, it does not correct an author.

### Precedence

From strongest to weakest, the four sources a paragraph's language can come from:

| Rank | Source |
|---|---|
| 1 | `ParagraphSettings.LanguageTag` on the element that begins the paragraph |
| 2 | the paragraph's own text, when it carries kana or Hangul |
| 3 | `TypographySettings.LanguageTag` |
| 4 | no language at all — the profile that makes no language assumption |

### How a tag is matched

- **Longest prefix first.** `zh-Hant-HK` finds `zh-Hant`, and a tag with no profile of its own falls back to the
  most specific registered ancestor; a profile may also name a fallback chain of its own.
- **Tags are normalized**, so `zh_Hans` and `ZH-Hans` find the same profile: case is ignored and `_` stands for
  `-`.
- **A tag that matches nothing is not guessed at.** The engine does not treat an unknown language as Chinese, or as
  any other language it knows: it lands on the profile that makes no language assumption (see *A language without a
  profile*).

## What a language decides

A profile owns behaviour, never geometry. Everything it can say is a value, and every value is a default a request
may override.

| Aspect | Profile parameter | Where it shows |
|---|---|---|
| Break opportunities | `BoundaryRuleFeature` | Which rule decides where a line may break: the Unicode algorithm, or prohibition rules tailored on top of it |
| Prohibition | `EnableLineProhibition`, `ProhibitionLevel`, `ProhibitionClassSetId` | A boundary forbidden at line start or line end, and a line that ends earlier than its width would allow |
| CJK/Latin gap | `EnableCjkLatinSpacing`, `CjkLatinSpacingEm`, `CjkLatinSpacingMinEm`, `CjkLatinSpacingMaxEm` | The natural spacing between two elements, and how far a justified line may stretch or squeeze it |
| Punctuation squeezing | `EnablePunctuationCompression` | How far a line may compress the marks it ends with |
| Line-end punctuation | `LineEndPunctuation` | Whether a full-width mark that ends a line is trimmed to half width, or keeps its half em |
| Opening bracket at line head | `HalfWidthOpeningBracketAtLineHead` | An element whose `Reason` is `OpeningBracketHalfWidth` |
| Hanging punctuation | `HangingPunctuation` | `LayoutElement.Hanging` when a mark is allowed to stick out past the line edge |
| Indent and alignment | `FirstLineIndent`, `Alignment`, `LineSpacing`, `ParagraphSpacing` | The indent a first line starts at, and how a line fills the room it has |
| Text direction | `Direction` | Which edge a line fills from, and the order the elements of a line come out in |
| Writing mode | `WritingMode` | The mode a request is laid out in when it names none |
| Localized glyphs | `OpenTypeLanguageTag` | Which localized forms a font draws: the same code point is drawn differently by a Chinese, a Japanese and a Korean face |
| Automatic hyphenation | `HyphenationPatternsId` | Whether a long word may break inside itself, and where |
| Display forms | `EnableLetterformSubstitution`, `Letterforms` | `LayoutElement.DisplayText`, when the drawn character differs from the written one |
| Annotations | `RubyPlacement`, `RubyOrientation` | Where an annotation is placed, which way it reads, and the room the line reserved for it |
| Emphasis marks | `EmphasisSide`, `EmphasisMarkSizeEm` | Which side of the characters a mark goes on, and how large it is |

## Language by language

Every shipped profile declares horizontal writing; no language asks for columns, and a vertical layout is the
request's choice. Each section below says what the language's own convention contributes, and what to look for to
see it.

### Chinese, Simplified (`zh-Hans`)

- **Line breaking and prohibition.** The prohibition rule set, with the Chinese classes of clreq §6.1.1 at the
  basic level: pause and stop marks, closing quotation marks, closing brackets, connector marks, the interpunct and
  the solidus may not start a line, and opening marks may not end one. The GB-style level adds the solidus at line
  end, and the strict level adds dashes and ellipses at line start.
- **CJK/Latin gap.** A quarter em, compressible to 1/8 em and stretchable to 1/2 em (clreq §6.3.3), inserted where
  a Han character meets a Latin letter or a digit.
- **Indent and alignment.** A two-character first-line indent; justified lines.
- **Punctuation at the line end.** A full-width mark that ends a line has its trailing half em trimmed when the
  line needs the room (clreq §6.2.2.3). A mark that may not start a line may also hang past the line's end edge
  instead of moving down (clreq §6.1.3).
- **Display forms.** None: what was written is what is drawn. `“”` and `……` are already the simplified forms, so
  the profile substitutes nothing.
- **Annotations.** Above the character and along the line: a reading such as pinyin, tone marks included.
- **Emphasis marks.** Below the characters in horizontal writing (clreq §5.3.1), drawn as a filled circle.
- **What to look for.** A line that ends before its width would allow is prohibition at work, and the reason
  recorded for the rejected break says which rule refused it; a line that ends with a stop fitting where the glyph
  widths say it should not is the half em being trimmed.

### Chinese, Traditional (`zh-Hant`)

The same parameters as simplified Chinese — the same classes, indent, alignment, gap and line-end width rule —
with three differences.

- **Display forms.** Quotation marks are drawn as the corner brackets `「」『』` where `“”‘’` was written, and the
  ellipsis is centred in its em box where `…` was written (clreq §5.2, §5.1.4). The substitution changes the shaped
  glyph and the width it takes, and nothing else: the source range and the character class keep describing what the
  caller wrote, and the element reports both forms.
- **Annotations.** Bopomofo beside the base character, as a column of its own, in horizontal writing as much as in
  vertical (clreq §5.5.3.1). The room comes from the base text's own advance — half an em of the base size per
  annotated character (clreq §5.5.3.2) — so the line is the same height with or without annotations, unless a
  column is taller than the character it annotates. See [`annotations.md`](annotations.md).
- **Hanging punctuation.** Allowed in vertical writing only; in horizontal writing the mark moves to the next line,
  which is what the engine does today.
- **What to look for.** The corner brackets are the visible difference from simplified Chinese; the annotation
  direction is the other one, because a column runs down beside a character in an otherwise horizontal line.

### Japanese (`ja`)

- **Line breaking and prohibition.** The Japanese classes stated by jlreq §3.1.7 and §3.9: closing brackets,
  hyphens and dashes, dividing marks, middle dots, full stops, commas, iteration marks, the prolonged sound mark
  and small kana may not start a line, and opening brackets may not end one. Small kana and the prolonged sound
  mark are the two China's set does not prohibit.
- **CJK/Latin gap.** A quarter em, with the same 1/8 to 1/2 em range (jlreq §3.2.6).
- **Indent and alignment.** A one-character first-line indent; justified lines.
- **Punctuation at the line end.** The half em after a stop that ends a line is preserved and may not be compressed
  (jlreq §3.1.9), so a line that does not fit breaks earlier instead of squeezing the stop.
- **Display forms.** Quotation marks are drawn as corner brackets (jlreq §3.1.1). The ellipsis keeps its written
  form, which jlreq sets on the baseline rather than re-centring.
- **Annotations.** Above the character, in the space between the lines (jlreq's 行間処理). The engine reserves that
  band itself rather than trusting the author's line spacing, so two annotated lines cannot overlap even when no
  line spacing was asked for.
- **Emphasis marks.** Above the characters in horizontal writing (jlreq §3.3.9), drawn as a bullet by default and
  as the sesame dot where the element asks for it.
- **What to look for.** A line that could have held one more character but did not, because it ends with `。` —
  that is the preserved half em; and a small kana or a prolonged sound mark never opens a line, which is the class
  Japan's set adds to China's.

### Korean (`ko`)

- **Line breaking and prohibition.** The Korean classes of klreq §7.1.2 and §7.1.3: closing parentheses, hyphens,
  dividing marks, middle dots, commas, periods, iteration marks and the prolonged sound mark may not start a line,
  and opening parentheses may not end one.
- **CJK/Latin gap.** A quarter em. klreq §7.3.2 states that Hangul mixed with Latin has spacing of its own, but
  this engine has no value from the specification to encode, so it uses the quarter em the other CJK profiles use
  rather than inventing a number.
- **Indent and alignment.** A one-character first-line indent; justified lines.
- **Punctuation at the line end.** No width rule: Korean uses a narrower character in the first place, which is a
  display form rather than an adjustment made at the line's edge.
- **Display forms.** Corner brackets for quotation, and the narrow sentence marks of horizontal writing: `.` and
  `,` drawn where `。` and `、` were written (klreq §6.1.2, §6.1.3).
- **Annotations.** Korean states no annotation convention of its own; an annotation is laid out in the band above
  the character, along the line, because every profile needs an answer and that one is the engine's default rather
  than a claim about the language.
- **Emphasis marks.** The side is not stated by the language either; the engine's default puts them below the
  characters.
- **What to look for.** The narrow sentence marks: write `。` inside a Korean paragraph of horizontal writing and a
  `.` is drawn. That is a substitution, so `Text` still says what was written.

### Western languages (`en`, `en-GB`, `de`, `fr`)

- **Line breaking.** The Unicode line breaking algorithm (UAX #14), untailored: no prohibition classes, no
  punctuation squeezing and no CJK/Latin gap, so nothing is inserted between scripts and a line breaks where the
  algorithm alone allows.
- **Indent and alignment.** No first-line indent; left-aligned lines.
- **Hyphenation.** Automatic, from the language's own patterns: American English, British English, German (1996)
  and French each break a long word where their patterns allow it, and a break reserves the hyphen's advance, so
  the renderer still draws only what it was handed. Chinese, Japanese and Korean have no patterns, because their
  text breaks between characters and a word-level break would be a rule they do not have. A request can turn the
  behaviour off.
- **Display forms, annotations, emphasis marks.** None: these languages state none, so nothing is substituted and
  the annotation fields describe whatever the caller asked for rather than a convention.
- **A Western run inside a CJK paragraph.** The gap between the two scripts is decided at the boundary, and it is
  the CJK side that states one: a Latin word quoted in a Chinese sentence gets the quarter em, a Latin paragraph
  laid out as `en` does not. Line breaking follows the paragraph's rule set in both cases.
- **What to look for.** Ragged right edges with words hyphenated at the margin, and a justification that
  distributes space between words rather than between characters.

### Undeclared (`und`)

- **What it is.** The profile that makes no language assumption. It carries the parameters the engine had before
  languages existed, because applying a convention to text whose language nobody stated would be inventing one. It
  is a statement about history, not a claim that undeclared text is Chinese.
- **Line breaking and prohibition.** The prohibition rule set with the engine's historical class set — closing
  marks, pause and stop marks, the interpunct, the solidus, dashes and ellipses at line start, opening marks at
  line end — at the basic level.
- **CJK/Latin gap.** A quarter em, with no stated range, so the gap is inserted but not adjustable by
  justification.
- **Indent and alignment.** No indent; left-aligned lines.
- **Display forms and shaping.** None, and no OpenType language either: undeclared text shapes exactly as it
  always did.
- **Annotations and emphasis marks.** The engine's defaults: the band above the character, along the line;
  emphasis marks below.
- **What to look for.** Text that declares nothing behaves as it did before the language layer existed — which is
  why a document that wants a convention should name one.

### Arabic (`ar`) and Hebrew (`he`)

- **Line breaking.** The Unicode algorithm, with no CJK tailoring: no prohibition, no squeezing, no gap and no
  hyphenation.
- **Direction.** Right to left, and it belongs to the language: a paragraph that says it is Arabic is telling the
  layout which way its text runs. Lines fill from the right edge, and the runs of a mixed line are put in visual
  order.
- **Indent and alignment.** No first-line indent; a line is aligned to the paragraph's start, which for a
  right-to-left paragraph is the right edge.
- **Display forms.** The one substitution that applies is mirroring: a paired character such as a bracket or a
  quotation mark is drawn as its mirror image (UAX #9's rule L4). It reaches the output through the same channel as
  a language's quotation forms.
- **Annotations and emphasis marks.** The conventions state none; the engine's defaults apply.
- **What to look for.** The right-hand start edge, and brackets that point the other way than they were written.

## A language without a profile

There are two ways to end up without a language, and they end in the same place for different reasons.

- **No language at all.** The request declares none, the paragraph declares none, and the text carries neither kana
  nor Hangul, so the inference has nothing to answer with. The layout runs with the profile that makes no language
  assumption, which keeps the engine's historical behaviour instead of applying a convention nobody asked for.
- **An unregistered language.** A tag was given — a language the engine ships no profile for — and no profile
  matches it, not even after subtags are dropped. The engine does not fall back to a language it does know, because
  text in a language the engine has not been taught is exactly the case where guessing is worst. The tag resolves
  to the profile that makes no language assumption as well, so the text is laid out with the same untailored
  behaviour.
- **Neither one is an error.** The text is laid out and drawn; what it does not get is a convention it never named.
  Adding the profile, below, is what changes that.

## Adding a language

A language is added by adding a profile — a value, not a code path — and registering it where profiles are looked
up.

```csharp
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Languages;

// A profile names its tag, its fallback chain, the parameters its convention prescribes, and why it prescribes them.
var turkish = new LanguageProfile(
    "tr",
    ["tr-TR"],
    new TypographyParameters
    {
        BoundaryRuleFeature = TypographyFeatureRegistry.UnicodeBoundaryRuleId,
        EnableLineProhibition = false,
        EnableCjkLatinSpacing = false,
        EnablePunctuationCompression = false,
        FirstLineIndent = 0,
        Alignment = TextAlignment.Left,
        OpenTypeLanguageTag = "TRK",
    },
    "Turkish: Western line breaking, no CJK tailoring");

LanguageProfileRegistry.Shared.Register(turkish);   // checked here; it throws when the profile cannot be laid out
```

Registration is where a language pack is checked, so a broken one fails loudly instead of quietly laying text out
with a fallback rule. A profile is refused when:

- **the boundary rule it names is not registered.** The engine's two rule sets — the Unicode algorithm and the
  prohibition rules on top of it — declare a conflict with each other, so a profile names exactly one of them, and
  a name nobody registered is a problem rather than a silent fallback.
- **its prohibition class set is not registered.** A set the engine cannot find would mean "no prohibition at all",
  the opposite of what a profile that names one is asking for. The shipped sets are the Chinese, Japanese and
  Korean conventions and the engine's historical one; a host with its own classes registers them and names their
  id.
- **its writing mode is one the engine cannot lay out.** Horizontal writing and right-to-left columns are
  accepted; anything else is refused at registration, because a mode the stages cannot honour would come back as
  horizontal text claiming to be vertical.
- **it reserves a band above the line and sets its annotation down a column at the same time.** The band is as deep
  as the annotation is tall, while a column runs along the base character and needs room beside it; a profile that
  asks for both describes a layout that cannot exist.

When a language needs a **rule** rather than a value — a dictionary word breaker, say — a host implements the
boundary rule interface, registers it under its own id, and names that id from the profile. The line breaker needs
no change. That is the acceptance criterion for this layer: **a new language must not require touching the
framework**.
