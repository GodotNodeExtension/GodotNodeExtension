# Typography tools - the generated data tables

Two generators produce the tables the engine compiles against. They are **not** needed to use the
component - the tables ship with it - but they are needed to regenerate one, or to add a language, a
pattern set or a newer Unicode version.

| Script | Produces | Input |
| --- | --- | --- |
| `gen_unicode_tables.py` | `Core/Unicode/*.g.cs` | the Unicode Character Database: `LineBreak.txt`, `auxiliary/WordBreakProperty.txt`, `Scripts.txt`, `EastAsianWidth.txt`, `DerivedBidiClass.txt`, `BidiMirroring.txt`, `emoji-data.txt` |
| `gen_hyphenation_tables.py` | `Core/Hyphenation/*.g.cs` | the hyph-utf8 TeX patterns: `hyph-<lang>.pat.txt`, the optional `hyph-<lang>.hyp.txt` and the metadata in `hyph-<lang>.tex` |

```bash
# from the repository root; the inputs are downloads, so they belong in the git-ignored tmp/cache/
python Tools/Typography/gen_unicode_tables.py \
    --ucd-dir tmp/cache/ucd --out Component/Typography/Core/Unicode

python Tools/Typography/gen_hyphenation_tables.py --hyph-dir tmp/cache/hyph/src
```

Every generated file records the input it came from, that input's version and its SHA-256, so a table
can be traced back to exactly the data it was built from; the hyphenation tables also record the
licence the pattern file ships under, because hyph-utf8 collects its files under different terms.
Re-running a generator on the same input reproduces the file byte for byte, so a `*.g.cs` is never
edited by hand.
