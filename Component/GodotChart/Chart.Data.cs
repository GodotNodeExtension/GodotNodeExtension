using System.Collections.Generic;

namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Chart partial — data management, transforms, and series visibility.
/// </summary>
public partial class Chart
{
    // ── Data ──────────────────────────────────────────────────
    public Chart Data(IEnumerable<DataRow> rows)
    {
        _data.Clear();
        _data.AddRange(rows);
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
        _data.AddRange(rows);
        TrimToWindow();
        InvalidateData();
        return this;
    }

    private void InvalidateData()
    {
        _dataVersion++;
        _layoutVersion++;
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
    /// Add a data transform to the pipeline. Transforms are applied in order
    /// before mark rendering (e.g. <see cref="BinTransform"/> for histograms).
    /// </summary>
    public Chart Transform(IDataTransform transform)
    {
        _transforms.Add(transform);
        _layoutVersion++;
        return this;
    }

    // ── Series visibility ─────────────────────────────────────

    /// <summary>Hide a series entirely from rendering and hit testing.</summary>
    public Chart HideSeries(string seriesKey)
    {
        _hiddenSeries ??= new HashSet<string>();
        _hiddenSeries.Add(seriesKey);
        return this;
    }

    /// <summary>Show a previously hidden series.</summary>
    public Chart ShowSeries(string seriesKey)
    {
        _hiddenSeries?.Remove(seriesKey);
        return this;
    }

    /// <summary>Toggle series visibility.</summary>
    public Chart ToggleSeriesVisibility(string seriesKey)
    {
        _hiddenSeries ??= new HashSet<string>();
        if (!_hiddenSeries.Remove(seriesKey))
            _hiddenSeries.Add(seriesKey);
        return this;
    }

    /// <summary>Check if a series is hidden.</summary>
    public bool IsSeriesHidden(string seriesKey)
        => _hiddenSeries?.Contains(seriesKey) ?? false;

    /// <summary>Show all series.</summary>
    public Chart ShowAllSeries()
    {
        _hiddenSeries?.Clear();
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
