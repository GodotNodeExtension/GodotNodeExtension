namespace GodotNodeExtension.Tests.GodotChart.Support;

using GodotNodeExtension.Component.GodotChart.Canvas;

/// <summary>
/// Minimal <see cref="IImageHandle"/> for tooltip tests (the fake canvas cannot decode images).
/// </summary>
public sealed class FakeImageHandle(int width, int height) : IImageHandle
{
    /// <inheritdoc />
    public int Width { get; } = width;

    /// <inheritdoc />
    public int Height { get; } = height;

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
