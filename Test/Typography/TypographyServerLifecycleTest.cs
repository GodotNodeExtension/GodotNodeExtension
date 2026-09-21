namespace GodotNodeExtension.Tests.Typography;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Server;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for the layout-handle lifecycle and for the diagnostics a result carries.
/// <para>
/// These cases exist because both failure modes are silent: a stale handle produced no result and no
/// error (a canvas stayed blank forever), and the cost of a layout was unobservable, so the claim that
/// a resize only re-runs the cheap half could not be checked at all.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyServerLifecycleTest
{
    /// <summary>
    /// A shutdown invalidates the handles of the previous generation, requests on them are rejected
    /// instead of queued, and a fresh handle works again.
    /// </summary>
    [TestCase]
    public void ShutdownInvalidatesHandlesAndRejectsTheirRequests()
    {
        var server = TypographyServer.Instance;
        var stale = server.CreateHandle();
        long acceptedId = server.RequestFullLayout(stale, BuildElements(), Settings());
        AssertThat(acceptedId > 0).OverrideFailureMessage(
            "a live handle must accept a request").IsTrue();
        AssertThat(server.IsHandleValid(stale)).IsTrue();

        server.Shutdown();

        AssertThat(server.IsHandleValid(stale)).OverrideFailureMessage(
            "a handle from before the shutdown must not be valid").IsFalse();

        long rejectedId = server.RequestFullLayout(stale, BuildElements(), Settings());
        AssertThat(rejectedId).OverrideFailureMessage(
            "a request on a stale handle must report that nothing was submitted")
            .IsEqual(TypographyServer.NotSubmitted);

        // A new handle belongs to the new generation and works.
        server.EnsureStarted();
        var fresh = server.CreateHandle();
        try
        {
            AssertThat(server.IsHandleValid(fresh)).IsTrue();
            AssertThat(fresh.Generation != stale.Generation).OverrideFailureMessage(
                "the new handle must belong to the new server generation").IsTrue();

            long requestId = server.RequestFullLayout(fresh, BuildElements(), Settings());
            AssertThat(requestId > 0).IsTrue();
            var result = WaitForResult(server, fresh, requestId);
            AssertThat(result.Error).IsNull();
            AssertThat(result.Elements is { Count: > 0 }).IsTrue();
        }
        finally
        {
            server.ReleaseHandle(fresh);
        }
    }

    /// <summary>
    /// A released handle is rejected too, and the rejection is counted so a consumer bug cannot hide.
    /// </summary>
    [TestCase]
    public void ReleasedHandlesAreRejectedAndCounted()
    {
        var server = TypographyServer.Instance;
        var handle = server.CreateHandle();
        server.ReleaseHandle(handle);

        long before = server.RejectedRequests;
        AssertThat(server.IsHandleValid(handle)).IsFalse();

        long requestId = server.RequestRelayout(handle, Settings());
        AssertThat(requestId).IsEqual(TypographyServer.NotSubmitted);
        AssertThat(server.RejectedRequests).OverrideFailureMessage(
            "the rejection must be visible through RejectedRequests").IsEqual(before + 1);
    }

    /// <summary>
    /// Every result carries phase timings and counts, and reports at least one line for real text.
    /// The timings are not asserted to specific values (that would be a flaky test) — only that the
    /// pipeline filled them in.
    /// </summary>
    [TestCase]
    public void ResultsCarryDiagnostics()
    {
        var server = TypographyServer.Instance;
        var handle = server.CreateHandle();

        try
        {
            long requestId = server.RequestFullLayout(handle, BuildElements(), Settings());
            var result = WaitForResult(server, handle, requestId);

            AssertThat(result.LineCount > 0).IsTrue();
            AssertThat(result.ElementCount > 0).IsTrue();
            AssertThat(result.ElementCount >= result.LineCount).IsTrue();
            AssertThat(result.Timings.TotalMs > 0d).IsTrue();
            AssertThat(result.Timings.PrepareMs > 0d).OverrideFailureMessage(
                "the prepare phase must be timed, it is the expensive half that a relayout skips").IsTrue();
            AssertThat(result.ProhibitedBreakSkips >= 0).IsTrue();

            string described = result.Describe();
            AssertThat(described.Contains("lines=", StringComparison.Ordinal)).OverrideFailureMessage(
                $"Describe() must summarise the result, got: {described}").IsTrue();
        }
        finally
        {
            server.ReleaseHandle(handle);
        }
    }

    /// <summary>
    /// A relayout reuses the cached prepared content, so its prepare phase must be cheaper than a full
    /// layout's — the property <see cref="TypographyServer.RequestRelayout"/> promises. Compared
    /// qualitatively (relayout does not re-measure) rather than by wall-clock thresholds.
    /// </summary>
    [TestCase]
    public void RelayoutSkipsThePreparePhase()
    {
        var server = TypographyServer.Instance;
        var handle = server.CreateHandle();

        try
        {
            var elements = BuildElements();
            long fullId = server.RequestFullLayout(handle, elements, Settings());
            var full = WaitForResult(server, handle, fullId);
            AssertThat(full.Timings.PrepareMs > 0d).IsTrue();

            long relayoutId = server.RequestRelayout(handle, Settings(maxWidth: 320f));
            var relayout = WaitForResult(server, handle, relayoutId);

            AssertThat(relayout.Error).IsNull();
            AssertThat(relayout.Timings.PrepareMs).OverrideFailureMessage(
                "a relayout must not re-prepare (that is the point of the PreparedContent cache)")
                .IsEqual(0d);
        }
        finally
        {
            server.ReleaseHandle(handle);
        }
    }

    // ── Helpers ──

    private static DrawElement[] BuildElements() =>
    [
        TextElement("标点、断行与首行缩进的组合会被完整记录在结果里。"),
        TextElement("第二段用于验证多段落的结果计数。"),
    ];

    private static DrawElement TextElement(string text) => new()
    {
        Type = DrawElement.ElementType.Text,
        Text = text,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
        Color = new Color(0.1f, 0.1f, 0.1f),
    };

    private static TypographySettings Settings(float maxWidth = 240f) => new()
    {
        MaxWidth = maxWidth,
        LineSpacing = 0f,
        ParagraphSpacing = 0f,
    };

    private static LayoutResult WaitForResult(TypographyServer server, LayoutHandle handle, long requestId)
    {
        for (int attempt = 0; attempt < 600; attempt++)
        {
            while (server.TryGetResult(handle, out var result))
            {
                if (result.RequestId == requestId)
                    return result;
            }

            OS.DelayMsec(5);
        }

        throw new TimeoutException($"layout request {requestId} produced no result");
    }
}
