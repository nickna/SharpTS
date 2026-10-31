using System.Reflection;
using SharpTS.Compilation;
using Xunit;

namespace SharpTS.Tests.Infrastructure;

/// <summary>Finds the renamed actual source declaration in a saved native test image.</summary>
internal static class NativeSourceTypeLookup
{
    internal static Type Find(Assembly assembly, string logicalName) => Assert.Single(assembly.GetTypes(), type =>
        type.Namespace is { } ns && (ns.StartsWith(NativeSourceTypeNames.DeclarationDomain, StringComparison.Ordinal) ||
            ns.StartsWith(NativeSourceTypeNames.ExpressionDomain, StringComparison.Ordinal)) &&
        type.FullName!.EndsWith("." + logicalName, StringComparison.Ordinal));
}
