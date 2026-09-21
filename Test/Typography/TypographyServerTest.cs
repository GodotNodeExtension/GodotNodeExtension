namespace GodotNodeExtension.Tests.Typography;

using GdUnit4;
using GodotNodeExtension.Component.Typography.Server;
using static GdUnit4.Assertions;

/// <summary>
/// Behaviour specification for the layout thread of <see cref="TypographyServer"/> and for its
/// assembly-reload hook: the thread runs code of the project assembly, so a live thread keeps that
/// assembly - and the load context that owns it - alive, which is the state the editor reports as an
/// assembly it cannot unload (godot#78513). The hook has to stop it, and a stopped server has to come
/// back on the next use.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TypographyServerTest
{
    [TestCase]
    public void TheUnloadHookIsRegistered()
    {
        // The hook cannot be triggered from inside the process that still uses the assembly, so the
        // registration itself is what is asserted; the case below runs the exact cleanup it performs.
        AssertThat(TypographyServer.UnloadHookInstalled).IsTrue();
    }

    [TestCase]
    public void ShutdownStopsTheLayoutThreadAndEnsureStartedRestartsIt()
    {
        var server = TypographyServer.Instance;

        server.EnsureStarted();
        AssertThat(server.IsRunning).IsTrue();

        // This is what the unload hook calls: a running thread must not survive the assembly reload.
        server.Shutdown();
        AssertThat(server.IsRunning).IsFalse();

        // Shutting down twice (hook plus an explicit shutdown) must stay safe.
        server.Shutdown();
        AssertThat(server.IsRunning).IsFalse();

        // A shut-down server starts again on the next use, so the rest of the run is unaffected.
        server.EnsureStarted();
        AssertThat(server.IsRunning).IsTrue();
    }
}
