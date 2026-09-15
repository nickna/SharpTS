using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Optional filesystem data and synchronous I/O declarations for one compilation.
/// Owns directory/stat storage, file-descriptor access, and native hard-link declarations.
/// Reads require a declaration; completion validates and freezes every handle.
/// Async operations, streams, and watchers retain their separate ownership.
/// </summary>
public sealed class EmittedFileSystemRuntime
{
    internal EmittedFileSystemRuntime() { }

    public bool IsComplete { get; private set; }

    private MethodBuilder? _existsSync;
    public MethodBuilder ExistsSync
    {
        get => Require(_existsSync);
        internal set => Set(ref _existsSync, value);
    }

    private MethodBuilder? _encodingName;
    public MethodBuilder EncodingName
    {
        get => Require(_encodingName);
        internal set => Set(ref _encodingName, value);
    }

    private MethodBuilder? _toBytes;
    public MethodBuilder ToBytes
    {
        get => Require(_toBytes);
        internal set => Set(ref _toBytes, value);
    }

    private MethodBuilder? _readFileSync;
    public MethodBuilder ReadFileSync
    {
        get => Require(_readFileSync);
        internal set => Set(ref _readFileSync, value);
    }

    private MethodBuilder? _writeFileSync;
    public MethodBuilder WriteFileSync
    {
        get => Require(_writeFileSync);
        internal set => Set(ref _writeFileSync, value);
    }

    private MethodBuilder? _appendFileSync;
    public MethodBuilder AppendFileSync
    {
        get => Require(_appendFileSync);
        internal set => Set(ref _appendFileSync, value);
    }

    private MethodBuilder? _unlinkSync;
    public MethodBuilder UnlinkSync
    {
        get => Require(_unlinkSync);
        internal set => Set(ref _unlinkSync, value);
    }

    private MethodBuilder? _mkdirSync;
    public MethodBuilder MkdirSync
    {
        get => Require(_mkdirSync);
        internal set => Set(ref _mkdirSync, value);
    }

    private MethodBuilder? _rmdirSync;
    public MethodBuilder RmdirSync
    {
        get => Require(_rmdirSync);
        internal set => Set(ref _rmdirSync, value);
    }

    private MethodBuilder? _readdirSync;
    public MethodBuilder ReaddirSync
    {
        get => Require(_readdirSync);
        internal set => Set(ref _readdirSync, value);
    }

    private MethodBuilder? _statSync;
    public MethodBuilder StatSync
    {
        get => Require(_statSync);
        internal set => Set(ref _statSync, value);
    }

    private MethodBuilder? _lstatSync;
    public MethodBuilder LstatSync
    {
        get => Require(_lstatSync);
        internal set => Set(ref _lstatSync, value);
    }

    private MethodBuilder? _statRaw;
    public MethodBuilder StatRaw
    {
        get => Require(_statRaw);
        internal set => Set(ref _statRaw, value);
    }

    private MethodBuilder? _lstatRaw;
    public MethodBuilder LstatRaw
    {
        get => Require(_lstatRaw);
        internal set => Set(ref _lstatRaw, value);
    }

    private MethodBuilder? _fstatRaw;
    public MethodBuilder FstatRaw
    {
        get => Require(_fstatRaw);
        internal set => Set(ref _fstatRaw, value);
    }

    private MethodBuilder? _buildStatRecord;
    public MethodBuilder BuildStatRecord
    {
        get => Require(_buildStatRecord);
        internal set => Set(ref _buildStatRecord, value);
    }

    private MethodBuilder? _statTimeMs;
    public MethodBuilder StatTimeMs
    {
        get => Require(_statTimeMs);
        internal set => Set(ref _statTimeMs, value);
    }

    private MethodBuilder? _renameSync;
    public MethodBuilder RenameSync
    {
        get => Require(_renameSync);
        internal set => Set(ref _renameSync, value);
    }

    private MethodBuilder? _copyFileSync;
    public MethodBuilder CopyFileSync
    {
        get => Require(_copyFileSync);
        internal set => Set(ref _copyFileSync, value);
    }

    private MethodBuilder? _accessSync;
    public MethodBuilder AccessSync
    {
        get => Require(_accessSync);
        internal set => Set(ref _accessSync, value);
    }

    private MethodBuilder? _chmodSync;
    public MethodBuilder ChmodSync
    {
        get => Require(_chmodSync);
        internal set => Set(ref _chmodSync, value);
    }

    private MethodBuilder? _chownSync;
    public MethodBuilder ChownSync
    {
        get => Require(_chownSync);
        internal set => Set(ref _chownSync, value);
    }

    private MethodBuilder? _lchownSync;
    public MethodBuilder LchownSync
    {
        get => Require(_lchownSync);
        internal set => Set(ref _lchownSync, value);
    }

    private MethodBuilder? _truncateSync;
    public MethodBuilder TruncateSync
    {
        get => Require(_truncateSync);
        internal set => Set(ref _truncateSync, value);
    }

    private MethodBuilder? _symlinkSync;
    public MethodBuilder SymlinkSync
    {
        get => Require(_symlinkSync);
        internal set => Set(ref _symlinkSync, value);
    }

    private MethodBuilder? _readlinkSync;
    public MethodBuilder ReadlinkSync
    {
        get => Require(_readlinkSync);
        internal set => Set(ref _readlinkSync, value);
    }

    private MethodBuilder? _realpathSync;
    public MethodBuilder RealpathSync
    {
        get => Require(_realpathSync);
        internal set => Set(ref _realpathSync, value);
    }

    private MethodBuilder? _utimesSync;
    public MethodBuilder UtimesSync
    {
        get => Require(_utimesSync);
        internal set => Set(ref _utimesSync, value);
    }

    private MethodBuilder? _getConstants;
    public MethodBuilder GetConstants
    {
        get => Require(_getConstants);
        internal set => Set(ref _getConstants, value);
    }

    private MethodBuilder? _createDirent;
    public MethodBuilder CreateDirent
    {
        get => Require(_createDirent);
        internal set => Set(ref _createDirent, value);
    }

    private MethodBuilder? _openSync;
    public MethodBuilder OpenSync
    {
        get => Require(_openSync);
        internal set => Set(ref _openSync, value);
    }

    private MethodBuilder? _closeSync;
    public MethodBuilder CloseSync
    {
        get => Require(_closeSync);
        internal set => Set(ref _closeSync, value);
    }

    private MethodBuilder? _readSync;
    public MethodBuilder ReadSync
    {
        get => Require(_readSync);
        internal set => Set(ref _readSync, value);
    }

    private MethodBuilder? _writeSyncBuffer;
    public MethodBuilder WriteSyncBuffer
    {
        get => Require(_writeSyncBuffer);
        internal set => Set(ref _writeSyncBuffer, value);
    }

    private MethodBuilder? _fstatSync;
    public MethodBuilder FstatSync
    {
        get => Require(_fstatSync);
        internal set => Set(ref _fstatSync, value);
    }

    private MethodBuilder? _ftruncateSync;
    public MethodBuilder FtruncateSync
    {
        get => Require(_ftruncateSync);
        internal set => Set(ref _ftruncateSync, value);
    }

    private MethodBuilder? _fsyncSync;
    public MethodBuilder FsyncSync
    {
        get => Require(_fsyncSync);
        internal set => Set(ref _fsyncSync, value);
    }

    private MethodBuilder? _fdPath;
    public MethodBuilder FdPath
    {
        get => Require(_fdPath);
        internal set => Set(ref _fdPath, value);
    }

    private MethodBuilder? _statfsRaw;
    public MethodBuilder StatfsRaw
    {
        get => Require(_statfsRaw);
        internal set => Set(ref _statfsRaw, value);
    }

    private MethodBuilder? _flagsParsePure;
    public MethodBuilder FlagsParsePure
    {
        get => Require(_flagsParsePure);
        internal set => Set(ref _flagsParsePure, value);
    }

    private MethodBuilder? _createHardLinkPure;
    public MethodBuilder CreateHardLinkPure
    {
        get => Require(_createHardLinkPure);
        internal set => Set(ref _createHardLinkPure, value);
    }

    private FieldBuilder? _fileDescriptorTableInstance;
    public FieldBuilder FileDescriptorTableInstance
    {
        get => Require(_fileDescriptorTableInstance);
        internal set => Set(ref _fileDescriptorTableInstance, value);
    }

    private MethodBuilder? _fileDescriptorTableOpen;
    public MethodBuilder FileDescriptorTableOpen
    {
        get => Require(_fileDescriptorTableOpen);
        internal set => Set(ref _fileDescriptorTableOpen, value);
    }

    private MethodBuilder? _fileDescriptorTableGet;
    public MethodBuilder FileDescriptorTableGet
    {
        get => Require(_fileDescriptorTableGet);
        internal set => Set(ref _fileDescriptorTableGet, value);
    }

    private MethodBuilder? _fileDescriptorTableClose;
    public MethodBuilder FileDescriptorTableClose
    {
        get => Require(_fileDescriptorTableClose);
        internal set => Set(ref _fileDescriptorTableClose, value);
    }

    private ConstructorBuilder? _dirCtor;
    public ConstructorBuilder DirCtor
    {
        get => Require(_dirCtor);
        internal set => Set(ref _dirCtor, value);
    }

    private ConstructorBuilder? _direntCtor;
    public ConstructorBuilder DirentCtor
    {
        get => Require(_direntCtor);
        internal set => Set(ref _direntCtor, value);
    }

    private MethodBuilder? _mkdtempSync;
    public MethodBuilder MkdtempSync
    {
        get => Require(_mkdtempSync);
        internal set => Set(ref _mkdtempSync, value);
    }

    private MethodBuilder? _opendirSync;
    public MethodBuilder OpendirSync
    {
        get => Require(_opendirSync);
        internal set => Set(ref _opendirSync, value);
    }

    private MethodBuilder? _linkSync;
    public MethodBuilder LinkSync
    {
        get => Require(_linkSync);
        internal set => Set(ref _linkSync, value);
    }

    private Type? _statsType;
    public Type StatsType
    {
        get => Require(_statsType);
        internal set => Set(ref _statsType, value);
    }

    private ConstructorInfo? _statsCtor;
    public ConstructorInfo StatsCtor
    {
        get => Require(_statsCtor);
        internal set => Set(ref _statsCtor, value);
    }

    private MethodBuilder? _statsIsFile;
    public MethodBuilder StatsIsFile
    {
        get => Require(_statsIsFile);
        internal set => Set(ref _statsIsFile, value);
    }

    private MethodBuilder? _statsIsDirectory;
    public MethodBuilder StatsIsDirectory
    {
        get => Require(_statsIsDirectory);
        internal set => Set(ref _statsIsDirectory, value);
    }

    private MethodBuilder? _statsIsSymbolicLink;
    public MethodBuilder StatsIsSymbolicLink
    {
        get => Require(_statsIsSymbolicLink);
        internal set => Set(ref _statsIsSymbolicLink, value);
    }

    private MethodBuilder? _statsIsBlockDevice;
    public MethodBuilder StatsIsBlockDevice
    {
        get => Require(_statsIsBlockDevice);
        internal set => Set(ref _statsIsBlockDevice, value);
    }

    private MethodBuilder? _statsIsCharacterDevice;
    public MethodBuilder StatsIsCharacterDevice
    {
        get => Require(_statsIsCharacterDevice);
        internal set => Set(ref _statsIsCharacterDevice, value);
    }

    private MethodBuilder? _statsIsFIFO;
    public MethodBuilder StatsIsFIFO
    {
        get => Require(_statsIsFIFO);
        internal set => Set(ref _statsIsFIFO, value);
    }

    private MethodBuilder? _statsIsSocket;
    public MethodBuilder StatsIsSocket
    {
        get => Require(_statsIsSocket);
        internal set => Set(ref _statsIsSocket, value);
    }

    private MethodBuilder? _statsSizeGetter;
    public MethodBuilder StatsSizeGetter
    {
        get => Require(_statsSizeGetter);
        internal set => Set(ref _statsSizeGetter, value);
    }

    private FieldBuilder? _statsIsFileField;
    public FieldBuilder StatsIsFileField
    {
        get => Require(_statsIsFileField);
        internal set => Set(ref _statsIsFileField, value);
    }

    private FieldBuilder? _statsIsDirField;
    public FieldBuilder StatsIsDirField
    {
        get => Require(_statsIsDirField);
        internal set => Set(ref _statsIsDirField, value);
    }

    private FieldBuilder? _statsIsSymlinkField;
    public FieldBuilder StatsIsSymlinkField
    {
        get => Require(_statsIsSymlinkField);
        internal set => Set(ref _statsIsSymlinkField, value);
    }

    private FieldBuilder? _statsSizeField;
    public FieldBuilder StatsSizeField
    {
        get => Require(_statsSizeField);
        internal set => Set(ref _statsSizeField, value);
    }

    private FieldBuilder? _statsModeField;
    public FieldBuilder StatsModeField
    {
        get => Require(_statsModeField);
        internal set => Set(ref _statsModeField, value);
    }

    private FieldBuilder? _statsAtimeMsField;
    public FieldBuilder StatsAtimeMsField
    {
        get => Require(_statsAtimeMsField);
        internal set => Set(ref _statsAtimeMsField, value);
    }

    private FieldBuilder? _statsMtimeMsField;
    public FieldBuilder StatsMtimeMsField
    {
        get => Require(_statsMtimeMsField);
        internal set => Set(ref _statsMtimeMsField, value);
    }

    private FieldBuilder? _statsCtimeMsField;
    public FieldBuilder StatsCtimeMsField
    {
        get => Require(_statsCtimeMsField);
        internal set => Set(ref _statsCtimeMsField, value);
    }

    private FieldBuilder? _statsBirthtimeMsField;
    public FieldBuilder StatsBirthtimeMsField
    {
        get => Require(_statsBirthtimeMsField);
        internal set => Set(ref _statsBirthtimeMsField, value);
    }

    private FieldBuilder? _dirPathField;
    public FieldBuilder DirPathField
    {
        get => Require(_dirPathField);
        internal set => Set(ref _dirPathField, value);
    }

    private FieldBuilder? _dirEnumeratorField;
    public FieldBuilder DirEnumeratorField
    {
        get => Require(_dirEnumeratorField);
        internal set => Set(ref _dirEnumeratorField, value);
    }

    private FieldBuilder? _dirClosedField;
    public FieldBuilder DirClosedField
    {
        get => Require(_dirClosedField);
        internal set => Set(ref _dirClosedField, value);
    }

    private FieldBuilder? _direntNameField;
    public FieldBuilder DirentNameField
    {
        get => Require(_direntNameField);
        internal set => Set(ref _direntNameField, value);
    }

    private FieldBuilder? _direntIsFileField;
    public FieldBuilder DirentIsFileField
    {
        get => Require(_direntIsFileField);
        internal set => Set(ref _direntIsFileField, value);
    }

    private FieldBuilder? _direntIsDirField;
    public FieldBuilder DirentIsDirField
    {
        get => Require(_direntIsDirField);
        internal set => Set(ref _direntIsDirField, value);
    }

    private FieldBuilder? _direntIsSymlinkField;
    public FieldBuilder DirentIsSymlinkField
    {
        get => Require(_direntIsSymlinkField);
        internal set => Set(ref _direntIsSymlinkField, value);
    }

    private MethodBuilder? _kernel32CreateHardLink;
    public MethodBuilder Kernel32CreateHardLink
    {
        get => Require(_kernel32CreateHardLink);
        internal set => Set(ref _kernel32CreateHardLink, value);
    }

    private MethodBuilder? _libcLink;
    public MethodBuilder LibcLink
    {
        get => Require(_libcLink);
        internal set => Set(ref _libcLink, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Filesystem metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Filesystem metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = ExistsSync;
        _ = EncodingName;
        _ = ToBytes;
        _ = ReadFileSync;
        _ = WriteFileSync;
        _ = AppendFileSync;
        _ = UnlinkSync;
        _ = MkdirSync;
        _ = RmdirSync;
        _ = ReaddirSync;
        _ = StatSync;
        _ = LstatSync;
        _ = StatRaw;
        _ = LstatRaw;
        _ = FstatRaw;
        _ = BuildStatRecord;
        _ = StatTimeMs;
        _ = RenameSync;
        _ = CopyFileSync;
        _ = AccessSync;
        _ = ChmodSync;
        _ = ChownSync;
        _ = LchownSync;
        _ = TruncateSync;
        _ = SymlinkSync;
        _ = ReadlinkSync;
        _ = RealpathSync;
        _ = UtimesSync;
        _ = GetConstants;
        _ = CreateDirent;
        _ = OpenSync;
        _ = CloseSync;
        _ = ReadSync;
        _ = WriteSyncBuffer;
        _ = FstatSync;
        _ = FtruncateSync;
        _ = FsyncSync;
        _ = FdPath;
        _ = StatfsRaw;
        _ = FlagsParsePure;
        _ = CreateHardLinkPure;
        _ = FileDescriptorTableInstance;
        _ = FileDescriptorTableOpen;
        _ = FileDescriptorTableGet;
        _ = FileDescriptorTableClose;
        _ = DirCtor;
        _ = DirentCtor;
        _ = MkdtempSync;
        _ = OpendirSync;
        _ = LinkSync;
        _ = StatsType;
        _ = StatsCtor;
        _ = StatsIsFile;
        _ = StatsIsDirectory;
        _ = StatsIsSymbolicLink;
        _ = StatsIsBlockDevice;
        _ = StatsIsCharacterDevice;
        _ = StatsIsFIFO;
        _ = StatsIsSocket;
        _ = StatsSizeGetter;
        _ = StatsIsFileField;
        _ = StatsIsDirField;
        _ = StatsIsSymlinkField;
        _ = StatsSizeField;
        _ = StatsModeField;
        _ = StatsAtimeMsField;
        _ = StatsMtimeMsField;
        _ = StatsCtimeMsField;
        _ = StatsBirthtimeMsField;
        _ = DirPathField;
        _ = DirEnumeratorField;
        _ = DirClosedField;
        _ = DirentNameField;
        _ = DirentIsFileField;
        _ = DirentIsDirField;
        _ = DirentIsSymlinkField;
        _ = Kernel32CreateHardLink;
        _ = LibcLink;
        IsComplete = true;
    }
}
