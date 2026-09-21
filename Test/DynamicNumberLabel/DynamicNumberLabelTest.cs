namespace GodotNodeExtension.Tests.DynamicNumberLabel;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using DynamicNumberLabelNode = GodotNodeExtension.Component.DynamicNumberLabel.DynamicNumberLabel;

/// <summary>
/// Behaviour specification for <see cref="DynamicNumberLabelNode"/>: how a value is rendered
/// (prefix/suffix, thousands grouping, zero padding and the digit limit) and how the animation
/// entry points drive the display (start/stop/instant, random mode and its update timer).
/// <para>
/// The formatting contract is verified through <c>SetValueInstant()</c>, which renders without a
/// frame, and the animation lifecycle is verified without ever advancing one: a tween that is not
/// ticked keeps running, and the random-mode timer is driven by emitting its <c>Timeout</c> signal.
/// Only the one case that needs a real completion awaits the <c>AnimationFinished</c> signal.
/// </para>
/// <para>
/// The cases that call an entry point outside the SceneTree double as engine-error guards: the
/// test runner fails the whole run when the engine prints an ERROR line, which is what an
/// unguarded <c>Node.create_tween()</c> outside the tree or a zero <c>Timer.wait_time</c> produce.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class DynamicNumberLabelTest
{
    /// <summary>Whether this run has a SceneTree; without one the node cannot be added to a tree.</summary>
    private static bool HasSceneTree()
    {
        if (Engine.GetMainLoop() is SceneTree) return true;
        GD.Print("[skip] DynamicNumberLabel tests need a SceneTree; none in this run");
        return false;
    }

    /// <summary>Add a label to the active scene tree, which triggers its <c>_Ready</c>.</summary>
    private static DynamicNumberLabelNode AddToTree(DynamicNumberLabelNode label)
    {
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(label);
        return label;
    }

    /// <summary>Render <paramref name="value"/> on a throwaway label and return its text.</summary>
    private static string Rendered(int value, Action<DynamicNumberLabelNode>? configure = null)
    {
        var label = AutoFree(new DynamicNumberLabelNode());
        configure?.Invoke(label);
        label.SetValueInstant(value);
        return label.Text;
    }

    /// <summary>The random-mode update timers the label currently owns.</summary>
    private static List<Timer> TimersOf(Node node)
    {
        var timers = new List<Timer>();
        foreach (var child in node.GetChildren())
        {
            if (child is Timer timer) timers.Add(timer);
        }
        return timers;
    }

    /// <summary>A label in the tree that interpolates with random values.</summary>
    private static DynamicNumberLabelNode RandomLabel()
        => AutoFree(AddToTree(new DynamicNumberLabelNode
        {
            RandomMode = true,
            AnimationDuration = 5f,
            UseDelimiter = false,
        }));

    // ── Rendering ────────────────────────────────────────────────────────────

    [TestCase]
    public void TheRenderedTextIsPrefixNumberSuffix()
    {
        if (!HasSceneTree()) return;

        var label = AutoFree(AddToTree(new DynamicNumberLabelNode
        {
            Prefix = "Score: ",
            Suffix = " pts",
            UseDelimiter = false,
            Value = 1500,
        }));

        AssertThat(label.Text).IsEqual("Score: 1500 pts");
        AssertThat(label.Value).IsEqual(1500);
    }

    [TestCase]
    public void ThousandsAreGroupedWithTheConfiguredDelimiter()
    {
        AssertThat(Rendered(1_234_567)).IsEqual("1,234,567");
        AssertThat(Rendered(-1_234_567)).IsEqual("-1,234,567");
        AssertThat(Rendered(999)).IsEqual("999");
        AssertThat(Rendered(1_234_567, l => l.Delimiter = " ")).IsEqual("1 234 567");
        AssertThat(Rendered(1_234_567, l => l.Delimiter = "'")).IsEqual("1'234'567");
    }

    [TestCase]
    public void GroupingCanBeTurnedOff()
    {
        AssertThat(Rendered(1_234_567, l => l.UseDelimiter = false)).IsEqual("1234567");
    }

    [TestCase]
    public void GroupingDoesNotDependOnTheAmbientCulture()
    {
        // "N0" formats with the current culture: on a de-DE machine the group separator is "."
        // and replacing "," with the configured delimiter would silently do nothing.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            AssertThat(Rendered(1_234_567, l => l.Delimiter = "'")).IsEqual("1'234'567");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [TestCase]
    public void AnEmptyOrNullDelimiterFallsBackToUngroupedNumbers()
    {
        // "" used to reach string.Replace() and throw ArgumentException on the padding path.
        AssertThat(Rendered(1_234, l => { l.Delimiter = ""; l.MaxDigits = 4; })).IsEqual("1234");
        AssertThat(Rendered(1_234, l => { l.Delimiter = null!; l.MaxDigits = 4; })).IsEqual("1234");
        AssertThat(Rendered(7, l => { l.Delimiter = ""; l.MaxDigits = 2; })).IsEqual("07");
    }

    [TestCase]
    public void MaxDigitsPadsToAFixedWidthIncludingTheSign()
    {
        AssertThat(Rendered(7, l => l.MaxDigits = 3)).IsEqual("007");
        AssertThat(Rendered(-7, l => l.MaxDigits = 3)).IsEqual("-07");
        AssertThat(Rendered(5, l => l.MaxDigits = 2)).IsEqual("05");
        AssertThat(Rendered(-12, l => l.MaxDigits = 4)).IsEqual("-012");
        AssertThat(Rendered(5, l => l.MaxDigits = 0)).IsEqual("5");
    }

    [TestCase]
    public void MaxDigitsCountsDigitsNotDelimiters()
    {
        AssertThat(Rendered(12_345, l => { l.MaxDigits = 6; l.UseDelimiter = true; })).IsEqual("012,345");
    }

    [TestCase]
    public void MaxDigitsClampsValuesThatWouldNotFit()
    {
        var label = AutoFree(new DynamicNumberLabelNode { MaxDigits = 4, UseDelimiter = false });

        label.SetValueInstant(99_999);

        AssertThat(label.Value).IsEqual(9_999);
        AssertThat(label.Text).IsEqual("9999");
    }

    [TestCase]
    public void MaxDigitsReservesOneDigitForTheMinusSign()
    {
        var label = AutoFree(new DynamicNumberLabelNode { MaxDigits = 3, UseDelimiter = false });

        label.SetValueInstant(-500);

        AssertThat(label.Value).IsEqual(-99);
        AssertThat(label.Text).IsEqual("-99");
    }

    [TestCase]
    public void LimitsAtOrBeyondTheIntRangeDoNotOverflowOrCorruptTheValue()
    {
        // 10 ^ MaxDigits does not fit in an int: the bound used to wrap around and clamp every
        // value - including 2 000 000 000 - onto a bogus bound.
        var label = AutoFree(new DynamicNumberLabelNode { MaxDigits = 10, UseDelimiter = false });

        label.SetValueInstant(2_000_000_000);
        AssertThat(label.Value).IsEqual(2_000_000_000);
        AssertThat(label.Text).IsEqual("2000000000");

        label.SetValueInstant(int.MaxValue);
        AssertThat(label.Text).IsEqual("2147483647");

        // Formatting int.MinValue must not throw (Math.Abs on the int would).
        label.SetValueInstant(int.MinValue);
        AssertThat(label.Text).IsEqual("-2147483648");
    }

    [TestCase]
    public void ReadyClampsTheExportedValueAndRendersItImmediately()
    {
        if (!HasSceneTree()) return;

        var label = AutoFree(AddToTree(new DynamicNumberLabelNode
        {
            MaxDigits = 4,
            UseDelimiter = false,
            Value = 99_999,
        }));

        AssertThat(label.Value).IsEqual(9_999);
        AssertThat(label.Text).IsEqual("9999");
    }

    [TestCase]
    public void AssigningValueOnlyStoresItWithoutRefreshingTheText()
    {
        if (!HasSceneTree()) return;

        var label = AutoFree(AddToTree(new DynamicNumberLabelNode { UseDelimiter = false }));
        label.SetValueInstant(10);

        label.Value = 20;

        AssertThat(label.Value).IsEqual(20);
        AssertThat(label.Text).IsEqual("10");
    }

    // ── Animation lifecycle ──────────────────────────────────────────────────

    [TestCase]
    public void StartAnimationRunsFromZeroToTheTarget()
    {
        if (!HasSceneTree()) return;

        var label = AutoFree(AddToTree(new DynamicNumberLabelNode { UseDelimiter = false }));

        AssertThat(label.IsAnimating()).IsFalse();

        label.StartAnimation(500);

        AssertThat(label.Value).IsEqual(500);
        AssertThat(label.Text).IsEqual("0");   // the animation shows its start value right away
        AssertThat(label.IsAnimating()).IsTrue();

        label.StopAnimation();

        AssertThat(label.IsAnimating()).IsFalse();
        AssertThat(label.Text).IsEqual("500");
    }

    [TestCase]
    public void StartAnimationCanBeginAtAnExplicitValue()
    {
        if (!HasSceneTree()) return;

        var label = AutoFree(AddToTree(new DynamicNumberLabelNode { Prefix = "HP ", MaxDigits = 4, UseDelimiter = false }));

        label.StartAnimation(100, 500);

        AssertThat(label.Text).IsEqual("HP 0100");
        AssertThat(label.Value).IsEqual(500);
        AssertThat(label.IsAnimating()).IsTrue();

        label.StopAnimation();

        AssertThat(label.Text).IsEqual("HP 0500");
    }

    [TestCase]
    public void AnAnimationStartValueIsClampedLikeTheTarget()
    {
        if (!HasSceneTree()) return;

        var label = AutoFree(AddToTree(new DynamicNumberLabelNode { MaxDigits = 4, UseDelimiter = false }));

        label.StartAnimation(99_999, 500);

        AssertThat(label.Text).IsEqual("9999");   // not the raw 99 999
        label.StopAnimation();
    }

    [TestCase]
    public void AnimateOnReadyIsOptIn()
    {
        if (!HasSceneTree()) return;

        var plain = AutoFree(AddToTree(new DynamicNumberLabelNode { Value = 500, UseDelimiter = false }));

        AssertThat(plain.Text).IsEqual("500");
        AssertThat(plain.IsAnimating()).IsFalse();

        var animating = AutoFree(AddToTree(new DynamicNumberLabelNode
        {
            Value = 500,
            UseDelimiter = false,
            AnimateOnReady = true,
        }));

        AssertThat(animating.Text).IsEqual("0");
        AssertThat(animating.IsAnimating()).IsTrue();

        animating.StopAnimation();
    }

    [TestCase]
    public void StopAnimationWithoutARunningAnimationKeepsTheDisplayedValue()
    {
        if (!HasSceneTree()) return;

        var label = AutoFree(AddToTree(new DynamicNumberLabelNode { Value = 123, UseDelimiter = false }));
        AssertThat(label.Text).IsEqual("123");

        label.StopAnimation();

        AssertThat(label.IsAnimating()).IsFalse();
        AssertThat(label.Text).IsEqual("123");
        AssertThat(label.Value).IsEqual(123);
    }

    [TestCase]
    public void AnimateToValueStartsFromTheDisplayedValueAndReportsTheChange()
    {
        if (!HasSceneTree()) return;

        var label = AutoFree(AddToTree(new DynamicNumberLabelNode { UseDelimiter = false }));
        var changes = new List<(int Old, int New)>();
        label.ValueChanged += (oldValue, newValue) => changes.Add((oldValue, newValue));

        label.Value = 900;   // a stored target that was never displayed
        label.AnimateToValue(250);

        AssertThat(changes.Count).IsEqual(1);
        AssertThat(changes[0].Old).IsEqual(900);
        AssertThat(changes[0].New).IsEqual(250);
        AssertThat(label.Value).IsEqual(250);
        AssertThat(label.Text).IsEqual("0");   // ... animated from the displayed value
        AssertThat(label.IsAnimating()).IsTrue();

        label.StopAnimation();

        AssertThat(label.Text).IsEqual("250");
    }

    [TestCase]
    public void ANewAnimationReplacesTheRunningOne()
    {
        if (!HasSceneTree()) return;

        var label = AutoFree(AddToTree(new DynamicNumberLabelNode { UseDelimiter = false }));

        label.StartAnimation(100);
        label.StartAnimation(0, 200);

        AssertThat(label.Value).IsEqual(200);
        AssertThat(label.Text).IsEqual("0");
        AssertThat(label.IsAnimating()).IsTrue();

        label.StopAnimation();

        AssertThat(label.Text).IsEqual("200");
    }

    [TestCase]
    public void StoppingOrReplacingAnAnimationDoesNotReportCompletion()
    {
        if (!HasSceneTree()) return;

        var label = AutoFree(AddToTree(new DynamicNumberLabelNode()));
        int finished = 0;
        label.AnimationFinished += () => finished++;

        label.StartAnimation(500);
        label.StopAnimation();
        label.StartAnimation(600);
        label.StopAnimation();

        AssertThat(finished).IsEqual(0);
    }

    // ── Random mode ──────────────────────────────────────────────────────────

    [TestCase]
    public void RandomModeDrivesTheDisplayFromATimerThatStoppingRemoves()
    {
        if (!HasSceneTree()) return;

        var label = RandomLabel();

        label.StartAnimation(500);

        AssertThat(TimersOf(label).Count).IsEqual(1);
        AssertThat(label.IsAnimating()).IsTrue();
        AssertThat(label.Text).IsEqual("0");

        label.StopAnimation();

        AssertThat(TimersOf(label).Count).IsEqual(0);
        AssertThat(label.IsAnimating()).IsFalse();
        AssertThat(label.Text).IsEqual("500");
    }

    [TestCase]
    public void SetValueInstantAlsoCancelsARunningRandomAnimation()
    {
        if (!HasSceneTree()) return;

        // The update timer used to survive SetValueInstant(): the label snapped back to random
        // values on the next tick and kept on updating for as long as the node lived.
        var label = RandomLabel();

        label.StartAnimation(500);
        AssertThat(TimersOf(label).Count).IsEqual(1);

        label.SetValueInstant(42);

        AssertThat(label.Text).IsEqual("42");
        AssertThat(label.Value).IsEqual(42);
        AssertThat(label.IsAnimating()).IsFalse();
        AssertThat(TimersOf(label).Count).IsEqual(0);
    }

    [TestCase]
    public void RestartingRandomModeKeepsASingleUpdateTimer()
    {
        if (!HasSceneTree()) return;

        var label = RandomLabel();

        label.StartAnimation(100);
        label.StartAnimation(0, 200);   // the first timer used to linger until the end of the frame

        AssertThat(TimersOf(label).Count).IsEqual(1);

        label.StopAnimation();

        AssertThat(TimersOf(label).Count).IsEqual(0);
    }

    [TestCase]
    public void ANonPositiveRandomIntervalFallsBackToTheDefault()
    {
        if (!HasSceneTree()) return;

        // Timer.wait_time <= 0 raises an engine error ("Time should be greater than zero"), and the
        // property keeps its old value, so the update rate silently stayed at 1 Hz.
        foreach (float interval in new[] { 0f, -1f })
        {
            var label = AutoFree(AddToTree(new DynamicNumberLabelNode
            {
                RandomMode = true,
                AnimationDuration = 5f,
                RandomUpdateInterval = interval,
            }));

            label.StartAnimation(100);

            var timers = TimersOf(label);
            AssertThat(timers.Count).IsEqual(1);
            AssertThat(timers[0].WaitTime).IsEqual(0.05f);

            label.StopAnimation();
        }
    }

    [TestCase]
    public void RandomUpdatesStayWithinTheAnimatedRange()
    {
        if (!HasSceneTree()) return;

        var label = RandomLabel();

        label.StartAnimation(0, 100);
        var timer = TimersOf(label)[0];

        // Advance the random updates by hand: no frame is needed to observe the value bounds.
        for (int i = 0; i < 32; i++)
        {
            timer.EmitSignal(Timer.SignalName.Timeout);

            int shown = int.Parse(label.Text, CultureInfo.InvariantCulture);
            AssertThat(shown is >= 0 and <= 100).IsTrue();
        }

        label.StopAnimation();
    }

    // ── Outside the SceneTree ────────────────────────────────────────────────

    [TestCase]
    public void StartingAnAnimationOutsideTheSceneTreeSettlesImmediately()
    {
        // Node.create_tween() reports "Can't create Tween when not inside scene tree", and the
        // bound tween would never run: a label built in code but not added yet applies the value.
        var label = AutoFree(new DynamicNumberLabelNode { UseDelimiter = false });

        label.StartAnimation(400);

        AssertThat(label.Value).IsEqual(400);
        AssertThat(label.Text).IsEqual("400");
        AssertThat(label.IsAnimating()).IsFalse();
    }

    [TestCase]
    public void RandomModeOutsideTheSceneTreeDoesNotLeaveATimerBehind()
    {
        var label = AutoFree(new DynamicNumberLabelNode { RandomMode = true, UseDelimiter = false });

        label.StartAnimation(0, 300);
        label.AnimateToValue(400);

        AssertThat(label.Value).IsEqual(400);
        AssertThat(label.Text).IsEqual("400");
        AssertThat(TimersOf(label).Count).IsEqual(0);
        AssertThat(label.IsAnimating()).IsFalse();
    }

    // ── Completion ───────────────────────────────────────────────────────────

    [TestCase]
    public async Task ACompletedRandomAnimationReportsCompletionAndDropsItsTimer()
    {
        if (!HasSceneTree()) return;

        var label = AutoFree(AddToTree(new DynamicNumberLabelNode
        {
            RandomMode = true,
            UseDelimiter = false,
            AnimationDuration = 0.05f,
            RandomUpdateInterval = 0.005f,
        }));

        label.StartAnimation(120);
        AssertThat(TimersOf(label).Count).IsEqual(1);

        await AssertSignal(label).IsEmitted("AnimationFinished").WithTimeout(5000);

        AssertThat(TimersOf(label).Count).IsEqual(0);
        AssertThat(label.IsAnimating()).IsFalse();
        AssertThat(label.Text).IsEqual("120");
        GD.Print("[test] DynamicNumberLabel completion path exercised");
    }

    [TestCase]
    public void LeavingTheTreeCancelsARunningAnimation()
    {
        if (!HasSceneTree()) return;

        var label = AddToTree(new DynamicNumberLabelNode());
        label.SetValueInstant(0);
        label.AnimateToValue(75);
        AssertThat(label.IsAnimating()).IsTrue();

        // A running tween is engine-owned and holds Callables into this assembly. Leaving the tree (a
        // scene reload in the editor, for example) has to release it, otherwise the tween keeps firing
        // into a detached node and keeps the old assembly referenced across a hot reload (godot#78513).
        ((SceneTree)Engine.GetMainLoop()).Root.RemoveChild(label);

        AssertThat(label.IsAnimating()).IsFalse();

        label.Free();
    }
}
