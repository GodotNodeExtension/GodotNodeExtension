namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Default layout metrics of a chart. Named in one place so the values are documented and can be
/// referenced by hosts and tests instead of being repeated as literals.
/// </summary>
public static class ChartDefaults
{
    /// <summary>Space reserved on the left for the Y axis labels.</summary>
    public const float PaddingLeft = 50f;

    /// <summary>Space reserved on the right.</summary>
    public const float PaddingRight = 20f;

    /// <summary>Space reserved at the top.</summary>
    public const float PaddingTop = 20f;

    /// <summary>Space reserved at the bottom for the X axis labels.</summary>
    public const float PaddingBottom = 40f;

    /// <summary>Default chart width in pixels.</summary>
    public const float Width = 600f;

    /// <summary>Default chart height in pixels.</summary>
    public const float Height = 400f;
}
