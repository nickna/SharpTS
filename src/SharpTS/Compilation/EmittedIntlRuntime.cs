using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>Optional Intl declarations for one compilation, checked and frozen at completion.</summary>
public sealed class EmittedIntlRuntime
{
    internal EmittedIntlRuntime() { }

    public bool IsComplete { get; private set; }

    private FieldBuilder? _namespaceField;
    public FieldBuilder NamespaceField
    {
        get => Require(_namespaceField);
        internal set => Set(ref _namespaceField, value);
    }

    private MethodBuilder? _namespacePopulate;
    public MethodBuilder NamespacePopulate
    {
        get => Require(_namespacePopulate);
        internal set => Set(ref _namespacePopulate, value);
    }

    private MethodBuilder? _createNumberFormat;
    public MethodBuilder CreateNumberFormat
    {
        get => Require(_createNumberFormat);
        internal set => Set(ref _createNumberFormat, value);
    }

    private MethodBuilder? _createDateTimeFormat;
    public MethodBuilder CreateDateTimeFormat
    {
        get => Require(_createDateTimeFormat);
        internal set => Set(ref _createDateTimeFormat, value);
    }

    private MethodBuilder? _createCollator;
    public MethodBuilder CreateCollator
    {
        get => Require(_createCollator);
        internal set => Set(ref _createCollator, value);
    }

    private MethodBuilder? _createPluralRules;
    public MethodBuilder CreatePluralRules
    {
        get => Require(_createPluralRules);
        internal set => Set(ref _createPluralRules, value);
    }

    private MethodBuilder? _createRelativeTimeFormat;
    public MethodBuilder CreateRelativeTimeFormat
    {
        get => Require(_createRelativeTimeFormat);
        internal set => Set(ref _createRelativeTimeFormat, value);
    }

    private MethodBuilder? _createListFormat;
    public MethodBuilder CreateListFormat
    {
        get => Require(_createListFormat);
        internal set => Set(ref _createListFormat, value);
    }

    private MethodBuilder? _createDisplayNames;
    public MethodBuilder CreateDisplayNames
    {
        get => Require(_createDisplayNames);
        internal set => Set(ref _createDisplayNames, value);
    }

    private MethodBuilder? _createSegmenter;
    public MethodBuilder CreateSegmenter
    {
        get => Require(_createSegmenter);
        internal set => Set(ref _createSegmenter, value);
    }

    private static T Require<T>(T? handle, [CallerMemberName] string name = "") where T : class =>
        handle ?? throw new InvalidOperationException($"Intl metadata '{name}' has not been declared.");

    private void Set<T>(ref T? field, T value) where T : class
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(value);
        field = value;
    }

    private void EnsureMutable()
    {
        if (IsComplete)
            throw new InvalidOperationException("Intl metadata emission is already complete.");
    }

    internal void CompleteEmission()
    {
        EnsureMutable();
        _ = NamespaceField;
        _ = NamespacePopulate;
        _ = CreateNumberFormat;
        _ = CreateDateTimeFormat;
        _ = CreateCollator;
        _ = CreatePluralRules;
        _ = CreateRelativeTimeFormat;
        _ = CreateListFormat;
        _ = CreateDisplayNames;
        _ = CreateSegmenter;
        IsComplete = true;
    }
}
