using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// Real multi-series data page: the World Bank's annual population growth (<c>SP.POP.GROW</c>) for 24 major
/// countries, 1970-2023 — 1296 rows loaded from the CSV the page ships, one series per country.
/// <para>
/// It exists because the other pages answer "which mark does what" and "how expensive is a redraw", but not
/// "does the library stay usable on a chart a reader actually asks for": a wide data table, one colour per
/// series, a legend long enough to need filtering, and a time axis the reader wants to zoom into. The chart
/// itself is a plain <see cref="ChartView"/> node wired in the scene; this script only loads the CSV and
/// reports what came out of it.
/// </para>
/// <para>
/// The file carries the attribution in its comment lines (the parser skips them), and the country names are
/// comma-free on purpose: the inline CSV parser splits on <c>,</c>.
/// </para>
/// </summary>
public partial class ChartBigDataDemo : Control
{
    /// <summary>The chart the CSV is loaded into (a <see cref="ChartView"/> node from the scene).</summary>
    [Export] public ChartView Chart { get; set; } = null!;

    /// <summary>What was loaded and what the reader can do with it.</summary>
    [Export] public RichTextLabel Readout { get; set; } = null!;

    /// <summary>Data file shipped next to the page; comment lines carry the source and the licence.</summary>
    private const string CsvPath = "res://Example/GodotChart/Assets/worldbank_population_growth.csv";

    private double _sinceReadout;

    /// <inheritdoc />
    public override void _Ready()
    {
        if (!FileAccess.FileExists(CsvPath))
        {
            Readout.Text = $"missing {CsvPath}";
            return;
        }

        string csv = FileAccess.GetFileAsString(CsvPath);
        Chart.SetCsv(csv);
        RefreshReadout();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        // The frame rate is the one number that changes while the reader zooms and pans; refresh it a few
        // times a second instead of every frame.
        _sinceReadout += delta;
        if (_sinceReadout < 0.5) return;

        _sinceReadout = 0;
        RefreshReadout();
    }

    private void RefreshReadout()
    {
        var rows = Chart.DataRows;
        var countries = new HashSet<string>();
        var years = new List<double>();

        foreach (var row in rows)
        {
            if (row.TryGet<string>("country", out var country)) countries.Add(country);
            if (row.TryGet("year", out double year)) years.Add(year);
        }

        years.Sort();

        Readout.Text =
            $"[b]{rows.Count:N0} rows[/b] · {countries.Count} countries · " +
            (years.Count > 0 ? $"years {years[0]:F0}-{years[^1]:F0}" : "no years") +
            $" · {Engine.GetFramesPerSecond():F0} fps · one series and one colour per country\n" +
            "[color=#93a1b5]wheel = zoom X at the pointer · drag = pan · double click = reset · " +
            "click a legend entry = hide/show that country[/color]";
    }
}
