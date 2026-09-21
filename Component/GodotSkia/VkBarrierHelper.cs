using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using static GodotNodeExtension.Component.GodotSkia.VkInterop;
using Godot;

namespace GodotNodeExtension.Component.GodotSkia;

// source: https://github.com/MrJul/Estragonia/blob/main/src/JLeb.Estragonia/VkBarrierHelper.cs
// author: MrJul
// License: MIT

/// <summary>
/// Helper class to create and submit Vulkan image layout transition barriers.
/// Manages reusable command buffers and fences for efficient barrier submission.
/// </summary>
internal sealed class VkBarrierHelper : IDisposable
{
    private readonly VkDevice _device;
    private readonly VkQueue _queue;
    private readonly VkDeviceApi _deviceApi;
    private readonly uint _queueFamilyIndex;
    private readonly List<ReusableBuffer> _reusableBuffers = [];

    private bool _isDisposed;

    public VkBarrierHelper(VkDevice device, VkQueue queue, VkDeviceApi deviceApi, uint queueFamilyIndex)
    {
        _device = device;
        _queue = queue;
        _deviceApi = deviceApi;
        _queueFamilyIndex = queueFamilyIndex;
    }

    /// <summary>
    /// Gets the source pipeline stage flags for a given image layout.
    /// Mirrors Skia's <c>LayoutToPipelineSrcStageFlags</c> in GrVkImage.cpp.
    /// </summary>
    private static VkPipelineStageFlags LayoutToSrcStageFlags(VkImageLayout layout) => layout switch
    {
        VkImageLayout.UNDEFINED => VkPipelineStageFlags.TOP_OF_PIPE_BIT,
        VkImageLayout.GENERAL => VkPipelineStageFlags.ALL_COMMANDS_BIT,
        VkImageLayout.COLOR_ATTACHMENT_OPTIMAL => VkPipelineStageFlags.COLOR_ATTACHMENT_OUTPUT_BIT,
        VkImageLayout.TRANSFER_SRC_OPTIMAL => VkPipelineStageFlags.TRANSFER_BIT,
        VkImageLayout.TRANSFER_DST_OPTIMAL => VkPipelineStageFlags.TRANSFER_BIT,
        VkImageLayout.SHADER_READ_ONLY_OPTIMAL => VkPipelineStageFlags.FRAGMENT_SHADER_BIT,
        VkImageLayout.PRESENT_SRC_KHR => VkPipelineStageFlags.COLOR_ATTACHMENT_OUTPUT_BIT,
        _ => VkPipelineStageFlags.ALL_COMMANDS_BIT
    };

    /// <summary>
    /// Gets the destination pipeline stage flags for a given image layout.
    /// </summary>
    private static VkPipelineStageFlags LayoutToDstStageFlags(VkImageLayout layout) => layout switch
    {
        VkImageLayout.COLOR_ATTACHMENT_OPTIMAL => VkPipelineStageFlags.COLOR_ATTACHMENT_OUTPUT_BIT,
        VkImageLayout.TRANSFER_SRC_OPTIMAL => VkPipelineStageFlags.TRANSFER_BIT,
        VkImageLayout.TRANSFER_DST_OPTIMAL => VkPipelineStageFlags.TRANSFER_BIT,
        VkImageLayout.SHADER_READ_ONLY_OPTIMAL => VkPipelineStageFlags.FRAGMENT_SHADER_BIT,
        _ => VkPipelineStageFlags.ALL_COMMANDS_BIT
    };

    /// <summary>
    /// Gets the source access mask for a given image layout.
    /// Mirrors Skia's <c>LayoutToSrcAccessMask</c> in GrVkImage.cpp.
    /// </summary>
    public static VkAccessFlags LayoutToSrcAccessMask(VkImageLayout layout) => layout switch
    {
        VkImageLayout.UNDEFINED => 0,
        VkImageLayout.COLOR_ATTACHMENT_OPTIMAL => VkAccessFlags.COLOR_ATTACHMENT_WRITE_BIT,
        VkImageLayout.TRANSFER_DST_OPTIMAL => VkAccessFlags.TRANSFER_WRITE_BIT,
        VkImageLayout.TRANSFER_SRC_OPTIMAL => 0,
        VkImageLayout.SHADER_READ_ONLY_OPTIMAL => 0,
        VkImageLayout.GENERAL => VkAccessFlags.COLOR_ATTACHMENT_WRITE_BIT
                               | VkAccessFlags.TRANSFER_WRITE_BIT
                               | VkAccessFlags.HOST_WRITE_BIT,
        VkImageLayout.PRESENT_SRC_KHR => 0,
        _ => VkAccessFlags.MEMORY_READ_BIT | VkAccessFlags.MEMORY_WRITE_BIT
    };

    /// <summary>
    /// Gets the destination access mask for a given image layout.
    /// </summary>
    public static VkAccessFlags LayoutToDstAccessMask(VkImageLayout layout) => layout switch
    {
        VkImageLayout.COLOR_ATTACHMENT_OPTIMAL => VkAccessFlags.COLOR_ATTACHMENT_READ_BIT
                                                | VkAccessFlags.COLOR_ATTACHMENT_WRITE_BIT,
        VkImageLayout.TRANSFER_DST_OPTIMAL => VkAccessFlags.TRANSFER_WRITE_BIT,
        VkImageLayout.TRANSFER_SRC_OPTIMAL => VkAccessFlags.TRANSFER_READ_BIT,
        VkImageLayout.SHADER_READ_ONLY_OPTIMAL => VkAccessFlags.SHADER_READ_BIT,
        _ => VkAccessFlags.MEMORY_READ_BIT | VkAccessFlags.MEMORY_WRITE_BIT
    };

    /// <summary>
    /// Transitions a Vulkan image from one layout to another using a pipeline barrier.
    /// Uses access masks and pipeline stage flags that mirror Skia's internal
    /// <c>LayoutToSrcAccessMask</c> and <c>LayoutToPipelineSrcStageFlags</c>.
    /// </summary>
    /// <param name="image">The Vulkan image to transition.</param>
    /// <param name="sourceLayout">The current layout of the image.</param>
    /// <param name="sourceAccessMask">The source access mask.</param>
    /// <param name="destinationLayout">The target layout to transition to.</param>
    /// <param name="destinationAccessMask">The destination access mask.</param>
    /// <param name="waitForCompletion">When true, blocks until the barrier completes on the GPU.
    /// Can be false when queue ordering guarantees subsequent work waits (e.g., Skia Submit(true) follows).</param>
    public void TransitionImageLayout(
        VkImage image,
        VkImageLayout sourceLayout,
        VkAccessFlags sourceAccessMask,
        VkImageLayout destinationLayout,
        VkAccessFlags destinationAccessMask,
        bool waitForCompletion = true)
    {
        if (_isDisposed)
            ThrowDisposed();

        // The stage flags follow from the layouts (Skia's own mapping): every caller is Skia-driven, which
        // is why the two optional parameters that used to let a caller override them were never passed.
        var srcStageFlags = LayoutToSrcStageFlags(sourceLayout);
        var dstStageFlags = LayoutToDstStageFlags(destinationLayout);

        var reusableBuffer = GetOrCreateReusableBuffer();

        try
        {
            SubmitBarrier(reusableBuffer, image, sourceLayout, sourceAccessMask,
                          destinationLayout, destinationAccessMask,
                          srcStageFlags, dstStageFlags, waitForCompletion);
        }
        catch
        {
            // The fence was reset but the submission did not complete: nobody can ever wait on it, so the
            // buffer is marked dead and the pool replaces it on the next call.
            reusableBuffer.MarkUnusable();
            throw;
        }
    }

    private unsafe void SubmitBarrier(
        ReusableBuffer reusableBuffer,
        VkImage image,
        VkImageLayout sourceLayout,
        VkAccessFlags sourceAccessMask,
        VkImageLayout destinationLayout,
        VkAccessFlags destinationAccessMask,
        VkPipelineStageFlags srcStageFlags,
        VkPipelineStageFlags dstStageFlags,
        bool waitForCompletion)
    {
        var fence = reusableBuffer.Fence;
        _deviceApi.ResetFences(_device, 1, &fence);

        var commandBuffer = reusableBuffer.CommandBuffer;

        var beginInfo = new VkCommandBufferBeginInfo
        {
            sType = VkStructureType.COMMAND_BUFFER_BEGIN_INFO,
            flags = VkCommandBufferUsageFlags.ONE_TIME_SUBMIT_BIT
        };

        _deviceApi.BeginCommandBuffer(commandBuffer, ref beginInfo);

        var barrier = new VkImageMemoryBarrier
        {
            sType = VkStructureType.IMAGE_MEMORY_BARRIER,
            srcAccessMask = sourceAccessMask,
            dstAccessMask = destinationAccessMask,
            oldLayout = sourceLayout,
            newLayout = destinationLayout,
            srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED,
            image = image,
            subresourceRange = new VkImageSubresourceRange
            {
                aspectMask = VkImageAspectFlags.COLOR_BIT,
                baseMipLevel = 0,
                levelCount = 1,
                baseArrayLayer = 0,
                layerCount = 1
            }
        };

        _deviceApi.CmdPipelineBarrier(
            reusableBuffer.CommandBuffer,
            srcStageFlags,
            dstStageFlags,
            0,
            0,
            IntPtr.Zero,
            0,
            IntPtr.Zero,
            1,
            &barrier);

        _deviceApi.EndCommandBuffer(commandBuffer);

        var submitInfo = new VkSubmitInfo
        {
            sType = VkStructureType.SUBMIT_INFO,
            waitSemaphoreCount = 0,
            pWaitSemaphores = null,
            pWaitDstStageMask = null,
            commandBufferCount = 1,
            pCommandBuffers = &commandBuffer,
            signalSemaphoreCount = 0,
            pSignalSemaphores = null
        };

        _deviceApi.QueueSubmit(_queue, 1, &submitInfo, fence);

        if (waitForCompletion)
        {
            // Wait for the barrier to complete before returning.
            // This ensures the layout transition is fully done before Skia renders
            // (after transition to COLOR_ATTACHMENT) or Godot samples
            // (after transition to SHADER_READ_ONLY).
            // The wait is unbounded here (UInt64.MaxValue), so the only possible outcomes are VK_SUCCESS or
            // a real error (device lost, ...) - both are reported instead of being swallowed.
            var result = _deviceApi.WaitForFences(_device, 1, &fence, 1, UInt64.MaxValue);
            if (result != VkResult.VK_SUCCESS)
                throw new InvalidOperationException(
                    $"VkBarrierHelper: waiting for the barrier fence failed with {result}.");
            _pendingBuffer = null;
        }
        else
        {
            // The caller asked for no wait; remember the buffer so it can wait later, outside its own
            // locks (see WaitForPendingBarrier). The reusable pool hands out another buffer until the
            // wait is consumed, so the handle stays valid.
            _pendingBuffer = reusableBuffer;
        }
    }

    /// <summary>
    /// Buffer holding the barrier submitted by the last <see cref="TransitionImageLayout"/> call that passed
    /// <c>waitForCompletion: false</c>, otherwise null. The buffer (not just its fence) is remembered so a
    /// wait that times out can mark the right buffer dead instead of leaving it in the pool.
    /// </summary>
    private ReusableBuffer? _pendingBuffer;

    /// <summary>
    /// Wait for the barrier submitted by the last <see cref="TransitionImageLayout"/> call that passed
    /// <c>waitForCompletion: false</c>. Lets the caller keep GPU waits outside its own locks: one
    /// texture waiting on the GPU no longer blocks the other textures' submissions.
    /// Safe to call when nothing is pending.
    /// </summary>
    /// <returns>
    /// True when the barrier is known to have completed (including "nothing was pending"), false when the
    /// fence did not signal within <see cref="FenceWaitTimeoutNs"/>. A false result means the layout
    /// transition must <b>not</b> be treated as done, and the buffer is dropped from the pool because it
    /// may still be executing - re-recording it (ResetFences + Begin) would race the GPU.
    /// </returns>
    public bool WaitForPendingBarrier()
    {
        if (_isDisposed || _pendingBuffer is not { } buffer) return true;
        _pendingBuffer = null;

        if (buffer.WaitForFence(FenceWaitTimeoutNs)) return true;

        GD.PushError(
            $"VkBarrierHelper: a barrier fence did not signal within " +
            $"{FenceWaitTimeoutNs / 1_000_000_000} s; the command buffer is dropped and the layout " +
            "transition is reported as incomplete.");
        buffer.MarkUnusable();
        return false;
    }

    private const int MaxReusableBuffers = 8;

    /// <summary>
    /// Upper bound for a fence wait. A submitted barrier finishes in microseconds; a device that is lost
    /// (or a submission that failed before the fence was armed) never signals at all, and an unlimited wait
    /// used to hang the whole process while the caller held the static queue lock.
    /// </summary>
    private const ulong FenceWaitTimeoutNs = 5_000_000_000UL;

    private ReusableBuffer GetOrCreateReusableBuffer()
    {
        for (var i = 0; i < _reusableBuffers.Count; i++)
        {
            // A buffer whose submission failed can never signal its fence again: drop it instead of
            // waiting for it forever (and instead of letting it fill the pool's capacity).
            if (_reusableBuffers[i].IsUnusable)
            {
                _reusableBuffers[i].ForceDispose();
                _reusableBuffers.RemoveAt(i--);
                continue;
            }

            if (_reusableBuffers[i].IsAvailable())
                return _reusableBuffers[i];
        }

        if (_reusableBuffers.Count >= MaxReusableBuffers)
        {
            // All buffers busy and at capacity: wait for the oldest one - but only hand it out when its
            // fence actually signalled. A buffer that never signalled (lost device, a submission that
            // failed before the fence was armed) may still be executing, and re-recording it would race
            // the GPU, so it is dropped and replaced instead.
            var buffer = _reusableBuffers[0];
            if (buffer.WaitForFence(FenceWaitTimeoutNs))
                return buffer;

            GD.PushError(
                $"VkBarrierHelper: the reusable barrier pool is full and the oldest fence did not signal " +
                $"within {FenceWaitTimeoutNs / 1_000_000_000} s; dropping that buffer.");
            buffer.ForceDispose();
            _reusableBuffers.RemoveAt(0);

            var fresh = new ReusableBuffer(_device, _deviceApi, _queueFamilyIndex);
            _reusableBuffers.Add(fresh);
            return fresh;
        }

        var newBuffer = new ReusableBuffer(_device, _deviceApi, _queueFamilyIndex);
        _reusableBuffers.Add(newBuffer);
        return newBuffer;
    }

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowDisposed()
        => throw new ObjectDisposedException(nameof(VkBarrierHelper));

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        // Every buffer is released on its own: one throwing (a lost device during the fence wait) must not
        // leave the remaining native objects behind, and Dispose may not be retried after this flag.
        for (var i = _reusableBuffers.Count - 1; i >= 0; --i)
        {
            try
            {
                _reusableBuffers[i].Dispose();
            }
            catch (Exception ex)
            {
                GD.PushWarning($"VkBarrierHelper: releasing a barrier buffer failed ({ex.Message}).");
                _reusableBuffers[i].ForceDispose();
            }
        }

        _reusableBuffers.Clear();
    }

    /// <summary>
    /// Contains a reusable command pool, command buffer and an associated fence.
    /// </summary>
    private sealed class ReusableBuffer
    {
        private readonly VkDevice _device;
        private readonly VkDeviceApi _deviceApi;
        private readonly VkCommandPool _commandPool;
        private bool _isDisposed;

        public VkCommandBuffer CommandBuffer { get; }
        public VkFence Fence { get; }

        /// <summary>
        /// True once a submission on this buffer failed: its fence can never signal again.
        /// </summary>
        public bool IsUnusable { get; private set; }

        /// <summary>Mark the buffer dead after a failed submission (see <see cref="IsUnusable"/>).</summary>
        public void MarkUnusable() => IsUnusable = true;

        public bool IsAvailable()
            => !IsUnusable && _deviceApi.GetFenceStatus(_device, Fence) == VkResult.VK_SUCCESS;

        /// <summary>
        /// Wait (bounded) for this buffer's fence to signal.
        /// </summary>
        /// <param name="timeoutNs">Upper bound for the wait, in nanoseconds.</param>
        /// <returns>True when the fence signalled, false on <c>VK_TIMEOUT</c> or any other non-success
        /// result - the caller must not reuse the buffer in that case.</returns>
        public unsafe bool WaitForFence(ulong timeoutNs)
        {
            var fence = Fence;
            return _deviceApi.WaitForFences(_device, 1, &fence, 1, timeoutNs) == VkResult.VK_SUCCESS;
        }

        /// <summary>Dispose without waiting for the fence (the buffer is unusable already).</summary>
        public void ForceDispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            IsUnusable = true;

            _deviceApi.DestroyFence(_device, Fence, IntPtr.Zero);

            var commandBuffer = CommandBuffer;
            unsafe
            {
                _deviceApi.FreeCommandBuffers(_device, _commandPool, 1, &commandBuffer);
            }
            _deviceApi.DestroyCommandPool(_device, _commandPool, IntPtr.Zero);
        }

        public unsafe ReusableBuffer(VkDevice device, VkDeviceApi deviceApi, uint queueFamilyIndex)
        {
            _device = device;
            _deviceApi = deviceApi;

            var poolCreateInfo = new VkCommandPoolCreateInfo
            {
                sType = VkStructureType.COMMAND_POOL_CREATE_INFO,
                flags = VkCommandPoolCreateFlags.RESET_COMMAND_BUFFER_BIT,
                queueFamilyIndex = queueFamilyIndex
            };

            try
            {
                deviceApi.CreateCommandPool(device, ref poolCreateInfo, IntPtr.Zero, out _commandPool);

                var bufferAllocateInfo = new VkCommandBufferAllocateInfo
                {
                    sType = VkStructureType.COMMAND_BUFFER_ALLOCATE_INFO,
                    commandPool = _commandPool,
                    level = VkCommandBufferLevel.PRIMARY,
                    commandBufferCount = 1
                };

                VkCommandBuffer commandBuffer;
                deviceApi.AllocateCommandBuffers(_device, ref bufferAllocateInfo, &commandBuffer);
                CommandBuffer = commandBuffer;

                var fenceCreateInfo = new VkFenceCreateInfo
                {
                    sType = VkStructureType.FENCE_CREATE_INFO,
                    flags = VkFenceCreateFlags.SIGNALED_BIT
                };

                deviceApi.CreateFence(device, ref fenceCreateInfo, IntPtr.Zero, out var fence);
                Fence = fence;
            }
            catch
            {
                // A construction that fails halfway owns native objects nothing else knows about: the
                // instance never reaches the pool, so no Dispose would ever run for them (device-lost and
                // out-of-memory are exactly when the second or third call fails). Destroy what was
                // created, newest first, and let the original error out.
                if (Fence.Handle != 0)
                {
                    _deviceApi.DestroyFence(_device, Fence, IntPtr.Zero);
                }

                if (CommandBuffer.Handle != IntPtr.Zero)
                {
                    var commandBuffer = CommandBuffer;
                    _deviceApi.FreeCommandBuffers(_device, _commandPool, 1, &commandBuffer);
                }

                if (_commandPool.Handle != 0)
                {
                    _deviceApi.DestroyCommandPool(_device, _commandPool, IntPtr.Zero);
                }

                throw;
            }
        }

        public unsafe void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            if (!IsUnusable)
            {
                // The wait is only a safety net; a device that is already lost must not stop the native
                // objects from being destroyed (that would leak the pool, the buffer and the fence).
                // A timeout is therefore ignored here on purpose - unlike in WaitForPendingBarrier, where
                // the caller has to know that the transition did not complete.
                WaitForFence(FenceWaitTimeoutNs);
            }

            _deviceApi.DestroyFence(_device, Fence, IntPtr.Zero);

            var commandBuffer = CommandBuffer;
            _deviceApi.FreeCommandBuffers(_device, _commandPool, 1, &commandBuffer);

            _deviceApi.DestroyCommandPool(_device, _commandPool, IntPtr.Zero);
        }
    }
}
