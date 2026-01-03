namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton marker for the JavaScript Math object.
/// </summary>
/// <remarks>
/// This class exists primarily as a type marker for <c>Math.method()</c> call resolution.
/// The actual Math methods (abs, floor, random, etc.) and constants (PI, E) are handled
/// as special cases in <see cref="Interpreter"/>. The singleton pattern ensures only one
/// Math object exists, consistent with JavaScript semantics.
/// </remarks>
public class SharpTSMath
{
    public static readonly SharpTSMath Instance = new();
    private SharpTSMath() { }

    public override string ToString() => "[object Math]";
}
