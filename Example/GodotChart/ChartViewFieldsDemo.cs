using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// Every <see cref="ChartView"/> knob, set from code. The scene holds the layout and the nodes
/// (wired through <c>[Export]</c> + <c>node_paths</c>); this script does the configuring, so you can
/// read one file and see the whole node API:
/// <list type="bullet">
/// <item>the six data channels (<see cref="ChartView.XField"/> ... <see cref="ChartView.ShapeField"/>)
/// plus their ranges (<see cref="ChartView.SizeRange"/>, <see cref="ChartView.OpacityRange"/>,
/// <see cref="ChartView.ShapeSymbols"/>) on one bubble chart;</item>
/// <item>four of the five <see cref="ColorMappingKind"/> values (every one but <c>Auto</c>);</item>
/// <item>pinned axis domains (<see cref="ChartView.XAxisRange"/> / <see cref="ChartView.YAxisRange"/>),
/// the built-in palettes (<see cref="ChartView.ThemeKind"/>) and a theme <b>resource</b> built in code
/// and assigned to <see cref="ChartView.CustomTheme"/>;</item>
/// <item>axes, units and the legend (<see cref="ChartView.XAxisUnit"/>, <see cref="ChartView.Legend"/>,
/// <see cref="ChartView.ShowTooltip"/>, <see cref="ChartView.ShowCrosshair"/>);</item>
/// <item>every data entry point: <see cref="ChartView.SetValues"/>,
/// <see cref="ChartView.SetData(DataRow[])"/>, <see cref="ChartView.SetCsv"/> /
/// <see cref="ChartView.ParseCsv"/>, <see cref="ChartView.AddRow"/> with
/// <see cref="ChartView.WindowSize"/>, <see cref="ChartView.Clear"/> and
/// <see cref="ChartView.DataRows"/>;</item>
/// <item>the two refresh verbs: <see cref="ChartView.Refresh"/> (new <see cref="Chart"/> instance) versus
/// <see cref="ChartView.Repaint"/> (same instance, new frame).</item>
/// </list>
/// <para>
/// The details the captions used to spell out: a pinned domain
/// (<see cref="ChartView.XAxisRange"/> / <see cref="ChartView.YAxisRange"/>) applies to a <i>numeric</i>
/// axis only - a category axis ignores it - which is why that chart binds a number to X.
/// <see cref="ColorMappingKind.Sequential"/> runs the colour field through
/// <see cref="ChartTheme.SequentialGradient"/> and draws no legend (the values are not categories),
/// <see cref="ColorMappingKind.Identity"/> uses the value as the colour itself (<c>#rrggbb</c> strings
/// work), and <see cref="ColorMappingKind.Diverging"/> wants signed values around a midpoint.
/// <see cref="ChartView.ThemeKind"/> picks a built-in palette, <see cref="ChartView.CustomTheme"/> wins
/// over it once assigned, and that resource is the only place the look is defined (the node has no
/// background export of its own).
/// </para>
/// <para>
/// <see cref="ChartView.EditorPreview"/> has no runtime effect whatsoever - it only tells the editor to
/// skip the live surface, and since this script is not a tool script the export has to sit in the scene
/// for the editor to honour it. <see cref="ChartView.WindowSize"/> is what makes
/// <see cref="ChartView.AddRow"/> a bounded stream: past the cap the oldest row is dropped.
/// </para>
/// </summary>
public partial class ChartViewFieldsDemo : Control
{
    private const string CsvFeed = """
        # a CSV feed: header line, then one row per line
        label,value
        Mon,32
        Tue,41
        """;

    [Export] public ChartView ChannelsChart { get; set; } = null!;
    [Export] public ChartView SequentialChart { get; set; } = null!;
    [Export] public ChartView DivergingChart { get; set; } = null!;
    [Export] public ChartView IdentityChart { get; set; } = null!;
    [Export] public ChartView DomainsChart { get; set; } = null!;
    [Export] public ChartView LightChart { get; set; } = null!;
    [Export] public ChartView ResourceThemeChart { get; set; } = null!;
    [Export] public ChartView AxesChart { get; set; } = null!;
    [Export] public ChartView CsvChart { get; set; } = null!;

    [Export] public Button SetValuesButton { get; set; } = null!;
    [Export] public Button CsvButton { get; set; } = null!;
    [Export] public Button AddRowButton { get; set; } = null!;
    [Export] public Button ClearButton { get; set; } = null!;
    [Export] public Button RefreshButton { get; set; } = null!;
    [Export] public Button RepaintButton { get; set; } = null!;
    [Export] public Label Status { get; set; } = null!;

    private int _csvRow;
    private int _rebuilds;
    private int _values;        // SetValues presses: each one hands the light chart a new row set

    /// <inheritdoc />
    public override void _Ready()
    {
        ConfigureChannels();
        ConfigureColorMappings();
        ConfigureDomainsAndThemes();
        ConfigureAxes();
        ConfigureCsvChart();

        SetValuesButton.Pressed += OnSetValues;
        CsvButton.Pressed += OnSetCsv;
        AddRowButton.Pressed += OnAddRow;
        ClearButton.Pressed += OnClear;
        RefreshButton.Pressed += OnRefresh;
        RepaintButton.Pressed += OnRepaint;

        // ConfigureChart runs on every rebuild, which is also where a hook would re-subscribe: the
        // node replaces its Chart instance on each Refresh, so state hung on the old one is gone.
        AxesChart.ConfigureChart = _ => _rebuilds++;

        Report("everything configured from code - every chart below is a ChartView node");
    }

    // ── The six channels ────────────────────────────────────────────────────

    /// <summary>
    /// One bubble chart carrying all six channels at once: X/Y are the position, ColorField makes the
    /// legend, SizeField and OpacityField take their ranges from the exports, ShapeField cycles the
    /// symbols listed in <see cref="ChartView.ShapeSymbols"/>.
    /// </summary>
    private void ConfigureChannels()
    {
        ChannelsChart.XField = "category";
        ChannelsChart.YField = "value";
        ChannelsChart.ColorField = "segment";
        ChannelsChart.SizeField = "weight";
        ChannelsChart.OpacityField = "confidence";
        ChannelsChart.ShapeField = "role";
        ChannelsChart.SizeRange = new Vector2(5f, 26f);
        ChannelsChart.OpacityRange = new Vector2(0.3f, 1f);
        ChannelsChart.ShapeSymbols =

        [
            ShapeKind.Circle, ShapeKind.Square, ShapeKind.Triangle, ShapeKind.Diamond, ShapeKind.Cross,
        ];
        ChannelsChart.ColorMapping = ColorMappingKind.Category;
        ChannelsChart.Legend = LegendPosition.Bottom;

        ChannelsChart.SetData(
        [
            Bubble("A", 22, "north", 0.4, 0.3, "lead"),
            Bubble("B", 46, "north", 1.0, 0.95, "support"),
            Bubble("C", 31, "south", 0.7, 0.6, "lead"),
            Bubble("D", 58, "south", 0.2, 0.8, "review"),
            Bubble("E", 39, "north", 0.85, 0.5, "review"),
            Bubble("F", 12, "south", 0.55, 1.0, "support"),
        ]);
    }

    private static DataRow Bubble(string category, double value, string segment, double weight,
        double confidence, string role)
        => new DataRow(6)
            .Set("category", category)
            .Set("value", value)
            .Set("segment", segment)
            .Set("weight", weight)
            .Set("confidence", confidence)
            .Set("role", role);

    // ── ColorMapping: the four mapping kinds ────────────────────────────────

    /// <summary>
    /// <see cref="ColorMappingKind.Sequential"/> (a number onto the theme gradient),
    /// <see cref="ColorMappingKind.Diverging"/> (around zero, signed), and
    /// <see cref="ColorMappingKind.Identity"/> (the value <i>is</i> the colour: <c>#rrggbb</c> strings).
    /// <see cref="ColorMappingKind.Auto"/> is the default and is what every other chart here uses.
    /// </summary>
    private void ConfigureColorMappings()
    {
        SequentialChart.ColorField = "value";
        SequentialChart.ColorMapping = ColorMappingKind.Sequential;
        SequentialChart.SetData(
        [
            new DataRow(4).Set("category", "A").Set("value", 4.0).Set("weight", 0.0).Set("role", "one"),
            new DataRow(4).Set("category", "B").Set("value", 18.0).Set("weight", 0.0).Set("role", "one"),
            new DataRow(4).Set("category", "C").Set("value", 33.0).Set("weight", 0.0).Set("role", "one"),
            new DataRow(4).Set("category", "D").Set("value", 51.0).Set("weight", 0.0).Set("role", "one"),
            new DataRow(4).Set("category", "E").Set("value", 72.0).Set("weight", 0.0).Set("role", "one"),
        ]);

        DivergingChart.ColorField = "delta";
        DivergingChart.ColorMapping = ColorMappingKind.Diverging;
        DivergingChart.SetData(
        [
            new DataRow(3).Set("category", "Q1").Set("value", -12.0).Set("delta", -12.0),
            new DataRow(3).Set("category", "Q2").Set("value", -4.0).Set("delta", -4.0),
            new DataRow(3).Set("category", "Q3").Set("value", 9.0).Set("delta", 9.0),
            new DataRow(3).Set("category", "Q4").Set("value", 21.0).Set("delta", 21.0),
            new DataRow(3).Set("category", "Q5").Set("value", 3.0).Set("delta", 3.0),
        ]);

        IdentityChart.ColorField = "slice";
        IdentityChart.ColorMapping = ColorMappingKind.Identity;
        IdentityChart.SetData(
        [
            new DataRow(2).Set("category", "amber").Set("value", 27.0).Set("slice", "#f0b429"),
            new DataRow(2).Set("category", "teal").Set("value", 19.0).Set("slice", "#2ec4b6"),
            new DataRow(2).Set("category", "plum").Set("value", 14.0).Set("slice", "#9b5de5"),
            new DataRow(2).Set("category", "coral").Set("value", 9.0).Set("slice", "#ff6b6b"),
        ]);
    }

    // ── Pinned domains, built-in palettes and a theme resource ──────────────

    /// <summary>
    /// A pinned axis domain needs a <i>numeric</i> axis: <see cref="ChartView.XAxisRange"/> and
    /// <see cref="ChartView.YAxisRange"/> are ignored by a category axis (that is why this chart binds a
    /// number to X). The next chart switches the whole palette with one export, and the third one takes a
    /// <see cref="ChartTheme"/> resource built in code - the only place the look is defined.
    /// </summary>
    private void ConfigureDomainsAndThemes()
    {
        DomainsChart.XField = "t";
        DomainsChart.YField = "value";
        DomainsChart.XAxisRange = new Vector2(2f, 10f);    // (0, 0) would fit the data instead
        DomainsChart.YAxisRange = new Vector2(0f, 100f);
        DomainsChart.SetData(
        [
            new DataRow(2).Set("t", 1.0).Set("value", 18.0),
            new DataRow(2).Set("t", 3.0).Set("value", 46.0),
            new DataRow(2).Set("t", 5.0).Set("value", 72.0),
            new DataRow(2).Set("t", 7.0).Set("value", 55.0),
            new DataRow(2).Set("t", 9.0).Set("value", 88.0),
            new DataRow(2).Set("t", 11.0).Set("value", 63.0),
        ]);

        // One export swaps the palette; the theme resource below then wins over it.
        LightChart.ThemeKind = ChartThemeKind.Light;
        LightChart.SetValues([("Mon", 34.0), ("Tue", 48.0), ("Wed", 29.0), ("Thu", 61.0), ("Fri", 52.0)]);

        ResourceThemeChart.CustomTheme = BuildThemeResource();
        ResourceThemeChart.ColorField = "series";
        ResourceThemeChart.ColorMapping = ColorMappingKind.Category;
        ResourceThemeChart.Legend = LegendPosition.Bottom;
        ResourceThemeChart.SetData(
        [
            new DataRow(3).Set("category", "Mon").Set("value", 22.0).Set("series", "measured"),
            new DataRow(3).Set("category", "Tue").Set("value", 38.0).Set("series", "budget"),
            new DataRow(3).Set("category", "Wed").Set("value", 31.0).Set("series", "measured"),
            new DataRow(3).Set("category", "Thu").Set("value", 46.0).Set("series", "budget"),
        ]);
    }

    /// <summary>
    /// A theme resource created and edited from code. Assigning it to
    /// <see cref="ChartView.CustomTheme"/> replaces the node's whole look - palette, background, grid,
    /// typography - and it is also what a <c>.tres</c> would carry if you saved it
    /// (<see cref="Resource.TakeOverPath"/> then <c>ResourceSaver.Save</c>).
    /// </summary>
    private static ChartTheme BuildThemeResource()
    {
        var theme = ChartTheme.Dark();
        theme.Palette = [new Color(0.98f, 0.62f, 0.29f), new Color(0.36f, 0.83f, 0.72f)];
        theme.BackgroundColor = new Color(0.05f, 0.07f, 0.10f);
        theme.GridColor = new Color(0.6f, 0.75f, 1f, 0.12f);
        theme.AxisColor = new Color(0.75f, 0.85f, 1f, 0.55f);
        theme.TitleColor = new Color(1f, 0.85f, 0.6f);
        theme.TitleFontSize = 15f;
        theme.LabelFontSize = 12f;
        theme.CornerRadius = 6f;
        theme.BackgroundCornerRadius = 10f;
        return theme;
    }

    // ── Axes, units, legend and pointer feedback ────────────────────────────

    /// <summary>
    /// Axis titles with units, a legend on the right, and pointer feedback turned off on the second
    /// chart: <see cref="ChartView.ShowCrosshair"/> and <see cref="ChartView.ShowTooltip"/> are the two
    /// switches, and they are what the plain-scene demos rely on by default.
    /// </summary>
    private void ConfigureAxes()
    {
        AxesChart.ColorField = "series";
        AxesChart.ColorMapping = ColorMappingKind.Category;
        AxesChart.XAxisTitle = "Month";
        AxesChart.XAxisUnit = "2025";
        AxesChart.YAxisTitle = "Revenue";
        AxesChart.YAxisUnit = "USD";
        AxesChart.Legend = LegendPosition.Right;

        var rows = new List<DataRow>();
        foreach (var (month, shop, bulk) in new[]
                 {
                     ("Jan", 42.0, 28.0), ("Feb", 51.0, 31.0), ("Mar", 47.0, 36.0), ("Apr", 63.0, 33.0),
                 })
        {
            // Short series keys on purpose: a legend on the right only reserves ~40 px for its label.
            rows.Add(new DataRow(3).Set("category", month).Set("value", shop).Set("series", "shop"));
            rows.Add(new DataRow(3).Set("category", month).Set("value", bulk).Set("series", "bulk"));
        }
        AxesChart.SetData(rows);

        // The crosshair and the tooltip are independent switches; the caption says which is off.
        LightChart.ShowCrosshair = false;
    }

    // ── The data entry points ───────────────────────────────────────────────

    /// <summary>
    /// <see cref="ChartView.SetCsv"/> (and the <see cref="ChartView.ParseCsv"/> it uses),
    /// <see cref="ChartView.WindowSize"/> with <see cref="ChartView.AddRow"/>, and
    /// <see cref="ChartView.Clear"/>. <see cref="ChartView.EditorPreview"/> is off so the editor shows
    /// the placeholder rectangle of this chart instead of a live surface.
    /// </summary>
    private void ConfigureCsvChart()
    {
        CsvChart.XField = "label";
        CsvChart.YField = "value";
        CsvChart.WindowSize = 6;            // AddRow past this drops the oldest row
        CsvChart.EditorPreview = false;     // visible in the editor as a placeholder (tool script)
        CsvChart.SetCsv(CsvFeed);
        _csvRow = CsvChart.DataRows.Count;
    }

    /// <summary>
    /// Feeds the light chart through <see cref="ChartView.SetValues"/>, the two-field shortcut: it writes one
    /// row per category under the channel field names (<c>category</c> + <c>value</c>), so it belongs on a
    /// chart without a colour field - the axes chart below binds <c>series</c> to its colour channel, and
    /// replacing its rows with this shortcut would drop the colour scale and with it the legend.
    /// </summary>
    private void OnSetValues()
    {
        _values++;
        LightChart.SetValues(
        [
            ("Mon", 24.0 + 6 * _values), ("Tue", 38.0 + 5 * _values), ("Wed", 31.0 + 4 * _values),
            ("Thu", 52.0 + 3 * _values), ("Fri", 45.0 + 2 * _values),
        ]);
        Report($"SetValues(...) -> {LightChart.DataRows.Count} rows of category + value on the light chart: the "
            + "shortcut writes those two fields only, so it is fed to a chart without a ColorField; the axes "
            + "chart needs its series column (and SetData keeps it)");
    }

    private void OnSetCsv()
    {
        CsvChart.SetCsv(CsvFeed);
        _csvRow = CsvChart.DataRows.Count;
        Report($"SetCsv(...) -> {CsvChart.DataRows.Count} rows via ParseCsv (header line gives the field names)");
    }

    private void OnAddRow()
    {
        _csvRow++;
        CsvChart.AddRow(new DataRow(2).Set("label", $"W{_csvRow}").Set("value", 20 + (_csvRow * 7) % 60));
        Report($"AddRow(...) -> {CsvChart.DataRows.Count} rows shown; WindowSize = {CsvChart.WindowSize} drops the oldest");
    }

    private void OnClear()
    {
        CsvChart.Clear();
        _csvRow = 0;
        Report("Clear() -> no rows and an empty surface");
    }

    private void OnRefresh()
    {
        // Refresh() only marks the node dirty (ChartView.Refresh() is an Invalidate()); the new Chart
        // instance - and with it the ConfigureChart pass that counts _rebuilds - is built in the node's own
        // _Process, which runs after this handler. The report is deferred so it reads the counter once that
        // rebuild has happened, instead of naming the previous one.
        int before = _rebuilds;
        AxesChart.Refresh();
        Callable.From(() => ReportRefresh(before)).CallDeferred();
    }

    /// <summary>The deferred half of the Refresh report: the rebuild it caused has happened by now.</summary>
    private void ReportRefresh(int before)
        => Report($"Refresh() -> Rebuild() in the node's next _Process: a NEW Chart instance "
            + $"(rebuild #{before} -> #{_rebuilds}), so hover and animation state is thrown away");

    private void OnRepaint()
    {
        AxesChart.Repaint();
        Report($"Repaint() -> same Chart instance (still rebuild #{_rebuilds}): a new frame only, so hover/animation state survives");
    }

    private void Report(string message) => Status.Text = message;
}
