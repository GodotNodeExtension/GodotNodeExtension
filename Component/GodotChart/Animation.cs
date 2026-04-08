using System;
using Godot;

namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Easing function types for chart animations.
/// </summary>
public enum EaseType
{
    Linear,
    EaseInQuad,
    EaseOutQuad,
    EaseOutCubic,
    EaseInOutCubic,
    EaseOutBack,
    EaseOutElastic,
}

/// <summary>
/// Collection of easing functions for smooth animations.
/// </summary>
public static class Ease
{
    /// <summary>
    /// Apply the specified easing function to a linear progress value.
    /// </summary>
    public static float Apply(float t, EaseType type) => type switch
    {
        EaseType.Linear         => t,
        EaseType.EaseInQuad     => t * t,
        EaseType.EaseOutQuad    => t * (2f - t),
        EaseType.EaseOutCubic   => 1f - MathF.Pow(1f - t, 3f),
        EaseType.EaseInOutCubic => t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f,
        EaseType.EaseOutBack    => 1f + 2.70158f * MathF.Pow(t - 1f, 3f) + 1.70158f * MathF.Pow(t - 1f, 2f),
        EaseType.EaseOutElastic => t <= 0f ? 0f : t >= 1f ? 1f
            : MathF.Pow(2f, -10f * t) * MathF.Sin((t * 10f - 0.75f) * (2f * MathF.PI / 3f)) + 1f,
        _ => t,
    };
}

/// <summary>
/// Controls all chart animation tweens: entry, exit, hover, and data transition.
/// Uses Godot Tween API to drive animation progress values.
/// </summary>
public class AnimationController
{
    private Tween? _entryTween;
    private Tween? _hoverTween;
    private Tween? _dataTween;
    private Tween? _exitTween;

    // ── Entry animation ──────────────────────────────────────

    /// <summary>Overall entry progress [0,1].</summary>
    public float EntryProgress { get; private set; } = 1f;

    /// <summary>Per-series entry progress, staggered by delay.</summary>
    public float[] SeriesProgress { get; private set; } = Array.Empty<float>();

    /// <summary>Global opacity [0,1] for fade-in/out effect.</summary>
    public float GlobalOpacity { get; private set; } = 1f;

    /// <summary>Duration of the entry animation in seconds.</summary>
    public float EntryDuration { get; set; } = 0.6f;

    /// <summary>Delay between each series start in seconds.</summary>
    public float SeriesStagger { get; set; } = 0.1f;

    /// <summary>
    /// Maximum number of data elements for animation to be enabled.
    /// When total data count exceeds this threshold, all animations are skipped
    /// and elements are rendered at their final state immediately.
    /// Default: 2000 (same as ECharts default).
    /// </summary>
    public int AnimationThreshold { get; set; } = 2000;

    /// <summary>Whether any animation is currently active.</summary>
    public bool IsAnimating =>
        (_entryTween?.IsRunning() ?? false) ||
        (_hoverTween?.IsRunning() ?? false) ||
        (_dataTween?.IsRunning() ?? false) ||
        (_exitTween?.IsRunning() ?? false);

    // ── Hover micro-animation ────────────────────────────────

    /// <summary>Hover scale factor for the currently hovered element.</summary>
    public float HoverScale { get; private set; } = 1f;

    // ── Data transition ──────────────────────────────────────

    /// <summary>Data transition progress [0,1]. Used to lerp between old and new data.</summary>
    public float DataTransitionProgress { get; private set; } = 1f;

    // ── Exit animation ───────────────────────────────────────

    /// <summary>Exit progress [0,1]. 0 = fully visible, 1 = fully exited.</summary>
    public float ExitProgress { get; private set; }

    /// <summary>Duration of the exit animation in seconds.</summary>
    public float ExitDuration { get; set; } = 0.3f;

    /// <summary>
    /// Check if animation should be enabled based on element count.
    /// </summary>
    public bool ShouldAnimate(int elementCount) => elementCount <= AnimationThreshold;

    /// <summary>
    /// Start entry animation for the given number of series.
    /// Creates staggered tweens for each series + a global opacity fade-in.
    /// If totalElementCount exceeds AnimationThreshold, skips animation.
    /// </summary>
    public void StartEntry(Node owner, int seriesCount, int totalElementCount = 0)
    {
        _entryTween?.Kill();
        ExitProgress = 0f;

        if (!ShouldAnimate(totalElementCount))
        {
            EntryProgress = 1f;
            GlobalOpacity = 1f;
            var arr = new float[Math.Max(1, seriesCount)];
            Array.Fill(arr, 1f);
            SeriesProgress = arr;
            return;
        }

        EntryProgress = 0f;
        GlobalOpacity = 0f;
        SeriesProgress = new float[Math.Max(1, seriesCount)];

        _entryTween = owner.CreateTween();
        _entryTween.SetParallel();

        // Global entry progress: 0 -> 1, Cubic EaseOut
        _entryTween.TweenMethod(
            Callable.From((float v) => EntryProgress = v),
            0f, 1f, EntryDuration
        ).SetTrans(Tween.TransitionType.Cubic)
         .SetEase(Tween.EaseType.Out);

        // Global opacity fade-in: 0 -> 1, Linear
        _entryTween.TweenMethod(
            Callable.From((float v) => GlobalOpacity = v),
            0f, 1f, EntryDuration * 0.5f
        ).SetTrans(Tween.TransitionType.Linear);

        // Per-series staggered progress
        for (int i = 0; i < SeriesProgress.Length; i++)
        {
            float delay = i * SeriesStagger;
            int idx = i;
            _entryTween.TweenMethod(
                Callable.From((float v) => { if (idx < SeriesProgress.Length) SeriesProgress[idx] = v; }),
                0f, 1f, EntryDuration
            ).SetDelay(delay)
             .SetTrans(Tween.TransitionType.Cubic)
             .SetEase(Tween.EaseType.Out);
        }
    }

    /// <summary>
    /// Animate hover scale: smoothly transition to targetScale using Back easing.
    /// </summary>
    public void AnimateHover(Node owner, float targetScale)
    {
        _hoverTween?.Kill();
        _hoverTween = owner.CreateTween();
        _hoverTween.TweenMethod(
            Callable.From((float v) => HoverScale = v),
            HoverScale, targetScale, 0.15f
        ).SetTrans(Tween.TransitionType.Back)
         .SetEase(Tween.EaseType.Out);
    }

    /// <summary>
    /// Start a data transition animation. During transition, marks should lerp
    /// between old and new data values using DataTransitionProgress.
    /// </summary>
    public void StartDataTransition(Node owner, float duration = 0.4f)
    {
        _dataTween?.Kill();
        DataTransitionProgress = 0f;
        _dataTween = owner.CreateTween();
        _dataTween.TweenMethod(
            Callable.From((float v) => DataTransitionProgress = v),
            0f, 1f, duration
        ).SetTrans(Tween.TransitionType.Cubic)
         .SetEase(Tween.EaseType.InOut);
    }

    /// <summary>
    /// Start exit animation. On completion calls the provided callback
    /// (e.g. to dispose old chart and start new entry animation).
    /// </summary>
    public void StartExit(Node owner, Action? onComplete = null)
    {
        _exitTween?.Kill();
        ExitProgress = 0f;

        _exitTween = owner.CreateTween();
        _exitTween.SetParallel();

        // Fade out: GlobalOpacity 1 -> 0
        _exitTween.TweenMethod(
            Callable.From((float v) => GlobalOpacity = v),
            GlobalOpacity, 0f, ExitDuration
        ).SetTrans(Tween.TransitionType.Linear);

        // Shrink: ExitProgress 0 -> 1
        _exitTween.TweenMethod(
            Callable.From((float v) => ExitProgress = v),
            0f, 1f, ExitDuration
        ).SetTrans(Tween.TransitionType.Cubic)
         .SetEase(Tween.EaseType.In);

        if (onComplete != null)
        {
            _exitTween.SetParallel(false);
            _exitTween.TweenCallback(Callable.From(onComplete));
        }
    }

    /// <summary>
    /// Reset all animation state to defaults (no animation).
    /// </summary>
    public void Reset()
    {
        _entryTween?.Kill();
        _hoverTween?.Kill();
        _dataTween?.Kill();
        _exitTween?.Kill();

        EntryProgress = 1f;
        GlobalOpacity = 1f;
        HoverScale = 1f;
        DataTransitionProgress = 1f;
        ExitProgress = 0f;
        SeriesProgress = Array.Empty<float>();
    }
}

/// <summary>
/// Animation parameters passed to marks during rendering.
/// Built from AnimationController state each frame.
/// </summary>
public class AnimationContext
{
    /// <summary>Entry progress for this specific series [0,1].</summary>
    public float EntryProgress { get; init; } = 1f;

    /// <summary>Global opacity [0,1].</summary>
    public float GlobalOpacity { get; init; } = 1f;

    /// <summary>Hover scale factor for the current element.</summary>
    public float HoverScale { get; init; } = 1f;

    /// <summary>Exit progress [0,1]. 0 = fully visible, 1 = fully exited.</summary>
    public float ExitProgress { get; init; }

    /// <summary>
    /// Data transition progress [0,1]. If less than 1, marks should lerp
    /// from PreviousDataRow to current DataRow.
    /// </summary>
    public float DataTransitionProgress { get; init; } = 1f;

    /// <summary>Default instance with no animation applied.</summary>
    public static AnimationContext Default { get; } = new();
}

