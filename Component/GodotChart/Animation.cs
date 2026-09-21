using System;
using Godot;

namespace GodotNodeExtension.Component.GodotChart;

/// <summary>
/// Easing function types for chart animations.
/// </summary>
public enum EaseType
{
    /// <summary>Constant speed.</summary>
    Linear,
    /// <summary>Quadratic acceleration from a standstill.</summary>
    EaseInQuad,
    /// <summary>Quadratic deceleration into the end state.</summary>
    EaseOutQuad,
    /// <summary>Cubic deceleration into the end state (the default for entries).</summary>
    EaseOutCubic,
    /// <summary>Accelerate in the middle, ease at both ends.</summary>
    EaseInOutCubic,
    /// <summary>Overshoot slightly before settling.</summary>
    EaseOutBack,
    /// <summary>Spring-like oscillation before settling.</summary>
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
public class AnimationController : IDisposable
{
    private Tween? _entryTween;
    private Tween? _hoverTween;
    private Tween? _dataTween;
    private Tween? _exitTween;
    private Action? _pendingExitCallback;

    // ── Entry animation ──────────────────────────────────────

    /// <summary>Overall entry progress [0,1].</summary>
    public float EntryProgress { get; private set; } = 1f;

    /// <summary>Per-series entry progress, staggered by delay.</summary>
    public float[] SeriesProgress { get; private set; } = [];

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

    private static float[] Filled(int count, float value)
    {
        var arr = new float[count];
        Array.Fill(arr, value);
        return arr;
    }

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
    public float ExitDuration { get; init; } = 0.3f;

    /// <summary>
    /// Number of elements the chart draws, so <see cref="AnimationThreshold"/> can take effect.
    /// <para>
    /// The controller only reads it; nothing inside the library writes it, because driving the
    /// animation is the host's job (a <see cref="Chart"/> is told its progress, see
    /// <see cref="Chart.Animate(float)"/>). A host that knows the count sets it once and calls
    /// <see cref="ShouldAnimate"/> without an argument; a host that does not leaves it at -1 and passes
    /// the count per call. Both unknown means "animate": a negative value never disables animation.
    /// </para>
    /// </summary>
    public int ElementCount { get; set; } = -1;

    /// <summary>
    /// Check if animation should be enabled for the given element count.
    /// Pass <paramref name="elementCount"/> explicitly, or set <see cref="ElementCount"/> once;
    /// when both are unknown animations stay enabled.
    /// </summary>
    public bool ShouldAnimate(int elementCount = -1)
    {
        int count = elementCount >= 0 ? elementCount : ElementCount;
        return count < 0 || count <= AnimationThreshold;
    }

    /// <summary>
    /// Start entry animation for the given number of series.
    /// Creates staggered tweens for each series + a global opacity fade-in.
    /// If totalElementCount exceeds AnimationThreshold, skips animation.
    /// </summary>
    public void StartEntry(Node owner, int seriesCount, int totalElementCount = -1)
    {
        _entryTween?.Kill();
        ExitProgress = 0f;

        if (!owner.IsInsideTree())
        {
            // Tweens only advance inside the scene tree: settle on the final state immediately.
            EntryProgress = 1f;
            GlobalOpacity = 1f;
            SeriesProgress = Filled(Math.Max(1, seriesCount), 1f);
            return;
        }

        if (!ShouldAnimate(totalElementCount))
        {
            EntryProgress = 1f;
            GlobalOpacity = 1f;
            SeriesProgress = Filled(Math.Max(1, seriesCount), 1f);
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
        if (!owner.IsInsideTree())
        {
            HoverScale = targetScale;
            return;
        }
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
        if (!owner.IsInsideTree())
        {
            DataTransitionProgress = 1f;
            return;
        }
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
        // A pending callback must not be lost: killing the previous tween would swallow it and
        // leave the caller (e.g. a pending tab switch) waiting forever.
        CompletePendingExit();

        if (!owner.IsInsideTree())
        {
            // Not in the tree: finish immediately instead of creating a tween that never runs.
            ExitProgress = 1f;
            GlobalOpacity = 0f;
            _pendingExitCallback = onComplete;
            CompletePendingExit();
            return;
        }

        _exitTween?.Kill();
        ExitProgress = 0f;
        _pendingExitCallback = onComplete;

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

        // The callback is tracked separately from the tween so it runs exactly once, whether the
        // tween finishes or is superseded by a new StartExit/Reset.
        if (onComplete != null)
        {
            _exitTween.SetParallel(false);
            _exitTween.TweenCallback(Callable.From(CompletePendingExit));
        }

        _exitTween.Finished += () =>
        {
            _exitTween = null;
            CompletePendingExit();
        };
    }

    /// <summary>Invoke the pending exit callback (if any) exactly once.</summary>
    private void CompletePendingExit()
    {
        var callback = _pendingExitCallback;
        _pendingExitCallback = null;
        if (callback == null) return;

        try
        {
            callback();
        }
        catch (Exception ex)
        {
            // A throwing callback must not break the tween chain or the caller's frame.
            GD.PushError($"AnimationController exit callback failed: {ex.Message}");
        }
    }

    /// <summary>Kill the four phase tweens (the controller holds nothing else).</summary>
    private void KillTweens()
    {
        _entryTween?.Kill();
        _hoverTween?.Kill();
        _dataTween?.Kill();
        _exitTween?.Kill();
    }

    /// <summary>
    /// Kill every running tween. Call it when the owner leaves the scene tree; the controller itself
    /// holds no native resources besides the tweens it created.
    /// </summary>
    public void Dispose()
    {
        KillTweens();
        _entryTween = null;
        _hoverTween = null;
        _dataTween = null;
        _exitTween = null;
        _pendingExitCallback = null;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Reset all animation state to defaults (no animation).
    /// </summary>
    public void Reset()
    {
        _pendingExitCallback = null;
        KillTweens();

        EntryProgress = 1f;
        GlobalOpacity = 1f;
        HoverScale = 1f;
        DataTransitionProgress = 1f;
        ExitProgress = 0f;
        SeriesProgress = [];
    }
}

/// <summary>
/// Animation parameters passed to marks during rendering.
/// Built from AnimationController state each frame.
/// </summary>
public class AnimationContext
{
    /// <summary>
    /// Entry progress for this specific series [0,1].
    /// This is the single source of truth: <c>Chart.Animate(float)</c> updates it too, so
    /// <c>AnimationProgress</c> and <c>Animation.EntryProgress</c> can never disagree.
    /// <para>
    /// Every member of this class is set when the context is built (an object initializer, once per
    /// frame) and read afterwards - the shared <see cref="Default"/> instance would otherwise be
    /// writable, and one host writing to it would change the fallback of every chart in the process.
    /// </para>
    /// </summary>
    public float EntryProgress { get; init; } = 1f;

    /// <summary>Global opacity [0,1].</summary>
    public float GlobalOpacity { get; init; } = 1f;

    /// <summary>Hover scale factor for the current element.</summary>
    public float HoverScale { get; init; } = 1f;

    /// <summary>Exit progress [0,1]. 0 = fully visible, 1 = fully exited.</summary>
    public float ExitProgress { get; init; }

    /// <summary>
    /// Data transition progress [0,1].
    /// <para>
    /// Not wired yet: no mark consumes this value and <c>AnimationController.StartDataTransition</c>
    /// has no caller, so a data transition currently renders as an instant change. The property is
    /// exposed so custom marks can already implement the interpolation themselves.
    /// </para>
    /// </summary>
    public float DataTransitionProgress { get; init; } = 1f;

    /// <summary>
    /// Per-series entry progress, staggered by series index. Empty when no stagger is running.
    /// <para>
    /// Not wired yet: marks receive only <see cref="EntryProgress"/>. Exposed for custom marks that
    /// want to stagger their own series.
    /// </para>
    /// </summary>
    public float[] SeriesProgress { get; init; } = [];

    /// <summary>
    /// Default instance with no animation applied (entry and transition progress 1, full opacity).
    /// Shared by every chart whose theme has <see cref="ChartTheme.EnableAnimation"/> off; it is
    /// immutable, so it stays that way.
    /// </summary>
    public static AnimationContext Default { get; } = new();
}

