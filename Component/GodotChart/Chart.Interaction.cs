using System;
using System.Collections.Generic;
using Godot;

namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Chart partial — interaction state, events, hit testing, and hover notification.
/// </summary>
public partial class Chart
{
    // ── Events ────────────────────────────────────────────────

    /// <summary>
    /// Fired when the user clicks on a chart element.
    /// </summary>
    public event EventHandler<ChartClickEventArgs>? OnClick;

    /// <summary>Fired when the hovered element changes.</summary>
    public event EventHandler<ChartHoverEventArgs>? OnHover;

    /// <summary>Fired when the focused series changes.</summary>
    public event EventHandler<ChartFocusEventArgs>? OnFocusChanged;

    /// <summary>Fired when the selected element changes.</summary>
    public event EventHandler<ChartSelectionEventArgs>? OnSelectionChanged;

    /// <summary>
    /// Fired when a legend item is clicked.
    /// Set Handled = true to prevent the default focus toggle.
    /// </summary>
    public event EventHandler<ChartLegendClickEventArgs>? OnLegendClick;

    // ── Interaction ───────────────────────────────────────────

    /// <summary>
    /// Set mouse position for hit testing and tooltip/crosshair overlays.
    /// </summary>
    public Chart Interaction(Vector2? mousePos)
    { _mousePos = mousePos; return this; }

    /// <summary>
    /// Set the focused series key. Non-focused series will render at reduced opacity.
    /// Pass null to clear focus.
    /// </summary>
    public Chart FocusSeries(string? seriesKey)
    {
        var prev = _focusedSeries;
        _focusedSeries = seriesKey;
        if (prev != seriesKey)
            OnFocusChanged?.Invoke(this, new ChartFocusEventArgs
            {
                PreviousSeriesKey = prev,
                SeriesKey = seriesKey,
            });
        return this;
    }

    /// <summary>
    /// Set the selected element by row index. Pass -1 to clear selection.
    /// </summary>
    public Chart Select(int rowIndex)
    {
        var prev = _selectedRowIndex;
        _selectedRowIndex = rowIndex;
        if (prev != rowIndex)
            OnSelectionChanged?.Invoke(this, new ChartSelectionEventArgs
            {
                PreviousRowIndex = prev,
                RowIndex = rowIndex,
                Row = rowIndex >= 0 && rowIndex < _data.Count ? _data[rowIndex] : null,
            });
        return this;
    }

    /// <summary>
    /// Set the hovered element by row index. Pass -1 to clear hover.
    /// </summary>
    public Chart Hover(int rowIndex)
    { _hoveredRowIndex = rowIndex; return this; }

    // ── Hover notification ────────────────────────────────────

    /// <summary>
    /// Notify the chart that the hovered element has changed (from external HitTest).
    /// Fires the OnHover event if the row index actually changed.
    /// </summary>
    public void NotifyHoverChanged(int newRowIndex, HitResult? hit)
    {
        int prev = _hoveredRowIndex;
        _hoveredRowIndex = newRowIndex;
        if (prev != newRowIndex)
        {
            OnHover?.Invoke(this, new ChartHoverEventArgs
            {
                PreviousRowIndex = prev,
                RowIndex = newRowIndex,
                Row = hit?.Row,
                SeriesKey = hit?.SeriesKey,
                MarkType = hit?.MarkType,
                ScreenPosition = hit != null
                    ? new Vector2(hit.ScreenX, hit.ScreenY) : Vector2.Zero,
            });
        }
    }

    // ── Hit test (public API) ─────────────────────────────────

    /// <summary>
    /// Run hit test against all marks at the given mouse position.
    /// Returns the first hit result, or null if nothing was hit.
    /// Must be called after Render() (needs computed layout).
    /// </summary>
    public HitResult? HitTest(Vector2 mousePos)
    {
        if (_lastPlot == null) return null;
        var ctx = BuildMarkContext();
        // 1. Test marks first (data points have priority)
        var markHit = ChartInteraction.TestAll(_marks, ctx, mousePos);
        if (markHit != null) return markHit;
        // 2. Test legend items
        var legendHit = TestLegendHit(mousePos);
        if (legendHit != null) return legendHit;
        // 3. Test axis zones
        return TestAxisHit(mousePos);
    }

    /// <summary>
    /// Process a click at the given mouse position: updates selection and fires OnClick.
    /// Returns the hit result, or null.
    /// </summary>
    public HitResult? HandleClick(Vector2 mousePos)
    {
        var hit = HitTest(mousePos);
        if (hit is { Hit: true })
        {
            // Legend click toggles focused series
            if (hit.MarkType == "Legend")
            {
                // Fire legend click event — allow external code to intercept
                var legendArgs = new ChartLegendClickEventArgs
                {
                    SeriesKey = hit.SeriesKey ?? "",
                    CurrentFocusedSeries = _focusedSeries,
                };
                OnLegendClick?.Invoke(this, legendArgs);

                if (!legendArgs.Handled)
                {
                    var prevFocus = _focusedSeries;
                    _focusedSeries = _focusedSeries == hit.SeriesKey ? null : hit.SeriesKey;
                    if (prevFocus != _focusedSeries)
                        OnFocusChanged?.Invoke(this, new ChartFocusEventArgs
                        {
                            PreviousSeriesKey = prevFocus,
                            SeriesKey = _focusedSeries,
                        });
                }
                return hit;
            }
            // Non-legend click clears focused series
            var prevFocused = _focusedSeries;
            _focusedSeries = null;
            if (prevFocused != null)
                OnFocusChanged?.Invoke(this, new ChartFocusEventArgs
                {
                    PreviousSeriesKey = prevFocused,
                    SeriesKey = null,
                });

            var prevSelected = _selectedRowIndex;
            _selectedRowIndex = hit.RowIndex;
            if (prevSelected != hit.RowIndex)
                OnSelectionChanged?.Invoke(this, new ChartSelectionEventArgs
                {
                    PreviousRowIndex = prevSelected,
                    RowIndex = hit.RowIndex,
                    Row = hit.Row,
                    SeriesKey = hit.SeriesKey,
                });

            OnClick?.Invoke(this, new ChartClickEventArgs
            {
                Row = hit.Row,
                RowIndex = hit.RowIndex,
                MarkType = hit.MarkType,
                ScreenPosition = new Vector2(hit.ScreenX, hit.ScreenY),
                SeriesKey = hit.SeriesKey,
            });
        }
        else
        {
            var prevFocused = _focusedSeries;
            _focusedSeries = null;
            if (prevFocused != null)
                OnFocusChanged?.Invoke(this, new ChartFocusEventArgs
                {
                    PreviousSeriesKey = prevFocused,
                    SeriesKey = null,
                });

            var prevSelected = _selectedRowIndex;
            _selectedRowIndex = -1;
            if (prevSelected != -1)
                OnSelectionChanged?.Invoke(this, new ChartSelectionEventArgs
                {
                    PreviousRowIndex = prevSelected,
                    RowIndex = -1,
                });
        }
        return hit;
    }

    /// <summary>
    /// Test if the mouse is hovering over a legend item.
    /// Uses cached legend layout from the last Render() to avoid redundant computation.
    /// Returns a HitResult with MarkType "Legend" and SeriesKey set to the item label.
    /// </summary>
    private HitResult? TestLegendHit(Vector2 mousePos)
    {
        var items = _cachedLegendItems;
        if (items == null) return null;

        foreach (var item in items)
        {
            if (mousePos.X >= item.X && mousePos.X <= item.X + item.Width &&
                mousePos.Y >= item.Y && mousePos.Y <= item.Y + item.Height)
            {
                return new HitResult
                {
                    Hit = true,
                    RowIndex = -1,
                    ScreenX = item.X + item.Width * 0.5f,
                    ScreenY = item.Y + item.Height * 0.5f,
                    Label = item.Key,
                    MarkType = "Legend",
                    SeriesKey = item.Key,
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Test if the mouse is hovering over the X or Y axis label area.
    /// Returns a HitResult with MarkType "XAxis" or "YAxis", or null.
    /// </summary>
    private HitResult? TestAxisHit(Vector2 mousePos)
    {
        if (_lastPlot == null) return null;
        var plot = _lastPlot.Value;

        // X axis zone: below the plot area
        if (_xAxisConfig != null &&
            mousePos.X >= plot.X && mousePos.X <= plot.X + plot.Width &&
            mousePos.Y > plot.Y + plot.Height && mousePos.Y <= plot.Y + plot.Height + PaddingBottom)
        {
            string label = BuildAxisLabel(_xAxisConfig);
            if (label.Length > 0)
            {
                return new HitResult
                {
                    Hit = true,
                    ScreenX = mousePos.X,
                    ScreenY = plot.Y + plot.Height + PaddingBottom * 0.5f,
                    Label = label,
                    MarkType = "XAxis",
                };
            }
        }

        // Y axis zone: left of the plot area
        if (_yAxisConfig != null &&
            mousePos.X >= plot.X - PaddingLeft && mousePos.X < plot.X &&
            mousePos.Y >= plot.Y && mousePos.Y <= plot.Y + plot.Height)
        {
            string label = BuildAxisLabel(_yAxisConfig);
            if (label.Length > 0)
            {
                return new HitResult
                {
                    Hit = true,
                    ScreenX = plot.X - PaddingLeft * 0.5f,
                    ScreenY = mousePos.Y,
                    Label = label,
                    MarkType = "YAxis",
                };
            }
        }

        return null;
    }

    private static string BuildAxisLabel(AxisConfig config)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrEmpty(config.Title))
            parts.Add(config.Title);
        if (!string.IsNullOrEmpty(config.Unit))
            parts.Add($"({config.Unit})");
        if (!string.IsNullOrEmpty(config.Description))
            parts.Add(config.Description);
        return string.Join("\n", parts);
    }
}
