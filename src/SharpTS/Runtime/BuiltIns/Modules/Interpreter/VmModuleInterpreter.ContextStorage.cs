namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

public static partial class VmModuleInterpreter
{
    /// <summary>Configures this output's actual plain-object storage before the host executes.</summary>
    public static void ConfigureExecutionHostContextStorage(object host, object domain, Func<object, bool> admits,
        Func<object, string[]> ownNames, Func<object, string, object?[]?> readDescriptor,
        Func<object, string, object?, bool> set, Func<object, string, object?[], bool> define,
        Func<object, string, bool> delete, Func<object, bool> isExtensible, Action<object> preventExtensions)
    {
        if (host is not VmExecutionHost owner)
            throw new ArgumentException("Expected an owned VM execution host.", nameof(host));
        owner.ConfigureContextStorage(new VmContextStorageCapabilities(domain, admits, ownNames, readDescriptor,
            set, define, delete, isExtensible, preventExtensions));
    }
}