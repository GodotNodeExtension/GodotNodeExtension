/* Documentation site runtime: render the embedded Markdown, assign the heading anchors the generator
   computed (so the site's ids are exactly the ones the repository's link checker validates), build the
   "on this page" list, wire the search box and the theme switch. No frameworks, no network. */

(function () {
  "use strict";

  const data = (id) => {
    const element = document.getElementById(id);
    if (!element) return null;
    try {
      return JSON.parse(element.textContent);
    } catch (error) {
      console.error("bad embedded data in #" + id, error);
      return null;
    }
  };

  // ── render ────────────────────────────────────────────────────────────────

  const source = document.getElementById("markdown-source");
  const content = document.getElementById("content");
  const ids = data("heading-ids") || [];
  const root = document.documentElement.dataset.root || "";

  if (source && content && window.marked) {
    let markdown = source.textContent;
    try {
      markdown = JSON.parse(markdown);
    } catch (error) {
      console.error("bad embedded Markdown", error);
    }
    content.innerHTML = window.marked.parse(markdown, { gfm: true, breaks: false });

    const headings = content.querySelectorAll("h1, h2, h3, h4, h5, h6");
    headings.forEach((heading, index) => {
      if (ids[index]) heading.id = ids[index];
      if (!heading.id) return;
      const link = document.createElement("a");
      link.className = "anchor";
      link.href = "#" + heading.id;
      link.textContent = "#";
      link.setAttribute("aria-label", "Link to this section");
      heading.appendChild(link);
    });

    // Syntax highlighting, only for the languages the vendored bundle knows: a fence whose language is
    // unknown (or a plain fence) stays readable as it is instead of being guessed at.
    if (window.hljs) {
      content.querySelectorAll("pre code").forEach((block) => {
        const language = /language-([\w-]+)/.exec(block.className || "");
        if (language && !window.hljs.getLanguage(language[1])) return;
        if (language) window.hljs.highlightElement(block);
      });
    }

    // "On this page" navigation, plus the scroll spy that marks the current section.
    const toc = document.getElementById("toc");
    if (toc) {
      const entries = [];
      headings.forEach((heading) => {
        if (!heading.id || heading.tagName === "H1") return;
        const link = document.createElement("a");
        link.href = "#" + heading.id;
        link.textContent = heading.firstChild ? heading.firstChild.textContent : heading.textContent;
        link.className = "lvl-" + heading.tagName[1];
        toc.appendChild(link);
        entries.push({ link: link, heading: heading });
      });
      if (!entries.length) {
        const empty = toc.closest("nav");
        if (empty) empty.style.display = "none";
      }
      const mark = () => {
        let current = entries[0];
        for (const entry of entries) {
          if (entry.heading.getBoundingClientRect().top <= 80) current = entry;
        }
        entries.forEach((entry) => entry.link.classList.toggle("active", entry === current));
      };
      mark();
      window.addEventListener("scroll", mark, { passive: true });
    }
  } else if (content) {
    content.innerHTML = "<noscript><p>This page renders its Markdown in the browser. Please enable " +
      "JavaScript - or read the source file in the repository.</p></noscript>";
  }

  // ── sidebar ───────────────────────────────────────────────────────────────

  // The component groups are <details>: their state is per component and remembered, so folding one away
  // never touches another, and returning to a page brings the reader back to the same spot in the list
  // (the sidebar has its own scroll position, which is not reset to the top on navigation).
  const sidebar = document.querySelector("nav.side");
  if (sidebar) {
    sidebar.querySelectorAll("details.group").forEach((group) => {
      const key = "docs-group-" + group.dataset.group;
      const stored = localStorage.getItem(key);
      if (stored !== null) group.open = stored === "1";
      group.addEventListener("toggle", () => localStorage.setItem(key, group.open ? "1" : "0"));
    });

    const storedScroll = sessionStorage.getItem("docs-sidebar-scroll");
    if (storedScroll !== null) sidebar.scrollTop = Number(storedScroll) || 0;
    const current = sidebar.querySelector("a.current");
    if (current) {
      const box = sidebar.getBoundingClientRect();
      const item = current.getBoundingClientRect();
      if (item.top < box.top || item.bottom > box.bottom - 8) {
        sidebar.scrollTop += (item.top - box.top) - 40;
      }
    }
    sidebar.addEventListener("scroll", () => {
      sessionStorage.setItem("docs-sidebar-scroll", String(sidebar.scrollTop));
    }, { passive: true });
  }

  // ── language ─────────────────────────────────────────────────────────────

  const language = document.getElementById("language");
  if (language) {
    language.addEventListener("change", () => {
      if (language.value) location.href = language.value;
    });
  }

  // ── scroll to the requested section ───────────────────────────────────────

  // The Markdown only exists after the render above, so a `#anchor` in the url has to be applied here -
  // otherwise following a link into a document would always land at the top of the page.
  const wanted = decodeURIComponent(location.hash.slice(1));
  if (wanted) {
    const target = document.getElementById(wanted);
    if (target) target.scrollIntoView();
  }

  // ── theme ─────────────────────────────────────────────────────────────────

  const themeButton = document.getElementById("theme");
  const stored = localStorage.getItem("docs-theme");
  if (stored) document.documentElement.dataset.theme = stored;
  if (themeButton) {
    themeButton.addEventListener("click", () => {
      const dark = document.documentElement.dataset.theme === "dark" ||
        (!document.documentElement.dataset.theme &&
          window.matchMedia("(prefers-color-scheme: dark)").matches);
      const next = dark ? "light" : "dark";
      document.documentElement.dataset.theme = next;
      localStorage.setItem("docs-theme", next);
      themeButton.textContent = next === "dark" ? "🌙" : "☀";
    });
  }

  // ── search ────────────────────────────────────────────────────────────────

  const box = document.getElementById("search-input");
  const results = document.getElementById("search-results");
  if (box && results) {
    let index = null;
    const load = () => {
      if (index) return Promise.resolve(index);
      return fetch(root + "search.json")
        .then((response) => response.json())
        .then((payload) => (index = payload.pages || []))
        .catch(() => (index = []));
    };

    const run = (query) => {
      const needle = query.trim().toLowerCase();
      if (needle.length < 2) {
        results.classList.remove("open");
        results.innerHTML = "";
        return;
      }
      load().then((pages) => {
        const hits = [];
        for (const page of pages) {
          const haystack = (page.title + " " + page.component + " " + page.headings.join(" ") + " " +
            page.text).toLowerCase();
          if (!haystack.includes(needle)) continue;
          const heading = page.headings.find((name) => name.toLowerCase().includes(needle));
          hits.push({ page: page, heading: heading, weight: heading ? 0 : 1 });
          if (hits.length >= 30) break;
        }
        hits.sort((a, b) => a.weight - b.weight);
        results.innerHTML = "";
        for (const hit of hits) {
          const link = document.createElement("a");
          link.href = root + hit.page.url + (hit.heading ? "#" + hit.heading : "");
          const title = document.createElement("span");
          title.textContent = hit.page.title + (hit.heading ? " › " + hit.heading : "");
          const where = document.createElement("span");
          where.className = "where";
          where.textContent = hit.page.component + (hit.page.lang === "cn" ? " · 中文" : "");
          link.appendChild(title);
          link.appendChild(where);
          results.appendChild(link);
        }
        if (!hits.length) {
          results.innerHTML = "";
          const empty = document.createElement("a");
          empty.textContent = "no match";
          results.appendChild(empty);
        }
        results.classList.add("open");
      });
    };

    box.addEventListener("input", () => run(box.value));
    box.addEventListener("focus", () => load());
    document.addEventListener("click", (event) => {
      if (!results.contains(event.target) && event.target !== box) results.classList.remove("open");
    });
    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape") results.classList.remove("open");
      if (event.key === "/" && document.activeElement !== box) {
        event.preventDefault();
        box.focus();
      }
    });
  }
})();
