namespace GodotNodeExtension.Tests.GodotChart.Support;

using System.Collections.Generic;
using GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Mark that draws nothing but records the context it was rendered with.
/// Used to observe what the chart pipeline hands to a mark.
/// </summary>
public sealed class CapturingMark : Mark
{
    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <summary>Number of rows in the last rendered context.</summary>
    public int LastDataCount { get; private set; } = -1;

    /// <summary>The layout version of the last rendered context.</summary>
    public int LastLayoutVersion { get; private set; } = -1;

    /// <summary>Type name of the Y scale of the last rendered context, or null when unset.</summary>
    public string? LastYScaleType { get; private set; }

    /// <summary>Lower bound of the linear Y scale of the last render (NaN for other scale types).</summary>
    public double LastYScaleMin { get; private set; } = double.NaN;

    /// <summary>Upper bound of the linear Y scale of the last render (NaN for other scale types).</summary>
    public double LastYScaleMax { get; private set; } = double.NaN;

    /// <summary>Type name of the X scale of the last rendered context, or null when unset.</summary>
    public string? LastXScaleType { get; private set; }

    /// <summary>Type name of the Color scale of the last rendered context, or null when unset.</summary>
    public string? LastColorScaleType { get; private set; }

    /// <summary><c>ctx.AnimationProgress</c> of the last render.</summary>
    public float LastAnimationProgress { get; private set; } = -1f;

    /// <summary><c>ctx.Animation.EntryProgress</c> of the last render.</summary>
    public float LastEntryProgress { get; private set; } = -1f;

    /// <summary>Full animation context of the last render (opacity, hover scale, exit/transition progress).</summary>
    public AnimationContext LastAnimation { get; private set; } = AnimationContext.Default;

    /// <summary>Plot area of the last render.</summary>
    public PlotArea? LastPlot { get; private set; }

    /// <summary><c>ctx.FocusedSeries</c> of the last render.</summary>
    public string? LastFocusedSeries { get; private set; }

    /// <summary><c>ctx.HiddenSeries</c> of the last render (null when nothing is hidden).</summary>
    public IReadOnlySet<string>? LastHiddenSeries { get; private set; }

    /// <summary><c>ctx.SelectedRowIndex</c> of the last render.</summary>
    public int LastSelectedRowIndex { get; private set; } = -1;

    /// <summary><c>ctx.DataVersion</c> of the last render.</summary>
    /// <summary>True when the last render received a non-null theme.</summary>
    public bool LastThemeIsNull => LastTheme == null;

    /// <summary><c>ctx.Theme</c> of the last render.</summary>
    public ChartTheme? LastTheme { get; private set; }

    /// <summary>How often <see cref="Mark.ContributeScales"/> was called.</summary>
    public int ContributeScalesCount { get; private set; }

    /// <summary>How often Render was called.</summary>
    public int RenderCount { get; private set; }

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        RenderCount++;
        LastDataCount = ctx.Data.Count;
        LastLayoutVersion = ctx.LayoutVersion;
        LastYScaleType = ctx.Scales.TryGet(YChannel)?.GetType().Name;
        if (ctx.Scales.TryGet(YChannel) is LinearScale linearY)
        {
            LastYScaleMin = linearY.Min;
            LastYScaleMax = linearY.Max;
        }
        LastXScaleType = ctx.Scales.TryGet(Channel.X)?.GetType().Name;
        LastColorScaleType = ctx.Scales.TryGet(Channel.Color)?.GetType().Name;
        LastAnimationProgress = ctx.AnimationProgress;
        LastEntryProgress = ctx.Animation.EntryProgress;
        LastAnimation = ctx.Animation;
        LastPlot = ctx.Plot;
        LastFocusedSeries = ctx.FocusedSeries;
        LastHiddenSeries = ctx.HiddenSeries;
        LastSelectedRowIndex = ctx.SelectedRowIndex;
        LastTheme = ctx.Theme;
    }

    /// <inheritdoc />
    public override void ContributeScales(ScaleSet scales, EncodeSet encodes, List<DataRow> data)
    {
        ContributeScalesCount++;
        base.ContributeScales(scales, encodes, data);
    }
}
