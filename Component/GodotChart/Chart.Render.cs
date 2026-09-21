using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Chart partial — rendering pipeline, context builders, and mark compatibility validation.
/// </summary>
public partial class Chart
{
    // ── Render ────────────────────────────────────────────────

    /// <summary>
    /// Draw one frame onto the canvas (call inside <c>BeginFrame</c>/<c>EndFrame</c>). Refits
    /// auto-inferred scales, validates mark compatibility and runs the renderer slots.
    /// <para>
    /// One frame has two layers (see <see cref="UseLayerCache"/>): the <b>data layer</b> - background,
    /// title, grid, axes, axis labels, legend and the marks' non-interactive state - and the
    /// <b>overlay</b> - the marks' interaction-state visuals and the crosshair. With the layer cache on the
    /// data layer is kept in an image and only rebuilt when one of its inputs changes, so moving the pointer
    /// costs the overlay alone; with it off (the default) the two layers are drawn one after the other in
    /// exactly the historical order.
    /// </para>
    /// </summary>
    public void Render()
    {
        // A renderer callback that calls back into Render - a host drawing its own overlay from inside a slot,
        // a mark that re-renders - would re-enter the layout and the layer cache mid-frame, and a callback that
        // always rendered would recurse until the stack ran out. The reentrant call is ignored: the frame that
        // is already running finishes, and the state it is building stays consistent.
        if (_isRendering) return;

        _isRendering = true;
        try
        {
            bool layered = LayerCaching();
            if (layered && TryPresentCachedLayer()) return;

            RenderDataLayer(layered);
            if (layered) CaptureDataLayer();
            RenderOverlayLayer(layered);
        }
        finally
        {
            _isRendering = false;
        }
    }

    /// <summary>
    /// Draw the part of the frame that does not depend on the pointer: background, title, grid, axes, axis
    /// labels, legend and the marks themselves. Returns after the legend, so the state visuals of the marks
    /// and the crosshair stay in <see cref="RenderOverlayLayer"/>.
    /// </summary>
    /// <param name="stateInOverlay">
    /// True when this data layer is kept in an image, which moves the marks' interaction-state visuals to
    /// the overlay (see <see cref="MarkContext.StateInOverlay"/>).
    /// </param>
    private void RenderDataLayer(bool stateInOverlay)
    {
        var renderData = GetRenderData();

        AutoFitScales();

        // Let marks contribute custom scale ranges (e.g., CandlestickMark Y-axis). This stage runs
        // before anything is drawn, so it gets the same containment as the render stages: a mark that
        // throws while fitting a scale must not take the whole frame down.
        var skipped = ValidateMarkCompatibility();
        foreach (var mark in _marks)
        {
            if (skipped.Contains(mark)) continue;
            var contributingMark = mark;
            // The mark's own bindings count here as well: a mark that maps the value channel to its own
            // field must have that field in the range it contributes, exactly like the auto-fit, which
            // collects the chart-level and the mark-level encodes together.
            var mergedEncodes = mark.ResolveEncodes(_encodes, EffectiveLayoutVersion);
            RenderStageSafely($"{mark.GetType().Name}.ContributeScales",
                () => contributingMark.ContributeScales(
                    _scales, mergedEncodes, contributingMark.Data ?? renderData));
        }

        // The contributions may have replaced a scale instance (a stacked bar installs its own Linear
        // scale, for example), so the pinned domains are re-applied on top of them: a ScaleDomain lock
        // has to survive both the auto-fit and whatever the marks decided afterwards.
        ApplyDomainLocks();

        // Extra padding for axis titles: the line height of the *themed* label font plus the shared
        // margin, so a larger themed font size cannot overlap the axis title.
        float labelLine = LabelLineHeight(ThemedLabelFontSize(_theme));

        // Left extra pad: the axis title *and* the tick label column, which add up - the title is drawn in its
        // own line of height next to the labels. Taking the larger of the two (what this did) left the title
        // sitting on the labels as soon as the labels needed more room than PaddingLeft. AxisTitleMargin is
        // absorbed by PaddingLeft, so only the line height is counted here.
        // The title needs its own band - the line of height, the margin the title renderer leaves at the chart
        // edge and the clearance that keeps it off the label column (see AxisTitleBand); the label column
        // (MeasureAxisLabelWidth) already carries its gap and margin.
        float titleLeftPad = _yAxisConfig?.Title != null
            ? AxisTitleBand(labelLine, _theme, withLabelClearance: true)
            : 0f;
        float extraLeftPad = titleLeftPad;
        float extraBottomPad = _xAxisConfig?.Title != null ? AxisTitleBand(labelLine, _theme) : 0f;
        // Reserve right padding when a Y2 axis is present: the theme's Y2LabelReservedWidth for the
        // labels plus the optional title space that mirrors the left side.
        bool hasY2 = _scales.Has(Channel.Y2);
        float extraRightPad = hasY2
            ? _theme.Y2LabelReservedWidth + (_y2AxisConfig?.Title != null ? AxisTitleBand(labelLine, _theme) : 0f)
            : 0f;
        // M34: widen the plot when the axis labels need more room than the configured padding.
        if (AutoPadding)
        {
            float neededLeft = MeasureAxisLabelWidth(Channel.Y, TextAlign.Right);
            // Both decorations have to fit beside each other: the title's band *and* the label column. Setting
            // extraLeftPad to the sum minus PaddingLeft loses the title's share whenever PaddingLeft is large
            // enough to cover the labels by itself, which is why the title kept sitting on them - so the title
            // band stays a floor of its own.
            float both = neededLeft + titleLeftPad - PaddingLeft;
            if (both > extraLeftPad) extraLeftPad = both;

            if (hasY2)
            {
                float neededRight = MeasureAxisLabelWidth(Channel.Y2, TextAlign.Left);
                if (neededRight > _theme.Y2LabelReservedWidth)
                    extraRightPad = MathF.Max(extraRightPad, neededRight - _theme.Y2LabelReservedWidth);
            }
        }
        // A legend on the left/right needs horizontal space: without it the legend was drawn on top
        // of the axis labels (and could run past the chart edge).
        if (_legendConfig is { Position: LegendPosition.Left or LegendPosition.Right } sideCfg &&
            _scales.TryGet(Channel.Color) is ICategoricalColorScale sideScale &&
            LegendRenderer != null)
        {
            // A right legend is drawn past the plot edge by the theme's LegendRightOffset on top of the
            // padding (see LegendLayoutHelper.Compute), so that offset is part of the reservation too -
            // without it the labels hang over the chart edge.
            float sideLegendWidth =
                LegendLayoutHelper.EstimateWidth(sideCfg, sideScale, _canvas, _theme) + sideCfg.Padding * 2f
                + (sideCfg.Position == LegendPosition.Right ? _theme.LegendRightOffset : 0f);
            if (sideCfg.Position == LegendPosition.Left)
                extraLeftPad = MathF.Max(extraLeftPad, sideLegendWidth);
            else
                extraRightPad = MathF.Max(extraRightPad, sideLegendWidth);
        }

        // Reserve space for a horizontal legend. The first estimate is a single row; after the plot
        // is known the legend is laid out for real (it may wrap) and the plot is recomputed once if
        // the legend needs more room than estimated.
        // Start from the height the legend actually needed last frame: with a single-row estimate the
        // first layout pass would use a different plot and the legend layout cache could never hit.
        float legendHeight = _lastLegendReservedHeight;
        if (legendHeight <= 0f && _legendConfig is { Position: LegendPosition.Top or LegendPosition.Bottom })
            legendHeight = MathF.Max(_legendConfig.SwatchSize, labelLine) + _legendConfig.Padding * 2f;

        PlotArea BuildPlot(float legendSpace) => new(
            OffsetX + PaddingLeft + extraLeftPad,
            OffsetY + PaddingTop + (Title != null ? _theme.TitleReservedHeight : 0f)
                + (_legendConfig?.Position == LegendPosition.Top ? legendSpace : 0f),
            // A node smaller than its own decorations would make these negative, and a plot rectangle with a
            // negative size draws nothing sensible: it collapses to one pixel instead (the chart is already
            // warning that it is below its content minimum).
            MathF.Max(1f, Width  - PaddingLeft - PaddingRight - extraLeftPad - extraRightPad),
            MathF.Max(1f, Height - PaddingTop  - PaddingBottom - (Title != null ? _theme.TitleReservedHeight : 0f)
                - extraBottomPad
                - (_legendConfig?.Position == LegendPosition.Top ? legendSpace : 0f)
                - (_legendConfig?.Position == LegendPosition.Bottom ? legendSpace : 0f)));

        // Two rectangles, on purpose: the decorations (above all the legend, which wraps to the width it is
        // handed) are laid out against the whole plot area, while the content - grid, axes, marks, the clip
        // they draw under and hit testing - gets the box ContentBox carves out of it. Nothing shaping the
        // content leaves the two identical.
        var fullPlot = BuildPlot(legendHeight);
        var plot = ContentBox(fullPlot);
        _lastPlot = plot;
        _fullPlot = fullPlot;

        // Skip the Cartesian grid/axes when no mark draws against them: either every mark lives in
        // another coordinate system, or the marks declare that they need no axes at all (a waffle is
        // laid out inside the plot rectangle but has no scale to show).
        _skipCartesianDecorations ??= _marks.Count > 0
            && _marks.TrueForAll(m => m.Coordinate != MarkCoordinate.Cartesian || !m.UsesAxes);

        // Legend — compute once and cache for the interaction layer + renderer
        ICategoricalColorScale? legendScale =
            _scales.TryGet(Channel.Color) as ICategoricalColorScale;
        if (legendScale is { Domain.Count: 0 }) legendScale = null;

        // The layout measures every label (an SKFont/SKTextBlob per item on the Skia backend), so it
        // is only recomputed when the layout version, the plot, the domain size or the legend
        // configuration changes (see LegendLayoutKey).
        LegendLayout? ComputeLegend(PlotArea area)
        {
            // A chart without a legend renderer (external legend UI) must not build a layout either:
            // it used to cost a measurement pass per frame and left a phantom legend hit region.
            if (_legendConfig is not { Position: not LegendPosition.None } ||
                legendScale == null ||
                LegendRenderer == null)
            {
                _cachedLegendLayout = null;
                return null;
            }

            var key = new LegendLayoutKey(
                EffectiveLayoutVersion, area, legendScale.Domain.Count,
                _legendConfig.Position, _legendConfig.SwatchSize,
                _legendConfig.ItemSpacing, _legendConfig.Padding);
            if (_cachedLegendLayout != null && _cachedLegendLayoutKey == key)
                return _cachedLegendLayout;

            _cachedLegendLayout = LegendLayoutHelper.Compute(
                area, _legendConfig, legendScale, OffsetX, _canvas, _theme);
            _cachedLegendLayoutKey = key;
            return _cachedLegendLayout;
        }

        // Measured against the whole plot area, not the content box: the legend wraps to the width it is given,
        // and a shaped (square) content box would break a one-row legend into four.
        _cachedLegendLayout = ComputeLegend(fullPlot);

        if (CachedLegendLayout is { } measured &&
            _legendConfig is { Position: LegendPosition.Top or LegendPosition.Bottom } legendCfg)
        {
            float needed = measured.Height + legendCfg.Padding * 2f;
            if (needed > legendHeight + 0.5f || needed < legendHeight - 0.5f)
            {
                legendHeight = needed;
                fullPlot = BuildPlot(legendHeight);
                plot = ContentBox(fullPlot);
                _lastPlot = plot;
                _fullPlot = fullPlot;
                _cachedLegendLayout = ComputeLegend(fullPlot);
            }
            _lastLegendReservedHeight = legendHeight;
        }

        // A vertical legend reserved its width from an estimate before the plot existed (see above).
        // The real geometry is known now, so the reservation is refined with the width the legend
        // actually needs - the one-shot correction the horizontal legend already makes for its height.
        // Only the legend's *own* width is taken from the layout: it is measured from the labels alone
        // and does not depend on the plot, so rebuilding the plot once cannot oscillate.
        if (CachedLegendLayout is { } sideMeasured &&
            _legendConfig is { Position: LegendPosition.Left or LegendPosition.Right } sideLegend)
        {
            float neededSide = sideMeasured.Width + sideLegend.Padding * 2f
                + (sideLegend.Position == LegendPosition.Right ? _theme.LegendRightOffset : 0f);
            bool leftSide = sideLegend.Position == LegendPosition.Left;
            if (neededSide > (leftSide ? extraLeftPad : extraRightPad) + 0.5f)
            {
                // The estimate was too small (a theme with a wide swatch-to-text gap, for example):
                // give the legend the room it really needs and lay the plot out again.
                if (leftSide) extraLeftPad = neededSide;
                else extraRightPad = neededSide;
                fullPlot = BuildPlot(legendHeight);
                plot = ContentBox(fullPlot);
                _lastPlot = plot;
                _fullPlot = fullPlot;
                _cachedLegendLayout = ComputeLegend(fullPlot);
            }
        }

        // Build render context for renderer slots
        // Content minimum, measured from the same reservations BuildPlot used (the extra pads are what the
        // axis labels and titles needed beyond the theme's padding, the label widths are cached per channel, and
        // the legend height is the value the second pass settled on). The sum lives in ComposeMinimumSize, which
        // the estimate used before the first frame shares - see Chart.MinimumSize.
        float yLabelColumn = _labelWidths.TryGetValue(Channel.Y, out float measuredY) ? measuredY : 0f;
        float y2Column = hasY2 ? _theme.Y2LabelReservedWidth : 0f;
        _minimumSize = ComposeMinimumSize(
            PaddingLeft, PaddingRight, PaddingTop, PaddingBottom,
            extraLeftPad, extraRightPad, extraBottomPad,
            yLabelColumn, y2Column,
            Title != null ? _theme.TitleReservedHeight : 0f,
            _legendConfig?.Position is LegendPosition.Top or LegendPosition.Bottom ? legendHeight : 0f,
            labelLine, _xAxisConfig?.Title != null);

        // Where the chart really drew: the content box plus every decoration band this frame reserved (see
        // Chart.DrawnBounds). The legend speaks for itself - its layout carries the item rectangles - while the
        // axis bands are the reservations BuildPlot worked with, so a side without a decoration is not included.
        bool cartesian = !(_skipCartesianDecorations ?? false);
        Rect2 drawn = new(plot.X, plot.Y, plot.Width, plot.Height);
        float leftBand = cartesian ? PaddingLeft + extraLeftPad : Title != null ? PaddingLeft : 0f;
        float rightBand = cartesian && hasY2 ? PaddingRight + extraRightPad : 0f;
        float topBand = Title != null ? PaddingTop + _theme.TitleReservedHeight : 0f;
        float bottomBand = cartesian ? PaddingBottom + extraBottomPad : 0f;
        if (leftBand > 0f) drawn = drawn.Merge(new Rect2(OffsetX, plot.Y, leftBand, plot.Height));
        if (rightBand > 0f)
            drawn = drawn.Merge(new Rect2(plot.X + plot.Width, plot.Y, rightBand, plot.Height));
        if (topBand > 0f) drawn = drawn.Merge(new Rect2(OffsetX, OffsetY, Width, topBand));
        if (bottomBand > 0f)
            drawn = drawn.Merge(new Rect2(plot.X, plot.Y + plot.Height, plot.Width, bottomBand));
        if (CachedLegendLayout is { Items.Count: > 0 } legend)
        {
            Rect2 legendBox = default;
            bool firstItem = true;
            foreach (var item in legend.Items)
            {
                var itemBox = new Rect2(item.X, item.Y, item.Width, item.Height);
                legendBox = firstItem ? itemBox : legendBox.Merge(itemBox);
                firstItem = false;
            }
            drawn = drawn.Merge(legendBox);
        }
        _drawnBounds = drawn;

        // Too small to stay readable: say so once, not once per frame (a per-frame warning would flood the log).
        // The chart keeps drawing - the axis thins its own labels - so this is a hint, not an error.
        if (_minimumSize.X > 0f && (Width < _minimumSize.X || Height < _minimumSize.Y))
        {
            if (!_warnedAboutMinimumSize)
            {
                _warnedAboutMinimumSize = true;
                GD.PushWarning(
                    $"{nameof(Chart)}: {Width:F0}x{Height:F0} is below the content minimum " +
                    $"{_minimumSize.X:F0}x{_minimumSize.Y:F0} (title, legend, axis labels and axis titles). " +
                    "Labels will be thinned - give the chart more room, or accept it on purpose.");
            }
        }
        else
        {
            _warnedAboutMinimumSize = false;    // it fits again, so a later shrink warns again
        }

        var renderCtx = BuildRenderContext(plot, renderData);

        RenderStageSafely("background renderer", () => BackgroundRenderer?.Invoke(renderCtx));

        if (Title != null)
            RenderStageSafely("title renderer", () => TitleRenderer?.Invoke(renderCtx));

        if (cartesian)
        {
            RenderStageSafely("grid renderer", () => GridRenderer?.Invoke(renderCtx));
            RenderStageSafely("axis renderer", () => AxisRenderer?.Invoke(renderCtx));
        }

        var ctx = BuildMarkContext(stateInOverlay);

        // Marks are clipped to the plot rectangle: an element outside the visible window (what a zoom or a
        // pan creates, and also a domain the host pinned) maps outside the plot, and without the clip those
        // elements are painted over the axis labels - the series look like they run past the Y axis. The clip
        // is the plot itself, so nothing a mark draws inside the window changes.
        using (new CanvasSaveScope(_canvas))
        {
            _canvas.ClipRect(ctx.Plot.X, ctx.Plot.Y, ctx.Plot.Width, ctx.Plot.Height);

            foreach (var mark in _marks)
            {
                if (skipped.Contains(mark)) continue;
                var markCtx = mark.BindEncodes(
                    mark.Data != null ? ctx.WithData(mark.Data) : ctx, _encodes, ctx.LayoutVersion);
                var markToRender = mark;
                RenderStageSafely($"{mark.GetType().Name}.Render", () => markToRender.Render(markCtx));
            }
        }

        if (cartesian)
            RenderStageSafely("axis label renderer", () => AxisLabelRenderer?.Invoke(renderCtx));

        // Legend
        if (CachedLegendLayout != null)
            RenderStageSafely("legend renderer", () => LegendRenderer?.Invoke(renderCtx));
    }

    /// <summary>
    /// Draw the second half of a frame: the marks' interaction-state visuals and the crosshair. Runs on
    /// every frame - including the frames that present a cached data layer, which is where the layer cache's
    /// "a pointer move is cheap" comes from - and after the data layer's legend, so the drawing order of a
    /// frame without the cache is exactly the historical one.
    /// </summary>
    /// <param name="stateInOverlay">
    /// True when the data layer of this frame is kept in an image. Only then do the marks paint their
    /// interaction state here: with the cache off they drew it themselves, and painting it twice would
    /// double-blend a translucent element.
    /// </param>
    private void RenderOverlayLayer(bool stateInOverlay)
    {
        var ctx = BuildMarkContext(stateInOverlay);

        if (stateInOverlay)
        {
            // The data layer clips its marks to the plot; the overlay clips the same way so a highlight at
            // the plot edge paints the same pixels in both modes. (The clip is not applied with the cache off
            // - there the marks draw no state here at all, and an extra save/restore pair would change a
            // frame that must stay pixel-identical.)
            using (new CanvasSaveScope(_canvas))
            {
                _canvas.ClipRect(ctx.Plot.X, ctx.Plot.Y, ctx.Plot.Width, ctx.Plot.Height);
                RenderMarkOverlays(ctx);
            }
        }

        // Interaction overlays: crosshair (drawn last, exactly where it was before the layers were split).
        // The decoration probe is nullable ("not decided yet"); a frame that got this far has drawn the data
        // layer, so "not known" is treated as "there are no cartesian decorations to skip".
        bool skipCartesianDecorations = _skipCartesianDecorations ?? false;
        if (_mousePos.HasValue && !skipCartesianDecorations && _theme.EnableCrosshair
            && _lastPlot is { } plot)
        {
            var crosshairCtx = BuildRenderContext(plot, GetRenderData());
            RenderStageSafely("crosshair renderer", () => CrosshairRenderer?.Invoke(crosshairCtx));
        }
    }

    /// <summary>Run every mark's overlay pass, skipping the ones the compatibility check rejected.</summary>
    private void RenderMarkOverlays(MarkContext ctx)
    {
        foreach (var mark in _marks)
        {
            if (_skippedMarks is { } skipped && skipped.Contains(mark)) continue;
            var markCtx = mark.BindEncodes(
                mark.Data != null ? ctx.WithData(mark.Data) : ctx, _encodes, ctx.LayoutVersion);
            var markToRender = mark;
            RenderStageSafely($"{mark.GetType().Name}.RenderOverlay",
                () => markToRender.RenderOverlay(markCtx));
        }
    }

    /// <summary>
    /// Run one rendering stage in isolation. A failing renderer or mark used to abort the whole
    /// frame (leaving a half-drawn chart and no diagnostic); now the remaining stages still run and
    /// the failure is reported once per stage name.
    /// </summary>
    private static void RenderStageSafely(string stage, Action render)
    {
        try
        {
            render();
        }
        catch (Exception ex)
        {
            GD.PushError($"GodotChart: {stage} failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Identity of a cached legend layout: everything the layout is computed from.
    /// <para>
    /// <see cref="LegendConfig"/> is a mutable class - the same instance is normally kept and edited
    /// (an inspector edit, or <c>cfg.SwatchSize = …</c> from code) - so its reference alone cannot
    /// tell the cache that the item positions changed. The layout-relevant values are therefore part
    /// of the key, and without them the swatch size, the item spacing and the padding stayed stale
    /// along with the legend's click regions.
    /// </para>
    /// </summary>
    private readonly record struct LegendLayoutKey(
        int Version, PlotArea Plot, int Count,
        LegendPosition Position, float SwatchSize, float ItemSpacing, float Padding);

    /// <summary>
    /// Build the render context for renderer slot functions.
    /// </summary>
    private RenderContext BuildRenderContext(PlotArea plot, List<DataRow> renderData)
    {
        // One reused instance: allocating a context per frame was a steady allocation source.
        var ctx = _renderContext;
        // The tick sets belong to the plot and the axes the previous build handed out (the grid and the label
        // pass share them, see RenderContext.TickCache), so they are dropped here - once per frame, or once per
        // plot within a frame - rather than kept across a layout change.
        ctx.TickCache.Clear();
        ctx.Canvas          = _canvas;
        ctx.Plot            = plot;
        ctx.FullPlot        = _fullPlot;
        ctx.DrawnBounds     = _drawnBounds;
        ctx.Theme           = _theme;
        ctx.Scales          = _scales;
        ctx.Encodes         = _encodes;
        ctx.Data            = renderData;
        ctx.OffsetX         = OffsetX;
        ctx.OffsetY         = OffsetY;
        ctx.Width           = Width;
        ctx.Height          = Height;
        ctx.Title           = Title;
        ctx.PaddingLeft     = PaddingLeft;
        ctx.PaddingRight    = PaddingRight;
        ctx.PaddingTop      = PaddingTop;
        ctx.PaddingBottom   = PaddingBottom;
        ctx.BackgroundColor = BackgroundColor;
        ctx.GridColor       = GridColor;
        ctx.AxisColor       = AxisColor;
        ctx.XAxisConfig     = _xAxisConfig;
        ctx.YAxisConfig     = _yAxisConfig;
        ctx.Y2AxisConfig    = _y2AxisConfig;
        ctx.LegendConfig    = _legendConfig;
        ctx.FocusedSeries   = _focusedSeries;
        ctx.HiddenSeries    = _hiddenSeries;
        ctx.MousePos        = _mousePos;
        ctx.ColorScale      = _scales.TryGet(Channel.Color) as ICategoricalColorScale;
        ctx.LegendLayout = CachedLegendLayout;
        return ctx;
    }


    private MarkContext BuildMarkContext(bool stateInOverlay = false)
    {
        if (_lastPlot == null)
            throw new InvalidOperationException(
                "Render() must be called before BuildMarkContext(). No layout has been computed yet.");

        // One reused instance per frame (see _markContext).
        var ctx = _markContext;
        ctx.Canvas            = _canvas;
        ctx.Plot              = _lastPlot.Value;
        ctx.Scales            = _scales;
        ctx.Encodes           = _encodes;
        ctx.Data              = GetRenderData();
        ctx.DataVersion       = _dataVersion;
        ctx.LayoutVersion     = EffectiveLayoutVersion;
        ctx.OwnerId           = this;
        ctx.AnimationProgress = _animationContext.EntryProgress;
        ctx.Animation         = _animationContext;
        ctx.HoveredRowIndex   = _hoveredRowIndex;
        ctx.SelectedRowIndex  = _selectedRowIndex;
        ctx.StateInOverlay    = stateInOverlay;
        ctx.FocusedSeries     = _focusedSeries;
        ctx.Theme             = _theme;
        ctx.HiddenSeries      = _hiddenSeries;
        return ctx;
    }

    // ── Layer cache ──────────────────────────────────────────

    /// <summary>
    /// Everything the cached data layer is built from. Written <b>strictly</b>: a component missing from this
    /// key shows up as a picture that stopped following its input, which is the one failure the cache must
    /// never have - so a doubtful case belongs in the key (one extra frame) rather than out of it.
    /// <para>
    /// The pointer state is deliberately absent: hover and selection live on the overlay, and rebuilding the
    /// layer for them is exactly what the cache exists to avoid.
    /// </para>
    /// </summary>
    /// <param name="DataVersion">Version of the data (and of the transform pipeline).</param>
    /// <param name="LayoutVersion">
    /// <see cref="EffectiveLayoutVersion"/>: the layout version, the plot inputs, the decorations and the
    /// theme's identity folded into one number.
    /// </param>
    /// <param name="Plot">The plot rectangle the layer was laid out for.</param>
    /// <param name="ThemeIdentity">The theme instance the layer was painted with.</param>
    /// <param name="Animation">
    /// Fingerprint of the animation inputs the data layer reads (entry, exit, global opacity, data
    /// transition): an animated chart has to rebuild while it animates, and stops once it settles.
    /// <c>Animation.HoverScale</c> is not part of it - it only scales the hover visuals, which the overlay
    /// owns.
    /// </param>
    /// <param name="FocusedSeries">The focused series: it dims the other series and the legend items, in the data layer.</param>
    /// <param name="LegendPosition">Legend position the layer reserved space for.</param>
    /// <param name="LegendSwatchSize">Legend swatch size the layer was laid out with.</param>
    /// <param name="LegendItemSpacing">Legend item spacing; <see cref="LegendConfig.ItemSpacing"/> is mutable in place.</param>
    /// <param name="LegendPadding">Legend padding the layer reserved space for.</param>
    private readonly record struct LayerKey(
        int DataVersion, int LayoutVersion, PlotArea Plot, int ThemeIdentity, int Animation,
        string? FocusedSeries, LegendPosition LegendPosition, float LegendSwatchSize,
        float LegendItemSpacing, float LegendPadding);

    /// <summary>The data layer as an image of the chart's own rectangle, or null while nothing is cached.</summary>
    private IImageHandle? _layerImage;

    /// <summary>Rectangle of the surface the cached layer covers (the blit uses exactly this one).</summary>
    private Rect2I _layerRect;

    /// <summary>Inputs the cached layer was built from (see <see cref="LayerKey"/>).</summary>
    private LayerKey _layerKey;

    /// <summary>
    /// True while the cached layer cannot be used: nothing captured yet, or <see cref="InvalidateLayerCache"/>
    /// asked for a rebuild.
    /// </summary>
    private bool _layerStale = true;

    /// <summary>Set when capturing failed, so the chart stops paying for a backend that cannot do it.</summary>
    private bool _layerUnavailable;

    /// <summary>One warning per chart when the layer cache stays off (the reason is worth reading once).</summary>
    private bool _layerWarningSent;

    /// <summary>The inputs the currently rendered data layer depends on.</summary>
    private LayerKey CurrentLayerKey() => new(
        _dataVersion,
        EffectiveLayoutVersion,
        _lastPlot ?? default,
        _theme.GetHashCode(),
        HashCode.Combine(_animationContext.EntryProgress, _animationContext.ExitProgress,
                         _animationContext.GlobalOpacity, _animationContext.DataTransitionProgress),
        _focusedSeries,
        _legendConfig?.Position ?? LegendPosition.None,
        _legendConfig?.SwatchSize ?? 0f,
        _legendConfig?.ItemSpacing ?? 0f,
        _legendConfig?.Padding ?? 0f);

    /// <summary>
    /// Whether this frame keeps the data layer in an image (see <see cref="UseLayerCache"/>). Besides the
    /// switch this needs a backend that can capture and draw images, and it needs <b>every</b> mark to paint
    /// its interaction state on the overlay: a cached layer holding one frozen highlight is precisely the
    /// failure this option must not introduce, so such a chart renders single-pass instead (and says so once).
    /// </summary>
    private bool LayerCaching()
    {
        if (!_useLayerCache || _layerUnavailable) return false;

        var capabilities = _canvas.Capabilities;
        if (!capabilities.SupportsSurfaceCapture || !capabilities.SupportsImages) return false;

        foreach (var mark in _marks)
        {
            if (mark.InteractionStateInOverlay) continue;

            if (!_layerWarningSent)
            {
                _layerWarningSent = true;
                GD.PushWarning(
                    $"GodotChart: UseLayerCache is on, but {mark.GetType().Name} paints its interaction " +
                    "state (hover/selection) in the data layer; a cached layer would freeze it. The chart " +
                    "renders without the cache - see Mark.InteractionStateInOverlay.");
            }
            return false;
        }

        return true;
    }

    /// <summary>
    /// The chart's own rectangle inside the surface, rounded <b>outwards</b> to whole pixels: the cached
    /// layer has to keep the anti-aliased edge of a chart drawn on a fractional offset, and the blit puts it
    /// back at exactly the same pixels.
    /// </summary>
    private Rect2I LayerRectangle()
    {
        int left = (int)MathF.Floor(OffsetX);
        int top = (int)MathF.Floor(OffsetY);
        int right = (int)MathF.Ceiling(OffsetX + Width);
        int bottom = (int)MathF.Ceiling(OffsetY + Height);
        return new Rect2I(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    /// <summary>
    /// Present the cached data layer and draw the overlay on top of it. Returns false when there is nothing
    /// to present (no capture yet, or the layer was invalidated), which sends the caller through the full
    /// two-layer render.
    /// </summary>
    private bool TryPresentCachedLayer()
    {
        if (_layerStale || _layerImage is null || _lastPlot is null) return false;
        if (_layerKey != CurrentLayerKey()) return false;

        _canvas.DrawImage(_layerImage, _layerRect.Position.X, _layerRect.Position.Y,
            _layerRect.Size.X, _layerRect.Size.Y);

        // The blit rewrote that whole rectangle: reporting it is exact (and what a backend that uploads only
        // the reported region needs in order to stay correct).
        _canvas.InvalidateRegion(_layerRect.Position.X, _layerRect.Position.Y,
            _layerRect.Size.X, _layerRect.Size.Y);

        RenderOverlayLayer(stateInOverlay: true);
        return true;
    }

    /// <summary>
    /// Keep the data layer that was just drawn as this chart's cached layer. A capture that comes back in
    /// another size than requested (the surface clipped it) or fails outright turns the cache off for this
    /// chart: a partial layer blitted over the whole chart would be visible wrongness, and retrying a broken
    /// backend every frame would be a cost without a benefit. <see cref="InvalidateLayerCache"/> re-arms it.
    /// </summary>
    private void CaptureDataLayer()
    {
        var rect = LayerRectangle();
        IImageHandle? captured;
        try
        {
            captured = _canvas.CaptureRegion(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y);
        }
        catch (Exception ex)
        {
            WarnLayerCaptureFailed(ex.Message);
            return;
        }

        if (captured.Width != rect.Size.X || captured.Height != rect.Size.Y)
        {
            captured.Dispose();
            WarnLayerCaptureFailed(
                $"the backend captured {captured.Width}x{captured.Height} px instead of " +
                $"{rect.Size.X}x{rect.Size.Y}");
            return;
        }

        _layerImage?.Dispose();
        _layerImage = captured;
        _layerRect = rect;
        _layerKey = CurrentLayerKey();
        _layerStale = false;
    }

    /// <summary>Report a capture that did not work, once per chart, and stop trying.</summary>
    private void WarnLayerCaptureFailed(string reason)
    {
        _layerUnavailable = true;
        if (_layerWarningSent) return;

        _layerWarningSent = true;
        GD.PushWarning(
            $"GodotChart: the data layer could not be kept in an image ({reason}); the chart renders " +
            "single-pass. Check CanvasCapabilities.SupportsSurfaceCapture and the chart rectangle " +
            "(it has to lie inside the canvas surface).");
    }

    /// <summary>Drop the cached layer image (switch off, invalidation or disposal).</summary>
    private void ReleaseLayer()
    {
        _layerImage?.Dispose();
        _layerImage = null;
        _layerStale = true;
    }

    // ── Mark compatibility validation ────────────────────────

    /// <summary>
    /// Validate that all marks in the chart use compatible coordinate systems.
    /// Returns a set of marks that should be skipped during rendering.
    /// </summary>
    private HashSet<Mark> ValidateMarkCompatibility()
    {
        if (_skippedMarks != null && _skippedMarksLayoutVersion == _layoutVersion)
            return _skippedMarks;

        _skippedMarks = [];
        _skippedMarksLayoutVersion = _layoutVersion;

        if (_marks.Count <= 1) return _skippedMarks;

        var primary = _marks[0].Coordinate;
        for (int i = 1; i < _marks.Count; i++)
        {
            var coord = _marks[i].Coordinate;
            if (coord != primary)
            {
                GD.PushWarning(
                    $"[GodotChart] Incompatible mark combination: " +
                    $"{_marks[0].GetType().Name} ({primary}) and " +
                    $"{_marks[i].GetType().Name} ({coord}) cannot coexist. " +
                    $"The mark will be skipped.");
                _skippedMarks.Add(_marks[i]);
            }
        }

        return _skippedMarks;
    }
}
