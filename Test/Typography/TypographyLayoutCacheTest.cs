namespace GodotNodeExtension.Tests.Typography;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for the compile/execute split: measuring and deciding boundaries is the expensive,
/// width-independent half, and a resize must re-run only the cheap half.
/// <para>
/// The split is asserted through the cache identity and the phase timings rather than through wall-clock
/// thresholds, so the test states the property ("the compile half did not run again") instead of a
/// machine-dependent number.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyLayoutCacheTest
{
    /// <summary>
    /// A width-only relayout reuses the prepared content and the boundaries, and reports zero compile
    /// time; a change that can affect a boundary decision rebuilds them.
    /// </summary>
    [TestCase]
    public void WidthOnlyRelayoutReusesTheCompilePhase()
    {
        using var engine = new TypographyEngine(Settings(240f, languageTag: "zh-Hans"));
        var elements = Elements();

        engine.PrepareAndLayout(elements.AsSpan(), out _);
        var boundaries = engine.LastBoundaries;

        AssertThat(boundaries.Count > 0).IsTrue();
        AssertThat(engine.LastTimings.PrepareMs > 0d).OverrideFailureMessage(
            "a full layout must measure text").IsTrue();
        AssertThat(engine.LastTimings.BoundaryMs > 0d).OverrideFailureMessage(
            "a full layout must build boundaries").IsTrue();

        // Same language, different width: the compile half must not run again.
        engine.Settings = Settings(200f, languageTag: "zh-Hans");
        engine.Layout(engine.CurrentPreparedContent!, out _);

        AssertThat(ReferenceEquals(boundaries, engine.LastBoundaries)).OverrideFailureMessage(
            "a width-only relayout must reuse the compiled boundaries").IsTrue();
        AssertThat(engine.LastTimings.PrepareMs).OverrideFailureMessage(
            "a width-only relayout must not re-measure text").IsEqual(0d);
        AssertThat(engine.LastTimings.BoundaryMs).OverrideFailureMessage(
            "a width-only relayout must not rebuild boundaries").IsEqual(0d);
        AssertThat(engine.LastTimings.BreakMs > 0d).OverrideFailureMessage(
            "the execute half must still run — this is what makes the cache a speed-up and not a no-op")
            .IsTrue();

        // Switching the script-spacing rule changes what a boundary means, so it must rebuild.
        engine.Settings = Settings(200f, languageTag: "zh-Hans", cjkLatinSpacing: false);
        engine.Layout(engine.CurrentPreparedContent!, out _);

        AssertThat(ReferenceEquals(boundaries, engine.LastBoundaries)).OverrideFailureMessage(
            "a change that affects boundary decisions must rebuild them").IsFalse();
        AssertThat(engine.LastTimings.BoundaryMs > 0d).IsTrue();
    }

    /// <summary>
    /// The boundary cache key must cover every input that can change a decision. A different language
    /// (a different profile) is one of them even when the values happen to match today, because the
    /// profile is what a later step aligns with the script conventions.
    /// </summary>
    [TestCase]
    public void BoundaryRebuildFollowsTheProfileAndItsRules()
    {
        using var engine = new TypographyEngine(Settings(240f, languageTag: "zh-Hans"));
        var elements = Elements();

        engine.PrepareAndLayout(elements.AsSpan(), out _);
        var hanBoundaries = engine.LastBoundaries;

        engine.Settings = Settings(240f, languageTag: "zh-Hant");
        engine.Layout(engine.CurrentPreparedContent!, out _);
        AssertThat(ReferenceEquals(hanBoundaries, engine.LastBoundaries)).OverrideFailureMessage(
            "a different profile must be able to change the decisions").IsFalse();

        var traditional = engine.LastBoundaries;
        engine.Settings = Settings(240f, languageTag: "zh-Hant");
        engine.Layout(engine.CurrentPreparedContent!, out _);
        AssertThat(ReferenceEquals(traditional, engine.LastBoundaries)).OverrideFailureMessage(
            "re-resolving the same profile must reuse the boundaries").IsTrue();
    }

    /// <summary>
    /// New measurement invalidates the cache: the boundaries describe clusters that the new measurement
    /// may not have produced.
    /// </summary>
    [TestCase]
    public void MeasuringAgainInvalidatesTheBoundaries()
    {
        using var engine = new TypographyEngine(Settings(240f));
        var elements = Elements();

        engine.PrepareAndLayout(elements.AsSpan(), out _);
        var boundaries = engine.LastBoundaries;

        AssertThat(engine.LastBoundaries.Count > 0).IsTrue();
        engine.Prepare(elements.AsSpan());
        AssertThat(ReferenceEquals(boundaries, engine.LastBoundaries)).IsFalse();
    }

    // ── Helpers ──

    private static DrawElement[] Elements() =>
    [
        Text("在Godot引擎中使用C#进行开发时，可以利用SkiaSharp渲染中文与Latin混排的文本。"),
        Text("第二段文字用于验证多段落下的缓存复用行为。"),
    ];

    private static DrawElement Text(string text) => new()
    {
        Type = DrawElement.ElementType.Text,
        Text = text,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
        Color = new Color(0.1f, 0.1f, 0.1f),
    };

    private static TypographySettings Settings(
        float maxWidth,
        string? languageTag = null,
        bool? cjkLatinSpacing = null) => new()
        {
            MaxWidth = maxWidth,
            LanguageTag = languageTag,
            EnableCjkLatinSpacing = cjkLatinSpacing,
        };
}
