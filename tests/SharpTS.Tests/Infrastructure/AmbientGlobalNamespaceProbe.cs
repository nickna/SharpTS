// This type is deliberately declared in the GLOBAL namespace and must stay there.
//
// Compiled TypeScript classes are emitted as public types in the global namespace under
// their bare TypeScript name, so `Assembly.GetType("Foo")` over the loaded assemblies can
// match one. Any compiler-side resolution that scans AppDomain.CurrentDomain.GetAssemblies()
// for a bare name is therefore capable of binding a TypeScript type reference to a type in
// an unrelated assembly, which emits an assembly reference the compiled program cannot
// resolve at runtime.
//
// AmbientTypeArgumentPrecedenceTests declares a TypeScript class of this exact name and
// asserts the compiler prefers it over this one. Renaming or namespacing this type silently
// disarms that test.

/// <summary>
/// Collision bait for <c>AmbientTypeArgumentPrecedenceTests</c>. Never referenced by
/// production code; exists only so a loaded assembly contains a global-namespace type whose
/// name a test TypeScript program can also declare.
/// </summary>
public sealed class SharpTsAmbientProbe
{
    public int Marker => 1;
}
