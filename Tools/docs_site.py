#!/usr/bin/env python3
"""Build the static documentation site of the components out of `Doc/`.

The site is plain static files: one HTML page per documented Markdown file, a component gallery generated
from the component metadata, a client-side search index and a language switch between the English page and
its `*.cn.md` twin. The Markdown itself is rendered **in the browser** (`Tools/docs_site/marked.umd.js`,
vendored, MIT), so this tool needs nothing beyond the standard library; what it does is the part a browser
cannot do - resolving the cross-references, computing the heading anchors with the same rule
`Tools/check_doc_links.py` validates, and laying out the navigation.

Usage (from the repository root):

    python Tools/docs_site.py build [--out tmp/site] [--repo-url URL] [--branch main]
    python Tools/docs_site.py serve [--out tmp/site] [--port 8000]
    python Tools/docs_site.py check [--out tmp/site]

`build` writes the site, `check` re-reads it and fails when a link, an anchor, an image or the language
switch of a page does not resolve, and `serve` previews the built site on <http://127.0.0.1:8000>.

Images and other assets live next to the documentation in `Doc/<Component>/assets/` (any non-Markdown
file of a component's documentation folder is copied to the same place in the site, so relative links keep
working). The reading order of a component's pages comes from the optional `Doc/<Component>/toc.json` - a
`{"pages": [...]}` list of English page names, where the `*.cn.md` twin follows automatically; without it the
order is index, guide, the rest alphabetically, then the API reference. Links to files outside `Doc/` become
links into the repository on GitHub: pass `--repo-url` (by default the repository's own `origin` remote) and
`--branch`.
"""

from __future__ import annotations

import argparse
import html
import json
import os
import posixpath
import re
import shutil
import sys
from dataclasses import dataclass, field
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from lib import enable_utf8_output  # noqa: E402
from lib.component import ComponentGraph, ToolError, project_root  # noqa: E402
from lib.gitutil import git  # noqa: E402

# The link checker owns the anchor rule and the Markdown scanning rules; the site reuses them so a link
# that passes `Tools/check_doc_links.py` resolves on the site as well.
from check_doc_links import FENCE, HEADING, INLINE_CODE, LINK, slug  # noqa: E402

#: Where the stylesheet, the script and the vendored Markdown renderer live.
ASSETS_DIR = Path(__file__).resolve().parent / "docs_site"
SITE_ASSETS = ("style.css", "app.js", "marked.umd.js", "marked.LICENSE.txt", "marked.VERSION.txt",
               "highlight.min.js", "highlight.LICENSE.txt", "highlight.VERSION.txt")

#: Suffixes of the documentation folder that are copied into the site (everything else under
#: `Doc/<Component>/` is Markdown or Godot bookkeeping).
SKIPPED_ASSET_SUFFIXES = frozenset({".md", ".import", ".uid"})

CN_SUFFIX = ".cn.md"
HOME = "index.html"
LANG_ATTR = {"en": "en", "cn": "zh-Hans"}

#: Languages the site can serve, in menu order. Adding one means: a `Doc/<Component>/X.<lang>.md` convention
#: (today only `*.cn.md` is paired), an entry here and one in `TEXT`.
LANGUAGES = {"en": "English", "cn": "中文"}

#: One home page per language, so a page without a translation can still switch language.
HOME_BY_LANG = {"en": "index.html", "cn": "index.cn.html"}

#: Interface strings, one dictionary per language.
TEXT = {
    "en": {
        "home": "Home",
        "components": "Components",
        "search": "Search",
        "on_this_page": "On this page",
        "edit": "Edit this page",
        "previous": "Previous",
        "next": "Next",
        "no_translation": "no translation of this page yet",
        "language": "Language",
        "fallback": "No translation of this page yet - this leads to that language's overview",
        "noscript": "This page renders its Markdown in the browser.",
        "generated": "Built from Doc/ in the repository",
        "version": "Version",
        "status": "Status",
        "read": "Documentation",
    },
    "cn": {
        "home": "首页",
        "components": "组件",
        "search": "搜索",
        "on_this_page": "本页目录",
        "edit": "在 GitHub 上编辑本页",
        "previous": "上一页",
        "next": "下一页",
        "no_translation": "本页暂无译文",
        "language": "语言",
        "fallback": "本页暂无译文——这里指向该语言的对应入口",
        "noscript": "本页的 Markdown 在浏览器里渲染。",
        "generated": "由仓库里的 Doc/ 生成",
        "version": "版本",
        "status": "状态",
        "read": "文档",
    },
}


# ── pages ───────────────────────────────────────────────────────────────────

@dataclass
class Page:
    """One site page: a Markdown document (or a generated one) in one language."""

    lang: str
    url: str                      # site relative, e.g. ``GodotChart/advanced.html``
    title: str
    component: str                # sidebar group title
    group: str                    # sidebar group key
    source: Path | None           # repository relative path of the Markdown file
    text: str                     # the Markdown that is embedded in the page
    headings: list[str]           # anchor ids, in document order
    twins: dict[str, str] = field(default_factory=dict)   # language -> url of the same document

    @property
    def directory(self) -> str:
        return posixpath.dirname(self.url) or "."


def _unescaped_lines(text: str):
    """Yield (lineno, line, in_fence) for a Markdown document."""
    in_fence = False
    for lineno, line in enumerate(text.splitlines(), start=1):
        if FENCE.match(line):
            in_fence = not in_fence
            yield lineno, line, True
            continue
        yield lineno, line, in_fence


def heading_titles(text: str) -> list[tuple[int, str]]:
    """(level, text) of every heading outside a fenced code block."""
    found: list[tuple[int, str]] = []
    for _, line, in_fence in _unescaped_lines(text):
        if in_fence:
            continue
        match = HEADING.match(line)
        if match:
            found.append((len(match.group("level")), match.group("text")))
    return found


def anchors(text: str) -> list[str]:
    """The anchor ids of a document, in document order (the rule `Tools/check_doc_links.py` validates)."""
    return [slug(title) for _, title in heading_titles(text)]


#: The `English | 中文` switcher line the documentation pairs start with.
LANGUAGE_LINE = re.compile(r"\]\([^)]*\.(?:cn\.)?md\)")


def strip_language_line(text: str) -> str:
    """Drop the switcher line a document pair starts with.

    The site has its own language menu, so rendering the line as well would show a second, non-functional
    switch at the top of the page. The file keeps it - the same markdown is what the repository, the
    documentation check and the installer ship.
    """
    lines = text.splitlines()
    for index, line in enumerate(lines[:4]):
        stripped = line.strip()
        if not stripped:
            continue
        if ("English" in stripped or "中文" in stripped) and "|" in stripped \
                and LANGUAGE_LINE.search(stripped):
            del lines[index]
            if index < len(lines) and not lines[index].strip():
                del lines[index]
        break
    return "\n".join(lines) + ("\n" if text.endswith("\n") else "")


def page_title(text: str, fallback: str) -> str:
    """The first heading of a document, or `fallback`."""
    for level, title in heading_titles(text):
        if level == 1:
            return re.sub(r"`", "", title).strip()
    for _, title in heading_titles(text):
        return re.sub(r"`", "", title).strip()
    return fallback


def stem_of(name: str) -> str:
    """`README.md` -> `index`, `advanced.cn.md` -> `advanced.cn`, `README.cn.md` -> `index.cn`."""
    stem = name[: -len(".md")] if name.endswith(".md") else name
    return {"README": "index", "README.cn": "index.cn"}.get(stem, stem)


#: Reading order a component's documentation falls back to when it has no `toc.json`.
FIRST_PAGES = ("README.md", "getting-started.md")
LAST_PAGES = ("api-reference.md",)
MANIFEST = "toc.json"


def default_order(english: dict[str, Path]) -> list[str]:
    """Index first, then the guide, then the rest alphabetically, with the reference at the end."""
    names = [name for name in english if name not in FIRST_PAGES + LAST_PAGES]
    return ([name for name in FIRST_PAGES if name in english] + sorted(names) +
            [name for name in LAST_PAGES if name in english])


def read_manifest(doc_dir: Path) -> tuple[list[str], dict[str, str]]:
    """(page order, title overrides) from an optional `Doc/<Component>/toc.json`.

    The file lists the English page names in reading order - the translation twin, if any, follows it:

        { "pages": [ { "page": "README.md", "title": "GodotChart" }, "getting-started.md" ] }

    A missing file means "use the default order"; an unreadable one is reported and ignored.
    """
    path = doc_dir / MANIFEST
    if not path.is_file():
        return [], {}
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        print(f"note: {path.as_posix()} is not readable ({error}); using the default page order")
        return [], {}
    entries = payload.get("pages", payload) if isinstance(payload, dict) else payload
    if not isinstance(entries, list):
        print(f"note: {path.as_posix()}: 'pages' has to be a list; using the default page order")
        return [], {}

    order: list[str] = []
    titles: dict[str, str] = {}
    for entry in entries:
        if isinstance(entry, str):
            name = entry
        elif isinstance(entry, dict):
            name = str(entry.get("page", ""))
            if entry.get("title"):
                titles[name] = str(entry["title"])
        else:
            continue
        if name and name not in order:
            order.append(name)
    return order, titles


def ordered_pages(doc_dir: Path) -> tuple[list[tuple[Path, Path | None]], dict[str, str]]:
    """(English document, its Chinese twin) in reading order, plus the manifest's title overrides."""
    english = {path.name: path for path in doc_dir.glob("*.md") if not path.name.endswith(CN_SUFFIX)}
    order, titles = read_manifest(doc_dir)
    listed = [name for name in order if name in english]
    listed += [name for name in default_order(english) if name not in listed]

    documents: list[tuple[Path, Path | None]] = []
    for name in listed:
        chinese = doc_dir / (name[: -len(".md")] + CN_SUFFIX)
        documents.append((english[name], chinese if chinese.is_file() else None))
    return documents, titles


def _site_url(source: Path) -> str:
    """Site url of a `Doc/<Component>/<file>.md` document."""
    component = source.parts[1]
    return f"{component}/{stem_of(source.name)}.html"


# ── link rewriting ──────────────────────────────────────────────────────────

#: A Markdown link with its text, so the rewrite keeps the label and only swaps the target.
SITE_LINK = re.compile(r"(?P<prefix>\[[^\]]*\]\()(?P<target>[^)\s]+)(?P<suffix>(?:\s+\"[^\"]*\")?\))")


class LinkRewriter:
    """Rewrites the links of one page: documentation links to site pages, the rest to GitHub."""

    def __init__(self, root: Path, pages: dict[Path, Page], repo_url: str | None, branch: str):
        self.root = root
        self.pages = pages
        self.urls = {page.url for page in pages.values()}
        self.repo_url = repo_url.rstrip("/") if repo_url else None
        self.branch = branch
        self.unresolved: list[str] = []

    def rewrite(self, text: str, page: Page) -> str:
        lines: list[str] = []
        for _, line, in_fence in _unescaped_lines(text):
            if in_fence:
                lines.append(line)
                continue
            protected: list[str] = []

            def hide(match: re.Match) -> str:
                protected.append(match.group(0))
                return f"\x00{len(protected) - 1}\x00"

            masked = INLINE_CODE.sub(hide, line)

            def replace(match: re.Match) -> str:
                return (match.group("prefix") + self._target(match.group("target"), page) +
                        match.group("suffix"))

            masked = SITE_LINK.sub(replace, masked)
            for index, original in enumerate(protected):
                masked = masked.replace(f"\x00{index}\x00", original)
            lines.append(masked)
        return "\n".join(lines) + ("\n" if text.endswith("\n") else "")

    def _target(self, target: str, page: Page) -> str:
        if target.startswith(("http://", "https://", "mailto:", "data:", "#")) or not target:
            return target
        path, separator, anchor = target.partition("#")
        if not path:
            return target
        if path == "COMPONENTS.md":
            return posixpath.relpath("components.html", page.directory) + separator + anchor
        if path in self.urls:                          # a link the generator wrote itself (the gallery)
            return posixpath.relpath(path, page.directory) + separator + anchor

        document = self._resolve(path, page)
        if document is None:
            self.unresolved.append(f"{page.url}: {target} (leaves the repository)")
            return target

        site_page = self.pages.get(document)
        if site_page is not None:
            return posixpath.relpath(site_page.url, page.directory) + separator + anchor
        if document.parts and document.parts[0] == "Doc":
            # Everything under Doc/ is mirrored into the site (assets next to the documentation), so the
            # link keeps pointing at the copy - `assets/x.png` stays `assets/x.png`.
            mirrored = Path(*document.parts[1:]).as_posix()
            return posixpath.relpath(mirrored, page.directory) + separator + anchor
        if not (self.root / document).exists():
            self.unresolved.append(f"{page.url}: {target} (no such file in the repository)")
            return target
        if self.repo_url is None:
            self.unresolved.append(f"{page.url}: {target} (no --repo-url)")
            return target
        return f"{self.repo_url}/blob/{self.branch}/{document.as_posix()}" + separator + anchor

    def _resolve(self, path: str, page: Page) -> Path | None:
        """The repository-relative file a link points at, or None when it leaves the repository."""
        base = page.source.parent if page.source is not None else Path(".")
        candidate = (self.root / base / path).resolve()
        try:
            return candidate.relative_to(self.root.resolve())
        except ValueError:
            return None


# ── page collection ─────────────────────────────────────────────────────────

def collect_pages(root: Path, graph: ComponentGraph) -> list[Page]:
    """Every page of the site: the home page, the component gallery and the component documentation."""
    pages: list[Page] = []

    for lang, name, url in (("en", "README.md", HOME_BY_LANG["en"]),
                            ("cn", "README.cn.md", HOME_BY_LANG["cn"])):
        source = root / name
        if not source.is_file():
            continue
        text = strip_language_line(source.read_text(encoding="utf-8"))
        pages.append(Page(lang=lang, url=url, title=page_title(text, "GodotNodeExtension"),
                          component="GodotNodeExtension", group="", source=Path(name), text=text,
                          headings=anchors(text)))

    for lang in LANGUAGES:
        text = gallery_markdown(graph, lang)
        url = "components.html" if lang == "en" else "components.cn.html"
        pages.append(Page(lang=lang, url=url, title=("Components" if lang == "en" else "组件"),
                          component=("Components" if lang == "en" else "组件"), group="", source=None,
                          text=text, headings=anchors(text)))

    for component in sorted(graph, key=lambda item: item.name):
        if not component.doc_dir.is_dir():
            continue
        documents, titles = ordered_pages(component.doc_dir)
        for english, chinese in documents:
            for document in (english, chinese):
                if document is None:
                    continue
                lang = "cn" if document.name.endswith(CN_SUFFIX) else "en"
                text = strip_language_line(document.read_text(encoding="utf-8"))
                title = titles.get(english.name) if lang == "en" else None
                pages.append(Page(lang=lang, url=_site_url(document.relative_to(root)),
                                  title=title or page_title(text, component.name),
                                  component=component.name, group=component.name,
                                  source=document.relative_to(root), text=text, headings=anchors(text)))

    # Pair the translations: `X.md` <-> `X.cn.md` (which covers `README.md` <-> `README.cn.md`).
    by_source = {page.source: page for page in pages if page.source is not None}
    for page in pages:
        if page.source is None:
            continue
        name = page.source.name
        if name.endswith(CN_SUFFIX):
            twin_source = page.source.with_name(name[: -len(CN_SUFFIX)] + ".md")
        else:
            twin_source = page.source.with_name(name[: -len(".md")] + CN_SUFFIX)
        twin = by_source.get(twin_source)
        if twin is not None:
            page.twins = {twin.lang: twin.url}
    for page in pages:
        if page.source is None:                       # the generated gallery pages
            other = "cn" if page.lang == "en" else "en"
            page.twins = {other: "components.cn.html" if other == "cn" else "components.html"}
    return pages


def gallery_markdown(graph: ComponentGraph, lang: str) -> str:
    """The component gallery, generated from the metadata (the same source as COMPONENTS.md)."""
    english = lang == "en"
    lines = ["# Components" if english else "# 组件", ""]
    lines.append("Every component, its version and what it does - the same metadata the installer and "
                 "`COMPONENTS.md` use." if english else
                 "每个组件、它的版本与用途——与安装器和 `COMPONENTS.md` 用的是同一份元数据。")
    lines.append("")
    for component in sorted(graph, key=lambda item: item.name):
        info = component.info
        has_docs = (component.doc_dir / "README.md").is_file()
        target = f"{component.name}/index.html" if has_docs else None
        name = f"[{component.name}]({target})" if target else f"{component.name}"
        lines.append(f"### {name}")
        lines.append("")
        lines.append(f"{info.get('description', '-')}")
        lines.append("")
        lines.append(f"- {'Version' if english else '版本'}: `{component.version or '-'}`")
        lines.append(f"- {'Author' if english else '作者'}: {info.get('author', '-')}")
        dependencies = ", ".join(component.dependencies) or "-"
        lines.append(f"- {'Depends on' if english else '依赖'}: {dependencies}")
        lines.append("")
    return "\n".join(lines) + "\n"


# ── rendering ───────────────────────────────────────────────────────────────

PAGE_TEMPLATE = """<!DOCTYPE html>
<html lang="{lang_attr}" data-root="{root}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title} - GodotNodeExtension</title>
<link rel="stylesheet" href="{root}style.css">
</head>
<body>
<header class="top">
  <a class="brand" href="{root}{home}">GodotNodeExtension</a>
  <span class="spacer"></span>
  <div class="search">
    <input id="search-input" type="search" placeholder="{search}" aria-label="{search}" autocomplete="off">
    <div class="results" id="search-results"></div>
  </div>
  <span class="lang">{language_select}</span>
  <button class="icon" id="theme" title="theme" aria-label="theme">☀</button>
</header>
<div class="layout">
  <nav class="side">{sidebar}</nav>
  <main>
    <div class="crumbs">{crumbs}</div>
    <article>
      <p class="edit"><a href="{edit_url}">{edit_label}</a></p>
      <noscript><p>{noscript}</p></noscript>
      <div id="content"></div>
    </article>
    <footer class="page-nav">{page_nav}</footer>
  </main>
  <nav class="toc" aria-label="{on_this_page}"><h2>{on_this_page}</h2><div id="toc"></div></nav>
</div>
<footer class="site">{generated} · <a href="{repo_url}">{repo_label}</a></footer>
<script type="application/json" id="heading-ids">{ids_json}</script>
<script type="text/markdown" id="markdown-source">{markdown}</script>
<script src="{root}marked.umd.js"></script>
<script src="{root}highlight.min.js"></script>
<script src="{root}app.js"></script>
</body>
</html>
"""


def _language_select(page: Page, pages: list[Page], text: dict[str, str]) -> str:
    """The language menu of the header: one option per language, in `LANGUAGES` order.

    A language whose translation of this page exists gets that page; otherwise the closest equivalent is
    offered (the component's index in that language, then the language's home), so adding a third language
    needs no change here. A `title` on such an option explains where it leads.
    """
    urls = {candidate.url for candidate in pages}
    options: list[str] = []
    for lang, label in LANGUAGES.items():
        if lang == page.lang:
            target, note = page.url, ""
        elif lang in page.twins:
            target, note = page.twins[lang], ""
        else:
            index = f"{page.component}/{'index.cn.html' if lang == 'cn' else 'index.html'}"
            target = index if index in urls else HOME_BY_LANG[lang]
            note = f' title="{html.escape(text["fallback"])}"'
        selected = " selected" if lang == page.lang else ""
        options.append(f'<option value="{posixpath.relpath(target, page.directory)}"'
                       f'{selected}{note}>{label}</option>')
    hint = ""
    if not page.twins:
        hint = f'<span class="hint">{text["no_translation"]}</span>'
    return (f'<select id="language" aria-label="{text["language"]}">{"".join(options)}</select>{hint}')


def _sidebar(page: Page, pages: list[Page], text: dict[str, str]) -> str:
    """Navigation of the current language: home, gallery, then one collapsible group per component.

    The groups are `<details>` elements so a reader can fold away the components they are not reading, and
    the page order inside a group is the one the component's `Doc/<Component>/toc.json` asks for.
    """
    groups: dict[str, list[Page]] = {}
    for candidate in pages:
        if candidate.lang != page.lang or not candidate.group:
            continue
        groups.setdefault(candidate.group, []).append(candidate)

    def link(candidate: Page) -> str:
        current = ' class="current"' if candidate.url == page.url else ""
        return (f'<a href="{posixpath.relpath(candidate.url, page.directory)}"{current}>'
                f'{html.escape(candidate.title)}</a>')

    lines = [f'<a href="{posixpath.relpath(HOME if page.lang == "en" else HOME.replace(".html", ".cn.html"), page.directory)}">'
             f'{text["home"]}</a>',
             f'<a href="{posixpath.relpath("components.html" if page.lang == "en" else "components.cn.html", page.directory)}">'
             f'{text["components"]}</a>']
    for group in sorted(groups, key=lambda name: name.lower()):
        inside = any(candidate.url == page.url for candidate in groups[group])
        lines.append(f'<details class="group" data-group="{html.escape(group)}"'
                     f'{" open" if inside else ""}>')
        lines.append(f"<summary>{html.escape(group)}</summary>")
        for candidate in groups[group]:
            lines.append(link(candidate))
        lines.append("</details>")
    return "\n".join(lines)


def _crumbs(page: Page, text: dict[str, str]) -> str:
    home = HOME if page.lang == "en" else HOME.replace(".html", ".cn.html")
    parts = [f'<a href="{posixpath.relpath(home, page.directory)}">{text["home"]}</a>']
    if page.group:
        gallery = "components.html" if page.lang == "en" else "components.cn.html"
        parts.append(f'<a href="{posixpath.relpath(gallery, page.directory)}">'
                     f'{html.escape(page.component)}</a>')
        if page.url not in (gallery, "components.html", "components.cn.html"):
            parts.append(html.escape(page.title))
    return " / ".join(parts)


def _neighbours(page: Page, pages: list[Page], text: dict[str, str]) -> str:
    """Previous/next links, walking the same order the sidebar shows (the manifest's order)."""
    siblings = [candidate for candidate in pages
                if candidate.lang == page.lang and candidate.group == page.group]
    if page not in siblings:
        return ""
    index = siblings.index(page)
    cells = ["<span></span>"]
    if index > 0:
        before = siblings[index - 1]
        cells.append(f'<a href="{posixpath.relpath(before.url, page.directory)}">← {text["previous"]}: '
                     f'{html.escape(before.title)}</a>')
    if index + 1 < len(siblings):
        after = siblings[index + 1]
        cells.append(f'<a href="{posixpath.relpath(after.url, page.directory)}">{text["next"]}: '
                     f'{html.escape(after.title)} →</a>')
    return "".join(cells) if len(cells) > 1 else ""


def render_page(page: Page, pages: list[Page], root: Path, repo_url: str | None, branch: str,
                rewriter: LinkRewriter) -> str:
    text = TEXT[page.lang]
    markdown = rewriter.rewrite(page.text, page)
    edit_url = "https://example.invalid"
    if page.source is not None and repo_url:
        edit_url = f"{repo_url.rstrip('/')}/blob/{branch}/{page.source.as_posix()}"
    elif repo_url:
        edit_url = repo_url
    return PAGE_TEMPLATE.format(
        lang_attr=LANG_ATTR[page.lang],
        root="" if page.directory == "." else posixpath.relpath(".", page.directory) + "/",
        title=html.escape(page.title),
        home=HOME,
        search=html.escape(text["search"]),
        language_select=_language_select(page, pages, text),
        sidebar=_sidebar(page, pages, text),
        crumbs=_crumbs(page, text),
        edit_url=edit_url,
        edit_label=text["edit"],
        noscript=text["noscript"],
        page_nav=_neighbours(page, pages, text),
        on_this_page=text["on_this_page"],
        generated=text["generated"],
        repo_url=repo_url or "https://github.com/GodotNodeExtension/GodotNodeExtension",
        repo_label="GitHub",
        ids_json=json.dumps(page.headings, ensure_ascii=False),
        # The Markdown travels as a JSON string: inside a `<script>` the content is raw text, so HTML
        # entities would not be decoded (a `>` would stop being a blockquote). `\u003c` keeps the string
        # from ending the element early, and `JSON.parse` restores the original characters exactly.
        markdown=json.dumps(markdown, ensure_ascii=False).replace("<", "\\u003c"),
    )


def search_index(pages: list[Page]) -> str:
    """The client-side search index: one entry per page, both languages."""
    entries = []
    for page in pages:
        plain = INLINE_CODE.sub("", page.text)
        plain = re.sub(r"```.*?```", " ", plain, flags=re.S)
        plain = re.sub(r"[|#>`*_\[\]()]", " ", plain)
        plain = re.sub(r"\s+", " ", plain).strip()
        entries.append({
            "url": page.url,
            "lang": page.lang,
            "component": page.component,
            "title": page.title,
            "headings": [title for _, title in heading_titles(page.text)],
            "text": plain[:600],
        })
    return json.dumps({"pages": entries}, ensure_ascii=False, indent=1) + "\n"


# ── commands ────────────────────────────────────────────────────────────────

def default_repo_url(root: Path) -> str | None:
    """The `origin` remote as an https URL, so "edit this page" works without an option."""
    remote = git(root, "config", "--get", "remote.origin.url", check=False).strip()
    if not remote:
        return None
    if remote.startswith("git@"):
        host, _, path = remote[4:].partition(":")
        return f"https://{host}/{path}"
    return remote[:-4] if remote.endswith(".git") else remote


def build(root: Path, out: Path, repo_url: str | None, branch: str) -> tuple[list[Page], LinkRewriter]:
    graph = ComponentGraph.load(root)
    pages = collect_pages(root, graph)
    rewriter = LinkRewriter(root, {page.source: page for page in pages if page.source is not None},
                            repo_url, branch)

    if out.exists():
        # Never wipe a directory that is not ours: the build replaces the whole output tree.
        ours = (out / ".nojekyll").exists() or "tmp" in out.relative_to(root).parts
        if not ours:
            raise ToolError(f"{out} is not a previous build (no .nojekyll) and is not under tmp/ - "
                            f"refusing to replace it")
        shutil.rmtree(out)
    out.mkdir(parents=True)
    for name in SITE_ASSETS:
        shutil.copy2(ASSETS_DIR / name, out / name)
    (out / ".nojekyll").write_text("", encoding="utf-8")

    for page in pages:
        target = out / page.url
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(render_page(page, pages, root, repo_url, branch, rewriter), encoding="utf-8",
                          newline="\n")

    # Assets: everything that is not Markdown, so `Doc/<Component>/assets/…` keeps its relative links.
    for component in sorted(graph, key=lambda item: item.name):
        if not component.doc_dir.is_dir():
            continue
        for asset in sorted(component.doc_dir.rglob("*")):
            if not asset.is_file() or asset.suffix.lower() in SKIPPED_ASSET_SUFFIXES:
                continue
            destination = out / component.name / asset.relative_to(component.doc_dir)
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(asset, destination)

    (out / "search.json").write_text(search_index(pages), encoding="utf-8", newline="\n")

    print(f"built {len(pages)} page(s) into {out.as_posix()}")
    for page in pages:
        other = [lang for lang in LANGUAGES if lang != page.lang]
        missing = [lang for lang in other if lang not in page.twins]
        note = f"  (no {', '.join(missing)} twin)" if missing else ""
        print(f"  {page.url:44s} {page.title}{note}")
    if rewriter.unresolved:
        print(f"note: {len(rewriter.unresolved)} link(s) left pointing at the repository "
              f"(pass --repo-url to make them browsable):")
        for entry in rewriter.unresolved[:10]:
            print(f"  {entry}")
    return pages, rewriter


def check(root: Path, out: Path) -> int:
    """Verify the built site: links, anchors, images, language switches and the search index."""
    problems: list[str] = []
    if not (out / HOME).is_file():
        print(f"error: {out} does not look like a built site (run `build` first)", file=sys.stderr)
        return 2

    id_cache: dict[Path, list[str]] = {}

    def ids_of(document: Path) -> list[str]:
        if document not in id_cache:
            match = re.search(r'<script type="application/json" id="heading-ids">(.*?)</script>',
                              document.read_text(encoding="utf-8"), re.S)
            id_cache[document] = json.loads(match.group(1)) if match else []
        return id_cache[document]

    pages = sorted(out.rglob("*.html"))
    for page in pages:
        body = page.read_text(encoding="utf-8")
        if 'id="markdown-source">""</script>' in body:
            problems.append(f"{page.relative_to(out)}: the Markdown source is empty")
        if not ids_of(page):
            problems.append(f"{page.relative_to(out)}: no heading anchors")

        for match in re.finditer(r'(?:href|src)="([^"]+)"', body):
            target = match.group(1)
            if target.startswith(("http://", "https://", "mailto:", "data:")):
                continue
            if target.startswith("#"):
                if target[1:] and target[1:] not in ids_of(page):
                    problems.append(f"{page.relative_to(out)}: no such anchor on the page: {target}")
                continue
            path, _, anchor = target.partition("#")
            document = (page.parent / path).resolve()
            if not document.is_file():
                problems.append(f"{page.relative_to(out)}: missing target: {target}")
                continue
            if anchor and document.suffix == ".html" and anchor not in ids_of(document):
                problems.append(f"{page.relative_to(out)}: no such anchor in {path}: #{anchor}")

        # The Markdown links live inside the page (the browser renders them), so they are checked from
        # the embedded source: every rewritten target has to be a page or asset of the site.
        source = re.search(r'<script type="text/markdown" id="markdown-source">(.*?)</script>', body,
                           re.S)
        if source:
            try:
                markdown = json.loads(source.group(1))
            except json.JSONDecodeError:
                problems.append(f"{page.relative_to(out)}: the embedded Markdown is not valid JSON")
                markdown = ""
            for _, line, in_fence in _unescaped_lines(markdown):
                if in_fence:
                    continue
                for match in LINK.finditer(INLINE_CODE.sub("", line)):
                    target = match.group("target")
                    if target.startswith(("http://", "https://", "mailto:", "data:")) or not target:
                        continue
                    path, _, anchor = target.partition("#")
                    if not path:
                        if anchor and anchor not in ids_of(page):
                            problems.append(f"{page.relative_to(out)}: no such anchor: {target}")
                        continue
                    document = (page.parent / path).resolve()
                    if not document.is_file():
                        problems.append(f"{page.relative_to(out)}: link does not resolve: {target}")
                    elif anchor and document.suffix == ".html" and anchor not in ids_of(document):
                        problems.append(f"{page.relative_to(out)}: no such anchor in {path}: #{anchor}")

    index = out / "search.json"
    try:
        entries = json.loads(index.read_text(encoding="utf-8")).get("pages", [])
    except (OSError, json.JSONDecodeError) as error:
        problems.append(f"search.json is unreadable: {error}")
        entries = []
    for entry in entries:
        if not (out / entry["url"]).is_file():
            problems.append(f"search.json points at a missing page: {entry['url']}")

    print(f"checked {len(pages)} page(s), {len(entries)} search entry(s)")
    for problem in problems:
        print(f"error: {problem}")
    if problems:
        print(f"{len(problems)} problem(s)")
        return 1
    print("every link, anchor, image and language switch resolves")
    return 0


def serve(out: Path, port: int) -> int:
    if not (out / HOME).is_file():
        print(f"error: {out} does not look like a built site (run `build` first)", file=sys.stderr)
        return 2
    handler = partial(SimpleHTTPRequestHandler, directory=str(out))
    with ThreadingHTTPServer(("127.0.0.1", port), handler) as server:
        print(f"serving {out.as_posix()} on http://127.0.0.1:{port}/ (Ctrl-C stops it)")
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            print("\nstopped")
    return 0


# ── entry point ─────────────────────────────────────────────────────────────

def main(argv: list[str] | None = None) -> int:
    enable_utf8_output()
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("command", choices=("build", "check", "serve"))
    parser.add_argument("--out", default="tmp/site", help="output directory (default: tmp/site)")
    parser.add_argument("--repo-url", default=None,
                        help="repository URL for the out-of-docs links and \"edit this page\" "
                             "(default: the origin remote)")
    parser.add_argument("--branch", default="main", help="branch the GitHub links point at")
    parser.add_argument("--port", type=int, default=8000, help="port for `serve`")
    args = parser.parse_args(argv)

    try:
        root = project_root()
    except ToolError as error:
        print(f"error: {error}", file=sys.stderr)
        return 2
    out = (root / args.out).resolve()

    if args.command == "build":
        repo_url = args.repo_url or default_repo_url(root)
        _, rewriter = build(root, out, repo_url, args.branch)
        return 0
    if args.command == "check":
        return check(root, out)
    return serve(out, args.port)


if __name__ == "__main__":
    raise SystemExit(main())
