using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Selected RegExp type, operations and protocol declarations for one compilation.</summary>
public sealed class EmittedRegExpImplementation
{
    internal EmittedRegExpImplementation() { }
    public bool IsComplete { get; private set; }
    private bool _splitProtocolBodyEmitted;
    private bool _matchAllProtocolBodyEmitted;

    private MethodBuilder? _buildNamedGroups;
    public MethodBuilder BuildNamedGroups
    {
        get => Require(_buildNamedGroups);
        internal set => SetHandle(ref _buildNamedGroups, value);
    }

    private MethodBuilder? _createWithFlags;
    public MethodBuilder CreateWithFlags
    {
        get => Require(_createWithFlags);
        internal set => SetHandle(ref _createWithFlags, value);
    }

    private MethodBuilder? _coerceArgument;
    public MethodBuilder CoerceArgument
    {
        get => Require(_coerceArgument);
        internal set => SetHandle(ref _coerceArgument, value);
    }

    private MethodBuilder? _exec;
    public MethodBuilder Exec
    {
        get => Require(_exec);
        internal set => SetHandle(ref _exec, value);
    }

    private MethodBuilder? _fromArguments;
    public MethodBuilder FromArguments
    {
        get => Require(_fromArguments);
        internal set => SetHandle(ref _fromArguments, value);
    }

    private MethodBuilder? _getDotAll;
    public MethodBuilder GetDotAll
    {
        get => Require(_getDotAll);
        internal set => SetHandle(ref _getDotAll, value);
    }

    private MethodBuilder? _getFlags;
    public MethodBuilder GetFlags
    {
        get => Require(_getFlags);
        internal set => SetHandle(ref _getFlags, value);
    }

    private MethodBuilder? _getGlobal;
    public MethodBuilder GetGlobal
    {
        get => Require(_getGlobal);
        internal set => SetHandle(ref _getGlobal, value);
    }

    private MethodBuilder? _getHasIndices;
    public MethodBuilder GetHasIndices
    {
        get => Require(_getHasIndices);
        internal set => SetHandle(ref _getHasIndices, value);
    }

    private MethodBuilder? _getIgnoreCase;
    public MethodBuilder GetIgnoreCase
    {
        get => Require(_getIgnoreCase);
        internal set => SetHandle(ref _getIgnoreCase, value);
    }

    private MethodBuilder? _getMultiline;
    public MethodBuilder GetMultiline
    {
        get => Require(_getMultiline);
        internal set => SetHandle(ref _getMultiline, value);
    }

    private MethodBuilder? _getSource;
    public MethodBuilder GetSource
    {
        get => Require(_getSource);
        internal set => SetHandle(ref _getSource, value);
    }

    private MethodBuilder? _getSticky;
    public MethodBuilder GetSticky
    {
        get => Require(_getSticky);
        internal set => SetHandle(ref _getSticky, value);
    }

    private MethodBuilder? _getUnicode;
    public MethodBuilder GetUnicode
    {
        get => Require(_getUnicode);
        internal set => SetHandle(ref _getUnicode, value);
    }

    private MethodBuilder? _getUnicodeSets;
    public MethodBuilder GetUnicodeSets
    {
        get => Require(_getUnicodeSets);
        internal set => SetHandle(ref _getUnicodeSets, value);
    }

    private MethodBuilder? _symbolMatchAllProtocol;
    public MethodBuilder SymbolMatchAllProtocol
    {
        get => Require(_symbolMatchAllProtocol);
        internal set => SetHandle(ref _symbolMatchAllProtocol, value);
    }

    private MethodBuilder? _symbolSplitProtocol;
    public MethodBuilder SymbolSplitProtocol
    {
        get => Require(_symbolSplitProtocol);
        internal set => SetHandle(ref _symbolSplitProtocol, value);
    }

    private MethodBuilder? _stableStringReplace;
    public MethodBuilder StableStringReplace
    {
        get => Require(_stableStringReplace);
        internal set => SetHandle(ref _stableStringReplace, value);
    }

    private MethodBuilder? _stringMatchAll;
    public MethodBuilder StringMatchAll
    {
        get => Require(_stringMatchAll);
        internal set => SetHandle(ref _stringMatchAll, value);
    }

    private MethodBuilder? _stringMatchAllPrepared;
    public MethodBuilder StringMatchAllPrepared
    {
        get => Require(_stringMatchAllPrepared);
        internal set => SetHandle(ref _stringMatchAllPrepared, value);
    }

    private MethodBuilder? _stringMatch;
    public MethodBuilder StringMatch
    {
        get => Require(_stringMatch);
        internal set => SetHandle(ref _stringMatch, value);
    }

    private MethodBuilder? _stringReplaceAll;
    public MethodBuilder StringReplaceAll
    {
        get => Require(_stringReplaceAll);
        internal set => SetHandle(ref _stringReplaceAll, value);
    }

    private MethodBuilder? _stringReplace;
    public MethodBuilder StringReplace
    {
        get => Require(_stringReplace);
        internal set => SetHandle(ref _stringReplace, value);
    }

    private MethodBuilder? _stringReplaceWithFunction;
    public MethodBuilder StringReplaceWithFunction
    {
        get => Require(_stringReplaceWithFunction);
        internal set => SetHandle(ref _stringReplaceWithFunction, value);
    }

    private MethodBuilder? _stringSearch;
    public MethodBuilder StringSearch
    {
        get => Require(_stringSearch);
        internal set => SetHandle(ref _stringSearch, value);
    }

    private MethodBuilder? _stringSplitProto;
    public MethodBuilder StringSplitProto
    {
        get => Require(_stringSplitProto);
        internal set => SetHandle(ref _stringSplitProto, value);
    }

    private MethodBuilder? _stringSplit;
    public MethodBuilder StringSplit
    {
        get => Require(_stringSplit);
        internal set => SetHandle(ref _stringSplit, value);
    }

    private MethodBuilder? _advanceStringIndex;
    public MethodBuilder AdvanceStringIndex
    {
        get => Require(_advanceStringIndex);
        internal set => SetHandle(ref _advanceStringIndex, value);
    }

    private ConstructorBuilder? _patternConstructor;
    public ConstructorBuilder PatternConstructor
    {
        get => Require(_patternConstructor);
        internal set => SetHandle(ref _patternConstructor, value);
    }

    private ConstructorBuilder? _patternFlagsConstructor;
    public ConstructorBuilder PatternFlagsConstructor
    {
        get => Require(_patternFlagsConstructor);
        internal set => SetHandle(ref _patternFlagsConstructor, value);
    }

    private MethodBuilder? _staticEscape;
    public MethodBuilder StaticEscape
    {
        get => Require(_staticEscape);
        internal set => SetHandle(ref _staticEscape, value);
    }

    private MethodBuilder? _instanceExec;
    public MethodBuilder InstanceExec
    {
        get => Require(_instanceExec);
        internal set => SetHandle(ref _instanceExec, value);
    }

    private MethodBuilder? _flagsGetter;
    public MethodBuilder FlagsGetter
    {
        get => Require(_flagsGetter);
        internal set => SetHandle(ref _flagsGetter, value);
    }

    private MethodBuilder? _globalGetter;
    public MethodBuilder GlobalGetter
    {
        get => Require(_globalGetter);
        internal set => SetHandle(ref _globalGetter, value);
    }

    private MethodBuilder? _ignoreCaseGetter;
    public MethodBuilder IgnoreCaseGetter
    {
        get => Require(_ignoreCaseGetter);
        internal set => SetHandle(ref _ignoreCaseGetter, value);
    }

    private MethodBuilder? _lastIndexGetter;
    public MethodBuilder LastIndexGetter
    {
        get => Require(_lastIndexGetter);
        internal set => SetHandle(ref _lastIndexGetter, value);
    }

    private MethodBuilder? _lastIndexSetter;
    public MethodBuilder LastIndexSetter
    {
        get => Require(_lastIndexSetter);
        internal set => SetHandle(ref _lastIndexSetter, value);
    }

    private MethodBuilder? _multilineGetter;
    public MethodBuilder MultilineGetter
    {
        get => Require(_multilineGetter);
        internal set => SetHandle(ref _multilineGetter, value);
    }

    private MethodBuilder? _normalizeFlags;
    public MethodBuilder NormalizeFlags
    {
        get => Require(_normalizeFlags);
        internal set => SetHandle(ref _normalizeFlags, value);
    }

    private MethodBuilder? _prototypeExec;
    public MethodBuilder PrototypeExec
    {
        get => Require(_prototypeExec);
        internal set => SetHandle(ref _prototypeExec, value);
    }

    private MethodBuilder? _prototypeGetDotAll;
    public MethodBuilder PrototypeGetDotAll
    {
        get => Require(_prototypeGetDotAll);
        internal set => SetHandle(ref _prototypeGetDotAll, value);
    }

    private MethodBuilder? _prototypeGetFlags;
    public MethodBuilder PrototypeGetFlags
    {
        get => Require(_prototypeGetFlags);
        internal set => SetHandle(ref _prototypeGetFlags, value);
    }

    private MethodBuilder? _prototypeGetGlobal;
    public MethodBuilder PrototypeGetGlobal
    {
        get => Require(_prototypeGetGlobal);
        internal set => SetHandle(ref _prototypeGetGlobal, value);
    }

    private MethodBuilder? _prototypeGetHasIndices;
    public MethodBuilder PrototypeGetHasIndices
    {
        get => Require(_prototypeGetHasIndices);
        internal set => SetHandle(ref _prototypeGetHasIndices, value);
    }

    private MethodBuilder? _prototypeGetIgnoreCase;
    public MethodBuilder PrototypeGetIgnoreCase
    {
        get => Require(_prototypeGetIgnoreCase);
        internal set => SetHandle(ref _prototypeGetIgnoreCase, value);
    }

    private MethodBuilder? _prototypeGetMultiline;
    public MethodBuilder PrototypeGetMultiline
    {
        get => Require(_prototypeGetMultiline);
        internal set => SetHandle(ref _prototypeGetMultiline, value);
    }

    private MethodBuilder? _prototypeGetSource;
    public MethodBuilder PrototypeGetSource
    {
        get => Require(_prototypeGetSource);
        internal set => SetHandle(ref _prototypeGetSource, value);
    }

    private MethodBuilder? _prototypeGetSticky;
    public MethodBuilder PrototypeGetSticky
    {
        get => Require(_prototypeGetSticky);
        internal set => SetHandle(ref _prototypeGetSticky, value);
    }

    private MethodBuilder? _prototypeGetUnicode;
    public MethodBuilder PrototypeGetUnicode
    {
        get => Require(_prototypeGetUnicode);
        internal set => SetHandle(ref _prototypeGetUnicode, value);
    }

    private MethodBuilder? _prototypeGetUnicodeSets;
    public MethodBuilder PrototypeGetUnicodeSets
    {
        get => Require(_prototypeGetUnicodeSets);
        internal set => SetHandle(ref _prototypeGetUnicodeSets, value);
    }

    private MethodBuilder? _prototypeTest;
    public MethodBuilder PrototypeTest
    {
        get => Require(_prototypeTest);
        internal set => SetHandle(ref _prototypeTest, value);
    }

    private MethodBuilder? _prototypeToString;
    public MethodBuilder PrototypeToString
    {
        get => Require(_prototypeToString);
        internal set => SetHandle(ref _prototypeToString, value);
    }

    private MethodBuilder? _instanceReplace;
    public MethodBuilder InstanceReplace
    {
        get => Require(_instanceReplace);
        internal set => SetHandle(ref _instanceReplace, value);
    }

    private MethodBuilder? _sourceGetter;
    public MethodBuilder SourceGetter
    {
        get => Require(_sourceGetter);
        internal set => SetHandle(ref _sourceGetter, value);
    }

    private MethodBuilder? _symbolMatchAll;
    public MethodBuilder SymbolMatchAll
    {
        get => Require(_symbolMatchAll);
        internal set => SetHandle(ref _symbolMatchAll, value);
    }

    private MethodBuilder? _symbolMatch;
    public MethodBuilder SymbolMatch
    {
        get => Require(_symbolMatch);
        internal set => SetHandle(ref _symbolMatch, value);
    }

    private MethodBuilder? _symbolReplace;
    public MethodBuilder SymbolReplace
    {
        get => Require(_symbolReplace);
        internal set => SetHandle(ref _symbolReplace, value);
    }

    private MethodBuilder? _symbolSearch;
    public MethodBuilder SymbolSearch
    {
        get => Require(_symbolSearch);
        internal set => SetHandle(ref _symbolSearch, value);
    }

    private MethodBuilder? _symbolSplit;
    public MethodBuilder SymbolSplit
    {
        get => Require(_symbolSplit);
        internal set => SetHandle(ref _symbolSplit, value);
    }

    private MethodBuilder? _instanceTest;
    public MethodBuilder InstanceTest
    {
        get => Require(_instanceTest);
        internal set => SetHandle(ref _instanceTest, value);
    }

    private TypeBuilder? _type;
    public TypeBuilder Type
    {
        get => Require(_type);
        internal set => SetHandle(ref _type, value);
    }

    private FieldBuilder? _regexField;
    public FieldBuilder RegexField
    {
        get => Require(_regexField);
        internal set => SetHandle(ref _regexField, value);
    }

    private FieldBuilder? _boxedLastIndexField;
    public FieldBuilder BoxedLastIndexField
    {
        get => Require(_boxedLastIndexField);
        internal set => SetHandle(ref _boxedLastIndexField, value);
    }

    private MethodBuilder? _instanceMatchAll;
    public MethodBuilder InstanceMatchAll
    {
        get => Require(_instanceMatchAll);
        internal set => SetHandle(ref _instanceMatchAll, value);
    }

    private MethodBuilder? _instanceSearch;
    public MethodBuilder InstanceSearch
    {
        get => Require(_instanceSearch);
        internal set => SetHandle(ref _instanceSearch, value);
    }

    private MethodBuilder? _instanceSplit;
    public MethodBuilder InstanceSplit
    {
        get => Require(_instanceSplit);
        internal set => SetHandle(ref _instanceSplit, value);
    }

    internal void MarkSplitProtocolBodyEmitted()
    {
        EnsureMutable();
        if (_splitProtocolBodyEmitted)
            throw new InvalidOperationException("RegExp split protocol body emission is already complete.");
        _splitProtocolBodyEmitted = true;
    }

    internal void MarkMatchAllProtocolBodyEmitted()
    {
        EnsureMutable();
        if (_matchAllProtocolBodyEmitted)
            throw new InvalidOperationException("RegExp matchAll protocol body emission is already complete.");
        _matchAllProtocolBodyEmitted = true;
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException("RegExp implementation metadata '" + name + "' has not been declared.");

    private void SetHandle<T>(ref T? field, T value, [CallerMemberName] string name = "") where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        if (field is not null)
            throw new InvalidOperationException("RegExp implementation metadata '" + name + "' has already been declared.");
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("RegExp implementation metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = BuildNamedGroups;
        _ = CreateWithFlags;
        _ = CoerceArgument;
        _ = Exec;
        _ = FromArguments;
        _ = GetDotAll;
        _ = GetFlags;
        _ = GetGlobal;
        _ = GetHasIndices;
        _ = GetIgnoreCase;
        _ = GetMultiline;
        _ = GetSource;
        _ = GetSticky;
        _ = GetUnicode;
        _ = GetUnicodeSets;
        _ = SymbolMatchAllProtocol;
        _ = SymbolSplitProtocol;
        _ = StableStringReplace;
        _ = StringMatchAll;
        _ = StringMatchAllPrepared;
        _ = StringMatch;
        _ = StringReplaceAll;
        _ = StringReplace;
        _ = StringReplaceWithFunction;
        _ = StringSearch;
        _ = StringSplitProto;
        _ = StringSplit;
        _ = AdvanceStringIndex;
        _ = PatternConstructor;
        _ = PatternFlagsConstructor;
        _ = StaticEscape;
        _ = InstanceExec;
        _ = FlagsGetter;
        _ = GlobalGetter;
        _ = IgnoreCaseGetter;
        _ = LastIndexGetter;
        _ = LastIndexSetter;
        _ = MultilineGetter;
        _ = NormalizeFlags;
        _ = PrototypeExec;
        _ = PrototypeGetDotAll;
        _ = PrototypeGetFlags;
        _ = PrototypeGetGlobal;
        _ = PrototypeGetHasIndices;
        _ = PrototypeGetIgnoreCase;
        _ = PrototypeGetMultiline;
        _ = PrototypeGetSource;
        _ = PrototypeGetSticky;
        _ = PrototypeGetUnicode;
        _ = PrototypeGetUnicodeSets;
        _ = PrototypeTest;
        _ = PrototypeToString;
        _ = InstanceReplace;
        _ = SourceGetter;
        _ = SymbolMatchAll;
        _ = SymbolMatch;
        _ = SymbolReplace;
        _ = SymbolSearch;
        _ = SymbolSplit;
        _ = InstanceTest;
        _ = Type;
        _ = RegexField;
        _ = BoxedLastIndexField;
        _ = InstanceMatchAll;
        _ = InstanceSearch;
        _ = InstanceSplit;
        if (!_splitProtocolBodyEmitted || !_matchAllProtocolBodyEmitted)
            throw new InvalidOperationException("RegExp protocol bodies have not all been emitted.");
        IsComplete = true;
    }
}
