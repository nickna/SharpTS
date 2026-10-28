using System.Reflection.Emit;

namespace SharpTS.Compilation;

internal sealed partial class NativePropertyLookupCatalog
{
    private void RegisterRuntimeOwners(EmittedRuntime runtime)
    {
        if (runtime.NativeCallables is { } callables)
            foreach (var carrier in callables.Carriers) RegisterOutputOwner(carrier);
        // Role handles positively identify declarations produced by these selected emitters.
        foreach (var type in new Type?[] {
            runtime.ArrayStorage.Type, runtime.Arguments.Type, runtime.EventEmitter.Type,
            runtime.FunctionValues.Type, runtime.Symbols.Type, runtime.Promise?.Type,
            runtime.Errors.Type, runtime.Errors.TypeErrorType, runtime.Errors.RangeErrorType,
            runtime.Errors.ReferenceErrorType, runtime.Errors.SyntaxErrorType, runtime.Errors.URIErrorType,
            runtime.Errors.EvalErrorType, runtime.Errors.AggregateErrorType,
            runtime.Dates.Implementation?.Type, runtime.RegExps.Implementation?.Type, runtime.Buffer?.Type,
            runtime.MessageChannels.Type, runtime.MessageChannels.Port.Type,
            runtime.TextEncoding?.EncoderType, runtime.TextEncoding?.DecoderType,
            runtime.ArrayBuffer?.Type, runtime.SharedArrayBuffer?.Type, runtime.DataView?.Type,
            runtime.TypedArrays.Implementation?.Int8ArrayType, runtime.TypedArrays.Implementation?.Uint8ArrayType,
            runtime.TypedArrays.Implementation?.Uint8ClampedArrayType, runtime.TypedArrays.Implementation?.Int16ArrayType,
            runtime.TypedArrays.Implementation?.Uint16ArrayType, runtime.TypedArrays.Implementation?.Int32ArrayType,
            runtime.TypedArrays.Implementation?.Uint32ArrayType, runtime.TypedArrays.Implementation?.Float32ArrayType,
            runtime.TypedArrays.Implementation?.Float64ArrayType, runtime.TypedArrays.Implementation?.BigInt64ArrayType,
            runtime.TypedArrays.Implementation?.BigUint64ArrayType,
            runtime.FileSystem?.DirCtor.DeclaringType, runtime.FileSystem?.DirentCtor.DeclaringType,
            runtime.NodeStreams?.ReadableType, runtime.NodeStreams?.WritableType, runtime.NodeStreams?.DuplexType,
            runtime.NodeStreams?.TransformType, runtime.NodeStreams?.PassThroughCtor.DeclaringType,
            runtime.WebStreams?.ReadableType, runtime.WebStreams?.WritableType, runtime.WebStreams?.TransformType,
            runtime.WebStreams?.ReadableReaderStreamField.DeclaringType, runtime.WebStreams?.ReadableControllerStreamField.DeclaringType,
            runtime.WebStreams?.WritableWriterStreamField.DeclaringType, runtime.WebStreams?.WritableControllerStreamField.DeclaringType,
            runtime.WebStreams?.CountQueuingStrategyCtor.DeclaringType, runtime.WebStreams?.ByteLengthQueuingStrategyCtor.DeclaringType,
            runtime.Crypto?.ECDHType, runtime.Crypto?.DiffieHellmanType,
            runtime.Crypto?.HashCtor.DeclaringType, runtime.Crypto?.HmacCtor.DeclaringType,
            runtime.Crypto?.CipherCtor.DeclaringType, runtime.Crypto?.DecipherCtor.DeclaringType,
            runtime.Crypto?.SignCtor.DeclaringType, runtime.Crypto?.VerifyCtor.DeclaringType,
            runtime.Crypto?.KeyObjectCtorSecret.DeclaringType, runtime.Crypto?.X509CertificateCtor.DeclaringType,
            runtime.Net?.SocketType, runtime.Net?.ServerType, runtime.Net?.BlockListType,
            runtime.Tls?.SocketType, runtime.Tls?.ServerCtor.DeclaringType, runtime.Dgram?.SocketType,
            runtime.Http?.ServerType, runtime.Http?.RequestType, runtime.Http?.ResponseType,
            runtime.Readline?.InterfaceType,
            runtime.Fetch.Implementation?.HeadersType, runtime.Fetch.Implementation?.ResponseType,
            runtime.Fetch.Implementation?.RequestCtor.DeclaringType,
            runtime.FileSystemStreams?.ReadType, runtime.FileSystemStreams?.WriteType,
            runtime.FileSystemWatchers?.WatcherType, runtime.FileSystemWatchers?.StatType
        }) if (type is not null) RegisterOutputOwner(type);
    }

    private IEnumerable<Type> FrameworkOwners() => [
        _types.Object, _types.String, _types.Double, _types.Boolean, _types.Int32, _types.BigInteger,
        _types.ListOfObject, _types.ObjectArray, _types.DictionaryStringObject,
        _types.DictionaryObjectObject, _types.HashSetOfObject, _types.ConditionalWeakTableObjectObject,
        _types.WeakReferenceObject, _types.TaskOfObject
    ];
}
