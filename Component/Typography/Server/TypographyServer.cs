using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Godot;
using GodotNodeExtension.Component.Typography.Core;
using GodotNodeExtension.Component.Typography.Core.Model;

namespace GodotNodeExtension.Component.Typography.Server;

/// <summary>
/// Typography server running on a dedicated background thread.
/// Supports multiple concurrent clients via the Handle model.
/// Each Handle maintains its own <see cref="TypographyEngine"/>, PreparedContent cache,
/// and resource references. Shared resources (HarfBuzzTextShaper, etc.)
/// use reference counting to avoid duplication and ensure safe cleanup.
///
/// Lifecycle: created once, runs until Shutdown() is called.
/// Modeled after Godot's built-in servers (RenderingServer, PhysicsServer).
/// <para>
/// The layout thread exists only outside the editor. A live thread whose entry point belongs to this
/// assembly keeps the assembly - and therefore the load context that owns it - reachable, which is the
/// state the editor reports as `Failed to unload assemblies` (godot#78513). In the editor the queue is
/// drained synchronously on the caller's thread instead; see <see cref="UseBackgroundThread"/>.
/// </para>
/// </summary>
public class TypographyServer : IDisposable
{
    private static readonly Lazy<TypographyServer> SInstance = new(
        () => new TypographyServer(), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Singleton instance. Lazily created on first access.</summary>
    public static TypographyServer Instance => SInstance.Value;

    /// <summary>
    /// Stop the layout thread when the load context unloads. The thread runs a method of this assembly,
    /// and a live thread keeps its assembly - and therefore the load context that owns it - alive; that is
    /// the state the editor reports as an assembly it cannot unload (godot#78513). The lazy singleton is
    /// only touched when it was actually created, so an unload never starts a server.
    /// </summary>
    static TypographyServer()
    {
        var context = System.Runtime.Loader.AssemblyLoadContext
            .GetLoadContext(typeof(TypographyServer).Assembly);
        if (context is null) return;

        context.Unloading += _ =>
        {
            if (SInstance.IsValueCreated) SInstance.Value.Shutdown();
        };
        UnloadHookInstalled = true;
    }

    /// <summary>Whether the unload hook is registered. Test seam (the event cannot be raised in-process).</summary>
    internal static bool UnloadHookInstalled { get; private set; }

    // ── Threading ──
    private Thread? _serverThread;
    private readonly ManualResetEventSlim _requestEvent = new(false);
    private volatile bool _shutdownRequested;
    private readonly Lock _drainLock = new();

    /// <summary>
    /// Whether layout runs on a dedicated background thread.
    /// <para>
    /// The editor rebuilds the project assembly on every build, and a live thread running a method of
    /// that assembly keeps the assembly - and with it the load context that owns it - reachable, so Godot
    /// reports `Failed to unload assemblies` (godot#78513). Stopping that thread from the load context's
    /// Unloading event does not help: the event only runs once the context is collectible, and the live
    /// thread is exactly what keeps it from becoming collectible. Editor previews are small, so there the
    /// queue is drained on the thread that submits the request (see
    /// <see cref="DrainQueueOnCallerThread"/>); a running game keeps the thread.
    /// </para>
    /// </summary>
    private static bool UseBackgroundThread => !Engine.IsEditorHint();

    // ── Request/result queues ──
    private readonly ConcurrentQueue<LayoutRequest> _requestQueue = new();

    // ── Handle management ──
    private long _nextHandleId;
    private long _nextRequestId;
    private readonly ConcurrentDictionary<long, HandleState> _handles = new();

    /// <summary>
    /// Bumped by every shutdown. Handles carry the generation they were created in, so a handle from a
    /// previous server life is rejected instead of silently producing no result.
    /// </summary>
    private int _generation;

    private long _rejectedRequests;

    /// <summary>Whether the server thread is running.</summary>
    public bool IsRunning => _serverThread is { IsAlive: true };

    // ── Handle lifecycle (called from main thread) ──

    /// <summary>
    /// Create a new layout handle for a client.
    /// Allocates per-handle state (TypographyEngine, caches).
    /// Thread-safe: can be called from any thread.
    /// </summary>
    /// <returns>A valid handle to use for requests.</returns>
    public LayoutHandle CreateHandle()
    {
        EnsureStarted();
        long id = Interlocked.Increment(ref _nextHandleId);
        var handle = new LayoutHandle(id, Volatile.Read(ref _generation));
        _handles[id] = new HandleState();
        return handle;
    }

    /// <summary>
    /// Release a handle and all its associated resources.
    /// Cancels any in-flight requests for this handle.
    /// After this call, the handle is invalid and must not be used.
    /// </summary>
    /// <param name="handle">The handle to release.</param>
    public void ReleaseHandle(LayoutHandle handle)
    {
        if (_handles.TryRemove(handle.Id, out var state))
        {
            state.Cancel();
            state.Dispose();
        }
    }

    /// <summary>
    /// Clear the cached PreparedContent for a handle without releasing the handle.
    /// Next FullLayout will re-Prepare from scratch. Shared resources (shapers) are retained.
    /// </summary>
    /// <param name="handle">The handle whose cache to clear.</param>
    public void ClearCache(LayoutHandle handle)
    {
        if (_handles.TryGetValue(handle.Id, out var state))
        {
            state.ClearPreparedContent();
        }
    }

    /// <summary>
    /// Whether a handle can still be used with this server.
    /// <para>
    /// <see cref="LayoutHandle.IsValid"/> only means "a server assigned this handle". A released handle
    /// and every handle from before a <see cref="Shutdown"/> are stale: requests on them cannot produce
    /// a result — there is no queue left to deliver it through — so a caller that keeps using one gets
    /// silence instead of an error. Consumers must check this and create a new handle (RichTextCanvas
    /// does it on its next request).
    /// </para>
    /// </summary>
    /// <param name="handle">The handle to test.</param>
    /// <returns>True when the handle belongs to the current generation and is still registered.</returns>
    public bool IsHandleValid(in LayoutHandle handle) =>
        handle.IsValid
        && handle.Generation == Volatile.Read(ref _generation)
        && _handles.ContainsKey(handle.Id);

    /// <summary>
    /// Number of requests rejected because they arrived on a stale handle. Non-zero means a consumer
    /// kept using a handle after a shutdown; the requests used to be dropped in silence.
    /// </summary>
    public long RejectedRequests => Interlocked.Read(ref _rejectedRequests);

    /// <summary>
    /// Reject a request that arrived on a stale handle and report it once per call site. Returning 0
    /// tells the caller nothing was submitted; the alternative (a request id that never yields a result)
    /// is what made a stale handle indistinguishable from a slow layout.
    /// </summary>
    /// <param name="handle">The stale handle.</param>
    /// <param name="operation">Name of the rejected operation, for the warning.</param>
    /// <returns>Always 0, so callers can return it directly.</returns>
    private long RejectStaleHandle(in LayoutHandle handle, string operation)
    {
        Interlocked.Increment(ref _rejectedRequests);
        GD.PushWarning(
            $"[Typography] {operation} rejected: handle {handle.Id} (generation {handle.Generation}) " +
            $"is not valid for the current server generation ({Volatile.Read(ref _generation)}). " +
            "Create a new handle after a shutdown.");

        return NotSubmitted;
    }

    /// <summary>
    /// Request id returned when a request was not submitted at all (stale handle). It is never a valid
    /// tracking id, so a caller comparing ids can tell "rejected" from "pending".
    /// </summary>
    public const long NotSubmitted = 0;

    // ── Request submission (called from main thread) ──

    /// <summary>
    /// Submit a full layout request (Prepare + Layout).
    /// Automatically cancels any pending request on the same handle.
    /// </summary>
    /// <param name="handle">Client handle.</param>
    /// <param name="elements">DrawElement array (ownership transferred to server).</param>
    /// <param name="settings">Typography settings.</param>
    /// <returns>Request ID for tracking.</returns>
    public long RequestFullLayout(LayoutHandle handle, DrawElement[] elements, TypographySettings settings)
    {
        if (!IsHandleValid(handle))
            return RejectStaleHandle(handle, nameof(RequestFullLayout));

        var requestId = Interlocked.Increment(ref _nextRequestId);

        // Resolve the platform fonts here, on the caller's (main) thread, before the elements reach the
        // layout thread: this is the single entry point every producer goes through, so no producer has
        // to remember it, and the layout thread never touches a Godot resource.
        FontCatalog.Shared.EnsureResolved(elements);

        if (_handles.TryGetValue(handle.Id, out var state))
            state.CancelAndRenew();

        _requestQueue.Enqueue(new LayoutRequest
        {
            Handle = handle,
            RequestId = requestId,
            Type = LayoutRequestType.FullLayout,
            Elements = elements,
            Settings = settings,
        });
        SignalOrDrain();
        return requestId;
    }

    /// <summary>
    /// Submit a relayout request (Layout only, reuse cached PreparedContent).
    /// Used when only MaxWidth changes (e.g., window resize).
    /// </summary>
    /// <param name="handle">Client handle.</param>
    /// <param name="settings">Updated typography settings.</param>
    /// <returns>Request ID for tracking.</returns>
    public long RequestRelayout(LayoutHandle handle, TypographySettings settings)
    {
        if (!IsHandleValid(handle))
            return RejectStaleHandle(handle, nameof(RequestRelayout));

        var requestId = Interlocked.Increment(ref _nextRequestId);

        if (_handles.TryGetValue(handle.Id, out var state))
            state.CancelAndRenew();

        _requestQueue.Enqueue(new LayoutRequest
        {
            Handle = handle,
            RequestId = requestId,
            Type = LayoutRequestType.Relayout,
            Settings = settings,
        });
        SignalOrDrain();
        return requestId;
    }

    /// <summary>
    /// Submit a streaming append request.
    /// </summary>
    /// <param name="handle">Client handle.</param>
    /// <param name="element">Element to append.</param>
    /// <param name="sourceIndex">Source index of the element.</param>
    /// <returns>Request ID for tracking.</returns>
    public long RequestStreamAppend(LayoutHandle handle, DrawElement element, int sourceIndex)
    {
        if (!IsHandleValid(handle))
            return RejectStaleHandle(handle, nameof(RequestStreamAppend));

        var requestId = Interlocked.Increment(ref _nextRequestId);

        // Same as full layout: resolve the font on the caller's thread before the element is queued.
        FontCatalog.Shared.EnsureResolved(ref element);

        _requestQueue.Enqueue(new LayoutRequest
        {
            Handle = handle,
            RequestId = requestId,
            Type = LayoutRequestType.StreamAppend,
            AppendElement = element,
            AppendSourceIndex = sourceIndex,
        });
        SignalOrDrain();
        return requestId;
    }

    /// <summary>
    /// Submit a stream flush (finalize all lines).
    /// </summary>
    /// <param name="handle">Client handle.</param>
    /// <returns>Request ID for tracking.</returns>
    public long RequestStreamFlush(LayoutHandle handle)
    {
        if (!IsHandleValid(handle))
            return RejectStaleHandle(handle, nameof(RequestStreamFlush));

        var requestId = Interlocked.Increment(ref _nextRequestId);
        _requestQueue.Enqueue(new LayoutRequest
        {
            Handle = handle,
            RequestId = requestId,
            Type = LayoutRequestType.StreamFlush,
        });
        SignalOrDrain();
        return requestId;
    }

    /// <summary>
    /// Hand a queued request to its consumer: wake the layout thread, or - in the editor, where there is
    /// none - drain the queue right here on the calling thread.
    /// </summary>
    private void SignalOrDrain()
    {
        if (UseBackgroundThread)
        {
            _requestEvent.Set();
            return;
        }

        DrainQueueOnCallerThread();
    }

    /// <summary>
    /// Process every queued request on the calling thread. Only used in the editor, where no layout thread
    /// exists (see <see cref="UseBackgroundThread"/>). The lock serializes concurrent callers - the main
    /// thread and the content-build worker - so the engine keeps its single-consumer guarantee.
    /// </summary>
    private void DrainQueueOnCallerThread()
    {
        lock (_drainLock)
        {
            while (!_shutdownRequested && _requestQueue.TryDequeue(out var request))
            {
                ProcessRequest(request);
            }
        }
    }

    // ── Result retrieval (called from main thread in _Process) ──

    /// <summary>
    /// Try to dequeue a completed layout result for the given handle.
    /// </summary>
    /// <param name="handle">The handle to check.</param>
    /// <param name="result">The dequeued result, if available.</param>
    /// <returns>True if a result was dequeued.</returns>
    public bool TryGetResult(LayoutHandle handle, out LayoutResult result)
    {
        if (_handles.TryGetValue(handle.Id, out var state))
        {
            return state.ResultQueue.TryDequeue(out result!);
        }
        result = null!;
        return false;
    }

    // ── Lifecycle ──

    /// <summary>
    /// Ensure the server thread is started. Called automatically on first CreateHandle().
    /// </summary>
    public void EnsureStarted()
    {
        if (!UseBackgroundThread) return;
        if (_serverThread is { IsAlive: true }) return;

        lock (this)
        {
            if (_serverThread is { IsAlive: true }) return;
            _shutdownRequested = false;
            _serverThread = new Thread(ServerLoop)
            {
                Name = "TypographyServer",
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
            };
            _serverThread.Start();
        }
    }

    /// <summary>
    /// Shutdown the server thread and release ALL resources.
    /// All handles become invalid. Blocks until the thread exits.
    /// </summary>
    public void Shutdown()
    {
        // Invalidate every existing handle first: after this point a request on one is rejected loudly
        // instead of being queued into a handle table that is about to be disposed.
        Interlocked.Increment(ref _generation);

        _shutdownRequested = true;
        _requestEvent.Set();

        _serverThread?.Join(TimeSpan.FromSeconds(5));
        _serverThread = null;

        // Dispose all handle states
        foreach (var kvp in _handles)
        {
            kvp.Value.Dispose();
        }
        _handles.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Shutdown();
        GC.SuppressFinalize(this);
    }

    // ── Server thread main loop ──

    private void ServerLoop()
    {
        while (!_shutdownRequested)
        {
            while (_requestQueue.TryDequeue(out var request))
            {
                if (_shutdownRequested) return;
                ProcessRequest(request);
            }

            _requestEvent.Reset();

            // Double-check after reset to avoid missed signals
            if (!_requestQueue.IsEmpty) continue;

            _requestEvent.Wait(TimeSpan.FromMilliseconds(100));
        }
    }

    private void ProcessRequest(LayoutRequest request)
    {
        if (!_handles.TryGetValue(request.Handle.Id, out var state))
        {
            // The handle was released between submission and execution. There is nobody to deliver an
            // error to, so the only honest thing left is to count it.
            Interlocked.Increment(ref _rejectedRequests);
            return;
        }

        // A superseded request has no consumer left: the caller already replaced its request id, and the
        // queue it would be delivered through is unbounded. Dropping it here is what keeps rapid resizes
        // from filling the queue with results nobody will read.
        if (state.IsCancelled)
            return;

        try
        {
            switch (request.Type)
            {
                case LayoutRequestType.FullLayout:
                    ProcessFullLayout(request, state);
                    break;

                case LayoutRequestType.Relayout:
                    ProcessRelayout(request, state);
                    break;

                case LayoutRequestType.StreamAppend:
                    ProcessStreamAppend(request, state);
                    break;

                case LayoutRequestType.StreamFlush:
                    ProcessStreamFlush(request, state);
                    break;
            }
        }
        catch (Exception ex)
        {
            state.EnqueueResult(new LayoutResult
            {
                Handle = request.Handle,
                RequestId = request.RequestId,
                Error = ex.Message,
            });
        }
    }

    /// <summary>
    /// Attach the engine's diagnostics to a result. Every result carries them so that cost and
    /// layout-quality signals (prohibited skips, forced breaks) need no opt-in to be observable.
    /// </summary>
    /// <param name="result">The result being published.</param>
    /// <param name="state">Handle state owning the engine the numbers come from.</param>
    /// <param name="elements">Flattened element stream (counts the flatten phase).</param>
    /// <param name="lines">Laid-out lines.</param>
    /// <returns>The same result, with diagnostics filled in.</returns>
    private static LayoutResult WithDiagnostics(
        LayoutResult result,
        HandleState state,
        List<LayoutElement> elements,
        IReadOnlyList<LayoutLine> lines)
    {
        result.LineCount = lines.Count;
        result.ElementCount = elements.Count;
        result.ProhibitedBreakSkips = state.Engine.LastProhibitedBreakSkips;
        result.Timings = state.Engine.LastTimings;
        return result;
    }

    private static void ProcessFullLayout(LayoutRequest request, HandleState state)
    {
        state.Engine.Settings = request.Settings;

        if (request.Elements == null || request.Elements.Length == 0)
        {
            state.EnqueueResult(new LayoutResult
            {
                Handle = request.Handle,
                RequestId = request.RequestId,
                Elements = [],
                ContentSize = Vector2.Zero,
            });
            return;
        }

        // Store source elements for block expansion
        state.SourceElements = request.Elements;

        // Prepare + Layout. The element stream is flattened before diagnostics are read so that the
        // flatten phase is part of the reported time.
        var lines = state.Engine.PrepareAndLayout(request.Elements.AsSpan(), out var contentSize);
        var elements = state.Engine.GetLayoutElements(state.SourceElements);

        state.EnqueueResult(WithDiagnostics(
            new LayoutResult
            {
                Handle = request.Handle,
                RequestId = request.RequestId,
                Elements = elements,
                Lines = lines,
                ContentSize = contentSize,
            },
            state,
            elements,
            lines));
    }

    private static void ProcessRelayout(LayoutRequest request, HandleState state)
    {
        state.Engine.Settings = request.Settings;

        var prepared = state.Engine.CurrentPreparedContent;
        if (prepared == null)
        {
            state.EnqueueResult(new LayoutResult
            {
                Handle = request.Handle,
                RequestId = request.RequestId,
                Error = "No PreparedContent cached. Call RequestFullLayout first.",
            });
            return;
        }

        var lines = state.Engine.Layout(prepared, out var contentSize);
        var elements = state.Engine.GetLayoutElements(state.SourceElements);

        state.EnqueueResult(WithDiagnostics(
            new LayoutResult
            {
                Handle = request.Handle,
                RequestId = request.RequestId,
                Elements = elements,
                Lines = lines,
                ContentSize = contentSize,
            },
            state,
            elements,
            lines));
    }

    private static void ProcessStreamAppend(LayoutRequest request, HandleState state)
    {
        if (request.AppendElement == null) return;

        var element = request.AppendElement.Value;
        state.Engine.Append(element, request.AppendSourceIndex);

        var elements = state.Engine.GetLayoutElements();
        var lines = state.Engine.Lines;

        state.EnqueueResult(WithDiagnostics(
            new LayoutResult
            {
                Handle = request.Handle,
                RequestId = request.RequestId,
                IsProgressive = true,
                Elements = elements,
                Lines = lines,
                ContentSize = state.Engine.ContentSize,
            },
            state,
            elements,
            lines));
    }

    private static void ProcessStreamFlush(LayoutRequest request, HandleState state)
    {
        state.Engine.Flush();

        var elements = state.Engine.GetLayoutElements();
        var lines = state.Engine.Lines;

        state.EnqueueResult(WithDiagnostics(
            new LayoutResult
            {
                Handle = request.Handle,
                RequestId = request.RequestId,
                Elements = elements,
                Lines = lines,
                ContentSize = state.Engine.ContentSize,
            },
            state,
            elements,
            lines));
    }

    // ── Per-Handle state ──

    private sealed class HandleState : IDisposable
    {
        public TypographyEngine Engine { get; }
        public ConcurrentQueue<LayoutResult> ResultQueue { get; } = new();

        /// <summary>
        /// Source DrawElement array from the last FullLayout request.
        /// Retained for block expansion in <see cref="TypographyEngine.GetLayoutElements(DrawElement[])"/>.
        /// </summary>
        public DrawElement[]? SourceElements;

        private CancellationTokenSource _cts = new();
        private bool _disposed;

        public HandleState()
        {
            Engine = new TypographyEngine(new TypographySettings());
        }

        public bool IsCancelled => _cts.IsCancellationRequested;

        public void Cancel()
        {
            _cts.Cancel();
        }

        public void CancelAndRenew()
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = new CancellationTokenSource();
        }

        public void ClearPreparedContent()
        {
            Engine.Reset();
            SourceElements = null;
        }

        /// <summary>
        /// Publish a result, replacing any result still waiting to be read. A consumer polls once per
        /// frame and only acts on its current request id, so an older result is dead weight — and with
        /// resize storms there can be many of them in flight.
        /// </summary>
        /// <param name="result">The newest result for this handle.</param>
        public void EnqueueResult(LayoutResult result)
        {
            while (ResultQueue.TryDequeue(out _))
            {
                // Superseded results are dropped rather than queued.
            }

            ResultQueue.Enqueue(result);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cts.Cancel();
            _cts.Dispose();
            Engine.Dispose();
        }
    }
}
