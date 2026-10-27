using System.Reflection.Emit;

namespace SharpTS.Compilation.Registries;

public sealed partial class ClassRegistry
{
    internal NativeGuestOwnPropertyRegistry? NativeOwnProperties { get; private set; }

    internal NativeGuestOwnPropertyRegistry BeginNativeOwnProperties(EmittedGuestOwnPropertyRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (NativeOwnProperties != null) throw new InvalidOperationException("Native source own-property registration has already started.");
        if (!runtime.IsComplete) throw new InvalidOperationException("Runtime own-property declarations must be complete before source registration.");
        return NativeOwnProperties = new(runtime,
            owner => _guestFieldOwners.Values.Contains(owner, ReferenceEqualityComparer.Instance));
    }
}
