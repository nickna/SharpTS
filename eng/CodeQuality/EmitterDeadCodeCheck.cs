using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Supplemental, deliberately conservative guard for the two large partial types
// where the SDK unused-member analyzer misses even injected unused methods.
internal static class EmitterDeadCodeCheck
{
    internal static List<string> Scan(string directory)
    {
        var trees = Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => CSharpSyntaxTree.ParseText(File.ReadAllText(path), path: path)).ToArray();
        return Check(trees);
    }

    private static List<string> Check(SyntaxTree[] trees)
    {
        var roots = trees.Select(t => t.GetRoot()).ToArray();
        var excluded = trees.Where(t => DuplicateCheck.Excluded(t.FilePath.Replace('\\', '/'), t.GetText().ToString())).ToHashSet();
        var names = roots.SelectMany(r => r.DescendantTokens())
            .Where(t => t.IsKind(SyntaxKind.IdentifierToken))
            .GroupBy(t => t.ValueText).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        // Literal registrations (GetMethod("Name")) are real uses. Do not guess
        // reachability or remove reflection roots. nameof is already an identifier.
        var strings = roots.SelectMany(r => r.DescendantNodes().OfType<LiteralExpressionSyntax>())
            .Where(l => l.IsKind(SyntaxKind.StringLiteralExpression))
            .Select(l => l.Token.ValueText).ToHashSet(StringComparer.Ordinal);
        var errors = new List<string>();
        foreach (var method in roots.SelectMany(r => r.DescendantNodes().OfType<MethodDeclarationSyntax>()))
        {
            if (excluded.Contains(method.SyntaxTree) || DuplicateCheck.GeneratedMember(method)) continue;
            if (method.Parent is not ClassDeclarationSyntax type ||
                type.Identifier.ValueText is not ("RuntimeEmitter" or "ILCompiler") ||
                !method.Modifiers.Any(SyntaxKind.PrivateKeyword)) continue;
            var name = method.Identifier.ValueText;
            if (names[name] > 1 || strings.Contains(name)) continue;
            var suppression = method.GetLeadingTrivia().ToFullString().Split('\n')
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith("// QLT001: ", StringComparison.Ordinal));
            if (suppression != null && suppression[11..].Trim().Length >= 15) continue;
            var location = method.GetLocation().GetLineSpan();
            errors.Add($"{location.Path}({location.StartLinePosition.Line + 1}): QLT001 private {type.Identifier.ValueText}.{name} has no identifier or literal registration in Compilation/. Remove it, register it with nameof, or document an external entry point with '// QLT001: <reason>'.");
        }
        return errors;
    }

    internal static void SelfTest()
    {
        static List<string> Analyze(params string[] sources) => Check(sources.Select(s => CSharpSyntaxTree.ParseText(s)).ToArray());
        if (Analyze("partial class RuntimeEmitter { private void Dead() {} public string Name() => nameof(System.Console.WriteLine); }").Count != 1 ||
            Analyze("partial class ILCompiler { private void Dead() {} }").Count != 1 ||
            Analyze("partial class RuntimeEmitter { private void Used() {} }", "partial class RuntimeEmitter { public void Run() => Used(); }").Count != 0 ||
            Analyze("partial class RuntimeEmitter { private void Callback() {} public string Register() => nameof(Callback); }").Count != 0 ||
            Analyze("partial class ILCompiler { private void Callback() {} public string Register() => \"Callback\"; }").Count != 0 ||
            Analyze("partial class ILCompiler {\n // QLT001: Invoked through an external host registration table.\n private void Callback() {} }").Count != 0 ||
            Analyze("partial class ILCompiler {\n // QLT001: \n private void Dead() {} }").Count != 1)
            throw new InvalidOperationException("Emitter dead-code self-tests failed.");
        Console.WriteLine("Emitter dead-code self-tests passed.");
    }
}
