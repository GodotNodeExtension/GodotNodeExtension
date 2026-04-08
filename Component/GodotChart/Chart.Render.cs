using System;
using System.Collections.Generic;
using GodotNodeExtension.Component.GodotChart.Canvas;

namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Chart partial — rendering pipeline, context builders, and mark compatibility validation.
/// </summary>
public partial class Chart
{
    // ── Render ────────────────────────────────────────────────
    public void Render()
    {
        var renderData = GetRenderData();

        AutoFitScales();

        // Let marks contribute custom scale ranges (e.g., CandlestickMark Y-axis)
        var skipped = ValidateMarkCompatibility();
        foreach (var mark in _marks)
        {
            if (skipped.Contains(mark)) continue;
            mark.ContributeScales(_scales, _encodes, mark.Data ?? renderData);
        }

        // Extra padding for axis titles: text line-height + shared margin,
        // computed from font metrics so the visual gap is font-size independent.
        float titleLineH = FontSettings.Default.Size * FontSettings.Default.LineHeightMultiplier;
        // Left extra pad: only the title width; AxisTitleMargin is absorbed
        // by PaddingLeft so the total left margin doesn't grow excessively.
        float extraLeftPad = _yAxisConfig?.Title != null ? titleLineH : 0f;
        float extraBottomPad = _xAxisConfig?.Title != null ? titleLineH + AxisTitleMargin : 0f;
        // Reserve right padding when Y2 axis is present:
        // 35px for Y2 labels + optional title space that mirrors the left side.
        bool hasY2 = _scales.Has(Channel.Y2);
        float extraRightPad = hasY2
            ? Y2LabelReservedWidth + (_y2AxisConfig?.Title != null ? titleLineH + AxisTitleMargin : 0f)
            : 0f;
        // Reserve top/bottom space for the legend when positioned above or below the plot.
        float legendHeight = 0f;
        if (_legendConfig is { Position: LegendPosition.Top or LegendPosition.Bottom })
            legendHeight = MathF.Max(_legendConfig.SwatchSize, titleLineH) + _legendConfig.Padding * 2f;
        float extraTopPad = _legendConfig?.Position == LegendPosition.Top ? legendHeight : 0f;
        float extraLegendBottom = _legendConfig?.Position == LegendPosition.Bottom ? legendHeight : 0f;
        var plot = new PlotArea(
            OffsetX + PaddingLeft + extraLeftPad,
            OffsetY + PaddingTop + (Title != null ? TitleReservedHeight : 0f) + extraTopPad,
            Width  - PaddingLeft - PaddingRight - extraLeftPad - extraRightPad,
            Height - PaddingTop  - PaddingBottom - (Title != null ? TitleReservedHeight : 0f) - extraBottomPad - extraTopPad - extraLegendBottom);
        _lastPlot = plot;

        // Skip Cartesian grid/axes when no Cartesian marks exist
        _skipCartesianDecorations ??= _marks.Count > 0
            && _marks.TrueForAll(m => m.Coordinate != MarkCoordinate.Cartesian);

        // Legend — compute layout once and cache for interaction layer + renderer
        if (_legendConfig is { Position: not LegendPosition.None })
        {
            _cachedLegendItems = _scales.TryGet(Channel.Color) is ColorScale { Domain.Count: > 0 } colorScale
                ? LegendLayoutHelper.Compute(
                    _lastPlot!.Value, _legendConfig, colorScale, OffsetX, Width, _canvas, _theme)
                : null;
        }
        else
        {
            _cachedLegendItems = null;
        }

        // Build render context for renderer slots
        var renderCtx = BuildRenderContext(plot, renderData);

        BackgroundRenderer?.Invoke(renderCtx);

        if (Title != null)
            TitleRenderer?.Invoke(renderCtx);

        if (!_skipCartesianDecorations.Value)
        {
            GridRenderer?.Invoke(renderCtx);
            AxisRenderer?.Invoke(renderCtx);
        }

        var ctx = BuildMarkContext();

        foreach (var mark in _marks)
        {
            if (skipped.Contains(mark)) continue;
            var markCtx = mark.Data != null ? ctx.WithData(mark.Data) : ctx;
            mark.Render(markCtx);
        }

        if (!_skipCartesianDecorations.Value)
            AxisLabelRenderer?.Invoke(renderCtx);

        // Legend
        if (_cachedLegendItems != null)
            LegendRenderer?.Invoke(renderCtx);

        // Interaction overlays: crosshair
        if (_mousePos.HasValue && !_skipCartesianDecorations.Value && _theme.EnableCrosshair)
            CrosshairRenderer?.Invoke(renderCtx);
    }

    /// <summary>
    /// Build the render context for renderer slot functions.
    /// </summary>
    private RenderContext BuildRenderContext(PlotArea plot, List<DataRow> renderData)
    {
        return new RenderContext
        {
            Canvas          = _canvas,
            Plot            = plot,
            Theme           = _theme,
            Scales          = _scales,
            Encodes         = _encodes,
            Data            = renderData,
            OffsetX         = OffsetX,
            OffsetY         = OffsetY,
            Width           = Width,
            Height          = Height,
            Title           = Title,
            PaddingLeft     = PaddingLeft,
            PaddingRight    = PaddingRight,
            PaddingTop      = PaddingTop,
            PaddingBottom   = PaddingBottom,
            BackgroundColor = BackgroundColor,
            GridColor       = GridColor,
            AxisColor       = AxisColor,
            XAxisConfig     = _xAxisConfig,
            YAxisConfig     = _yAxisConfig,
            Y2AxisConfig    = _y2AxisConfig,
            LegendConfig    = _legendConfig,
            FocusedSeries   = _focusedSeries,
            HiddenSeries    = _hiddenSeries,
            MousePos        = _mousePos,
            SkipCartesianDecorations = _skipCartesianDecorations ?? false,
            ColorScale      = _scales.TryGet(Channel.Color) as ColorScale,
            CachedLegendItems = _cachedLegendItems,
        };
    }

    private MarkContext BuildMarkContext()
    {
        if (_lastPlot == null)
            throw new InvalidOperationException(
                "Render() must be called before BuildMarkContext(). No layout has been computed yet.");

        return new MarkContext
        {
            Canvas           = _canvas,
            Plot             = _lastPlot.Value,
            Scales           = _scales,
            Encodes          = _encodes,
            Data             = GetRenderData(),
            DataVersion      = _dataVersion,
            LayoutVersion    = _layoutVersion,
            AnimationProgress = _animationContext.EntryProgress < 1f
                ? _animationContext.EntryProgress
                : _animationProgress,
            Animation        = _animationContext,
            HoveredRowIndex  = _hoveredRowIndex,
            SelectedRowIndex = _selectedRowIndex,
            FocusedSeries    = _focusedSeries,
            Theme            = _theme,
            HiddenSeries     = _hiddenSeries,
        };
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

        _skippedMarks = new HashSet<Mark>();
        _skippedMarksLayoutVersion = _layoutVersion;

        if (_marks.Count <= 1) return _skippedMarks;

        var primary = _marks[0].Coordinate;
        for (int i = 1; i < _marks.Count; i++)
        {
            var coord = _marks[i].Coordinate;
            if (coord != primary)
            {
                Godot.GD.PushWarning(
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
