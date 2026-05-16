using System.Collections.Generic;
using System.Linq;

namespace WorldBoxBridge.Commands;

/// <summary>
/// Central registry. Commands self-register at plugin startup; the registry is later queried by
/// the HTTP router and by the <c>/capabilities</c> endpoint.
/// </summary>
internal sealed class CommandRegistry
{
    private readonly Dictionary<string, ICommand> _byName = new(System.StringComparer.Ordinal);

    public void Register(ICommand command)
    {
        if (command == null)
        {
            throw new System.ArgumentNullException(nameof(command));
        }
        if (_byName.ContainsKey(command.Name))
        {
            throw new System.InvalidOperationException(
                $"Duplicate command name: '{command.Name}'."
            );
        }
        _byName[command.Name] = command;
    }

    public bool TryGet(string name, out ICommand command) => _byName.TryGetValue(name, out command!);

    public IEnumerable<ICommand> All => _byName.Values.OrderBy(c => c.Name);

    public int Count => _byName.Count;
}
