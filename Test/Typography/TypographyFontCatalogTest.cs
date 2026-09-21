namespace GodotNodeExtension.Tests.Typography;

using System;
using GdUnit4;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;
using GodotNodeExtension.Component.Typography.Server;
using static GdUnit4.Assertions;

/// <summary>
/// Specification for the font boundary of the typography server: producers resolve Godot fonts on the
/// caller's (main) thread, and the layout thread only ever performs a catalog lookup by id.
/// <para>
/// This is asserted rather than assumed because the failure mode is invisible: reading a Godot
/// resource from the layout thread races with font release and hot reload, and it would still produce
/// plausible layout output in a test that only checks geometry.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyFontCatalogTest
{
    /// <summary>
    /// The full-layout entry point resolves every font before the request is queued, and the layout
    /// thread adds no resolution of its own.
    /// </summary>
    [TestCase]
    public void FullLayoutResolvesFontsOnTheCallerThread()
    {
        var elements = BuildElements();
        AssertThat(elements[0].ResolvedFontId == 0).OverrideFailureMessage(
            "the test must start from unresolved elements").IsTrue();

        long resolutionsBefore = FontCatalog.Shared.ResolutionsOffMainThread;
        var server = TypographyServer.Instance;
        var handle = server.CreateHandle();

        try
        {
            long requestId = server.RequestFullLayout(
                handle, elements, new TypographySettings { MaxWidth = 200f });

            foreach (var element in elements)
            {
                if (element.Font == null) continue;
                AssertThat(element.ResolvedFontId != 0).OverrideFailureMessage(
                    "RequestFullLayout must resolve fonts on the caller's thread").IsTrue();
            }

            var result = WaitForResult(server, handle, requestId);
            AssertThat(result.Error).IsNull();
            AssertThat(result.Elements is { Count: > 0 }).IsTrue();
            AssertThat(FontCatalog.Shared.ResolutionsOffMainThread).OverrideFailureMessage(
                "the layout thread resolved a Godot font; producers must resolve them first")
                .IsEqual(resolutionsBefore);
        }
        finally
        {
            server.ReleaseHandle(handle);
        }
    }

    /// <summary>
    /// The streaming entry point resolves the appended element too: it takes the element by value, so
    /// forgetting this would silently move the resolution back onto the layout thread.
    /// </summary>
    [TestCase]
    public void StreamAppendResolvesTheFontOnTheCallerThread()
    {
        long resolutionsBefore = FontCatalog.Shared.ResolutionsOffMainThread;
        var server = TypographyServer.Instance;
        var handle = server.CreateHandle();

        try
        {
            var element = TextElement("流式追加的文字");

            // The element is passed by value, so the caller's copy cannot show the resolution: the
            // evidence is that the layout thread does not have to do it (asserted through the
            // catalog's violation counter below) and that a layout result is still produced.
            server.RequestStreamAppend(handle, element, 0);
            long flushId = server.RequestStreamFlush(handle);
            var result = WaitForResult(server, handle, flushId);

            AssertThat(result.Error).IsNull();
            AssertThat(result.Elements is { Count: > 0 }).IsTrue();
            AssertThat(FontCatalog.Shared.ResolutionsOffMainThread).OverrideFailureMessage(
                "the layout thread resolved a Godot font while streaming")
                .IsEqual(resolutionsBefore);
        }
        finally
        {
            server.ReleaseHandle(handle);
        }
    }

    /// <summary>
    /// A resolved id is what the catalog hands the layout thread; the catalog must be able to answer
    /// for it without the Godot font, which is the whole point of the split.
    /// </summary>
    [TestCase]
    public void ResolvedIdsAreLookupsNotGodotCalls()
    {
        var element = TextElement("解析后的字体编号可以被直接查询");
        FontCatalog.Shared.EnsureResolved(ref element);
        ulong id = element.ResolvedFontId;

        AssertThat(id != 0).IsTrue();
        AssertThat(FontCatalog.Shared.IsResolved(id)).IsTrue();
        AssertThat(FontCatalog.Shared.TryGetTypeface(id, out var typeface)).IsTrue();
        AssertThat(typeface != null).IsTrue();
    }

    // ── Helpers ──

    private static DrawElement[] BuildElements() =>
    [
        TextElement("字体解析发生在提交之前，排版线程只按编号查询。"),
        TextElement("第二段文字用于验证多段落的处理路径。"),
    ];

    private static DrawElement TextElement(string text) => new()
    {
        Type = DrawElement.ElementType.Text,
        Text = text,
        Font = ThemeDB.FallbackFont,
        FontSize = 16,
        Color = new Color(0.1f, 0.1f, 0.1f),
    };

    /// <summary>
    /// Poll for a specific request's result. The server runs on its own thread and the test runs on the
    /// main thread, so this waits the same way a canvas does in <c>_Process</c>.
    /// </summary>
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
