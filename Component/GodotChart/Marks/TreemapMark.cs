using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart.Marks;

/// <summary>
/// Layout algorithm of a <see cref="TreemapMark"/>.
/// </summary>
public enum TreemapLayoutMode
{
    /// <summary>Split the items in half by value, recursively (fast, aspect ratios not optimised).</summary>
    BinarySplit,

    /// <summary>Squarified layout (Bruls et al.): rows of items with the best achievable aspect ratio.</summary>
    Squarify,
}

/// <summary>
/// Treemap mark: rectangle areas represent values.
/// <para>
/// A row may name its parent through <see cref="ParentField"/>, which turns the flat row list into a
/// tree (the same convention <see cref="SunburstMark"/> uses): groups are laid out first and their
/// children partition the group rectangle, recursively. A group with no value of its own takes the sum
/// of its children. Rows without the parent field keep the single level layout, so flat data behaves
/// exactly as before.
/// </para>
/// <para>
/// Encodes: X = label, Y = value, Color = optional category. Classified as a hierarchical mark; no
/// Cartesian grid is drawn. Animation: cells grow from center outward.
/// </para>
/// <para>
/// Cell labels are formatted with <see cref="Mark.LabelFormat"/> ({0} = cell label, {1} = its value).
/// A cell places its label itself (centred in a leaf, in the header strip of a group), so
/// <see cref="Mark.LabelPosition"/> does not apply to this mark.
/// </para>
/// </summary>
public class TreemapMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Hierarchical;

    /// <summary>Gap between treemap cells in pixels.</summary>
    public float CellGap { get; set; } = 2f;

    /// <summary>
    /// Upper bound on the nodes this mark draws: a treemap draws one element per row, so a table of a hundred
    /// thousand rows is a hundred thousand shapes. Rows past the cap are not drawn and the mark warns once -
    /// aggregate the data (or raise the cap) instead of watching the page drop to single-digit fps. The cap
    /// covers hit testing too: a row that is never painted has nowhere to be hovered, so it is not hittable
    /// either.
    /// </summary>
    public int MaxNodes { get; set; } = 65_536;

    /// <summary>Corner radius for each cell.</summary>
    public float CornerRadius { get; set; } = 3f;

    /// <summary>Layout algorithm used to place the cells (applied on every level of the tree).</summary>
    public TreemapLayoutMode LayoutMode { get; set; } = TreemapLayoutMode.BinarySplit;

    /// <summary>
    /// Field holding the key of the parent node. Rows with an empty (or unknown) parent are the top
    /// level. When no row carries this field the mark lays out a single flat level.
    /// </summary>
    public string ParentField { get; set; } = "parent";

    /// <summary>
    /// Height in pixels of the strip reserved at the top of a group rectangle for its label. 0 disables
    /// the strip (a group is then only visible through its children). Groups only get a strip when the
    /// rectangle is tall enough to keep a usable child area.
    /// </summary>
    public float GroupHeaderHeight { get; set; } = 16f;

    /// <summary>Shading step applied to the n-th child of a group, so siblings of one family stay readable.</summary>
    public float SiblingShadeStep { get; set; } = 0.12f;

    /// <summary>Whether to show value labels inside cells.</summary>
    public override bool ShowLabel { get; set; } = true;

    /// <summary>
    /// <see cref="Cell.Family"/> of a cell drawn by the single level layout: no family, so the cells keep
    /// the classic one-colour look and no sibling shading is applied.
    /// </summary>
    private const int NoFamily = -1;

    /// <summary>One placed rectangle: a group background or a leaf, with the row it came from.</summary>
    /// <param name="Row">Source row this cell was laid out from.</param>
    /// <param name="RowIndex">Index of <paramref name="Row"/> in the mark's data list.</param>
    /// <param name="Label">Display label of the cell.</param>
    /// <param name="Path">Full path of the cell inside the hierarchy, used as its identity.</param>
    /// <param name="Value">Value the cell's area is proportional to.</param>
    /// <param name="X">Left edge of the cell in plot coordinates.</param>
    /// <param name="Y">Top edge of the cell in plot coordinates.</param>
    /// <param name="W">Width of the cell in plot coordinates.</param>
    /// <param name="H">Height of the cell in plot coordinates.</param>
    /// <param name="Family">Palette index of the top level branch this cell belongs to, or <see cref="NoFamily"/>.</param>
    /// <param name="SiblingIndex">Position among its siblings; drives the family shading.</param>
    /// <param name="IsGroup">True when the cell is a group background rather than a leaf.</param>
    private readonly record struct Cell(
        DataRow Row, int RowIndex, string Label, string Path, double Value,
        float X, float Y, float W, float H, int Family, int SiblingIndex, bool IsGroup);

    /// <summary>A row in the hierarchy while the layout is being built.</summary>
    private sealed class Node
    {
        public required string Label { get; init; }
        public required DataRow Row { get; init; }
        public required int RowIndex { get; init; }
        public required string ParentKey { get; init; }
        public double Value { get; init; }
        public double Subtree { get; set; }
        public string Path { get; set; } = "";
        public List<Node> Children { get; } = [];
    }

    // Cached layout to avoid recomputation in HitTest
    private List<Cell>? _cachedLayout;
    private LayoutCacheKey? _cacheKey;
    // <see cref="LayoutConfigHash"/> of the frame the cached layout was built for. Null while nothing
    // is cached.
    private int? _layoutConfigHash;

    /// <summary>Whether the "rows past <see cref="MaxNodes"/>" warning was already reported.</summary>
    private bool _warnedCap;

    /// <summary>Number of times the layout was rebuilt — used by tests to verify caching.</summary>
    internal int LayoutBuildCount { get; private set; }

    /// <summary>
    /// Force the cached layout to be rebuilt.
    /// The cache key already covers the chart, the layout version, the data list identity and the
    /// mark's own layout configuration, so this is only needed when the contents of the very same
    /// data list are mutated in place.
    /// </summary>
    public void InvalidateCache()
    {
        _cachedLayout = null;
        _cacheKey = null;
        _layoutConfigHash = null;
    }

    /// <summary>
    /// Hash of the mark properties that shape the layout but are invisible to <see cref="Mark.CacheKey"/>:
    /// they are plain auto-properties without a setter hook, so the cache has to notice a changed
    /// configuration by comparing this value instead of trusting the layout version.
    /// </summary>
    private int LayoutConfigHash()
        => HashCode.Combine(LayoutMode, CellGap, GroupHeaderHeight, ParentField, YChannel);

    /// <summary>Layout for the current context, built once per cache key and layout configuration.</summary>
    private List<Cell>? CachedLayout(MarkContext ctx)
    {
        var key = CacheKey(ctx);
        int configHash = LayoutConfigHash();
        if (_cachedLayout != null && _cacheKey == key && _layoutConfigHash == configHash)
            return _cachedLayout;

        _cachedLayout = PrepareLayout(ctx);
        _cacheKey = key;
        _layoutConfigHash = configHash;
        LayoutBuildCount++;
        return _cachedLayout;
    }

    /// <summary>
    /// Draw one treemap cell: its rectangle (shrunk toward its centre by the entry animation), the
    /// selection stroke and, when it is big enough, its label. The fill the caller passes in is the one the
    /// pass owns: the data layer hands over what <see cref="Mark.ResolveFill"/> answered (the hover look
    /// while it owns the state), and the overlay pass hands over <see cref="Mark.ActiveFillOf"/>.
    /// <para>
    /// The label belongs to this helper because it sits <i>inside</i> the cell: an overlay pass that
    /// repainted the cell without it would erase the text the cached layer holds.
    /// </para>
    /// </summary>
    /// <param name="ctx">Context of the frame.</param>
    /// <param name="cell">Cell to draw.</param>
    /// <param name="anim">Entry animation progress.</param>
    /// <param name="hovered">True when the cell's row is the hovered one.</param>
    /// <param name="selected">True when the cell's row is the selected one.</param>
    private void DrawCell(MarkContext ctx, Cell cell, float anim, bool hovered, bool selected)
    {
        // Animate from center
        float cx = cell.X + cell.W / 2f;
        float cy = cell.Y + cell.H / 2f;
        float aw = cell.W * anim;
        float ah = cell.H * anim;

        var color = CellColor(ctx, cell, cell.RowIndex);
        // While the overlay owns the state, CellColor answered the default fill: the hover brighten is
        // added here, exactly as the data layer does it while it owns the state.
        if (hovered && ctx.StateInOverlay) color = ActiveFillOf(ctx, color);
        float opacity = ComputeElementOpacity(ctx, cell.Row, cell.RowIndex);

        var path = ShapePath(ctx);
        var paint = ShapePaint(ctx);
        if (CornerRadius > 0)
            path.RoundRect(cx - aw / 2f, cy - ah / 2f, aw, ah, CornerRadius);
        else
            path.Rect(cx - aw / 2f, cy - ah / 2f, aw, ah);
        paint.SetColor(color).SetAntiAlias(true).SetOpacity(opacity);
        ctx.Canvas.Fill(path, paint);

        if (selected)
        {
            var selPaint = ShapePaint(ctx);
            ApplySelectionPaint(ctx, selPaint, opacity);
            ctx.Canvas.Stroke(path, selPaint);
        }

        if (!ShowLabel) return;

        // Thresholds come from the theme so they can follow the font size / DPI.
        float minW = (ctx.Theme ?? ChartTheme.Default).TreemapLabelMinWidth;
        float minH = (ctx.Theme ?? ChartTheme.Default).TreemapLabelMinHeight;
        if (aw <= minW || ah <= minH) return;

        var labelPaint = ShapePaint(ctx);
        labelPaint.SetColor(GetDataLabelColor(ctx)).SetOpacity(opacity);

        if (cell.IsGroup && GroupHeaderHeight > 0f && ah > GroupHeaderHeight)
        {
            // The group name sits in the strip reserved for it, left aligned.
            var headerFont = ThemedFont(ctx, new FontSettings
            {
                Size = FontSettings.Default.Size * 0.85f,
                Align = TextAlign.Left,
            });
            ctx.Canvas.DrawText(FormatLabel(LabelFormat, cell.Label, cell.Value),
                                cell.X + CellGap, cell.Y + GroupHeaderHeight * 0.72f,
                                headerFont, labelPaint);
        }
        else if (!cell.IsGroup)
        {
            DrawTextCentered(ctx, labelPaint, FormatLabel(LabelFormat, cell.Label, cell.Value),
                cx, cy, FontSettings.Default);
        }
    }

    /// <summary>
    /// A treemap cell is opaque, so its hover fill can be painted on the overlay pass without touching the
    /// cached layer - including its label, which the same helper redraws. The overlay uses the layout the
    /// data layer already built (<see cref="CachedLayout"/>), so a hover frame costs the interactive cells
    /// alone.
    /// </summary>
    public override bool InteractionStateInOverlay => true;

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        if (ctx.Data.Count == 0) return;
        float anim = ComputeAnimProgress(ctx);

        var cached = CachedLayout(ctx);
        if (cached == null) return;

        // The interaction state stays in the data layer only while the chart does not cache it; with the
        // cache on the overlay paints it (see InteractionStateInOverlay / RenderOverlay).
        bool stateHere = !ctx.StateInOverlay;

        // Cells come in parent-before-child order, so children paint on top of their group.
        foreach (var cell in cached)
        {
            DrawCell(ctx, cell, anim,
                hovered: stateHere && cell.RowIndex == ctx.HoveredRowIndex,
                selected: stateHere && cell.RowIndex == ctx.SelectedRowIndex);
        }
    }

    /// <inheritdoc />
    public override void RenderOverlay(MarkContext ctx)
    {
        // Only while the chart keeps the data layer in an image: with the cache off Render painted the state.
        if (!OverlayRows(ctx, out int hovered, out int selected)) return;

        var cached = CachedLayout(ctx);
        if (cached == null) return;

        float anim = ComputeAnimProgress(ctx);

        // Only the cells of the interactive rows are drawn, from the same layout the data layer used.
        foreach (var cell in cached)
        {
            bool isHovered  = cell.RowIndex == hovered;
            bool isSelected = cell.RowIndex == selected;
            if (!isHovered && !isSelected) continue;

            DrawCell(ctx, cell, anim, isHovered, isSelected);
        }
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        // Invalidate cache if layout version changed (data, encodes, scales, or plot changed)
        var layout = CachedLayout(ctx);
        if (layout == null) return null;

        // Reverse order: the deepest cell is drawn last, so it wins the hit.
        // The hit area is the rectangle Render paints, animation included: a cell grows out of its centre, so
        // the parts of the final rectangle it has not reached yet are not hittable (a cell that has not started
        // growing is invisible and therefore not a hit - see WaffleMark, which has always done this).
        float anim = ComputeAnimProgress(ctx);
        for (int i = layout.Count - 1; i >= 0; i--)
        {
            var cell = layout[i];
            float cx = cell.X + cell.W / 2f;
            float cy = cell.Y + cell.H / 2f;
            float halfW = cell.W * anim / 2f;
            float halfH = cell.H * anim / 2f;
            if (screenPos.X < cx - halfW || screenPos.X > cx + halfW ||
                screenPos.Y < cy - halfH || screenPos.Y > cy + halfH)
                continue;

            var yRaw = ctx.Encodes.Resolve(YChannel, cell.Row);
            return new HitResult
            {
                Hit = true, Row = cell.Row, RowIndex = cell.RowIndex,
                ScreenX = cx, ScreenY = cy,
                Label = $"{cell.Path}: {yRaw}",
                MarkType = nameof(TreemapMark),
            };
        }
        return null;
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build the cells for the current context. Flat rows (no <see cref="ParentField"/> values) give the
    /// classic one-level treemap; rows that reference a parent are linked into a tree and laid out
    /// recursively, so a group owns the rectangle its children split.
    /// </summary>
    private List<Cell>? PrepareLayout(MarkContext ctx)
    {
        if (ctx.Data.Count == 0) return null;

        var nodes = new List<Node>();
        bool hierarchical = false;
        int limit = Math.Min(ctx.Data.Count, MaxNodes);
        if (ctx.Data.Count > MaxNodes && !_warnedCap)
        {
            _warnedCap = true;
            GD.PushWarning(
                $"{nameof(TreemapMark)}: {ctx.Data.Count} rows exceed MaxNodes ({MaxNodes}); laying out the " +
                $"first {MaxNodes}, the rest are not drawn. Aggregate the data or raise MaxNodes.");
        }
        for (int i = 0; i < limit; i++)
        {
            var row = ctx.Data[i];
            if (IsSeriesHidden(ctx, row)) continue;

            var yRaw = ctx.Encodes.Resolve(YChannel, row);
            double val = yRaw != null ? ToDouble(yRaw, "Y") : 0;
            // A non-finite value would poison the total and every cell size: skip the row. A missing
            // value stays 0 so that a pure grouping row can still take the sum of its children.
            if (!double.IsFinite(val)) continue;

            var parentKey = GetStringOrNull(row, ParentField) ?? "";
            if (parentKey.Length > 0) hierarchical = true;

            nodes.Add(new Node
            {
                Label = ctx.Encodes.Resolve(Channel.X, row)?.ToString() ?? $"Item{i}",
                Row = row,
                RowIndex = i,
                ParentKey = parentKey,
                Value = Math.Max(0, val),
            });
        }
        if (nodes.Count == 0) return null;

        return hierarchical ? LayoutHierarchy(ctx, nodes) : LayoutFlat(ctx, nodes);
    }

    /// <summary>Single level layout: every row is a cell of the whole plot area.</summary>
    private List<Cell> LayoutFlat(MarkContext ctx, List<Node> nodes)
    {
        var cells = new List<Cell>(nodes.Count);
        var items = new List<Node>(nodes.Count);
        foreach (var node in nodes)
        {
            if (node.Value <= 0) continue;   // flat mode keeps the "positive values only" rule
            items.Add(node);
        }
        if (items.Count == 0) return cells;

        items.Sort((a, b) => b.Value.CompareTo(a.Value));
        double total = 0;
        foreach (var node in items) total += node.Value;

        var values = new List<double>(items.Count);
        foreach (var node in items) values.Add(node.Value);
        var rects = LayoutValues(values, total, ctx.Plot.X, ctx.Plot.Y, ctx.Plot.Width, ctx.Plot.Height, CellGap);

        for (int i = 0; i < items.Count; i++)
        {
            var (x, y, w, h) = rects[i];
            var node = items[i];
            node.Path = node.Label;
            cells.Add(new Cell(node.Row, node.RowIndex, node.Label, node.Label, node.Value,
                               x, y, w, h, NoFamily, 0, false));
        }
        return cells;
    }

    /// <summary>
    /// Nested layout: rows linked through <see cref="ParentField"/> form a tree, every group gets a header
    /// strip and its children partition what is left, recursively. Rows that sit in a parent cycle are
    /// promoted to the top level so no row can disappear from the chart.
    /// </summary>
    private List<Cell> LayoutHierarchy(MarkContext ctx, List<Node> nodes)
    {
        // A parent reference names a row by its label (the convention SunburstMark uses), so the index
        // is built on labels. Several rows may share one, hence the candidate list: a root level row
        // wins, otherwise the first candidate that is not the row itself.
        var byLabel = new Dictionary<string, List<Node>>();
        foreach (var node in nodes)
        {
            if (!byLabel.TryGetValue(node.Label, out var list))
                byLabel[node.Label] = list = [];
            list.Add(node);
        }

        var roots = new List<Node>();
        foreach (var node in nodes)
        {
            if (node.ParentKey.Length == 0)
            {
                roots.Add(node);
                continue;
            }

            Node? parent = null;
            if (byLabel.TryGetValue(node.ParentKey, out var candidates))
            {
                // A row is never its own parent, so a self reference falls through to "top level".
                foreach (var candidate in candidates)
                {
                    if (ReferenceEquals(candidate, node)) continue;
                    if (candidate.ParentKey.Length == 0) { parent = candidate; break; }
                }
                if (parent == null)
                {
                    foreach (var candidate in candidates)
                    {
                        if (ReferenceEquals(candidate, node)) continue;
                        parent = candidate;
                        break;
                    }
                }
            }

            if (parent == null) roots.Add(node);   // unknown or self parent: treat the row as top level
            else parent.Children.Add(node);
        }

        // Anything unreachable from a root sits in a parent cycle: promote one member at a time.
        var reachable = new HashSet<Node>();
        void Mark(Node node)
        {
            if (!reachable.Add(node)) return;
            foreach (var child in node.Children) Mark(child);
        }
        foreach (var root in roots) Mark(root);

        int promoted = 0;
        for (int pass = 0; pass < nodes.Count; pass++)
        {
            Node? next = null;
            foreach (var node in nodes)
            {
                if (reachable.Contains(node)) continue;
                next = node;
                break;
            }
            if (next == null) break;

            foreach (var parent in nodes)
                parent.Children.Remove(next);
            roots.Add(next);
            Mark(next);
            promoted++;
        }
        if (promoted > 0)
            GD.PushWarning($"TreemapMark: promoted {promoted} row(s) out of a parent cycle to the top level.");

        // A group without a value takes the sum of its children.
        foreach (var root in roots) ComputeSubtree(root);

        var cells = new List<Cell>();
        var topItems = new List<Node>();
        foreach (var root in roots)
        {
            Prune(root);
            if (root.Subtree > 0) topItems.Add(root);
        }
        if (topItems.Count == 0) return cells;

        topItems.Sort((a, b) => b.Subtree.CompareTo(a.Subtree));
        var values = new List<double>(topItems.Count);
        foreach (var node in topItems) values.Add(node.Subtree);
        double total = 0;
        foreach (var value in values) total += value;

        var rects = LayoutValues(values, total, ctx.Plot.X, ctx.Plot.Y, ctx.Plot.Width, ctx.Plot.Height, CellGap);
        for (int i = 0; i < topItems.Count; i++)
            Place(topItems[i], rects[i], i, 0, "", cells);
        return cells;
    }

    /// <summary>
    /// Weight of a node: its own value when it has one, otherwise the sum of its children.
    /// <para>
    /// A group node (a folder, a category, the synthetic root) carries no value of its own, so adding its
    /// value <i>and</i> its children's counted the children twice and made the cell areas disagree with the
    /// labels. <c>SunburstMark.PropagateValues</c> states the same rule for the rings.
    /// </para>
    /// </summary>
    private static double ComputeSubtree(Node node)
    {
        double childSum = 0;
        foreach (var child in node.Children) childSum += ComputeSubtree(child);
        node.Subtree = node.Value > 0 ? node.Value : childSum;
        return node.Subtree;
    }

    /// <summary>Drop children with nothing to show and sort the rest so the biggest cell comes first.</summary>
    private static void Prune(Node node)
    {
        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            Prune(node.Children[i]);
            if (node.Children[i].Subtree <= 0) node.Children.RemoveAt(i);
        }
        node.Children.Sort((a, b) => b.Subtree.CompareTo(a.Subtree));
    }

    /// <summary>Place one node and, recursively, its children inside <paramref name="rect"/>.</summary>
    private void Place(Node node, (float x, float y, float w, float h) rect, int family,
                       int sibling, string parentPath, List<Cell> cells)
    {
        node.Path = parentPath.Length == 0 ? node.Label : $"{parentPath} / {node.Label}";
        bool isGroup = node.Children.Count > 0;
        cells.Add(new Cell(node.Row, node.RowIndex, node.Label, node.Path, node.Subtree,
                           rect.x, rect.y, rect.w, rect.h, family, sibling, isGroup));
        if (!isGroup) return;

        // Reserve a strip for the group label when the rectangle can spare it.
        float header = GroupHeaderHeight > 0f && rect.h > GroupHeaderHeight * 2.5f ? GroupHeaderHeight : 0f;
        float cx = rect.x;
        float cy = rect.y + header;
        float cw = rect.w;
        float ch = rect.h - header;
        if (cw <= 1f || ch <= 1f) return;

        var values = new List<double>(node.Children.Count);
        foreach (var child in node.Children) values.Add(child.Subtree);
        double total = 0;
        foreach (var value in values) total += value;
        if (total <= 0) return;

        var rects = LayoutValues(values, total, cx, cy, cw, ch, CellGap);
        for (int i = 0; i < node.Children.Count; i++)
            Place(node.Children[i], rects[i], family, i, node.Path, cells);
    }

    /// <summary>
    /// Colour of a cell: one palette colour per top-level cell, and inside a group the family colour
    /// shaded by the cell's position among its siblings.
    /// </summary>
    private Color CellColor(MarkContext ctx, Cell cell, int index)
    {
        var palette = PaletteOf(ctx);

        // Single level layout: every cell is a top-level category of its own, so it takes the next
        // palette colour - the same rule the pie family follows. A colour channel still wins.
        if (cell.Family == NoFamily)
            return ResolveFill(ctx, cell.Row, index, palette[index % palette.Length]);

        var familyColor = palette[cell.Family % palette.Length];

        var color = ResolveFill(ctx, cell.Row, index, familyColor);
        if (cell.SiblingIndex == 0 || SiblingShadeStep <= 0f) return color;

        // Siblings of one group stay recognisable as a family: every step darkens the base colour.
        float factor = MathF.Max(0.4f, 1f - SiblingShadeStep * cell.SiblingIndex);
        return BrightenColor(color, factor);
    }

    /// <summary>Layout one level; the two modes differ only in how they split the rectangle.</summary>
    private List<(float x, float y, float w, float h)> LayoutValues(
        List<double> values, double total, float x, float y, float w, float h, float gap)
        => LayoutMode == TreemapLayoutMode.Squarify
            ? SquarifiedLayout(values, total, x, y, w, h, gap)
            : BinarySplitLayout(values, total, x, y, w, h, gap);

    /// <summary>
    /// Classic <b>squarified</b> treemap layout (Bruls et al.): items are packed into rows along the
    /// shorter side of the remaining rectangle, greedily extending a row while that keeps the worst
    /// aspect ratio from getting worse. Selected with <see cref="LayoutMode"/>.
    /// <para>
    /// The returned list always holds exactly one rectangle per value, in the same order (the callers
    /// index it by item). Once the remaining rectangle is too thin to hold another row, the leftover
    /// items get a degenerate <c>(x, y, 0, 0)</c> rectangle instead of being dropped.
    /// </para>
    /// </summary>
    private static List<(float x, float y, float w, float h)> SquarifiedLayout(
        List<double> values, double total, float x, float y, float w, float h, float gap)
    {
        var result = new List<(float x, float y, float w, float h)>(values.Count);
        if (values.Count == 0 || total <= 0) return result;

        int index = 0;
        float rx = x, ry = y, rw = w, rh = h;
        double remaining = total;

        while (index < values.Count)
        {
            // The remaining rectangle cannot take another row (thinner than a pixel, or no value
            // left to distribute). Fill the rest of the result with empty cells so the list stays
            // aligned with the values the callers index it by.
            if (rw <= 1f || rh <= 1f || remaining <= 0)
            {
                for (; index < values.Count; index++)
                    result.Add((rx + gap / 2f, ry + gap / 2f, 0f, 0f));
                break;
            }

            int rowEnd = index;
            double rowSum = 0;
            double bestWorst = double.MaxValue;
            float side = MathF.Max(1f, MathF.Min(rw, rh));

            // Extend the row while the worst aspect ratio does not get worse.
            while (rowEnd < values.Count)
            {
                double nextSum = rowSum + values[rowEnd];
                float thickness = (float)(nextSum / remaining) * (rw * rh) / side;
                double worst = WorstAspect(values, index, rowEnd, nextSum, thickness, side);
                if (rowEnd > index && worst > bestWorst) break;
                bestWorst = worst;
                rowSum = nextSum;
                rowEnd++;
            }

            float rowThickness = (float)(rowSum / remaining) * (rw * rh) / side;

            // Lay the row out along the shorter side.
            bool horizontal = rw >= rh; // row along the left edge, stacked vertically
            float offset = 0f;
            for (int i = index; i < rowEnd; i++)
            {
                float extent = (float)(values[i] / rowSum) * side;
                float cx, cy, cw, ch;
                if (horizontal)
                {
                    cx = rx;
                    cy = ry + offset;
                    cw = rowThickness;
                    ch = extent;
                }
                else
                {
                    cx = rx + offset;
                    cy = ry;
                    cw = extent;
                    ch = rowThickness;
                }
                result.Add((cx + gap / 2f, cy + gap / 2f,
                            MathF.Max(0f, cw - gap), MathF.Max(0f, ch - gap)));
                offset += extent;
            }

            // Shrink the remaining rectangle.
            if (horizontal)
            {
                rx += rowThickness;
                rw -= rowThickness;
            }
            else
            {
                ry += rowThickness;
                rh -= rowThickness;
            }
            remaining -= rowSum;
            index = rowEnd;
        }

        return result;
    }

    /// <summary>Worst aspect ratio of a candidate row (used by the squarified layout).</summary>
    private static double WorstAspect(
        List<double> values, int start, int end, double rowSum, float thickness, float side)
    {
        if (rowSum <= 0 || thickness <= 0) return double.MaxValue;
        double worst = 0;
        for (int i = start; i <= end; i++)
        {
            float extent = (float)(values[i] / rowSum) * side;
            if (extent <= 0) continue;
            double ratio = Math.Max(thickness / extent, extent / thickness);
            if (ratio > worst) worst = ratio;
        }
        return worst;
    }

    /// <summary>
    /// Binary-split treemap layout (the historical default): the items are split in half by value and
    /// the halves are laid out recursively. Aspect ratios are not optimised.
    /// </summary>
    private static List<(float x, float y, float w, float h)> BinarySplitLayout(
        List<double> values, double total, float x, float y, float w, float h, float gap)
    {
        var result = new List<(float x, float y, float w, float h)>();
        BinarySplitRecurse(values, 0, values.Count, total, x, y, w, h, gap, result);
        return result;
    }

    private static void BinarySplitRecurse(
        List<double> values, int start, int end, double total,
        float x, float y, float w, float h, float gap,
        List<(float x, float y, float w, float h)> result)
    {
        int count = end - start;
        if (count <= 0) return;
        if (count == 1)
        {
            // Thin slices can be smaller than the gap: clamp instead of emitting a negative size.
            result.Add((x + gap / 2f, y + gap / 2f, MathF.Max(0f, w - gap), MathF.Max(0f, h - gap)));
            return;
        }

        // Cut the item list where the running sum first reaches half of the total value.
        double halfTotal = 0;
        double sumSoFar  = 0;
        double halfTarget = total / 2.0;
        int splitIdx = start + 1;

        for (int i = start; i < end; i++)
        {
            sumSoFar += values[i];
            if (sumSoFar >= halfTarget)
            {
                splitIdx = i + 1;
                halfTotal = sumSoFar;
                break;
            }
        }
        if (splitIdx >= end) { splitIdx = end - 1; halfTotal = total - values[end - 1]; }

        double otherHalf = total - halfTotal;

        // Recurse into the two partitions, split along the longer side of the rectangle.
        if (w >= h)
        {
            // Split horizontally
            float leftW = (float)(halfTotal / total * w);
            BinarySplitRecurse(values, start, splitIdx, halfTotal, x, y, leftW, h, gap, result);
            BinarySplitRecurse(values, splitIdx, end, otherHalf, x + leftW, y, w - leftW, h, gap, result);
        }
        else
        {
            // Split vertically
            float topH = (float)(halfTotal / total * h);
            BinarySplitRecurse(values, start, splitIdx, halfTotal, x, y, w, topH, gap, result);
            BinarySplitRecurse(values, splitIdx, end, otherHalf, x, y + topH, w, h - topH, gap, result);
        }
    }
}
