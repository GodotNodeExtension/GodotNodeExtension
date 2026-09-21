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
    /// <para>
    /// The index is clamped to the rows that are actually rendered: an out-of-range one would otherwise be
    /// reported by <see cref="CurrentSelectedRowIndex"/> while nothing is drawn as selected, until some
    /// later data change happened to reset it. With no rendered rows the result is "nothing selected".
    /// </para>
    /// </summary>
    public Chart Select(int rowIndex)
    {
        int count = GetRenderData().Count;
        int clamped = rowIndex < 0 ? -1 : Math.Min(rowIndex, count - 1);
        var prev = _selectedRowIndex;
        _selectedRowIndex = clamped;
        if (prev != clamped)
            OnSelectionChanged?.Invoke(this, new ChartSelectionEventArgs
            {
                PreviousRowIndex = prev,
                RowIndex = clamped,
                // Selection refers to what is rendered, which is the transformed data.
                Row = clamped >= 0 ? GetRenderData()[clamped] : null,
            });
        return this;
    }

    /// <summary>
    /// Set the hovered element by row index. Pass -1 to clear hover.
    /// <para>
    /// This stores the row <b>silently</b>: no <see cref="OnHover"/> event is raised. A host that received a
    /// hit from its own input handling should call <see cref="NotifyHoverChanged"/> instead - it stores the
    /// row and fires the event when the row actually changed.
    /// </para>
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
        // 1. Test marks first (top-most first; incompatible marks are skipped like in Render)
        var markHit = ChartInteraction.TestAll(_marks, ctx, mousePos, _skippedMarks);
        if (markHit != null) return markHit;
        // 2. Test legend items
        var legendHit = TestLegendHit(mousePos);
        if (legendHit != null) return legendHit;
        // 3. Test axis zones
        return TestAxisHit(mousePos);
    }

    /// <summary>
    /// Process a click at the given mouse position: raises <see cref="OnClick"/> (carrying the button that
    /// produced it) for a click on a data element or an axis, and, for the left button only, updates the
    /// selection and the focused series. Other buttons are reported without changing chart state, so a host
    /// can use them for its own gestures.
    /// <para>
    /// A click on a <b>legend</b> item is the one exception: it raises <see cref="OnLegendClick"/> instead
    /// (carrying <see cref="ChartLegendClickEventArgs.Button"/>), because a legend item stands for a series
    /// rather than for a data row - firing <see cref="OnClick"/> with <c>RowIndex = -1</c> would make a host
    /// that counts clicks per row count legend clicks as well.
    /// </para>
    /// Returns the hit result, or null.
    /// </summary>
    public HitResult? HandleClick(Vector2 mousePos, MouseButton button = MouseButton.Left)
    {
        var hit = HitTest(mousePos);
        // The primary button is the one that drives the built-in selection / focus behaviour.
        bool primary = button == MouseButton.Left;
        if (hit is not null)
        {
            // Legend click toggles focused series
            if (hit.MarkType == "Legend")
            {
                // Fire legend click event — allow external code to intercept
                var legendArgs = new ChartLegendClickEventArgs
                {
                    SeriesKey = hit.SeriesKey ?? "",
                    CurrentFocusedSeries = _focusedSeries,
                    Button = button,
                };
                OnLegendClick?.Invoke(this, legendArgs);

                if (!legendArgs.Handled && primary)
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
                // Report the focus change so callers of HandleClick can react without the event.
                hit.FocusedSeries = _focusedSeries;
                return hit;
            }
            // Non-legend click on the primary button clears the focused series and selects the row; other
            // buttons are reported through OnClick without touching that state.
            if (primary)
            {
                ClearFocus();
                SetSelection(hit.RowIndex, hit.Row, hit.SeriesKey);
            }

            hit.FocusedSeries = _focusedSeries;
            OnClick?.Invoke(this, new ChartClickEventArgs
            {
                Row = hit.Row,
                RowIndex = hit.RowIndex,
                MarkType = hit.MarkType,
                Button = button,
                ScreenPosition = new Vector2(hit.ScreenX, hit.ScreenY),
                SeriesKey = hit.SeriesKey,
            });
        }
        else if (primary)
        {
            // A click on empty space clears the state - but only for the primary button: a stray
            // right-click somewhere on the surface should not drop the host's selection.
            ClearFocus();
            SetSelection(-1, null, null);
        }
        return hit;
    }

    /// <summary>
    /// Test if the mouse is hovering over a legend item.
    /// Uses cached legend layout from the last Render() to avoid redundant computation.
    /// Returns a HitResult with MarkType "Legend" and SeriesKey set to the item label.
    /// </summary>
    /// <summary>Drop the focused series, reporting the change when there was one.</summary>
    private void ClearFocus()
    {
        var previous = _focusedSeries;
        _focusedSeries = null;
        if (previous != null)
            OnFocusChanged?.Invoke(this, new ChartFocusEventArgs
            {
                PreviousSeriesKey = previous,
                SeriesKey = null,
            });
    }

    /// <summary>
    /// Move the selection to <paramref name="index"/> (a negative index clears it), reporting the change when
    /// it moved and carrying the row / series key of the hit that asked for it.
    /// </summary>
    private void SetSelection(int index, DataRow? row, string? seriesKey)
    {
        var previous = _selectedRowIndex;
        _selectedRowIndex = index;
        if (previous == index) return;

        OnSelectionChanged?.Invoke(this, new ChartSelectionEventArgs
        {
            PreviousRowIndex = previous,
            RowIndex = index,
            Row = row,
            SeriesKey = seriesKey,
        });
    }

    private HitResult? TestLegendHit(Vector2 mousePos)
    {
        var layout = CachedLegendLayout;
        if (layout == null) return null;

        foreach (var item in layout.Value.Items)
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
