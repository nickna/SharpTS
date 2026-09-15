using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Optional filesystem stream types, storage, methods, and factories for one compilation.
/// Declarations support forward calls; completion validates every handle and freezes writes.
/// </summary>
public sealed class EmittedFileSystemStreamRuntime
{
    internal EmittedFileSystemStreamRuntime() { }

    public bool IsComplete { get; private set; }

    private TypeBuilder? _readType;
    public TypeBuilder ReadType
    {
        get => Require(_readType);
        internal set => Set(ref _readType, value);
    }

    private TypeBuilder? _writeType;
    public TypeBuilder WriteType
    {
        get => Require(_writeType);
        internal set => Set(ref _writeType, value);
    }

    private FieldBuilder? _readPathField;
    public FieldBuilder ReadPathField
    {
        get => Require(_readPathField);
        internal set => Set(ref _readPathField, value);
    }

    private FieldBuilder? _readDataField;
    public FieldBuilder ReadDataField
    {
        get => Require(_readDataField);
        internal set => Set(ref _readDataField, value);
    }

    private FieldBuilder? _readBytesReadField;
    public FieldBuilder ReadBytesReadField
    {
        get => Require(_readBytesReadField);
        internal set => Set(ref _readBytesReadField, value);
    }

    private FieldBuilder? _readEmitCloseField;
    public FieldBuilder ReadEmitCloseField
    {
        get => Require(_readEmitCloseField);
        internal set => Set(ref _readEmitCloseField, value);
    }

    private FieldBuilder? _readFdField;
    public FieldBuilder ReadFdField
    {
        get => Require(_readFdField);
        internal set => Set(ref _readFdField, value);
    }

    private FieldBuilder? _readPendingField;
    public FieldBuilder ReadPendingField
    {
        get => Require(_readPendingField);
        internal set => Set(ref _readPendingField, value);
    }

    private FieldBuilder? _writePathField;
    public FieldBuilder WritePathField
    {
        get => Require(_writePathField);
        internal set => Set(ref _writePathField, value);
    }

    private FieldBuilder? _writeStreamField;
    public FieldBuilder WriteStreamField
    {
        get => Require(_writeStreamField);
        internal set => Set(ref _writeStreamField, value);
    }

    private FieldBuilder? _writeBytesWrittenField;
    public FieldBuilder WriteBytesWrittenField
    {
        get => Require(_writeBytesWrittenField);
        internal set => Set(ref _writeBytesWrittenField, value);
    }

    private MethodBuilder? _write;
    public MethodBuilder Write
    {
        get => Require(_write);
        internal set => Set(ref _write, value);
    }

    private MethodBuilder? _end;
    public MethodBuilder End
    {
        get => Require(_end);
        internal set => Set(ref _end, value);
    }

    private FieldBuilder? _writeAutoCloseField;
    public FieldBuilder WriteAutoCloseField
    {
        get => Require(_writeAutoCloseField);
        internal set => Set(ref _writeAutoCloseField, value);
    }

    private FieldBuilder? _writeEmitCloseField;
    public FieldBuilder WriteEmitCloseField
    {
        get => Require(_writeEmitCloseField);
        internal set => Set(ref _writeEmitCloseField, value);
    }

    private FieldBuilder? _writeFdField;
    public FieldBuilder WriteFdField
    {
        get => Require(_writeFdField);
        internal set => Set(ref _writeFdField, value);
    }

    private FieldBuilder? _writePendingField;
    public FieldBuilder WritePendingField
    {
        get => Require(_writePendingField);
        internal set => Set(ref _writePendingField, value);
    }

    private FieldBuilder? _writeClosedField;
    public FieldBuilder WriteClosedField
    {
        get => Require(_writeClosedField);
        internal set => Set(ref _writeClosedField, value);
    }

    private ConstructorBuilder? _readCtor;
    public ConstructorBuilder ReadCtor
    {
        get => Require(_readCtor);
        internal set => Set(ref _readCtor, value);
    }

    private ConstructorBuilder? _writeCtor;
    public ConstructorBuilder WriteCtor
    {
        get => Require(_writeCtor);
        internal set => Set(ref _writeCtor, value);
    }

    private MethodBuilder? _createReadStream;
    public MethodBuilder CreateReadStream
    {
        get => Require(_createReadStream);
        internal set => Set(ref _createReadStream, value);
    }

    private MethodBuilder? _createWriteStream;
    public MethodBuilder CreateWriteStream
    {
        get => Require(_createWriteStream);
        internal set => Set(ref _createWriteStream, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Filesystem stream metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Filesystem stream metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = ReadType;
        _ = WriteType;
        _ = ReadPathField;
        _ = ReadDataField;
        _ = ReadBytesReadField;
        _ = ReadEmitCloseField;
        _ = ReadFdField;
        _ = ReadPendingField;
        _ = WritePathField;
        _ = WriteStreamField;
        _ = WriteBytesWrittenField;
        _ = Write;
        _ = End;
        _ = WriteAutoCloseField;
        _ = WriteEmitCloseField;
        _ = WriteFdField;
        _ = WritePendingField;
        _ = WriteClosedField;
        _ = ReadCtor;
        _ = WriteCtor;
        _ = CreateReadStream;
        _ = CreateWriteStream;
        IsComplete = true;
    }
}
