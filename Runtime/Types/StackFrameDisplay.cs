using System.Diagnostics;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Extracts display-only method names from stack frames without requiring reflection metadata.
/// </summary>
internal static class StackFrameDisplay
{
    internal static (string TypeName, string MethodName)? GetMethodName(StackFrame frame)
    {
        var method = DiagnosticMethodInfo.Create(frame);
        if (method is null) return null;

        var qualifiedTypeName = method.DeclaringTypeName ?? "";
        var separator = Math.Max(
            qualifiedTypeName.LastIndexOf('.'),
            qualifiedTypeName.LastIndexOf('+'));
        var typeName = separator >= 0
            ? qualifiedTypeName[(separator + 1)..]
            : qualifiedTypeName;

        return (typeName, method.Name);
    }
}
