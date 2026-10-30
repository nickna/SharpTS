using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.Types;

namespace SharpTS.Execution;

public partial class Interpreter
{
    internal void SetPromiseConstructorStringIntegrity(object? owner, bool freeze)
    {
        if (owner is not SharpTSBuiltInConstructor constructor
            || GetPromiseConstructorState(constructor) is not { } realm)
            return;

        var keys = new List<string> { "name", "prototype" };
        keys.AddRange(PromiseBuiltIns.StaticMemberNames);
        if (realm.StringProperties.TryGetValue(constructor, out var existing))
            keys.AddRange(existing.Keys);

        foreach (var key in keys.Distinct(StringComparer.Ordinal).ToList())
        {
            // Descriptor lookup observes presence and attributes, never the value
            // of a guest accessor. Deleted virtual slots remain absent.
            var descriptor = ObjectBuiltIns.OwnPropertyDescriptorOf(this, constructor, key);
            if (descriptor is null) continue;
            descriptor.Configurable = false;
            descriptor.HasConfigurable = true;
            if (freeze && descriptor.HasValue)
            {
                descriptor.Writable = false;
                descriptor.HasWritable = true;
            }
            if (!realm.StringProperties.TryGetValue(constructor, out var properties))
                realm.StringProperties[constructor] = properties = [];
            properties[key] = descriptor;
        }
    }
}
