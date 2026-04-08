using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Component.GodotChart.Marks;

namespace GodotNodeExtension.Example.GodotChart;

/// <summary>
/// Demo showcase for GodotChart with TabBar-based chart switching,
/// entry/exit animation, hover highlight, tooltip, and click selection.
/// </summary>
public partial class GodotChartDemo : Control
{
    private ICanvas2D? _canvas;
    private TabBar?    _tabBar;
    private Control?   _chartArea;
    private int       _currentTab;
    private int       _canvasW, _canvasH;

    // Theme
    private ChartTheme _theme = ChartTheme.Dark();
    private bool      _isDarkTheme = true;
    private Button?   _themeButton;

    // Animation & interaction state
    private readonly AnimationController _anim    = new();
    private readonly TooltipRenderer     _tooltip = new() { Theme = ChartTheme.Dark() };
    private Vector2  _lastMousePos;
    private int      _hoveredRowIndex  = -1;
    private HitResult? _lastHit;
    private bool     _isExiting;

    // Persistent chart instances (one per tab, lazily created)
    private Chart?[]? _chartInstances;

    // ── Chart registry ────────────────────────────────────────
    private readonly List<(string Name, Func<Chart> Create)> _charts = new();

    /// <summary>
    /// Register all chart demos here. To add a new chart type,
    /// simply append a new entry to this list.
    /// </summary>
    private void RegisterCharts()
    {
        _charts.Add(("Bar Chart",    CreateBarChart));
        _charts.Add(("Line Chart",   CreateMultiLine));
        _charts.Add(("Bubble",       CreateBubbleChart));
        _charts.Add(("Candlestick",  CreateCandlestick));
        _charts.Add(("Area Chart",   CreateAreaChart));
        _charts.Add(("Pie Chart",    CreatePieChart));
        _charts.Add(("Radar Chart",  CreateRadarChart));
        _charts.Add(("Stacked Bar",  CreateStackedBar));
        _charts.Add(("Stacked Area", CreateStackedArea));
        _charts.Add(("Heatmap",      CreateHeatmap));
        _charts.Add(("Gauge",        CreateGauge));
        _charts.Add(("H-Bar",        CreateHorizontalBar));
        _charts.Add(("Step Line",    CreateStepLine));
        _charts.Add(("Box Plot",     CreateBoxPlot));
        _charts.Add(("Funnel",       CreateFunnel));
        _charts.Add(("Range Area",   CreateRangeArea));
        _charts.Add(("Timeline",     CreateTimeline));
        _charts.Add(("Log Scale",    CreateLogScale));
        _charts.Add(("Treemap",      CreateTreemap));
        _charts.Add(("Violin",       CreateViolin));
        _charts.Add(("Sunburst",     CreateSunburst));
        _charts.Add(("Sankey",       CreateSankey));
        _charts.Add(("Chord",        CreateChord));
        _charts.Add(("Lollipop",     CreateLollipop));
        _charts.Add(("Waffle",       CreateWaffle));
        _charts.Add(("Histogram",    CreateHistogram));
        _charts.Add(("Diverging",    CreateDivergingHeatmap));
        _charts.Add(("Composite",    CreateCompositeChart));
    }

    // ── Demo data ─────────────────────────────────────────────

    // Monthly sales data (bar / line)
    private static readonly List<DataRow> MonthlySales =
    [
        new DataRow().Set("month", "Jan").Set("revenue", 12400).Set("cost", 8200).Set("category", "Electronics"),
        new DataRow().Set("month", "Feb").Set("revenue", 18700).Set("cost", 11300).Set("category", "Electronics"),
        new DataRow().Set("month", "Mar").Set("revenue", 15200).Set("cost", 9800).Set("category", "Electronics"),
        new DataRow().Set("month", "Apr").Set("revenue", 23100).Set("cost", 14200).Set("category", "Electronics"),
        new DataRow().Set("month", "May").Set("revenue", 19800).Set("cost", 12100).Set("category", "Electronics"),
        new DataRow().Set("month", "Jun").Set("revenue", 28600).Set("cost", 17400).Set("category", "Electronics")
    ];

    // Multi-series time data (multi-line)
    private static readonly List<DataRow> MultiSeries = BuildMultiSeries();

    private static List<DataRow> BuildMultiSeries()
    {
        var rows   = new List<DataRow>();
        var months = new[] { "Jan","Feb","Mar","Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec" };
        var rng    = new Random(42);

        double aVal = 80, bVal = 60, cVal = 40;
        foreach (var m in months)
        {
            aVal += rng.NextDouble() * 20 - 8;
            bVal += rng.NextDouble() * 16 - 6;
            cVal += rng.NextDouble() * 24 - 10;
            rows.Add(new DataRow().Set("month", m).Set("value", Math.Max(10, aVal)).Set("series", "ProductA"));
            rows.Add(new DataRow().Set("month", m).Set("value", Math.Max(10, bVal)).Set("series", "ProductB"));
            rows.Add(new DataRow().Set("month", m).Set("value", Math.Max(10, cVal)).Set("series", "ProductC"));
        }
        return rows;
    }

    // Scatter / bubble data
    private static readonly List<DataRow> BubbleData = BuildBubbleData();

    private static List<DataRow> BuildBubbleData()
    {
        var rows = new List<DataRow>();
        var rng  = new Random(7);
        var cats = new[] { "Alpha", "Beta", "Gamma" };

        for (int i = 0; i < 30; i++)
        {
            rows.Add(new DataRow()
                .Set("x",          rng.NextDouble() * 100)
                .Set("y",          rng.NextDouble() * 100)
                .Set("size",       rng.NextDouble() * 80 + 10)
                .Set("confidence", rng.NextDouble() * 0.5 + 0.5)
                .Set("category",   cats[rng.Next(cats.Length)]));
        }
        return rows;
    }

    // Simulated stock OHLC data (20 trading days)
    private static readonly List<DataRow> StockData = BuildStockData();

    // Pie chart data: damage type distribution
    private static readonly List<DataRow> DamageDistribution =
    [
        new DataRow().Set("type", "Physical").Set("value", 3200),
        new DataRow().Set("type", "Magic").Set("value", 2100),
        new DataRow().Set("type", "Fire").Set("value", 1500),
        new DataRow().Set("type", "Ice").Set("value", 900),
        new DataRow().Set("type", "Lightning").Set("value", 600),
    ];

    // Radar chart data: RPG character stats (multi-character overlay)
    private static readonly List<DataRow> RadarData = BuildRadarData();

    private static List<DataRow> BuildRadarData()
    {
        var rows = new List<DataRow>();
        var dims = new[] { "Strength", "Agility", "Intelligence", "Endurance", "Charisma", "Luck" };
        var aliceStats = new[] { 85, 60, 90, 55, 70, 65 };
        var bobStats   = new[] { 70, 80, 45, 90, 50, 75 };

        for (int i = 0; i < dims.Length; i++)
        {
            rows.Add(new DataRow().Set("stat", dims[i]).Set("value", aliceStats[i]).Set("player", "Alice"));
            rows.Add(new DataRow().Set("stat", dims[i]).Set("value", bobStats[i]).Set("player", "Bob"));
        }
        return rows;
    }

    private static List<DataRow> BuildStockData()
    {
        var rows = new List<DataRow>();
        var rng  = new Random(123);
        double price = 150.0;

        for (int i = 1; i <= 20; i++)
        {
            double open   = price + (rng.NextDouble() - 0.5) * 3;
            double change = (rng.NextDouble() - 0.48) * 8;
            double close  = open + change;
            double high   = Math.Max(open, close) + rng.NextDouble() * 4;
            double low    = Math.Min(open, close) - rng.NextDouble() * 4;

            rows.Add(new DataRow()
                .Set("date",  $"D{i}")
                .Set("open",  Math.Round(open,  2))
                .Set("high",  Math.Round(high,  2))
                .Set("low",   Math.Round(low,   2))
                .Set("close", Math.Round(close, 2)));

            price = close;
        }
        return rows;
    }

    // Heatmap data: 5×5 correlation matrix
    private static readonly List<DataRow> HeatmapData = BuildHeatmapData();

    private static List<DataRow> BuildHeatmapData()
    {
        var rows = new List<DataRow>();
        var labels = new[] { "STR", "AGI", "INT", "END", "LCK" };
        var values = new[,]
        {
            { 1.0, 0.3, -0.2, 0.7, 0.1 },
            { 0.3, 1.0, 0.1, -0.1, 0.5 },
            { -0.2, 0.1, 1.0, -0.4, 0.2 },
            { 0.7, -0.1, -0.4, 1.0, 0.0 },
            { 0.1, 0.5, 0.2, 0.0, 1.0 },
        };
        for (int r = 0; r < 5; r++)
            for (int c = 0; c < 5; c++)
                rows.Add(new DataRow().Set("x", labels[c]).Set("y", labels[r]).Set("value", values[r, c]));
        return rows;
    }

    // Gauge data: single metric
    private static readonly List<DataRow> GaugeData =
    [
        new DataRow().Set("label", "Health").Set("value", 75),
    ];

    // Box plot data: character stat distributions
    private static readonly List<DataRow> BoxPlotData =
    [
        new DataRow().Set("stat", "STR").Set("min", 20).Set("q1", 40).Set("median", 55).Set("q3", 70).Set("max", 95),
        new DataRow().Set("stat", "AGI").Set("min", 15).Set("q1", 35).Set("median", 50).Set("q3", 65).Set("max", 85),
        new DataRow().Set("stat", "INT").Set("min", 30).Set("q1", 50).Set("median", 65).Set("q3", 80).Set("max", 100),
        new DataRow().Set("stat", "END").Set("min", 10).Set("q1", 30).Set("median", 45).Set("q3", 60).Set("max", 75),
        new DataRow().Set("stat", "LCK").Set("min", 5).Set("q1", 25).Set("median", 40).Set("q3", 55).Set("max", 90),
    ];

    // Funnel data: conversion stages
    private static readonly List<DataRow> FunnelData =
    [
        new DataRow().Set("stage", "Visitors").Set("count", 10000),
        new DataRow().Set("stage", "Sign-ups").Set("count", 6200),
        new DataRow().Set("stage", "Trial").Set("count", 3800),
        new DataRow().Set("stage", "Paid").Set("count", 1500),
        new DataRow().Set("stage", "Retained").Set("count", 900),
    ];

    // Range area data: temperature hi/lo over a week
    private static readonly List<DataRow> RangeAreaData =
    [
        new DataRow().Set("day", "Mon").Set("high", 28).Set("lower", 18),
        new DataRow().Set("day", "Tue").Set("high", 30).Set("lower", 20),
        new DataRow().Set("day", "Wed").Set("high", 26).Set("lower", 17),
        new DataRow().Set("day", "Thu").Set("high", 32).Set("lower", 22),
        new DataRow().Set("day", "Fri").Set("high", 29).Set("lower", 19),
        new DataRow().Set("day", "Sat").Set("high", 34).Set("lower", 24),
        new DataRow().Set("day", "Sun").Set("high", 31).Set("lower", 21),
    ];

    // Timeline data: combat buff/debuff timeline
    private static readonly List<DataRow> TimelineData =
    [
        new DataRow().Set("buff", "Shield").Set("start", 0).Set("end", 5).Set("type", "Buff"),
        new DataRow().Set("buff", "Haste").Set("start", 2).Set("end", 8).Set("type", "Buff"),
        new DataRow().Set("buff", "Poison").Set("start", 3).Set("end", 7).Set("type", "Debuff"),
        new DataRow().Set("buff", "Regen").Set("start", 6).Set("end", 12).Set("type", "Buff"),
        new DataRow().Set("buff", "Stun").Set("start", 9).Set("end", 11).Set("type", "Debuff"),
    ];

    // Log scale data: entity count by level (exponential growth)
    private static readonly List<DataRow> LogScaleData = BuildLogScaleData();

    private static List<DataRow> BuildLogScaleData()
    {
        var rows = new List<DataRow>();
        var rng = new Random(99);
        var cats = new[] { "Mob", "Boss", "NPC" };
        foreach (var cat in cats)
        {
            double val = 1 + rng.NextDouble() * 5;
            for (int lv = 1; lv <= 10; lv++)
            {
                rows.Add(new DataRow().Set("level", (double)lv).Set("count", Math.Round(val, 1)).Set("type", cat));
                val *= 1.4 + rng.NextDouble() * 0.6;
            }
        }
        return rows;
    }

    // Treemap data: resource allocation
    private static readonly List<DataRow> TreemapData =
    [
        new DataRow().Set("category", "Gold").Set("amount", 35.0),
        new DataRow().Set("category", "Wood").Set("amount", 25.0),
        new DataRow().Set("category", "Stone").Set("amount", 18.0),
        new DataRow().Set("category", "Iron").Set("amount", 12.0),
        new DataRow().Set("category", "Food").Set("amount", 7.0),
        new DataRow().Set("category", "Gems").Set("amount", 3.0),
    ];

    // Violin data: damage distribution per weapon type
    private static readonly List<DataRow> ViolinData = BuildViolinData();

    private static List<DataRow> BuildViolinData()
    {
        var rows = new List<DataRow>();
        var rng = new Random(42);
        var weapons = new[] { "Sword", "Bow", "Staff", "Dagger" };
        var means   = new[] { 50.0, 35.0, 65.0, 40.0 };
        var spreads = new[] { 15.0, 10.0, 25.0, 8.0 };
        for (int w = 0; w < weapons.Length; w++)
        {
            for (int j = 0; j < 40; j++)
            {
                double val = means[w] + (rng.NextDouble() * 2 - 1) * spreads[w];
                rows.Add(new DataRow().Set("weapon", weapons[w]).Set("damage", Math.Round(val, 1)));
            }
        }
        return rows;
    }

    // Sunburst data: skill tree hierarchy
    private static readonly List<DataRow> SunburstData =
    [
        // Root categories
        new DataRow().Set("skill", "Attack").Set("points", 40.0).Set("parent", ""),
        new DataRow().Set("skill", "Defense").Set("points", 30.0).Set("parent", ""),
        new DataRow().Set("skill", "Magic").Set("points", 30.0).Set("parent", ""),
        // Attack children
        new DataRow().Set("skill", "Slash").Set("points", 15.0).Set("parent", "Attack"),
        new DataRow().Set("skill", "Thrust").Set("points", 15.0).Set("parent", "Attack"),
        new DataRow().Set("skill", "Combo").Set("points", 10.0).Set("parent", "Attack"),
        // Defense children
        new DataRow().Set("skill", "Block").Set("points", 18.0).Set("parent", "Defense"),
        new DataRow().Set("skill", "Parry").Set("points", 12.0).Set("parent", "Defense"),
        // Magic children
        new DataRow().Set("skill", "Fire").Set("points", 12.0).Set("parent", "Magic"),
        new DataRow().Set("skill", "Ice").Set("points", 10.0).Set("parent", "Magic"),
        new DataRow().Set("skill", "Lightning").Set("points", 8.0).Set("parent", "Magic"),
    ];

    // Sankey data: resource flow
    private static readonly List<DataRow> SankeyData =
    [
        new DataRow().Set("source", "Mine").Set("target", "Smelter").Set("amount", 30.0),
        new DataRow().Set("source", "Mine").Set("target", "Storage").Set("amount", 10.0),
        new DataRow().Set("source", "Forest").Set("target", "Sawmill").Set("amount", 25.0),
        new DataRow().Set("source", "Smelter").Set("target", "Forge").Set("amount", 20.0),
        new DataRow().Set("source", "Smelter").Set("target", "Market").Set("amount", 10.0),
        new DataRow().Set("source", "Sawmill").Set("target", "Forge").Set("amount", 15.0),
        new DataRow().Set("source", "Sawmill").Set("target", "Market").Set("amount", 10.0),
        new DataRow().Set("source", "Forge").Set("target", "Army").Set("amount", 25.0),
        new DataRow().Set("source", "Forge").Set("target", "Market").Set("amount", 10.0),
    ];

    // Chord data: faction trade relationships
    private static readonly List<DataRow> ChordData =
    [
        new DataRow().Set("source", "Human").Set("target", "Elf").Set("trade", 20.0),
        new DataRow().Set("source", "Human").Set("target", "Dwarf").Set("trade", 30.0),
        new DataRow().Set("source", "Elf").Set("target", "Dwarf").Set("trade", 15.0),
        new DataRow().Set("source", "Elf").Set("target", "Orc").Set("trade", 5.0),
        new DataRow().Set("source", "Dwarf").Set("target", "Orc").Set("trade", 10.0),
        new DataRow().Set("source", "Human").Set("target", "Orc").Set("trade", 8.0),
    ];

    // ── Godot lifecycle ───────────────────────────────────────

    public override void _Ready()
    {
        RegisterCharts();
        BuildUI();
        CallDeferred(nameof(InitializeChart));
    }

    public override void _ExitTree()
    {
        _canvas?.Dispose();
    }

    public override void _Process(double delta)
    {
        bool needsRedraw = false;
        float dt = (float)delta;

        // Tween-driven animation: check if any tween is active
        if (_anim.IsAnimating)
            needsRedraw = true;

        // Mouse tracking for interaction (with distance threshold to avoid per-frame jitter)
        var chart = GetCurrentChart();
        if (_chartArea != null && !_isExiting && chart != null)
        {
            var mousePos = _chartArea.GetLocalMousePosition();
            if (mousePos.DistanceTo(_lastMousePos) > 1f)
            {
                _lastMousePos = mousePos;
                // Crosshair follows the mouse — always redraw when position changes
                needsRedraw = true;

                // Single hit test per mouse move (reuse cached chart)
                var hit = chart.HitTest(mousePos);
                _lastHit = hit;
                // Legend/axis hits should not change hovered row index
                int newHover = hit is { Hit: true, MarkType: not "Legend" and not "XAxis" and not "YAxis" }
                    ? hit.RowIndex : -1;

                if (newHover != _hoveredRowIndex)
                {
                    _hoveredRowIndex = newHover;
                    // Use NotifyHoverChanged to fire OnHover events
                    chart.NotifyHoverChanged(newHover, hit);
                    _anim.AnimateHover(this, newHover >= 0 ? 1.05f : 1f);
                }

                // Update tooltip smooth follow + fade
                _tooltip.Update(dt, hit);
            }
            else
            {
                // Still update tooltip fade even when mouse hasn't moved
                _tooltip.Update(dt, _lastHit);
                if (_tooltip.IsVisible)
                    needsRedraw = true;
            }
        }

        if (needsRedraw)
            RedrawCurrentChart();

        _canvas?.Tick();
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }
            && _chartArea != null)
        {
            var chart = GetCurrentChart();
            if (chart == null) return;

            var localPos = _chartArea.GetLocalMousePosition();
            if (localPos.X >= 0 && localPos.X <= _canvasW
                && localPos.Y >= 0 && localPos.Y <= _canvasH)
            {
                // Chart.HandleClick manages _focusedSeries and _selectedRowIndex internally
                chart.HandleClick(localPos);
                RedrawCurrentChart();
            }
        }
    }

    // ── UI construction ───────────────────────────────────────

    private void BuildUI()
    {
        // Root layout container
        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(vbox);

        // Top bar: tabs + theme toggle
        var topBar = new HBoxContainer();
        vbox.AddChild(topBar);

        _tabBar = new TabBar
        {
            TabCloseDisplayPolicy = TabBar.CloseButtonDisplayPolicy.ShowNever,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        foreach (var (name, _) in _charts)
            _tabBar.AddTab(name);
        _tabBar.CurrentTab = 0;
        _tabBar.TabChanged += OnTabChanged;
        topBar.AddChild(_tabBar);

        _themeButton = new Button { Text = "Light" };
        _themeButton.Pressed += OnThemeToggle;
        topBar.AddChild(_themeButton);

        // Chart rendering area (fills remaining space)
        _chartArea = new Control
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        vbox.AddChild(_chartArea);
    }

    private void InitializeChart()
    {
        // Get actual chart area size after layout
        var size = _chartArea!.Size;
        _canvasW = (int)(size.X > 10 ? size.X : 780);
        _canvasH = (int)(size.Y > 10 ? size.Y : 530);

        _canvas = Canvas2DFactory.Create(_chartArea, _canvasW, _canvasH);

        // Start entry animation for the first chart
        if (_theme.EnableAnimation)
            _anim.StartEntry(this, GetSeriesCount(_currentTab));
        else
            _anim.Reset();
        RedrawCurrentChart();
    }

    // ── Tab switching ─────────────────────────────────────────

    private void OnTabChanged(long index)
    {
        int newTab = (int)index;
        if (newTab == _currentTab) return;

        // Start exit animation, then switch to new chart on completion
        if (_theme.EnableAnimation)
        {
            _isExiting = true;
            _anim.StartExit(this, () =>
            {
                _currentTab = newTab;
                _hoveredRowIndex = -1;
                _isExiting = false;
                _anim.StartEntry(this, GetSeriesCount(newTab));
                RedrawCurrentChart();
            });
        }
        else
        {
            _currentTab = newTab;
            _hoveredRowIndex = -1;
            _anim.Reset();
            RedrawCurrentChart();
        }
    }

    private void OnThemeToggle()
    {
        _isDarkTheme = !_isDarkTheme;
        _theme = _isDarkTheme ? ChartTheme.Dark() : ChartTheme.Light();
        _tooltip.Theme = _theme;
        if (_themeButton != null)
            _themeButton.Text = _isDarkTheme ? "Light" : "Dark";
        // Invalidate all persistent chart instances so they pick up the new theme
        _chartInstances = null;
        RedrawCurrentChart();
    }

    /// <summary>
    /// Get the number of series for a given chart tab index.
    /// </summary>
    private int GetSeriesCount(int tabIndex)
    {
        return tabIndex switch
        {
            0 => 2,  // Bar: Revenue + Cost
            1 => 3,  // MultiLine: ProductA/B/C
            2 => 3,  // Bubble: Alpha/Beta/Gamma
            3 => 1,  // Candlestick: single series
            4 => 1,  // Area: single series
            5 => 5,  // Pie: 5 damage types
            6 => 2,  // Radar: Alice + Bob
            7 => 2,  // Stacked Bar: Revenue + Cost
            8 => 3,  // Stacked Area: ProductA/B/C
            9 => 1,  // Heatmap: single matrix
            10 => 1, // Gauge: single metric
            11 => 1, // Horizontal Bar: Revenue only
            12 => 3, // Step Line: ProductA/B/C
            13 => 1, // Box Plot
            14 => 1, // Funnel
            15 => 1, // Range Area
            16 => 1, // Timeline
            17 => 3, // Log Scale: Mob/Boss/NPC
            18 => 1, // Treemap
            19 => 1, // Violin
            20 => 1, // Sunburst
            21 => 1, // Sankey
            22 => 1, // Chord
            23 => 1, // Lollipop
            24 => 1, // Waffle
            25 => 1, // Histogram
            26 => 1, // Diverging Heatmap
            _ => 1,
        };
    }

    /// <summary>
    /// Get (or lazily create) the persistent Chart instance for the current tab.
    /// </summary>
    private Chart? GetCurrentChart()
    {
        if (_canvas == null || _currentTab < 0 || _currentTab >= _charts.Count) return null;
        _chartInstances ??= new Chart?[_charts.Count];
        return _chartInstances[_currentTab] ??= _charts[_currentTab].Create();
    }

    /// <summary>
    /// Redraw the currently selected chart with animation and interaction state.
    /// </summary>
    private void RedrawCurrentChart()
    {
        if (_canvas == null) return;
        var chart = GetCurrentChart();
        if (chart == null) return;

        // Update per-frame state on the persistent instance
        chart
            .Animate(BuildAnimationContext())
            .Hover(_hoveredRowIndex)
            .Interaction(_lastMousePos);

        _canvas.BeginFrame();
        _canvas.Clear(_theme.BackgroundColor);
        chart.Render();

        // Tooltip overlay
        if (_theme.EnableTooltip)
            _tooltip.Draw(_canvas, _canvasW, _canvasH);

        _canvas.EndFrame();
    }

    /// <summary>
    /// Build the current animation context from AnimationController state.
    /// </summary>
    private AnimationContext BuildAnimationContext()
    {
        return new AnimationContext
        {
            EntryProgress          = _anim.EntryProgress,
            GlobalOpacity          = _anim.GlobalOpacity,
            HoverScale             = _anim.HoverScale,
            ExitProgress           = _anim.ExitProgress,
            DataTransitionProgress = _anim.DataTransitionProgress,
        };
    }

    /// <summary>
    /// Create a new Chart with common layout settings applied.
    /// </summary>
    private Chart NewChart(string? title = null)
    {
        var chart = new Chart(_canvas!)
        {
            Width = _canvasW, Height = _canvasH, OffsetX = 0, OffsetY = 0,
            PaddingLeft = 55, PaddingBottom = 45,
            Title = title,
        };
        chart.Theme(_theme);
        return chart;
    }

    // ── Chart draw methods ────────────────────────────────────

    private Chart CreateBarChart()
    {
        var revenueData = MonthlySales.ConvertAll(r =>
            new DataRow().Set("month", r.Get<string>("month"))
                         .Set("value", r.Get<int>("revenue"))
                         .Set("type", "Revenue"));

        var costData = MonthlySales.ConvertAll(r =>
            new DataRow().Set("month", r.Get<string>("month"))
                         .Set("value", r.Get<int>("cost"))
                         .Set("type", "Cost"));

        var combined = new List<DataRow>();
        combined.AddRange(revenueData);
        combined.AddRange(costData);

        var chart = NewChart("Monthly Revenue vs Cost")
        .Data(combined)
        .Mark(new IntervalMark { CornerRadius = 3, BarPadding = 0.25f, ShowLabel = true })
        .Encode(Channel.X,     "month")
        .Encode(Channel.Y,     "value")
        .Encode(Channel.Color, "type")
        .XAxis(new AxisConfig { Title = "Month", Description = "Monthly sales data for 2024" })
        .YAxis(new AxisConfig { Title = "Amount", Unit = "USD", Description = "Revenue and cost in US dollars" })
        .Legend(new LegendConfig { Position = LegendPosition.Top });

        // Demo: Custom background renderer — gradient effect
        chart.BackgroundRenderer = ctx =>
        {
            using var path  = ctx.Canvas.CreatePath();
            using var paint = ctx.Canvas.CreatePaint();
            // Draw base background
            path.RoundRect(ctx.OffsetX, ctx.OffsetY, ctx.Width, ctx.Height, 8f);
            paint.SetColor(ctx.BackgroundColor);
            ctx.Canvas.Fill(path, paint);
            // Subtle accent stripe at the top
            using var stripe = ctx.Canvas.CreatePath();
            stripe.RoundRect(ctx.OffsetX, ctx.OffsetY, ctx.Width, 4f, 8f);
            paint.SetColor(new Color(0.3f, 0.6f, 1f, 0.5f));
            ctx.Canvas.Fill(stripe, paint);
        };

        // Demo: Event logging
        chart.OnSelectionChanged += (_, e) =>
            GD.Print($"[BarChart] Selection: row {e.RowIndex}, series={e.SeriesKey}");

        return chart;
    }

    private Chart CreateMultiLine()
    {
        var chart = NewChart("Product Sales Trend (12 Months)")
        .Data(MultiSeries)
        .Mark(new LineMark { Smooth = true, ShowArea = false, StrokeWidth = 2.5f })
        .Encode(Channel.X,     "month")
        .Encode(Channel.Y,     "value")
        .Encode(Channel.Color, "series")
        .XAxis(new AxisConfig { Title = "Month" })
        .YAxis(new AxisConfig { Title = "Sales", Unit = "Units" })
        .Legend(new LegendConfig { Position = LegendPosition.Top });

        return chart;
    }

    private Chart CreateBubbleChart()
    {
        var chart = NewChart("Bubble Chart (Size = Market Share)")
        .Data(BubbleData)
        .Mark(new PointMark { DefaultRadius = 6f })
        .Encode(Channel.X,       "x")
        .Encode(Channel.Y,       "y")
        .Encode(Channel.Size,    "size")
        .Encode(Channel.Color,   "category")
        .Encode(Channel.Opacity, "confidence")
        .Scale(Channel.X, new LinearScale(0, 100))
        .Scale(Channel.Y, new LinearScale(0, 100));

        // Demo: Hover event logging
        chart.OnHover += (_, e) =>
        {
            if (e.RowIndex >= 0)
                GD.Print($"[Bubble] Hover: row {e.RowIndex}, series={e.SeriesKey}");
        };

        return chart;
    }

    private Chart CreateCandlestick()
    {
        return NewChart("Stock Price (20 Trading Days)")
        .Data(StockData)
        .Mark(new CandlestickMark
        {
            OpenField      = "open",
            HighField      = "high",
            LowField       = "low",
            CloseField     = "close",
            BodyWidthRatio = 0.55f,
            WickWidth      = 1.5f,
            CornerRadius   = 1f,
        })
        .Encode(Channel.X, "date");
    }

    private Chart CreateAreaChart()
    {
        var seriesA = MultiSeries.FindAll(r =>
            r.Get<string>("series") == "ProductA");

        return NewChart("ProductA Monthly Performance")
        .Data(seriesA)
        .Mark(new LineMark
        {
            Smooth      = true,
            ShowArea    = true,
            AreaOpacity = 0.25f,
            StrokeWidth = 3f,
        })
        .Encode(Channel.X, "month")
        .Encode(Channel.Y, "value")
        .Encode(Channel.Color, "constant:ProductA");
    }

    private Chart CreatePieChart()
    {
        return NewChart("Damage Distribution (Pie)")
        .Data(DamageDistribution)
        .Mark(new PieMark { ShowLabel = true, LabelDistance = 1.18f })
        .Encode(Channel.X,     "type")
        .Encode(Channel.Y,     "value")
        .Encode(Channel.Color, "type")
        .Legend(new LegendConfig { Position = LegendPosition.Top });
    }

    private Chart CreateRadarChart()
    {
        return NewChart("Character Stats (Radar)")
        .Data(RadarData)
        .Mark(new RadarMark { FillOpacity = 0.2f, StrokeWidth = 2.5f, PointRadius = 4f })
        .Encode(Channel.X,     "stat")
        .Encode(Channel.Y,     "value")
        .Encode(Channel.Color, "player")
        .Scale(Channel.Y, new LinearScale(0, 100))
        .Legend(new LegendConfig { Position = LegendPosition.Top });
    }

    private Chart CreateStackedBar()
    {
        var revenueData = MonthlySales.ConvertAll(r =>
            new DataRow().Set("month", r.Get<string>("month"))
                         .Set("value", r.Get<int>("revenue"))
                         .Set("type", "Revenue"));

        var costData = MonthlySales.ConvertAll(r =>
            new DataRow().Set("month", r.Get<string>("month"))
                         .Set("value", r.Get<int>("cost"))
                         .Set("type", "Cost"));

        var combined = new List<DataRow>();
        combined.AddRange(revenueData);
        combined.AddRange(costData);

        var chart = NewChart("Monthly Revenue + Cost (Stacked)")
        .Data(combined)
        .Mark(new IntervalMark { CornerRadius = 2, BarPadding = 0.25f, Stack = StackMode.Stack })
        .Encode(Channel.X,     "month")
        .Encode(Channel.Y,     "value")
        .Encode(Channel.Color, "type")
        .Legend(new LegendConfig { Position = LegendPosition.Top });

        return chart;
    }

    private Chart CreateStackedArea()
    {
        return NewChart("Product Sales (Stacked Area)")
        .Data(MultiSeries)
        .Mark(new LineMark
        {
            Smooth      = true,
            ShowArea    = true,
            AreaOpacity = 0.4f,
            StrokeWidth = 2f,
            Stack       = StackMode.Stack,
        })
        .Encode(Channel.X,     "month")
        .Encode(Channel.Y,     "value")
        .Encode(Channel.Color, "series")
        .Legend(new LegendConfig { Position = LegendPosition.Top });
    }

    private Chart CreateHeatmap()
    {
        return NewChart("Stat Correlation (Heatmap)")
        .Data(HeatmapData)
        .Mark(new HeatmapMark { CellGap = 2f, CornerRadius = 3f, ShowLabel = true })
        .Encode(Channel.X,     "x")
        .Encode(Channel.Y,     "y")
        .Encode(Channel.Color, "value");
    }

    private Chart CreateGauge()
    {
        return NewChart("Player Health (Gauge)")
        .Data(GaugeData)
        .Mark(new GaugeMark
        {
            ArcWidth        = 0.14f,
            ShowCenterLabel = true,
            ValueColor      = new Color(0.29f, 0.85f, 0.60f),
        })
        .Encode(Channel.X, "label")
        .Encode(Channel.Y, "value")
        .Scale(Channel.Y, new LinearScale(0, 100));
    }

    private Chart CreateHorizontalBar()
    {
        // Horizontal bar: single series (Revenue only) to avoid overlap
        var revenueData = MonthlySales.ConvertAll(r =>
            new DataRow().Set("month", r.Get<string>("month"))
                         .Set("value", r.Get<int>("revenue")));

        return NewChart("Monthly Revenue (Horizontal)")
        .Data(revenueData)
        .Mark(new IntervalMark
        {
            CornerRadius = 3,
            BarPadding   = 0.25f,
            Orientation  = BarOrientation.Horizontal,
        })
        .Encode(Channel.X,     "value")
        .Encode(Channel.Y,     "month")
        .XAxis(new AxisConfig { Title = "Amount", Unit = "USD" })
        .YAxis(new AxisConfig { Title = "Month" });
    }

    private Chart CreateStepLine()
    {
        return NewChart("Product Sales (Step Line)")
        .Data(MultiSeries)
        .Mark(new LineMark
        {
            Smooth      = false,
            ShowArea    = false,
            StrokeWidth = 2.5f,
            Step        = StepMode.After,
        })
        .Encode(Channel.X,     "month")
        .Encode(Channel.Y,     "value")
        .Encode(Channel.Color, "series")
        .XAxis(new AxisConfig { Title = "Month" })
        .YAxis(new AxisConfig { Title = "Sales", Unit = "Units" });
    }

    private Chart CreateBoxPlot()
    {
        return NewChart("Character Stat Distributions (Box Plot)")
        .Data(BoxPlotData)
        .Mark(new BoxMark
        {
            MinField     = "min",
            Q1Field      = "q1",
            MedianField  = "median",
            Q3Field      = "q3",
            MaxField     = "max",
            BoxWidthRatio = 0.5f,
            CornerRadius  = 2f,
        })
        .Encode(Channel.X, "stat")
        .Encode(Channel.Y, "median")
        .Scale(Channel.Y, new LinearScale(0, 100))
        .XAxis(new AxisConfig { Title = "Stat" })
        .YAxis(new AxisConfig { Title = "Value" });
    }

    private Chart CreateFunnel()
    {
        return NewChart("Conversion Funnel")
        .Data(FunnelData)
        .Mark(new FunnelMark { ShowLabel = true })
        .Encode(Channel.X, "stage")
        .Encode(Channel.Y, "count");
    }

    // ── 4th priority charts ──────────────────────────────────

    private Chart CreateTreemap()
    {
        return NewChart("Resource Allocation")
        .Data(TreemapData)
        .Mark(new TreemapMark { ShowLabel = true, CellGap = 3f, CornerRadius = 4f })
        .Encode(Channel.X, "category")
        .Encode(Channel.Y, "amount")
        .Encode(Channel.Color, "category");
    }

    private Chart CreateViolin()
    {
        return NewChart("Damage Distribution by Weapon")
        .Data(ViolinData)
        .Mark(new ViolinMark { BinCount = 15, ShowMedian = true, ShowBox = true })
        .Encode(Channel.X, "weapon")
        .Encode(Channel.Y, "damage")
        .Encode(Channel.Color, "weapon")
        .XAxis(new AxisConfig { Title = "Weapon" })
        .YAxis(new AxisConfig { Title = "Damage" });
    }

    private Chart CreateSunburst()
    {
        return NewChart("Skill Tree Distribution")
        .Data(SunburstData)
        .Mark(new SunburstMark { ShowLabel = true })
        .Encode(Channel.X, "skill")
        .Encode(Channel.Y, "points")
        .Encode(Channel.Color, "skill");
    }

    private Chart CreateSankey()
    {
        return NewChart("Resource Flow")
        .Data(SankeyData)
        .Mark(new SankeyMark { ShowLabel = true, NodeWidth = 14f })
        .Encode(Channel.Y, "amount")
        .Encode(Channel.Color, "source");
    }

    private Chart CreateChord()
    {
        return NewChart("Faction Trade Relations")
        .Data(ChordData)
        .Mark(new ChordMark { ShowLabel = true })
        .Encode(Channel.Y, "trade")
        .Encode(Channel.Color, "source");
    }

    private Chart CreateLollipop()
    {
        var revenueData = MonthlySales.ConvertAll(r =>
            new DataRow().Set("month", r.Get<string>("month"))
                         .Set("value", r.Get<int>("revenue")));

        return NewChart("Monthly Revenue (Lollipop)")
        .Data(revenueData)
        .Mark(new LollipopMark { DotRadius = 7f, StemWidth = 2.5f })
        .Encode(Channel.X, "month")
        .Encode(Channel.Y, "value")
        .Encode(Channel.Color, "constant:Revenue")
        .XAxis(new AxisConfig { Title = "Month" })
        .YAxis(new AxisConfig { Title = "Revenue", Unit = "USD" });
    }

    private Chart CreateWaffle()
    {
        return NewChart("Damage Distribution (Waffle)")
        .Data(DamageDistribution)
        .Mark(new WaffleMark { TotalCells = 100, Columns = 10, CellGap = 3f, CellRadius = 3f })
        .Encode(Channel.X, "type")
        .Encode(Channel.Y, "value")
        .Encode(Channel.Color, "type");
    }

    private Chart CreateHistogram()
    {
        // Use damage distribution raw values via BinTransform
        var rawData = ViolinData.FindAll(r => r.Get<string>("weapon") == "Sword");
        var swordData = rawData.ConvertAll(r =>
            new DataRow().Set("value", r.Get<double>("damage")));

        return NewChart("Sword Damage Histogram")
        .Data(swordData)
        .Transform(new BinTransform { Field = "value", BinCount = 12 })
        .Mark(new IntervalMark { CornerRadius = 2, BarPadding = 0.05f })
        .Encode(Channel.X, "BinMid")
        .Encode(Channel.Y, "Count")
        .XAxis(new AxisConfig { Title = "Damage" })
        .YAxis(new AxisConfig { Title = "Frequency" });
    }

    private Chart CreateDivergingHeatmap()
    {
        return NewChart("Stat Correlation (Diverging)")
        .Data(HeatmapData)
        .Mark(new HeatmapMark { CellGap = 2f, CornerRadius = 3f, ShowLabel = true })
        .Encode(Channel.X, "x")
        .Encode(Channel.Y, "y")
        .Encode(Channel.Color, "value")
        .Scale(Channel.Color, new DivergingColorScale(-1, 1));
    }

    private Chart CreateRangeArea()
    {
        return NewChart("Weekly Temperature Range")
        .Data(RangeAreaData)
        .Mark(new RangeAreaMark
        {
            LowerField      = "lower",
            FillOpacity     = 0.3f,
            ShowBorderLines = true,
            StrokeWidth     = 2f,
            Smooth          = true,
        })
        .Encode(Channel.X, "day")
        .Encode(Channel.Y, "high")
        .Scale(Channel.Y, new LinearScale(10, 40))
        .XAxis(new AxisConfig { Title = "Day" })
        .YAxis(new AxisConfig { Title = "Temperature", Unit = "°C" });
    }

    private Chart CreateTimeline()
    {
        return NewChart("Combat Buff Timeline")
        .Data(TimelineData)
        .Mark(new TimelineMark
        {
            StartField     = "start",
            EndField        = "end",
            BarHeightRatio = 0.6f,
            CornerRadius   = 4f,
            ShowLabel      = true,
        })
        .Encode(Channel.Y,     "buff")
        .Encode(Channel.Color, "type")
        .Scale(Channel.X, new LinearScale(0, 14))
        .XAxis(new AxisConfig { Title = "Time", Unit = "s" })
        .YAxis(new AxisConfig { Title = "Buff" });
    }

    private Chart CreateLogScale()
    {
        return NewChart("Entity Count by Level (Log Scale)")
        .Data(LogScaleData)
        .Mark(new PointMark { DefaultRadius = 5f })
        .Encode(Channel.X,     "level")
        .Encode(Channel.Y,     "count")
        .Encode(Channel.Color, "type")
        .Scale(Channel.X, new LinearScale(1, 10))
        .Scale(Channel.Y, new LogScale(1, 10000))
        .XAxis(new AxisConfig { Title = "Level" })
        .YAxis(new AxisConfig { Title = "Count" });
    }

    private Chart CreateCompositeChart()
    {
        var barMark = new IntervalMark { CornerRadius = 3, BarPadding = 0.3f };

        var lineMark = new LineMark
        {
            Smooth = true, StrokeWidth = 2.5f, ShowArea = false,
            YChannel = Channel.Y2,
        };

        return NewChart("Revenue (Bar) vs Cost (Line)")
        .Data(MonthlySales)
        .Mark(barMark)
        .Mark(lineMark)
        .Encode(Channel.X,  "month")
        .Encode(Channel.Y,  "revenue")
        .Encode(Channel.Y2, "cost")
        .XAxis(new AxisConfig { Title = "Month" })
        .YAxis(new AxisConfig { Title = "Revenue", Unit = "USD" })
        .Y2Axis(new AxisConfig { Title = "Cost", Unit = "USD" });
    }
}

