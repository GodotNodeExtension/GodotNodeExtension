using System;
using System.Collections.Generic;

namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Chart partial — data management, transforms, and series visibility.
/// </summary>
public partial class Chart
{
    // ── Data ──────────────────────────────────────────────────
    /// <summary>
    /// Replace the whole data set. Invalidates the data and layout caches.
    /// Like <see cref="AppendData(DataRow)"/>, the result is trimmed to <see cref="WindowSize"/>
    /// when a window is configured, so "keep the newest N rows" holds whichever way rows arrive.
    /// </summary>
    public Chart Data(IEnumerable<DataRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        // Handing the chart its own list back would make the clear below empty the source as well, so that
        // case is a no-op rather than a wipe.
        if (ReferenceEquals(rows, _data)) return this;

        // A List is copied by AddRange below, so it can be cleared first. Anything else (a LINQ query, a
        // lazily built sequence, any view over a list) is materialised *before* the clear: a view over the
        // list being replaced would be enumerated empty and silently wipe the chart - which is exactly what
        // `chart.Data(chart.GetRenderDataSnapshot())` used to do, because the "snapshot" was a view.
        var replacement = rows as List<DataRow> ?? [.. rows];
        _data.Clear();
        _data.AddRange(replacement);
        TrimToWindow();
        InvalidateData();
        return this;
    }

    /// <summary>
    /// Append a single data row. Auto-trims oldest rows if <see cref="WindowSize"/> is set.
    /// Efficient for real-time streaming charts.
    /// </summary>
    public Chart AppendData(DataRow row)
    {
        _data.Add(row);
        TrimToWindow();
        InvalidateData();
        return this;
    }

    /// <summary>
    /// Append multiple data rows at once. Auto-trims oldest rows if <see cref="WindowSize"/> is set.
    /// </summary>
    public Chart AppendData(IEnumerable<DataRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        _data.AddRange(rows);
        TrimToWindow();
        InvalidateData();
        return this;
    }

    private void InvalidateData()
    {
        _dataVersion++;
        _layoutVersion++;
        // A row that no longer exists cannot stay hovered or selected: after Clear() the chart kept
        // reporting row 5, so fresh data made an element look selected out of nowhere. Streaming keeps
        // its selection, because the indices stay inside the new range.
        if (_selectedRowIndex >= _data.Count) _selectedRowIndex = -1;
        if (_hoveredRowIndex >= _data.Count) _hoveredRowIndex = -1;
        foreach (var ch in _autoFittedChannels)
            _scales.Remove(ch);
        _autoFittedChannels.Clear();
    }

    private void TrimToWindow()
    {
        if (WindowSize > 0 && _data.Count > WindowSize)
            _data.RemoveRange(0, _data.Count - WindowSize);
    }

    // ── Transform ─────────────────────────────────────────────

    /// <summary>
    /// Add a data transform to the pipeline. Transforms are applied in order, before mark rendering
    /// (e.g. <see cref="BinTransform"/> for histograms), and the result is cached until the data or
    /// the transform list changes.
    /// </summary>
    public Chart Transform(IDataTransform transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        _transforms.Add(transform);
        // The cached transform result belongs to the previous pipeline. Without invalidating it a
        // newly added transform would be silently ignored until the data set changes again.
        _transformedData = null;
        _transformedDataVersion = -1;
        InvalidateLayout();
        return this;
    }

    // ── Series visibility ─────────────────────────────────────

    /// <summary>Hide a series entirely from rendering and hit testing.</summary>
    public Chart HideSeries(string seriesKey)
    {
        _hiddenSeries ??= [];
        _hiddenSeries.Add(seriesKey);
        InvalidateLayout(); // cached mark layouts still contain the hidden series
        return this;
    }

    /// <summary>Show a previously hidden series.</summary>
    public Chart ShowSeries(string seriesKey)
    {
        _hiddenSeries?.Remove(seriesKey);
        InvalidateLayout();
        return this;
    }

    /// <summary>Toggle series visibility.</summary>
    public Chart ToggleSeriesVisibility(string seriesKey)
    {
        _hiddenSeries ??= [];
        if (!_hiddenSeries.Remove(seriesKey))
            _hiddenSeries.Add(seriesKey);
        InvalidateLayout();
        return this;
    }

    /// <summary>Check if a series is hidden.</summary>
    public bool IsSeriesHidden(string seriesKey)
        => _hiddenSeries?.Contains(seriesKey) ?? false;

    /// <summary>Show all series.</summary>
    public Chart ShowAllSeries()
    {
        _hiddenSeries?.Clear();
        InvalidateLayout();
        return this;
    }

    /// <summary>
    /// Get effective render data after applying transform pipeline.
    /// Result is cached until data version changes.
    /// </summary>
    private List<DataRow> GetRenderData()
    {
        if (_transforms.Count == 0) return _data;
        if (_transformedData != null && _transformedDataVersion == _dataVersion)
            return _transformedData;

        _transformedData = _data;
        foreach (var t in _transforms)
            _transformedData = t.Apply(_transformedData);
        _transformedDataVersion = _dataVersion;
        return _transformedData;
    }
}
