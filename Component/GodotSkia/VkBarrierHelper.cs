using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using static GodotNodeExtension.Component.GodotSkia.VkInterop;

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
    private readonly List<ReusableBuffer> _reusableBuffers = new();

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
    public static VkPipelineStageFlags LayoutToSrcStageFlags(VkImageLayout layout) => layout switch
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
    public static VkPipelineStageFlags LayoutToDstStageFlags(VkImageLayout layout) => layout switch
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
    /// <param name="srcStageFlags">Source pipeline stage flags (defaults to auto-computed from layout).</param>
    /// <param name="dstStageFlags">Destination pipeline stage flags (defaults to auto-computed from layout).</param>
    /// <param name="waitForCompletion">When true, blocks until the barrier completes on the GPU.
    /// Can be false when queue ordering guarantees subsequent work waits (e.g., Skia Submit(true) follows).</param>
    public unsafe void TransitionImageLayout(
        VkImage image,
        VkImageLayout sourceLayout,
        VkAccessFlags sourceAccessMask,
        VkImageLayout destinationLayout,
        VkAccessFlags destinationAccessMask,
        VkPipelineStageFlags srcStageFlags = 0,
        VkPipelineStageFlags dstStageFlags = 0,
        bool waitForCompletion = true)
    {
        if (_isDisposed)
            ThrowDisposed();

        // Auto-compute stage flags from layouts if not explicitly provided
        if (srcStageFlags == 0)
            srcStageFlags = LayoutToSrcStageFlags(sourceLayout);
        if (dstStageFlags == 0)
            dstStageFlags = LayoutToDstStageFlags(destinationLayout);

        var reusableBuffer = GetOrCreateReusableBuffer();

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

        // Wait for the barrier to complete before returning.
        // This ensures the layout transition is fully done before Skia renders
        // (after transition to COLOR_ATTACHMENT) or Godot samples
        // (after transition to SHADER_READ_ONLY).
        // Can be skipped when queue ordering provides sufficient guarantees.
        if (waitForCompletion)
            _deviceApi.WaitForFences(_device, 1, &fence, 1, UInt64.MaxValue);
    }

    private const int MaxReusableBuffers = 8;

    private ReusableBuffer GetOrCreateReusableBuffer()
    {
        foreach (var existingBuffer in _reusableBuffers)
        {
            if (existingBuffer.IsAvailable())
                return existingBuffer;
        }

        if (_reusableBuffers.Count >= MaxReusableBuffers)
        {
            // All buffers busy and at capacity — wait for the first one
            var buffer = _reusableBuffers[0];
            buffer.WaitUntilAvailable();
            return buffer;
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

        for (var i = _reusableBuffers.Count - 1; i >= 0; --i)
            _reusableBuffers[i].Dispose();

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

        public bool IsAvailable()
            => _deviceApi.GetFenceStatus(_device, Fence) == VkResult.VK_SUCCESS;

        public unsafe void WaitUntilAvailable()
        {
            var fence = Fence;
            _deviceApi.WaitForFences(_device, 1, &fence, 1, UInt64.MaxValue);
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

        public unsafe void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            var fence = Fence;
            _deviceApi.WaitForFences(_device, 1, &fence, 1, UInt64.MaxValue);

            _deviceApi.DestroyFence(_device, fence, IntPtr.Zero);

            var commandBuffer = CommandBuffer;
            _deviceApi.FreeCommandBuffers(_device, _commandPool, 1, &commandBuffer);

            _deviceApi.DestroyCommandPool(_device, _commandPool, IntPtr.Zero);
        }
    }
}
