using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace SharpTS.ConsoleRestorationFixture;

public static class ConsoleRestorationFixtureMarker { }

internal static class Program
{
    private static int Main(string[] args)
    {
        TextWriter report = Console.Out;
        TextWriter errors = Console.Error;
        try
        {
            string assemblyPath = Path.GetFullPath(args.Single());
            string directory = Path.GetDirectoryName(assemblyPath)!;
            // Resolve the real test assembly's dependencies from its output,
            // without a project reference that would introduce a build cycle.
            AssemblyLoadContext.Default.Resolving += (_, name) =>
            {
                string dependency = Path.Combine(directory, name.Name + ".dll");
                return File.Exists(dependency) ? Assembly.LoadFrom(dependency) : null;
            };
            Assembly tests = Assembly.LoadFrom(assemblyPath);
            RuntimeHelpers.RunModuleConstructor(tests.ManifestModule.ModuleHandle);
            Type contract = tests.GetType("SharpTS.Tests.Compilation.CompilationServiceTests", throwOnError: true)!;
            TextWriter rawOut = Console.Out;
            TextWriter rawErr = Console.Error;
            if (rawOut.GetType().Name != "ProxyWriter" || rawErr.GetType().Name != "ProxyWriter")
                throw new InvalidOperationException("The actual test module initializer did not install raw console proxies.");

            RunContract(contract);
            AssertWriters(rawOut, rawErr);

            using var publicOut = new StringWriter();
            using var publicErr = new StringWriter();
            Console.SetOut(publicOut);
            Console.SetError(publicErr);
            TextWriter priorOut = Console.Out;
            TextWriter priorErr = Console.Error;
            RunContract(contract);
            AssertWriters(priorOut, priorErr);
            report.WriteLine("console restoration controls passed");
            return 0;
        }
        catch (Exception exception)
        {
            // The test initializer intentionally mutes unscoped stderr.
            errors.WriteLine(exception);
            return 1;
        }
    }

    private static void RunContract(Type contract)
    {
        using var instance = (IDisposable)Activator.CreateInstance(contract)!;
        TextWriter priorOut = Console.Out;
        TextWriter priorErr = Console.Error;
        foreach (string method in new[]
        {
            "Execute_RestoresConsoleAfterRun", // First and repeated executions, exact output and writer identities.
            "Execute_GuestThrow_ReturnsFailedRunResultWithoutThrowing",
            "Execute_InvalidAssembly_ReportsFailedLoadAndAggregateTime",
            "Execute_InfiniteLoop_CancellationUnwindsCooperatively"
        })
        {
            if (contract.GetMethod(method)!.Invoke(instance, null) is Task task)
                task.GetAwaiter().GetResult();
            AssertWriters(priorOut, priorErr);
        }
    }

    private static void AssertWriters(TextWriter stdout, TextWriter stderr)
    {
        if (!ReferenceEquals(stdout, Console.Out) || !ReferenceEquals(stderr, Console.Error))
            throw new InvalidOperationException("Console stdout/stderr identities were not restored.");
    }
}
