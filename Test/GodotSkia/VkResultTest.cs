namespace GodotNodeExtension.Tests.GodotSkia;

using System;
using GdUnit4;
using GodotNodeExtension.Component.GodotSkia;
using static GdUnit4.Assertions;

/// <summary>
/// The Vulkan result guard the whole barrier layer is built on: every command-pool, command-buffer,
/// submit and fence call reports its failure through it.
/// <para>
/// Vulkan has several <b>positive</b> codes that are not successes - <c>VK_TIMEOUT</c> = 2 above all, which
/// is exactly what a bounded <c>vkWaitForFences</c> returns. The guard used to reject only negative codes,
/// so a wait that timed out counted as a completed operation: the barrier helper cleared its pending fence
/// and the texture recorded a layout transition that had not happened, and a buffer whose fence never
/// signalled could be handed out of the reusable pool again while the GPU was still executing it.
/// </para>
/// <para>
/// The device-side halves of those paths (the pool, the waits) need a real GPU and are covered by the
/// integration suites and the code review listed in the design document, not here.
/// </para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class VkResultTest
{
    private static Exception? CaptureException(Action action)
    {
        try { action(); return null; }
        catch (Exception ex) { return ex; }
    }

    [TestCase]
    public void SuccessIsAccepted()
    {
        AssertThat(CaptureException(() => VkInterop.VkResult.VK_SUCCESS.VerifySuccess("CreateFence")) is null).IsTrue();
    }

    [TestCase]
    public void EveryPositiveNonSuccessCodeIsAFailure()
    {
        // The three codes Vulkan uses for "the operation is fine but did not do the thing".
        AssertThat(CaptureException(() => VkInterop.VkResult.VK_NOT_READY.VerifySuccess("WaitForFences")) is InvalidOperationException).IsTrue();
        AssertThat(CaptureException(() => VkInterop.VkResult.VK_TIMEOUT.VerifySuccess("WaitForFences")) is InvalidOperationException).IsTrue();
        AssertThat(CaptureException(() => VkInterop.VkResult.VK_INCOMPLETE.VerifySuccess("WaitForFences")) is InvalidOperationException).IsTrue();
    }

    [TestCase]
    public void ANegativeCodeIsAFailureAndNamesTheOperation()
    {
        var ex = CaptureException(() => VkInterop.VkResult.VK_ERROR_DEVICE_LOST.VerifySuccess("QueueSubmit"));

        AssertThat(ex is InvalidOperationException).IsTrue();
        AssertThat(ex!.Message.Contains("QueueSubmit")).IsTrue();
        AssertThat(ex.Message.Contains("DEVICE_LOST")).IsTrue();
    }
}
