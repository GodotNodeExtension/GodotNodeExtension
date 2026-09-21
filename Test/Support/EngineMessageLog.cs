namespace GodotNodeExtension.Tests.Support;

using System.Collections.Generic;
using Godot;

/// <summary>
/// Captures the engine's own error/warning stream (see <see cref="Godot.Logger"/>) so a case can assert
/// that a message was - or was not - pushed. The engine keeps printing to the console, so this only
/// observes. The runtime calls the hooks from several threads, hence the lock, and it hands the message
/// in <c>code</c> with the level in <c>errorType</c>.
/// <para>
/// It lives outside every component's own <c>Test/&lt;component&gt;/</c> tree on purpose: several
/// components' suites use it, and a fixture that belongs to one of them would leave the others unable to
/// build on their own.
/// </para>
/// </summary>
internal sealed partial class EngineMessageLog : Logger
{
    private readonly List<string> _errors = [];
    private readonly List<string> _warnings = [];
    private readonly object _gate = new();

    /// <summary>Start receiving the engine's messages.</summary>
    public static EngineMessageLog Attach()
    {
        var log = new EngineMessageLog();
        OS.AddLogger(log);
        return log;
    }

    /// <summary>Stop receiving them. The logged messages stay readable.</summary>
    public void Detach() => OS.RemoveLogger(this);

    /// <inheritdoc />
    public override void _LogError(string function, string file, int line, string code, string rationale,
                                   bool editorNotify, int errorType,
                                   Godot.Collections.Array<Godot.ScriptBacktrace> scriptBacktraces)
    {
        string text = string.IsNullOrEmpty(rationale) ? code : $"{code} {rationale}";
        lock (_gate)
        {
            if ((Logger.ErrorType)errorType == Logger.ErrorType.Warning)
                _warnings.Add(text);
            else
                _errors.Add(text);
        }
    }

    /// <summary>The warnings whose text contains <paramref name="fragment"/>, in arrival order.</summary>
    public string[] WarningsContaining(string fragment) => Containing(_warnings, fragment);

    /// <summary>The errors whose text contains <paramref name="fragment"/>, in arrival order.</summary>
    public string[] ErrorsContaining(string fragment) => Containing(_errors, fragment);

    private string[] Containing(List<string> messages, string fragment)
    {
        lock (_gate)
        {
            var hits = new List<string>();
            foreach (string message in messages)
            {
                if (message.Contains(fragment, System.StringComparison.Ordinal))
                    hits.Add(message);
            }
            return hits.ToArray();
        }
    }
}
