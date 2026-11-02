using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.Exceptions;

/// <summary>
/// Retains one nominal host clone failure until a guest catch/rejection binding.
/// Function-result propagation unwraps the original exception; it is not a guest value.
/// </summary>
internal sealed class HostDataCloneErrorValue(StructuredClone.DataCloneError exception)
{
    internal StructuredClone.DataCloneError Exception { get; } = exception;

    internal SharpTSError CreateGuestError()
    {
        const string prefix = "DataCloneError: ";
        string message = Exception.Message;
        if (message.StartsWith(prefix, StringComparison.Ordinal))
            message = message.Substring(prefix.Length);
        return new SharpTSError(message) { Name = "DataCloneError" };
    }

    // Top-level completion printing must retain the original host message.
    public override string ToString() => Exception.Message;
}
