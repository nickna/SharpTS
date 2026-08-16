using SharpTS.Runtime.BuiltIns;
using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Represents a MessageChannel providing two connected MessagePorts.
/// </summary>
/// <remarks>
/// MessageChannel creates a pair of connected ports that can communicate with each other.
/// Messages sent to port1 are received by port2 and vice versa. This enables bidirectional
/// communication channels independent of the Worker/parent relationship.
/// </remarks>
public class SharpTSMessageChannel : ITypeCategorized
{
    /// <summary>
    /// Gets the first port of the channel.
    /// </summary>
    public SharpTSMessagePort Port1 { get; }

    /// <summary>
    /// Gets the second port of the channel.
    /// </summary>
    public SharpTSMessagePort Port2 { get; }

    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Record;

    /// <summary>
    /// Creates a new MessageChannel with two connected ports.
    /// </summary>
    public SharpTSMessageChannel()
    {
        Port1 = new SharpTSMessagePort();
        Port2 = new SharpTSMessagePort();

        // Connect the ports to each other
        Port1.SetPartner(Port2);
        Port2.SetPartner(Port1);
    }

    /// <summary>
    /// Gets a member (property) by name.
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "port1" => Port1,
            "port2" => Port2,
            _ => null
        };
    }

    public override string ToString() => "MessageChannel { port1, port2 }";
}
