namespace GodotNodeExtension.Tests.GodotChart;

using System.Collections.Generic;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.GodotChart.Canvas;
using GodotNodeExtension.Tests.Support;
using Support;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for <see cref="Canvas2DControl"/>: the node owns a canvas, keeps its
/// surface in sync with the node size, runs the frame loop (tick → optional clear → draw → commit)
/// once per invalidation, presents the resulting texture, and releases the canvas with the node.
/// <para>
/// The hosted canvas is injected through <see cref="Canvas2DControl.CanvasFactory"/>, so the
/// lifecycle is verified without a GPU. The default factory path (a real Skia backend, including the
/// pixels it presents) is covered by <c>Canvas2DControlUsesTheConfiguredBackendAndPresentsItsPixels</c>,
/// which skips when the run has no rendering device.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class Canvas2DControlTest
{
    /// <summary>
    /// Message of the exception the failing draw callback raises in
    /// <see cref="AThrowingDrawCallbackIsReportedCommittedAndRetried"/>. The control reports the failure
    /// with <c>GD.PushError</c> - which is the behaviour under test - and a run fails on any engine
    /// ERROR line that <c>Tools/lib/godot.py</c> does not allow-list. That file is outside this suite's
    /// scope, so the message reuses the phrase already listed for the throwing-mark fixture.
    /// </summary>
    private const string ReportedDrawFailure = "deliberate draw-callback failure";

    /// <summary>Add a control to the active scene tree, which triggers its <c>_Ready</c>.</summary>
    private static Canvas2DControl AddToTree(Canvas2DControl control)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.Root.AddChild(control);
        return control;
    }

    private static Canvas2DControl ControlWithFake(
        FakeCanvas2D fake, Vector2 size, out List<Vector2I> createdSizes)
    {
        var sizes = new List<Vector2I>();
        createdSizes = sizes;

        return new Canvas2DControl
        {
            Size = size,
            CanvasFactory = (w, h) =>
            {
                sizes.Add(new Vector2I(w, h));
                return fake;
            },
        };
    }

    [TestCase]
    public void TheFirstCanvasIsBuiltAtTheNodeSizeForBothResizeModes()
    {
        Asserts.RequireSceneTree();

        // Default (AutoResize on): the surface is the node size.
        var autoFake = new FakeCanvas2D();
        var auto = ControlWithFake(autoFake, new Vector2(120, 80), out var autoSizes);
        AutoFree(AddToTree(auto));

        AssertThat(auto.IsReady).IsTrue();
        AssertThat(autoSizes.Count).IsEqual(1);
        AssertThat(autoSizes[0]).IsEqual(new Vector2I(120, 80));
        AssertThat(auto.CanvasSize).IsEqual(new Vector2I(120, 80));
        AssertThat(ReferenceEquals(auto.Canvas, autoFake)).IsTrue();

        // AutoResize off: nothing sized the surface yet, so CanvasSize is still (0,0). Falling back to
        // the node size is what keeps that from becoming a 1x1 surface that presents a blank node.
        var fixedFake = new FakeCanvas2D();
        var fixedControl = ControlWithFake(fixedFake, new Vector2(120, 90), out var fixedSizes);
        fixedControl.AutoResize = false;
        AutoFree(AddToTree(fixedControl));

        AssertThat(fixedSizes.Count).IsEqual(1);
        AssertThat(fixedSizes[0]).IsEqual(new Vector2I(120, 90));
        AssertThat(fixedControl.CanvasSize).IsEqual(new Vector2I(120, 90));
    }

    [TestCase]
    public void RedrawClearsAndRunsOneFrameAroundTheDrawCallback()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var control = ControlWithFake(fake, new Vector2(64, 64), out _);
        var background = new Color(0.1f, 0.2f, 0.3f);
        control.BackgroundColor = background;

        int draws = 0;
        int filesAtDraw = -1;
        int endsAtDraw = -1;
        control.CanvasDraw += (_, canvas) =>
        {
            draws++;
            filesAtDraw = fake.BeginFrameCount;
            endsAtDraw = fake.EndFrameCount;
            AssertThat(ReferenceEquals(canvas, fake)).IsTrue();
        };

        AutoFree(AddToTree(control));

        control.Invalidate();
        control._Process(0.016);

        AssertThat(draws).IsEqual(1);
        AssertThat(fake.ClearColors.Count).IsEqual(1);
        AssertThat(fake.ClearColors[0].ToHtml()).IsEqual(background.ToHtml());
        // The callback runs inside the frame: begun but not yet committed.
        AssertThat(filesAtDraw).IsEqual(1);
        AssertThat(endsAtDraw).IsEqual(0);
        AssertThat(fake.BeginFrameCount).IsEqual(1);
        AssertThat(fake.EndFrameCount).IsEqual(1);
    }

    [TestCase]
    public void AnIdleFrameTicksButDoesNotRedraw()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var control = ControlWithFake(fake, new Vector2(32, 32), out _);
        int draws = 0;
        control.CanvasDraw += (_, _) => draws++;

        AutoFree(AddToTree(control));

        control.Invalidate();
        control._Process(0.016);
        control._Process(0.016);   // nothing changed
        control._Process(0.016);

        AssertThat(draws).IsEqual(1);
        AssertThat(fake.TickCount).IsEqual(3);
    }

    [TestCase]
    public void ClearingCanBeTurnedOffForAccumulatingContent()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var control = ControlWithFake(fake, new Vector2(32, 32), out _);
        control.ClearBeforeDraw = false;

        AutoFree(AddToTree(control));

        control.Invalidate();
        control._Process(0.016);

        AssertThat(fake.ClearColors.Count).IsEqual(0);
        AssertThat(fake.BeginFrameCount).IsEqual(1);
    }

    [TestCase]
    public void ResizingTheNodeResizesTheSurface()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var control = ControlWithFake(fake, new Vector2(50, 40), out _);
        AutoFree(AddToTree(control));

        control.Size = new Vector2(200, 100);

        AssertThat(control.CanvasSize).IsEqual(new Vector2I(200, 100));
        AssertThat(fake.ResizeCalls.Count).IsEqual(1);
        AssertThat(fake.ResizeCalls[0]).IsEqual((200, 100));
    }

    [TestCase]
    public void AutoResizeOffLeavesTheSurfaceSizeToTheHost()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var control = ControlWithFake(fake, new Vector2(50, 40), out _);
        control.AutoResize = false;
        AutoFree(AddToTree(control));

        // Resizing the node must not move the surface ...
        control.Size = new Vector2(300, 300);
        AssertThat(control.CanvasSize).IsEqual(new Vector2I(50, 40));
        AssertThat(fake.ResizeCalls.Count).IsEqual(0);

        // ... ResizeCanvas is the documented way to pick another resolution.
        control.ResizeCanvas(new Vector2I(128, 96));
        AssertThat(control.CanvasSize).IsEqual(new Vector2I(128, 96));
        AssertThat(fake.ResizeCalls.Count).IsEqual(1);
        AssertThat(fake.ResizeCalls[0]).IsEqual((128, 96));
    }

    [TestCase]
    public void LeavingTheTreeDisposesTheCanvas()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var control = ControlWithFake(fake, new Vector2(32, 32), out _);
        AddToTree(control);

        control.GetParent().RemoveChild(control);

        AssertThat(control.Canvas is null).IsTrue();
        AssertThat(control.IsReady).IsFalse();
        AssertThat(fake.DisposeCount).IsEqual(1);

        control.Free();
    }

    [TestCase]
    public void ASharedCanvasSurvivesWhenTheNodeDoesNotOwnIt()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var control = ControlWithFake(fake, new Vector2(32, 32), out _);
        control.OwnsCanvas = false;
        AddToTree(control);

        control.GetParent().RemoveChild(control);

        AssertThat(control.Canvas is null).IsTrue();
        AssertThat(fake.DisposeCount).IsEqual(0);

        control.Free();
    }

    [TestCase]
    public void ReEnteringTheTreeBuildsAFreshCanvasAndKeepsTrackingTheSize()
    {
        Asserts.RequireSceneTree();

        var fake = new FakeCanvas2D();
        var control = ControlWithFake(fake, new Vector2(40, 30), out var createdSizes);
        AddToTree(control);

        control.GetParent().RemoveChild(control);
        AssertThat(control.IsReady).IsFalse();

        // Re-entering must build a new canvas (the old one was released) and re-hook the resize
        // signal: _Ready runs only once per instance, so a hook installed there would be gone.
        AddToTree(control);

        AssertThat(control.IsReady).IsTrue();
        AssertThat(createdSizes.Count).IsEqual(2);

        control.Size = new Vector2(120, 90);
        AssertThat(control.CanvasSize).IsEqual(new Vector2I(120, 90));
        AssertThat(fake.ResizeCalls.Count).IsEqual(1);
        AssertThat(fake.ResizeCalls[0]).IsEqual((120, 90));

        control.GetParent().RemoveChild(control);
        control.Free();
    }

    [TestCase]
    public void ExitingTwiceAndExitingWithoutEnteringAreSafe()
    {
        Asserts.RequireSceneTree();

        // A node that never entered the tree has no connection to drop: releasing it must be a no-op
        // rather than a "disconnect a nonexistent connection" error.
        var never = new Canvas2DControl();
        never._ExitTree();
        AssertThat(never.Canvas is null).IsTrue();
        never.Free();

        // The engine can deliver EXIT_TREE more than once while tearing a scene down.
        var fake = new FakeCanvas2D();
        var control = ControlWithFake(fake, new Vector2(16, 16), out _);
        AddToTree(control);

        control.GetParent().RemoveChild(control);
        control._ExitTree();   // duplicate exit

        AssertThat(control.Canvas is null).IsTrue();
        AssertThat(fake.DisposeCount).IsEqual(1);   // released exactly once

        control.Free();
    }

    /// <summary>
    /// A factory that cannot build a canvas must not take the node down with it: the failure is reported
    /// once as a warning, <see cref="Canvas2DControl.Canvas"/> stays null, and the node keeps running
    /// frames (it simply presents nothing) instead of raising on every one.
    /// </summary>
    [TestCase]
    public void AFailingCanvasFactoryWarnsAndKeepsTheNodeUsable()
    {
        Asserts.RequireSceneTree();

        var log = EngineMessageLog.Attach();
        try
        {
            var control = new Canvas2DControl
            {
                Size = new Vector2(40, 30),
                CanvasFactory = (_, _) => throw new System.InvalidOperationException("no canvas for this case"),
            };
            AutoFree(AddToTree(control));

            AssertThat(control.Canvas is null).IsTrue();
            AssertThat(control.IsReady).IsFalse();

            // Still usable: a frame runs (and ticks nothing) instead of raising.
            control.Invalidate();
            control._Process(0.016);
            control._Process(0.016);

            var warnings = log.WarningsContaining("could not create a canvas");
            AssertThat(warnings.Length).IsEqual(1);
            AssertThat(warnings[0].Contains("InvalidOperationException")).IsTrue();
        }
        finally
        {
            log.Detach();
        }
    }

    /// <summary>
    /// A draw callback that throws must not cost the frame: the control reports the failure, still
    /// commits the frame (<c>EndFrame</c> runs, so the commands it recorded are flushed), and retries on
    /// the next one instead of freezing behind "not dirty".
    /// </summary>
    [TestCase]
    public void AThrowingDrawCallbackIsReportedCommittedAndRetried()
    {
        Asserts.RequireSceneTree();

        var log = EngineMessageLog.Attach();
        try
        {
            var fake = new FakeCanvas2D();
            var control = ControlWithFake(fake, new Vector2(32, 32), out _);
            bool fail = true;
            int draws = 0;
            control.CanvasDraw += (_, canvas) =>
            {
                draws++;
                if (fail) throw new System.InvalidOperationException(ReportedDrawFailure);

                using var path = canvas.CreatePath();
                using var paint = canvas.CreatePaint();
                path.Rect(0, 0, 8, 8);
                canvas.Fill(path, paint);
            };
            AutoFree(AddToTree(control));

            control.Invalidate();
            control._Process(0.016);

            AssertThat(draws).IsEqual(1);
            AssertThat(fake.EndFrameCount).IsEqual(1);   // committed despite the throw
            AssertThat(log.ErrorsContaining("canvas draw failed").Length).IsEqual(1);

            // The failed frame stays dirty, so the next one runs the same draw again.
            fail = false;
            control._Process(0.016);

            AssertThat(draws).IsEqual(2);
            AssertThat(fake.EndFrameCount).IsEqual(2);
            AssertThat(fake.FillCount).IsEqual(1);
        }
        finally
        {
            log.Detach();
        }
    }

    [TestCase]
    public void Canvas2DControlUsesTheConfiguredBackendAndPresentsItsPixels()
    {
        // The Skia backend allocates a RenderingDevice texture, which a headless run does not have.
        // Run with `python Tools/run_tests.py --render` (or from the editor) to exercise this path.
        if (RenderingServer.GetRenderingDevice() is null)
        {
            GD.Print("[skip] Canvas2DControlUsesTheConfiguredBackendAndPresentsItsPixels: no rendering device in this run");
            return;
        }

        Asserts.RequireSceneTree();

        var control = new Canvas2DControl { Size = new Vector2(64, 64), BackgroundColor = Colors.Black };
        int draws = 0;
        control.CanvasDraw += (_, canvas) =>
        {
            draws++;
            // Draw through the real backend so the GPU surface is exercised end to end.
            using var path = canvas.CreatePath();
            using var paint = canvas.CreatePaint();
            path.Rect(8, 8, 24, 24);
            paint.SetColor(Colors.Red);
            canvas.Fill(path, paint);
        };
        AutoFree(AddToTree(control));

        AssertThat(control.Canvas is SkiaCanvas2DBackend).IsTrue();
        AssertThat(control.Texture is not null).IsTrue();
        AssertThat(control.CanvasSize).IsEqual(new Vector2I(64, 64));

        // One full frame: begin -> clear -> draw -> commit (texture upload / layout transition).
        control.Invalidate();
        control._Process(0.016);

        AssertThat(draws).IsEqual(1);

        // The presented texture really holds the drawn pixels: the rect is where it was drawn, the rest
        // of the node carries the colour the surface was cleared with.
        using var image = control.Texture!.GetImage();
        var inside = image.GetPixel(20, 20);
        var outside = image.GetPixel(52, 52);
        AssertThat(inside.R > 0.5f && inside.G < 0.1f && inside.B < 0.1f).IsTrue();
        AssertThat(outside.R < 0.05f && outside.G < 0.05f && outside.B < 0.05f).IsTrue();
    }
}
