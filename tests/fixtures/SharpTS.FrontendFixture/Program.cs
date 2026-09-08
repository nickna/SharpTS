using System.Text.Json;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.FrontendFixture;

public static class FrontendFixtureMarker { }
public sealed record FrontendResult(bool IsSuccess, string[] Diagnostics);

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            // Fault modes exercise the parent harness, not guest language behavior.
            if (args is ["hang"]) Thread.Sleep(Timeout.Infinite);
            if (args is ["crash"]) throw new InvalidOperationException("injected frontend defect");
            if (args is not ["check"])
                throw new ArgumentException("Expected check, hang, or crash.");

            string source = Console.In.ReadToEnd();
            FrontendResult result;
            try
            {
                var parsed = new Parser(new Lexer(source).ScanTokens()).Parse();
                var diagnostics = parsed.IsSuccess
                    ? new TypeChecker().CheckWithRecovery(parsed.Statements).Diagnostics
                    : parsed.Diagnostics;
                string[] errors = diagnostics
                    .Where(d => d.Severity == SharpTS.Diagnostics.DiagnosticSeverity.Error)
                    .Select(d => d.ToString()).ToArray();
                result = new FrontendResult(errors.Length == 0, errors);
            }
            catch (LexicalException error)
            {
                result = new FrontendResult(false, [error.Message]);
            }
            Console.WriteLine(JsonSerializer.Serialize(result));
            return result.IsSuccess ? 0 : 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
