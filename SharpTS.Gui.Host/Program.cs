#pragma warning disable SHARPTS_HOSTING001

using System.Reflection;
using SharpTS.Hosting;

namespace SharpTS.Gui.Host;

public static class Program
{
    [STAThread]
    public static int Main(string[] args) => Run(args, embeddedPayloadAssembly: null);

    public static int MainEmbedded(string[] args, Assembly payloadAssembly)
    {
        ArgumentNullException.ThrowIfNull(payloadAssembly);
        return Run(args, payloadAssembly, staticCompiledFactory: null);
    }

    public static int MainStatic(
        string[] args,
        Assembly payloadAssembly,
        ISharpTSHostedProgramFactory factory)
    {
        ArgumentNullException.ThrowIfNull(payloadAssembly);
        ArgumentNullException.ThrowIfNull(factory);
        return Run(args, payloadAssembly, factory);
    }

    private static int Run(
        string[] args,
        Assembly? embeddedPayloadAssembly,
        ISharpTSHostedProgramFactory? staticCompiledFactory = null)
    {
        HostOptions options;
        try
        {
            options = HostOptionsParser.Parse(
                args,
                embeddedPayloadAssembly is null ? GuestMode.Interpreted : GuestMode.Compiled);
            if (embeddedPayloadAssembly is not null && options.Mode != GuestMode.Compiled)
            {
                throw new ArgumentException(
                    "The self-contained SharpTS GUI executable contains only the compiled guest; " +
                    "--mode interpreted is available from development and directory outputs.");
            }
            if (embeddedPayloadAssembly is not null && options.ValidateDepsDirectory is not null)
                throw new ArgumentException("--validate-deps is not available from an embedded single-file application.");
            if (options.Watch && options.Mode != GuestMode.Interpreted)
                throw new ArgumentException("--watch is available only in interpreted mode.");
            if (options.Watch && embeddedPayloadAssembly is not null)
                throw new ArgumentException("--watch is unavailable from an embedded single-file application.");
        }
        catch (Exception exception)
        {
            FatalDiagnostics.Report(exception, HostOptionsParser.ShouldShowFatalDialog(args));
            return 64;
        }

        if (options.ValidateDepsDirectory != null)
        {
            var failures = DepsAssetValidator.Validate(
                options.ValidateDepsDirectory,
                requireSourcePayload: !options.ValidateCompiledOnly);
            foreach (string failure in failures)
                Console.Error.WriteLine(failure);
            if (failures.Count == 0)
                Console.WriteLine("SharpTS GUI publish asset closure validated.");
            return failures.Count == 0 ? 0 : 1;
        }

        try
        {
            return DesktopApplicationHost.Run(options, embeddedPayloadAssembly, staticCompiledFactory);
        }
        catch (Exception exception)
        {
            FatalDiagnostics.Report(exception, HostOptionsParser.ShouldShowFatalDialog(args));
            return 1;
        }
    }
}
