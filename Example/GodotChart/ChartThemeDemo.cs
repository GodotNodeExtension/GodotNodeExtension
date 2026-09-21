using Godot;
using GodotNodeExtension.Component.GodotChart;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// The <see cref="ChartTheme"/> configuration surface, one cell per property group, every bar cell drawn from
/// the same four months x two series of rows so that the only variable between neighbours is the theme:
/// <list type="bullet">
/// <item>the two built-in palettes, picked with <see cref="ChartView.ThemeKind"/> alone
/// (<see cref="ChartThemeKind.Dark"/> and <see cref="ChartThemeKind.Light"/>);</item>
/// <item><see cref="ChartTheme.Clone"/> - the documented way to derive a variant - with
/// <see cref="ChartTheme.Palette"/> (two colours here) and <see cref="ChartTheme.SequentialGradient"/>;</item>
/// <item>the "Chart Frame" group (<see cref="ChartTheme.BackgroundColor"/>, <see cref="ChartTheme.GridColor"/>,
/// <see cref="ChartTheme.AxisColor"/>, <see cref="ChartTheme.BackgroundCornerRadius"/>);</item>
/// <item>the "Typography" group (<see cref="ChartTheme.Font"/>, the <see cref="ChartTheme.FontFamily"/>
/// alternative, <see cref="ChartTheme.TitleColor"/> / <see cref="ChartTheme.TitleFontSize"/>,
/// <see cref="ChartTheme.LabelColor"/> / <see cref="ChartTheme.LabelFontSize"/>,
/// <see cref="ChartTheme.DataLabelColor"/>);</item>
/// <item>the line widths and the bubble metrics (<see cref="ChartTheme.GridLineWidth"/>,
/// <see cref="ChartTheme.AxisLineWidth"/>, <see cref="ChartTheme.CrosshairStrokeWidth"/>,
/// <see cref="ChartTheme.SelectionStrokeWidth"/>, <see cref="ChartTheme.CornerRadius"/>,
/// <see cref="ChartTheme.StrokeWidth"/>, and the tooltip group
/// <see cref="ChartTheme.TooltipBorderWidth"/> / <see cref="ChartTheme.TooltipCornerRadius"/> /
/// <see cref="ChartTheme.TooltipPadding"/> / <see cref="ChartTheme.TooltipFontSize"/>);</item>
/// <item>all six "Feature Toggles" off, three of them on the pie
/// (<see cref="ChartTheme.EnableHoverExplode"/>, <see cref="ChartTheme.EnableHoverHighlight"/>,
/// <see cref="ChartTheme.EnableAnimation"/>) and three on the bars
/// (<see cref="ChartTheme.EnableSelection"/>, <see cref="ChartTheme.EnableCrosshair"/>,
/// <see cref="ChartTheme.EnableTooltip"/>);</item>
/// <item>a theme resource edited while the demo runs (<see cref="ChartTheme.BackgroundColor"/> +
/// <see cref="ChartTheme.Palette"/> assignments) - the same path an Inspector edit of a saved <c>.tres</c>
/// takes, because <see cref="ChartView.CustomTheme"/> follows the resource's
/// <see cref="Resource.Changed"/> signal, which those setters emit by themselves;</item>
/// <item>and the chart-level override route, <see cref="ChartView.ConfigureChart"/> setting
/// <see cref="Chart.BackgroundColor"/>, <see cref="Chart.GridColor"/> and <see cref="Chart.AxisColor"/>, which
/// win over the theme.</item>
/// </list>
/// <para>
/// The two baselines: <see cref="ChartTheme.Dark"/> is the plain default set (BackgroundColor 0.08/0.08/0.12,
/// GridColor white at 8% alpha with GridLineWidth 1, AxisColor white at 40% with AxisLineWidth 2, and the six
/// colours of <see cref="ChartTheme.DefaultPalette"/>), and <see cref="ChartTheme.Light"/> is a Dark clone
/// that overrides <see cref="ChartTheme.Palette"/>, the frame colours, the typography colours
/// (<see cref="ChartTheme.TitleColor"/> / <see cref="ChartTheme.LabelColor"/> /
/// <see cref="ChartTheme.DataLabelColor"/>), <see cref="ChartTheme.DefaultMarkColor"/>, the selection and
/// focus values (<see cref="ChartTheme.SelectionColor"/> / <see cref="ChartTheme.HoverBrighten"/> /
/// <see cref="ChartTheme.UnfocusedOpacity"/>) plus the tooltip, crosshair, line-hover ring, point, radar, box,
/// violin, gauge, sankey and candlestick colours and the polar <see cref="ChartTheme.SegmentBorderColor"/> -
/// which is why one export redraws its whole cell.
/// </para>
/// <para>
/// The captions in the scene are deliberately one line long - each one only names the properties its cell
/// changes - so every conclusion that needs prose lives here. Four theme members need a word about *where*
/// they apply: <see cref="ChartTheme.EnableTooltip"/> is a master switch that
/// <see cref="ChartView"/> ANDs with its own <see cref="ChartView.ShowTooltip"/>, and
/// <see cref="ChartTheme.EnableAnimation"/> makes <see cref="Chart.Animate(float)"/> settle on the end state
/// instead of animating - so a theme can turn both off for every chart that uses it.
/// <see cref="ChartTheme.CornerRadius"/> and <see cref="ChartTheme.StrokeWidth"/> are the mark defaults:
/// <see cref="ChartView"/> applies them to the mark it builds, before <c>ConfigureMark</c> runs, so the
/// widths cell's bars and line take the theme's 3/2 unless the host overrides them on the mark.
/// </para>
/// <para>
/// Two routes can override the theme. <see cref="ChartView.ConfigureChart"/> runs on every rebuild and hands
/// out the <see cref="Chart"/> instance, where <see cref="Chart.BackgroundColor"/>,
/// <see cref="Chart.GridColor"/> and <see cref="Chart.AxisColor"/> win over the theme and each one falls back
/// to the theme value only while it is left unset - and because <see cref="ChartView"/> has no background
/// export of its own, the frame colour can only come from the theme or from that chart-level override.
/// Everything else (fonts, axis-line widths, the background corner radius) still comes from the theme.
/// </para>
/// <para>
/// A theme resource also stays live after it has been assigned: <see cref="ChartView.CustomTheme"/> follows
/// the resource's <see cref="Resource.Changed"/> signal and rebuilds, which is how a <c>.tres</c> edited in
/// the Inspector updates the view in place. A script edit of an exported property needs no
/// <see cref="Resource.EmitChanged"/> of its own: the theme's setters already emit <see cref="Resource.Changed"/>
/// for every assignment, an array-valued export included (the whole <see cref="ChartTheme.Palette"/> counts as
/// one value). Only editing an array <i>in place</i> - one <c>Palette[i] = ...</c> at a time - leaves the
/// resource silent, so that is the case the manual call exists for.
/// </para>
/// <para>
/// Three visual notes the captions no longer spell out. <see cref="ChartTheme.BackgroundCornerRadius"/> rounds
/// the chart's own background rectangle; the surface is always cleared transparent, so the four corners stay
/// see-through (the page behind them shows) and the radius is visible on any page. The chart-level cell paints
/// a different colour inside that frame, which is what a host override looks like.
/// <see cref="ChartTheme.Clone"/> deep-copies <see cref="ChartTheme.Palette"/> and
/// <see cref="ChartTheme.SequentialGradient"/> (the two palette entries then colour the <c>act</c> and the
/// <c>plan</c> series, which is what the legend swatches show), so a theme derived here can never leak back
/// into <see cref="ChartTheme.Dark"/>. And the two series are named <c>act</c> / <c>plan</c> - four characters
/// at most - because the legend reserves only <see cref="ChartTheme.LegendRightOffset"/> (40px) on the right
/// of the plot, and a longer label is clipped.
/// </para>
/// <para>
/// What the measured knobs actually reach, for the cells that only show values:
/// <see cref="ChartTheme.GridLineWidth"/> and <see cref="ChartTheme.AxisLineWidth"/> are read by the default
/// grid and axis renderers, <see cref="ChartTheme.CrosshairColor"/> with
/// <see cref="ChartTheme.CrosshairStrokeWidth"/> and <see cref="ChartTheme.CrosshairDashLength"/> styles the
/// dashed crosshair that follows the pointer, <see cref="ChartTheme.SelectionColor"/> with
/// <see cref="ChartTheme.SelectionStrokeWidth"/> the ring around a clicked bar, and the tooltip group
/// (<see cref="ChartTheme.TooltipBackground"/>, <see cref="ChartTheme.TooltipTextColor"/>,
/// <see cref="ChartTheme.TooltipBorderColor"/>, <see cref="ChartTheme.TooltipBorderWidth"/>,
/// <see cref="ChartTheme.TooltipCornerRadius"/>, <see cref="ChartTheme.TooltipPadding"/>,
/// <see cref="ChartTheme.TooltipFontSize"/>) styles the bubble that follows the pointer - so the widths cell
/// is the one whose grid, crosshair, selection ring and tooltip all change at once.
/// </para>
/// <para>
/// Typography specifics: <see cref="ChartTheme.Font"/> (a Godot <see cref="Font"/> resource, and
/// <see cref="ThemeDB.FallbackFont"/> is the safe one to reach for) wins over the
/// <see cref="ChartTheme.FontFamily"/> name when both are set, while leaving both null keeps the system
/// default; and <see cref="ChartTheme.DataLabelColor"/> reaches the labels a mark draws on itself, which is
/// why the typography cell demonstrates the sizes and the title/label colours while the pie cell carries the
/// data-label colour where the slice labels can be seen.
/// </para>
/// </summary>
public partial class ChartThemeDemo : MarginContainer
{
    /// <summary>Baseline cell: <see cref="ChartThemeKind.Dark"/>, no theme resource.</summary>
    [Export] public ChartView DarkChart { get; set; } = null!;

    /// <summary>Baseline cell: <see cref="ChartThemeKind.Light"/>, the same chart under the light palette.</summary>
    [Export] public ChartView LightChart { get; set; } = null!;

    /// <summary>Cell showing <see cref="ChartTheme.Clone"/> plus a hand-made two-colour palette.</summary>
    [Export] public ChartView CloneChart { get; set; } = null!;

    /// <summary>Cell showing the frame colours and <see cref="ChartTheme.BackgroundCornerRadius"/>.</summary>
    [Export] public ChartView FrameChart { get; set; } = null!;

    /// <summary>Cell showing the typography group (font, title/label/data-label colours and sizes).</summary>
    [Export] public ChartView TypographyChart { get; set; } = null!;

    /// <summary>Cell showing the line widths and the theme's mark size defaults.</summary>
    [Export] public ChartView MetricsChart { get; set; } = null!;

    /// <summary>Pie cell with the hover, explode and animation switches off.</summary>
    [Export] public ChartView HoverChart { get; set; } = null!;

    /// <summary>Bar cell with the selection, crosshair and tooltip switches off.</summary>
    [Export] public ChartView InteractionChart { get; set; } = null!;

    /// <summary>Cell whose theme resource is edited while the demo runs (the buttons below).</summary>
    [Export] public ChartView LiveThemeChart { get; set; } = null!;

    /// <summary>Cell showing chart-level colour overrides winning over the theme.</summary>
    [Export] public ChartView OverrideChart { get; set; } = null!;

    /// <summary>Edits the live theme resource (background + palette) and emits its Changed signal.</summary>
    [Export] public Button MutateButton { get; set; } = null!;

    /// <summary>Restores the live theme resource to <see cref="ChartTheme.DefaultPalette"/>.</summary>
    [Export] public Button ResetButton { get; set; } = null!;

    /// <summary>One-line report of what the last button did.</summary>
    [Export] public Label Status { get; set; } = null!;

    // The theme resource the two buttons mutate while the demo runs. It is one instance, held by the node
    // through CustomTheme, so editing it is exactly what the Inspector does to a saved .tres.
    private readonly ChartTheme _liveTheme = ChartTheme.Dark().Clone();

    private int _themeStep;

    /// <inheritdoc />
    public override void _Ready()
    {
        ConfigureBaselineCells();
        ConfigureCloneCell();
        ConfigureFrameCell();
        ConfigureTypographyCell();
        ConfigureMetricsCell();
        ConfigureHoverCell();
        ConfigureInteractionCell();
        ConfigureLiveThemeCell();
        ConfigureOverrideCell();

        MutateButton.Pressed += OnMutateTheme;
        ResetButton.Pressed += OnResetTheme;

        Report("hover a bar or a slice and click one: the highlight, the crosshair, the tooltip and the selection ring are theme switches");
    }

    // ── Shared chart, so only the theme differs between the bar cells ───────

    /// <summary>
    /// The channels and the legend every bar cell uses: months on X, the value on Y and the series on the
    /// colour channel, which is what makes the legend appear and the palette visible. A null theme keeps
    /// <see cref="ChartView.ThemeKind"/> in charge.
    /// </summary>
    private static void ConfigureBars(ChartView view, ChartTheme? theme)
    {
        view.XField = "category";
        view.YField = "value";
        view.ColorField = "series";
        view.ColorMapping = ColorMappingKind.Category;
        view.Legend = LegendPosition.Right;
        view.CustomTheme = theme;
        view.SetData(BuildBars());
    }

    /// <summary>Four months of an act and a plan figure - the same rows in every bar cell.</summary>
    private static DataRow[] BuildBars() =>
    [
        Row("Jan", 42, "act"), Row("Jan", 34, "plan"),
        Row("Feb", 51, "act"), Row("Feb", 40, "plan"),
        Row("Mar", 47, "act"), Row("Mar", 45, "plan"),
        Row("Apr", 63, "act"), Row("Apr", 52, "plan"),
    ];

    private static DataRow Row(string category, double value, string series)
        => new DataRow(3).Set("category", category).Set("value", value).Set("series", series);

    // ── The two built-in palettes ───────────────────────────────────────────

    /// <summary>
    /// <see cref="ChartView.ThemeKind"/> decides the whole look here, because no
    /// <see cref="ChartView.CustomTheme"/> is assigned: the node builds a fresh
    /// <see cref="ChartTheme.Dark"/> or <see cref="ChartTheme.Light"/> on every rebuild. The scene sets the
    /// Light cell's export as well, so its editor preview already matches the runtime.
    /// </summary>
    private void ConfigureBaselineCells()
    {
        ConfigureBars(DarkChart, null);
        DarkChart.ThemeKind = ChartThemeKind.Dark;

        ConfigureBars(LightChart, null);
        LightChart.ThemeKind = ChartThemeKind.Light;
    }

    /// <summary>
    /// <see cref="ChartTheme.Clone"/> derives a variant from a built-in theme - the recommended pattern, so
    /// that properties added to <see cref="ChartTheme"/> later are inherited instead of forgotten. The clone
    /// owns its <see cref="ChartTheme.Palette"/> and <see cref="ChartTheme.SequentialGradient"/> arrays, so
    /// replacing the two palette colours here can never change <see cref="ChartTheme.Dark"/>.
    /// </summary>
    private void ConfigureCloneCell()
    {
        var theme = ChartTheme.Dark().Clone();
        theme.Palette = [new Color(0.98f, 0.62f, 0.20f), new Color(0.30f, 0.85f, 0.85f)];
        theme.SequentialGradient = [new Color(0.09f, 0.16f, 0.45f), new Color(0.95f, 0.65f, 0.25f)];
        theme.BackgroundColor = new Color(0.10f, 0.08f, 0.16f);
        ConfigureBars(CloneChart, theme);
    }

    // ── Chart Frame ─────────────────────────────────────────────────────────

    /// <summary>
    /// The frame group. The chart paints its own rounded background rectangle on a surface that is always
    /// cleared transparent, so <see cref="ChartTheme.BackgroundCornerRadius"/> really shows: the corners are
    /// see-through and the page behind them is what you see there.
    /// </summary>
    private void ConfigureFrameCell()
    {
        var theme = ChartTheme.Dark().Clone();
        theme.BackgroundColor = new Color(0.03f, 0.10f, 0.12f);
        theme.GridColor = new Color(0.35f, 0.85f, 0.85f, 0.22f);
        theme.AxisColor = new Color(0.55f, 0.95f, 0.90f, 0.9f);
        theme.BackgroundCornerRadius = 20f;
        ConfigureBars(FrameChart, theme);
    }

    // ── Typography ──────────────────────────────────────────────────────────

    /// <summary>
    /// The typography group. <see cref="ChartTheme.Font"/> takes a Godot <see cref="Font"/> resource -
    /// <see cref="ThemeDB.FallbackFont"/> is the safe one to reach for - and wins over the
    /// <see cref="ChartTheme.FontFamily"/> name when both are set; leaving both null keeps the system default.
    /// <see cref="ChartTheme.DataLabelColor"/> reaches the labels a mark draws on itself, so it is shown on
    /// the pie cell rather than here.
    /// </summary>
    private void ConfigureTypographyCell()
    {
        var theme = ChartTheme.Dark().Clone();
        theme.Font = ThemeDB.FallbackFont;
        theme.TitleColor = new Color(1f, 0.85f, 0.55f);
        theme.TitleFontSize = 17f;
        theme.LabelColor = new Color(0.72f, 0.84f, 1f, 0.75f);
        theme.LabelFontSize = 11f;
        ConfigureBars(TypographyChart, theme);
    }

    // ── Line widths and bubble metrics ──────────────────────────────────────

    /// <summary>
    /// The widths. <see cref="ChartTheme.GridLineWidth"/> and <see cref="ChartTheme.AxisLineWidth"/> are read
    /// by the default grid and axis renderers, <see cref="ChartTheme.CrosshairStrokeWidth"/> (with
    /// <see cref="ChartTheme.CrosshairDashLength"/> and <see cref="ChartTheme.CrosshairColor"/>) by the
    /// crosshair, <see cref="ChartTheme.SelectionStrokeWidth"/> (with
    /// <see cref="ChartTheme.SelectionColor"/>) by the selection ring of a clicked bar, and the tooltip has a
    /// group of its own (<see cref="ChartTheme.TooltipBackground"/>, <see cref="ChartTheme.TooltipTextColor"/>,
    /// <see cref="ChartTheme.TooltipBorderColor"/>, <see cref="ChartTheme.TooltipBorderWidth"/>,
    /// <see cref="ChartTheme.TooltipCornerRadius"/>, <see cref="ChartTheme.TooltipPadding"/>,
    /// <see cref="ChartTheme.TooltipFontSize"/>). The two mark defaults,
    /// <see cref="ChartTheme.CornerRadius"/> and <see cref="ChartTheme.StrokeWidth"/>, are applied by
    /// <see cref="ChartView"/> to the mark it builds - here they show up as the bars' corner radius and the
    /// line's stroke width, and a <c>ConfigureMark</c> callback on the node would win over them.
    /// </summary>
    private void ConfigureMetricsCell()
    {
        var theme = ChartTheme.Dark().Clone();
        theme.GridLineWidth = 2.5f;
        theme.AxisLineWidth = 4f;
        theme.CrosshairColor = new Color(1f, 0.75f, 0.2f, 0.5f);
        theme.CrosshairStrokeWidth = 2.5f;
        theme.CrosshairDashLength = 8f;
        theme.SelectionColor = new Color(1f, 0.75f, 0.2f);
        theme.SelectionStrokeWidth = 4f;
        theme.TooltipBackground = new Color(0.11f, 0.16f, 0.20f, 0.96f);
        theme.TooltipTextColor = new Color(0.88f, 0.98f, 1f);
        theme.TooltipBorderColor = new Color(1f, 0.75f, 0.2f, 0.6f);
        theme.TooltipBorderWidth = 2f;
        theme.TooltipCornerRadius = 10f;
        theme.TooltipPadding = 10f;
        theme.TooltipFontSize = 13f;
        theme.CornerRadius = 8f;
        theme.StrokeWidth = 4f;
        ConfigureBars(MetricsChart, theme);
    }

    // ── Feature Toggles ─────────────────────────────────────────────────────

    /// <summary>
    /// The hover switches, on the one kind where all of them are visible: a pie.
    /// <see cref="ChartTheme.EnableHoverExplode"/> stops a hovered slice from sliding out
    /// (<see cref="ChartTheme.PieExplodeRatio"/> is the distance it would travel) and
    /// <see cref="ChartTheme.EnableHoverHighlight"/> stops the brightening and scaling of the hovered mark.
    /// <see cref="ChartTheme.EnableAnimation"/> is turned off too: <see cref="Chart.Animate(AnimationContext?)"/>
    /// reads it and settles on the end state, so a host that drives the animation gets a still chart. The slice
    /// labels are drawn in the theme's <see cref="ChartTheme.DataLabelColor"/>.
    /// </summary>
    private void ConfigureHoverCell()
    {
        var theme = ChartTheme.Dark().Clone();
        theme.PieExplodeRatio = 0.08f;
        theme.EnableHoverExplode = false;
        theme.EnableHoverHighlight = false;
        theme.EnableAnimation = false;
        theme.DataLabelColor = new Color(1f, 0.92f, 0.65f);

        HoverChart.XField = "category";
        HoverChart.YField = "value";
        HoverChart.ColorField = "category";     // one slice per month, so each slice takes a palette colour
        HoverChart.ColorMapping = ColorMappingKind.Category;
        HoverChart.Legend = LegendPosition.Bottom;
        HoverChart.CustomTheme = theme;
        HoverChart.SetData(BuildSlices());
    }

    /// <summary>The act series of the bar cells as pie slices.</summary>
    private static DataRow[] BuildSlices() =>
    [
        Slice("Jan", 42), Slice("Feb", 51), Slice("Mar", 47), Slice("Apr", 63),
    ];

    private static DataRow Slice(string category, double value)
        => new DataRow(2).Set("category", category).Set("value", value);

    /// <summary>
    /// The pointer switches that live on the theme. <see cref="ChartTheme.EnableSelection"/> drops the ring
    /// drawn around a selected bar and <see cref="ChartTheme.EnableCrosshair"/> makes the renderer skip the
    /// crosshair overlay; both are read from the theme on every frame. <see cref="ChartTheme.EnableTooltip"/>
    /// is the theme's master switch for the bubble, ANDed with the node's <see cref="ChartView.ShowTooltip"/>
    /// - this cell turns both off, either one alone would already hide it.
    /// </summary>
    private void ConfigureInteractionCell()
    {
        var theme = ChartTheme.Dark().Clone();
        theme.EnableSelection = false;
        theme.EnableCrosshair = false;
        theme.EnableTooltip = false;
        ConfigureBars(InteractionChart, theme);
        InteractionChart.ShowTooltip = false;
    }

    // ── Editing a theme resource while the demo runs ────────────────────────

    /// <summary>
    /// Hands one theme instance to <see cref="ChartView.CustomTheme"/> (which also means
    /// <see cref="ChartView.ThemeKind"/> is ignored for this cell). The buttons below then edit that
    /// instance: assigning an exported property already emits the <see cref="Resource.Changed"/> signal the
    /// Inspector emits on its own (the theme's setters do it per assignment, whole arrays included), so they
    /// need no explicit <see cref="Resource.EmitChanged"/> - that call is only required when an array is
    /// edited in place, element by element.
    /// </summary>
    private void ConfigureLiveThemeCell()
    {
        _liveTheme.Palette = [.. ChartTheme.DefaultPalette];
        _liveTheme.BackgroundColor = new Color(0.05f, 0.09f, 0.14f);
        ConfigureBars(LiveThemeChart, _liveTheme);
    }

    /// <summary>Swap the live theme between two palettes and two frame colours.</summary>
    private void OnMutateTheme()
    {
        _themeStep++;
        if (_themeStep % 2 == 0)
        {
            _liveTheme.BackgroundColor = new Color(0.16f, 0.06f, 0.14f);
            _liveTheme.Palette = [new Color(0.98f, 0.60f, 0.20f), new Color(0.98f, 0.35f, 0.55f)];
        }
        else
        {
            _liveTheme.BackgroundColor = new Color(0.04f, 0.11f, 0.15f);
            _liveTheme.Palette = [new Color(0.55f, 0.35f, 0.98f), new Color(0.30f, 0.80f, 0.95f)];
        }

        // The two assignments above are the whole story: the setters emit Resource.Changed, ChartView follows
        // it and rebuilds the cell - no EmitChanged() call here (it would only emit the signal a second time).
        Report($"theme.Palette + theme.BackgroundColor swapped on the live resource (step {_themeStep}); "
            + "the setters' Changed signal rebuilt the cell - the .tres in the Inspector takes this same path");
    }

    /// <summary>Put the live theme back to <see cref="ChartTheme.DefaultPalette"/> and this cell's first frame colour.</summary>
    private void OnResetTheme()
    {
        _themeStep = 0;
        _liveTheme.Palette = [.. ChartTheme.DefaultPalette];
        _liveTheme.BackgroundColor = new Color(0.05f, 0.09f, 0.14f);
        Report("theme.Palette = ChartTheme.DefaultPalette and the frame colour back to where this cell started");
    }

    // ── Chart level overrides ───────────────────────────────────────────────

    /// <summary>
    /// The other override route, under a theme: <see cref="ChartView.ConfigureChart"/> runs on every rebuild
    /// and hands out the <see cref="Chart"/> instance, whose <see cref="Chart.BackgroundColor"/>,
    /// <see cref="Chart.GridColor"/> and <see cref="Chart.AxisColor"/> win over the theme - each of them falls
    /// back to the theme value only while it is left unset. Everything else stays the theme's: the fonts, the
    /// axis line widths, and the background corner radius you can see ringing this frame.
    /// </summary>
    private void ConfigureOverrideCell()
    {
        var theme = ChartTheme.Dark().Clone();
        theme.BackgroundColor = new Color(0.05f, 0.05f, 0.09f);
        theme.BackgroundCornerRadius = 18f;
        ConfigureBars(OverrideChart, theme);

        OverrideChart.ConfigureChart = chart =>
        {
            chart.BackgroundColor = new Color(0.18f, 0.13f, 0.06f);
            chart.GridColor = new Color(1f, 0.75f, 0.3f, 0.35f);
            chart.AxisColor = new Color(1f, 0.85f, 0.55f);
        };
    }

    private void Report(string message) => Status.Text = message;
}
