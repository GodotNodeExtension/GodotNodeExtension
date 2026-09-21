namespace GodotNodeExtension.Tests.GodotChart.Support;

using System;
using System.Collections.Generic;
using Godot;
using GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Mark with a callback that throws on purpose, one stage at a time (<see cref="ThrowFrom"/>): the render
/// stages are isolated, so a case can pin exactly which failure the frame survives - and that a host-facing
/// call like <see cref="Mark.HitTest"/> is <i>not</i> isolated.
/// </summary>
public sealed class ThrowingMark : Mark
{
    /// <summary>The callback (or callbacks) that throw.</summary>
    [Flags]
    public enum Callback
    {
        /// <summary><see cref="Mark.Render"/>, the stage the original fixture covered.</summary>
        Render = 1,

        /// <summary><see cref="Mark.ContributeScales"/>.</summary>
        ContributeScales = 2,

        /// <summary><see cref="Mark.HitTest"/>.</summary>
        HitTest = 4,

        /// <summary><see cref="Mark.RenderOverlay"/>.</summary>
        RenderOverlay = 8,
    }

    /// <summary>Which callback throws. Defaults to <see cref="Callback.Render"/>.</summary>
    public Callback ThrowFrom { get; init; } = Callback.Render;

    /// <inheritdoc />
    public override MarkCoordinate Coordinate => MarkCoordinate.Cartesian;

    /// <inheritdoc />
    public override void ContributeScales(ScaleSet scales, EncodeSet encodes, List<DataRow> data)
    {
        if (ThrowFrom.HasFlag(Callback.ContributeScales))
            throw new InvalidOperationException("intentional ContributeScales failure for tests");
    }

    /// <inheritdoc />
    public override HitResult? HitTest(MarkContext ctx, Vector2 screenPos)
    {
        if (ThrowFrom.HasFlag(Callback.HitTest))
            throw new InvalidOperationException("intentional HitTest failure for tests");
        return null;
    }

    /// <inheritdoc />
    public override void RenderOverlay(MarkContext ctx)
    {
        if (ThrowFrom.HasFlag(Callback.RenderOverlay))
            throw new InvalidOperationException("intentional RenderOverlay failure for tests");
    }

    /// <inheritdoc />
    public override void Render(MarkContext ctx)
    {
        if (ThrowFrom.HasFlag(Callback.Render))
            throw new InvalidOperationException("intentional failure for tests");
    }
}
