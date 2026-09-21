namespace GodotNodeExtension.Tests.GodotChart;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Tests for the animation layer (Animation.cs): the easing functions, the
/// <see cref="AnimationController"/> state machine (threshold, entry/hover/data/exit lifecycles,
/// reset and callback handling) and <see cref="AnimationContext"/> / <see cref="Chart.Animate(float)"/>
/// which are the single source of truth a mark reads.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class AnimationTest
{
    /// <summary>Two-series progress used by the <see cref="AnimationContext"/> cases.</summary>
    private static readonly float[] SeriesProgressPair = { 0.1f, 0.2f };

    private static DataRow D(params (string Field, object Value)[] fields)
    {
        var row = new DataRow(fields.Length);
        foreach (var (field, value) in fields)
            row.Set(field, value);
        return row;
    }

    private static void Approx(float actual, float expected, float tolerance = 1e-4f)
        => AssertThat(MathF.Abs(actual - expected) <= tolerance).IsTrue();

    // ── Easing functions ────────────────────────────────────────────────────

    [TestCase]
    public void EaseLinearMidpoint()
        => Approx(Ease.Apply(0.5f, EaseType.Linear), 0.5f);

    [TestCase]
    public void EaseInQuadMidpoint()
        => Approx(Ease.Apply(0.5f, EaseType.EaseInQuad), 0.25f);

    [TestCase]
    public void EaseOutQuadMidpoint()
        => Approx(Ease.Apply(0.5f, EaseType.EaseOutQuad), 0.75f);

    [TestCase]
    public void EaseOutCubicMidpoint()
        => Approx(Ease.Apply(0.5f, EaseType.EaseOutCubic), 0.875f);

    [TestCase]
    public void EaseInOutCubicMidpoint()
        => Approx(Ease.Apply(0.5f, EaseType.EaseInOutCubic), 0.5f);

    [TestCase]
    public void EaseOutBackMidpointOvershoots()
        => Approx(Ease.Apply(0.5f, EaseType.EaseOutBack), 1.0877f, 1e-3f);

    [TestCase]
    public void EaseOutElasticMidpoint()
        => Approx(Ease.Apply(0.5f, EaseType.EaseOutElastic), 1.015625f, 1e-3f);

    [TestCase]
    public void AllEaseTypesAnchorAtTheEndpoints()
    {
        foreach (EaseType type in Enum.GetValues<EaseType>())
        {
            Approx(Ease.Apply(0f, type), 0f, 1e-5f);
            Approx(Ease.Apply(1f, type), 1f, 1e-5f);
        }
    }

    // ── AnimationController defaults and threshold ──────────────────────────

    [TestCase]
    public void AnimationControllerDefaults()
    {
        var controller = new AnimationController();

        AssertThat(controller.EntryProgress).IsEqual(1f);
        AssertThat(controller.GlobalOpacity).IsEqual(1f);
        AssertThat(controller.HoverScale).IsEqual(1f);
        AssertThat(controller.DataTransitionProgress).IsEqual(1f);
        AssertThat(controller.ExitProgress).IsEqual(0f);
        AssertThat(controller.EntryDuration).IsEqual(0.6f);
        AssertThat(controller.SeriesStagger).IsEqual(0.1f);
        AssertThat(controller.ExitDuration).IsEqual(0.3f);
        AssertThat(controller.AnimationThreshold).IsEqual(2000);
        AssertThat(controller.ElementCount).IsEqual(-1);
        AssertThat(controller.SeriesProgress.Length).IsEqual(0);
        AssertThat(controller.IsAnimating).IsFalse();
    }

    [TestCase]
    public void ShouldAnimateThresholdBoundary()
    {
        var controller = new AnimationController { AnimationThreshold = 100 };

        AssertThat(controller.ShouldAnimate(0)).IsTrue();
        AssertThat(controller.ShouldAnimate(100)).IsTrue();  // boundary is inclusive
        AssertThat(controller.ShouldAnimate(101)).IsFalse();
        AssertThat(controller.ShouldAnimate()).IsTrue();     // unknown count keeps animation on

        controller.ElementCount = 100;
        AssertThat(controller.ShouldAnimate()).IsTrue();
        controller.ElementCount = 101;
        AssertThat(controller.ShouldAnimate()).IsFalse();

        controller.AnimationThreshold = 0;
        controller.ElementCount = -1;
        AssertThat(controller.ShouldAnimate(0)).IsTrue();
        AssertThat(controller.ShouldAnimate(1)).IsFalse();
    }

    [TestCase]
    public void AnimationThresholdIsAppliedWhenTheCountIsKnown()
    {
        var controller = new AnimationController { AnimationThreshold = 100 };

        AssertThat(controller.ShouldAnimate(50)).IsTrue();
        AssertThat(controller.ShouldAnimate(500)).IsFalse();
    }

    [TestCase]
    public void AnimationThresholdUsesTheConfiguredElementCount()
    {
        var controller = new AnimationController { AnimationThreshold = 100 };

        // An unknown count keeps animations enabled.
        AssertThat(controller.ShouldAnimate()).IsTrue();

        controller.ElementCount = 5000;
        AssertThat(controller.ShouldAnimate()).IsFalse();

        controller.ElementCount = 10;
        AssertThat(controller.ShouldAnimate()).IsTrue();
    }

    /// <summary>
    /// The count a host set once reaches <see cref="AnimationController.StartEntry"/> as well: over
    /// <see cref="AnimationController.AnimationThreshold"/> the entry settles on the end state instead
    /// of tweening (the node is in the tree, so the threshold - not the "not in the tree" fallback - is
    /// what skips the animation).
    /// </summary>
    [TestCase]
    public void StartEntryHonoursTheConfiguredElementCount()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            GD.Print("[skip] StartEntryHonoursTheConfiguredElementCount: no SceneTree in this run");
            return;
        }

        var node = AutoFree(new Node2D());
        tree.Root.AddChild(node);

        var controller = new AnimationController { AnimationThreshold = 100, ElementCount = 5000 };
        controller.StartEntry(node, seriesCount: 3);

        AssertThat(controller.EntryProgress).IsEqual(1f);
        AssertThat(controller.GlobalOpacity).IsEqual(1f);
        foreach (float progress in controller.SeriesProgress)
            AssertThat(progress).IsEqual(1f);
        AssertThat(controller.IsAnimating).IsFalse();
    }

    // ── Entry / hover / data-transition lifecycles ──────────────────────────

    [TestCase]
    public void EntryAnimationSettlesWhenNotInTheTree()
    {
        var node = AutoFree(new Node2D()); // constructed but never added to the scene tree
        var controller = new AnimationController { AnimationThreshold = 10 };

        controller.StartEntry(node, 3, totalElementCount: 9999);

        AssertThat(controller.EntryProgress).IsEqual(1f);
        AssertThat(controller.GlobalOpacity).IsEqual(1f);
        AssertThat(controller.SeriesProgress.Length).IsEqual(3);
        foreach (float progress in controller.SeriesProgress)
            AssertThat(progress).IsEqual(1f);
        AssertThat(controller.IsAnimating).IsFalse();
    }

    [TestCase]
    public void HoverAndDataTransitionSettleWhenNotInTheTree()
    {
        var node = AutoFree(new Node2D());
        var controller = new AnimationController();

        controller.AnimateHover(node, 1.4f);
        AssertThat(controller.HoverScale).IsEqual(1.4f);

        controller.StartDataTransition(node);
        AssertThat(controller.DataTransitionProgress).IsEqual(1f);
        AssertThat(controller.IsAnimating).IsFalse();
    }

    [TestCase]
    public void ResetRestoresDefaultsAndClearsSeriesProgress()
    {
        var node = AutoFree(new Node2D());
        var controller = new AnimationController();
        controller.StartEntry(node, 3);
        AssertThat(controller.SeriesProgress.Length).IsEqual(3);

        controller.Reset();

        AssertThat(controller.EntryProgress).IsEqual(1f);
        AssertThat(controller.GlobalOpacity).IsEqual(1f);
        AssertThat(controller.HoverScale).IsEqual(1f);
        AssertThat(controller.DataTransitionProgress).IsEqual(1f);
        AssertThat(controller.ExitProgress).IsEqual(0f);
        AssertThat(controller.SeriesProgress.Length).IsEqual(0);
        AssertThat(controller.IsAnimating).IsFalse();
    }

    // ── Exit lifecycle and callbacks ────────────────────────────────────────

    [TestCase]
    public void StartExitSettlesImmediatelyForANodeOutsideTheTree()
    {
        var controller = new AnimationController();
        var node = AutoFree(new Node2D());
        int completed = 0;

        controller.StartExit(node, () => completed++);

        AssertThat(completed).IsEqual(1);
        AssertThat(controller.ExitProgress).IsEqual(1f);
    }

    [TestCase]
    public void StartExitCallbackExceptionIsSwallowed()
    {
        var node = AutoFree(new Node2D());
        var controller = new AnimationController();

        bool threw = false;
        try
        {
            // Outside the tree the callback runs synchronously; a throwing callback must not escape.
            controller.StartExit(node, () => throw new InvalidOperationException("exit callback"));
        }
        catch (Exception)
        {
            threw = true;
        }

        AssertThat(threw).IsFalse();
        AssertThat(controller.ExitProgress).IsEqual(1f);
        AssertThat(controller.GlobalOpacity).IsEqual(0f);
    }

    [TestCase]
    public void SupersededExitDoesNotSwallowItsCallback()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            GD.Print("[skip] SupersededExitDoesNotSwallowItsCallback: no SceneTree in this run");
            return;
        }

        var node = AutoFree(new Node2D());
        tree.Root.AddChild(node);

        var controller = new AnimationController();
        int first = 0, second = 0;

        controller.StartExit(node, () => first++);
        controller.StartExit(node, () => second++); // supersedes the first exit

        AssertThat(first).IsEqual(1);  // the superseded callback was not lost
        AssertThat(second).IsEqual(0); // still pending until its own tween finishes
    }

    // ── Tween progression (real frames) ─────────────────────────────────────

    /// <summary>
    /// Wall-clock budget a case gives a tween before it declares it stuck. A tween advances by the frame delta,
    /// so what it needs is real time - never a frame count: a headless frame can be a small fraction of a
    /// millisecond, and the tiny durations these cases use still finish in milliseconds of wall time.
    /// </summary>
    private const int TweenWaitBudgetMs = 5000;

    /// <summary>
    /// Safety net for the loops below: an engine that stops advancing time must not hang the suite for ever.
    /// </summary>
    private const int TweenFrameBudget = 20000;

    /// <summary>
    /// Advance real process frames until <paramref name="controller"/> stops animating (or the wall-clock/frame
    /// budget ran out) and report how many frames were awaited. A tween only advances inside the scene tree, so
    /// this is the only way to cover progress over time.
    /// </summary>
    private static async Task<int> AdvanceFramesAsync(SceneTree tree, AnimationController controller,
                                                     int maxFrames = TweenFrameBudget)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int frames = 0;
        while (controller.IsAnimating && frames < maxFrames && watch.ElapsedMilliseconds < TweenWaitBudgetMs)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            frames++;
        }
        return frames;
    }

    /// <summary>
    /// The entry tween really runs: it starts at zero, both progress values grow monotonically while frames
    /// pass, and it settles on the end state. Without this the only covered entry states were "the node is not
    /// in the tree" (instant end state) and the threshold skip - no test ever saw a value between the two.
    /// </summary>
    [TestCase]
    public async Task EntryTweenAdvancesAndSettlesOnTheEndState()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            GD.Print("[skip] EntryTweenAdvancesAndSettlesOnTheEndState: no SceneTree in this run");
            return;
        }

        var node = AutoFree(new Node2D());
        tree.Root.AddChild(node);

        var controller = new AnimationController { EntryDuration = 0.05f, SeriesStagger = 0.01f };
        controller.StartEntry(node, seriesCount: 3);

        // A tween that is really scheduled starts at the beginning of its range: these are the values a mark
        // reads on the first frame, and no "not in the tree" case can produce them.
        AssertThat(controller.IsAnimating).IsTrue();
        AssertThat(controller.EntryProgress).IsEqual(0f);
        AssertThat(controller.GlobalOpacity).IsEqual(0f);
        AssertThat(controller.SeriesProgress.Length).IsEqual(3);
        AssertThat(controller.SeriesProgress.All(v => v == 0f)).IsTrue();

        float lastEntry = controller.EntryProgress;
        float lastOpacity = controller.GlobalOpacity;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int frames = 0;
        while (controller.IsAnimating && frames < TweenFrameBudget && watch.ElapsedMilliseconds < TweenWaitBudgetMs)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            frames++;

            // Direction only - the exact easing value of a frame is a timing detail.
            AssertThat(controller.EntryProgress >= lastEntry).IsTrue();
            AssertThat(controller.GlobalOpacity >= lastOpacity).IsTrue();
            AssertThat(controller.EntryProgress <= 1f).IsTrue();
            AssertThat(controller.GlobalOpacity <= 1f).IsTrue();
            lastEntry = controller.EntryProgress;
            lastOpacity = controller.GlobalOpacity;
        }

        AssertThat(frames > 0).IsTrue();
        AssertThat(controller.IsAnimating).IsFalse();
        AssertThat(controller.EntryProgress).IsEqual(1f);
        AssertThat(controller.GlobalOpacity).IsEqual(1f);
        foreach (float progress in controller.SeriesProgress)
            AssertThat(progress).IsEqual(1f);
    }

    /// <summary>
    /// The hover tween moves <see cref="AnimationController.HoverScale"/> away from its neutral 1 towards the
    /// target and settles exactly on it - the direction of an intermediate value, never the value itself.
    /// </summary>
    [TestCase]
    public async Task HoverTweenMovesTheScaleTowardTheTarget()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            GD.Print("[skip] HoverTweenMovesTheScaleTowardTheTarget: no SceneTree in this run");
            return;
        }

        var node = AutoFree(new Node2D());
        tree.Root.AddChild(node);

        var controller = new AnimationController();
        controller.AnimateHover(node, 1.4f);
        AssertThat(controller.IsAnimating).IsTrue();

        var samples = new List<float>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int frames = 0;
        while (controller.IsAnimating && frames < TweenFrameBudget && watch.ElapsedMilliseconds < TweenWaitBudgetMs)
        {
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            frames++;
            samples.Add(controller.HoverScale);
        }

        AssertThat(frames > 0).IsTrue();
        AssertThat(samples.Any(v => v > 1f)).IsTrue();    // it left the neutral value
        AssertThat(samples.All(v => v >= 1f)).IsTrue();   // ...in the direction of the target, never past the start
        AssertThat(controller.IsAnimating).IsFalse();
        Approx(controller.HoverScale, 1.4f, 1e-3f);
    }

    /// <summary>
    /// Inside the scene tree the exit callback belongs to the tween: it does not run before the first frame,
    /// runs exactly once when the exit finishes, and never again afterwards.
    /// </summary>
    [TestCase]
    public async Task ExitTweenCallsTheCallbackExactlyOnce()
    {
        if (Engine.GetMainLoop() is not SceneTree tree)
        {
            GD.Print("[skip] ExitTweenCallsTheCallbackExactlyOnce: no SceneTree in this run");
            return;
        }

        var node = AutoFree(new Node2D());
        tree.Root.AddChild(node);

        var controller = new AnimationController { ExitDuration = 0.05f };
        int completed = 0;
        controller.StartExit(node, () => completed++);

        AssertThat(completed).IsEqual(0);   // the tween owns the callback, so nothing ran yet
        AssertThat(controller.IsAnimating).IsTrue();

        int frames = await AdvanceFramesAsync(tree, controller);

        AssertThat(frames > 0).IsTrue();
        AssertThat(controller.IsAnimating).IsFalse();
        AssertThat(controller.ExitProgress).IsEqual(1f);
        AssertThat(completed).IsEqual(1);

        // A finished tween must not fire it a second time.
        for (int i = 0; i < 10; i++)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        AssertThat(completed).IsEqual(1);
        AssertThat(controller.ExitProgress).IsEqual(1f);
    }

    // ── AnimationContext defaults ───────────────────────────────────────────

    [TestCase]
    public void AnimationContextDefaultIsNeutral()
    {
        var d = AnimationContext.Default;

        AssertThat(d.EntryProgress).IsEqual(1f);
        AssertThat(d.GlobalOpacity).IsEqual(1f);
        AssertThat(d.HoverScale).IsEqual(1f);
        AssertThat(d.ExitProgress).IsEqual(0f);
        AssertThat(d.DataTransitionProgress).IsEqual(1f);
        AssertThat(d.SeriesProgress.Length).IsEqual(0);
    }

    // ── Chart.Animate shares one state with the context ─────────────────────

    [TestCase]
    public void AnimateFloatUpdatesTheAnimationContext()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow> { D(("cat", "A"), ("value", 1.0)) });
        chart.Mark(probe);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");

        chart.Animate(0.25f);
        chart.Render();

        AssertThat(probe.LastEntryProgress).IsEqual(0.25f);
        // Both values a mark can read must agree.
        AssertThat(probe.LastAnimationProgress).IsEqual(probe.LastEntryProgress);
    }

    [TestCase]
    public void AnimateContextAndFloatShareOneState()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow> { D(("cat", "A"), ("value", 1.0)) });
        chart.Mark(probe);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");

        // Setting a context first must not freeze the simpler overload.
        chart.Animate(new AnimationContext { EntryProgress = 0.1f, GlobalOpacity = 0.5f });
        chart.Animate(0.75f);
        chart.Render();

        AssertThat(probe.LastEntryProgress).IsEqual(0.75f);
        AssertThat(probe.LastAnimationProgress).IsEqual(0.75f);
    }

    [TestCase]
    public void AnimateFloatPreservesTheRestOfTheContext()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow> { D(("cat", "A"), ("value", 1.0)) });
        chart.Mark(probe);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");

        chart.Animate(new AnimationContext
        {
            EntryProgress = 0.1f,
            GlobalOpacity = 0.5f,
            HoverScale = 1.2f,
            ExitProgress = 0.3f,
            DataTransitionProgress = 0.4f,
            SeriesProgress = SeriesProgressPair,
        });
        chart.Animate(0.7f); // only the entry progress changes

        chart.Render();

        AssertThat(probe.LastAnimation.EntryProgress).IsEqual(0.7f);
        AssertThat(probe.LastAnimation.GlobalOpacity).IsEqual(0.5f);
        AssertThat(probe.LastAnimation.HoverScale).IsEqual(1.2f);
        AssertThat(probe.LastAnimation.ExitProgress).IsEqual(0.3f);
        AssertThat(probe.LastAnimation.DataTransitionProgress).IsEqual(0.4f);
        AssertThat(probe.LastAnimation.SeriesProgress.Length).IsEqual(2);
        AssertThat(probe.LastAnimation.SeriesProgress[1]).IsEqual(0.2f);
    }

    [TestCase]
    public void AnimateNullRestoresTheDefaultContext()
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow> { D(("cat", "A"), ("value", 1.0)) });
        chart.Mark(probe);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");

        chart.Animate(new AnimationContext { EntryProgress = 0.3f, GlobalOpacity = 0.4f });
        chart.Animate(null);
        chart.Render();

        AssertThat(probe.LastEntryProgress).IsEqual(1f);
        AssertThat(probe.LastAnimation.GlobalOpacity).IsEqual(1f);
    }

    // ── The theme's animation switch ────────────────────────────────────────

    /// <summary>Chart whose theme has <see cref="ChartTheme.EnableAnimation"/> off, with a probe mark.</summary>
    private static (Chart Chart, CapturingMark Probe, ChartTheme Theme) ThemedChart(bool animate)
    {
        var canvas = new FakeCanvas2D();
        var probe = new CapturingMark();
        var theme = ChartTheme.Dark().Clone();
        theme.EnableAnimation = animate;

        var chart = new Chart(canvas) { Width = 400f, Height = 300f };
        chart.Data(new List<DataRow> { D(("cat", "A"), ("value", 1.0)) });
        chart.Mark(probe);
        chart.Encode(Channel.X, "cat");
        chart.Encode(Channel.Y, "value");
        chart.Theme(theme);
        return (chart, probe, theme);
    }

    /// <summary>
    /// <see cref="ChartTheme.EnableAnimation"/> is the theme-level switch: with it off
    /// <see cref="Chart.Animate(AnimationContext)"/> settles on the end state instead of animating,
    /// whatever context the host passes.
    /// </summary>
    [TestCase]
    public void AThemeWithoutAnimationSettlesTheContextOnTheEndState()
    {
        var (chart, probe, theme) = ThemedChart(animate: false);

        chart.Animate(new AnimationContext
        {
            EntryProgress = 0.2f,
            GlobalOpacity = 0.5f,
            HoverScale = 1.3f,
            ExitProgress = 0.4f,
            SeriesProgress = SeriesProgressPair,
        });
        chart.Render();

        AssertThat(probe.LastEntryProgress).IsEqual(1f);
        AssertThat(probe.LastAnimation.GlobalOpacity).IsEqual(1f);
        AssertThat(probe.LastAnimation.HoverScale).IsEqual(1f);
        AssertThat(probe.LastAnimation.ExitProgress).IsEqual(0f);
        AssertThat(probe.LastAnimation.SeriesProgress.Length).IsEqual(0);

        // Switching the theme on makes the very next call animate again (the switch is live, not a
        // one-time decision taken when the theme was assigned).
        theme.EnableAnimation = true;
        chart.Animate(new AnimationContext { EntryProgress = 0.2f, GlobalOpacity = 0.5f });
        chart.Render();

        AssertThat(probe.LastEntryProgress).IsEqual(0.2f);
        AssertThat(probe.LastAnimation.GlobalOpacity).IsEqual(0.5f);
    }

    /// <summary>The same switch for the simple overload: <see cref="Chart.Animate(float)"/>. </summary>
    [TestCase]
    public void AThemeWithoutAnimationSettlesAnimateFloatOnTheEndState()
    {
        var (chart, probe, theme) = ThemedChart(animate: false);

        chart.Animate(0.25f);
        chart.Render();
        AssertThat(probe.LastEntryProgress).IsEqual(1f);

        theme.EnableAnimation = true;
        chart.Animate(0.25f);
        chart.Render();
        AssertThat(probe.LastEntryProgress).IsEqual(0.25f);
    }
}
