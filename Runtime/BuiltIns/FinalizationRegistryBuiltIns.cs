using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Built-in method implementations for FinalizationRegistry instances.
/// </summary>
public static class FinalizationRegistryBuiltIns
{
    public static object? GetMember(SharpTSFinalizationRegistry receiver, string name)
    {
        return name switch
        {
            "register" => BuiltInMethod.CreateV2("register", 1, 3, static (interp, recv, args) =>
            {
                var registry = (SharpTSFinalizationRegistry)recv.ToObject()!;
                var target = args.Length > 0 ? args[0].ToObject() : null;
                var heldValue = args.Length > 1 ? args[1].ToObject() : null;
                var token = args.Length > 2 ? args[2].ToObject() : null;
                // Enroll with the event loop so pending cleanups actually drain:
                // registries used to enqueue heldValues that nothing ever read,
                // so cleanup callbacks never fired.
                interp.TrackFinalizationRegistry(registry);
                registry.Register(target!, heldValue, token);
                return RuntimeValue.Null;
            }),

            "unregister" => BuiltInMethod.CreateV2("unregister", 1, static (_, recv, args) =>
            {
                var registry = (SharpTSFinalizationRegistry)recv.ToObject()!;
                var token = args.Length > 0 ? args[0].ToObject() : null;
                return RuntimeValue.FromBoolean(registry.Unregister(token));
            }),

            _ => null
        };
    }
}
