using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Security.Cryptography;

namespace SharpTS.Compilation.Symbols;

/// <summary>
/// Accumulates source documents and per-method sequence points during IL emission, then builds the
/// debug metadata tables for the portable PDB.
/// </summary>
/// <remarks>
/// <para><b>Why SharpTS builds these tables itself.</b> <c>PersistedAssemblyBuilder</c> can populate
/// a PDB <see cref="MetadataBuilder"/> from <see cref="ILGenerator.MarkSequencePoint"/>, but it adds
/// a <c>MethodDebugInformation</c> row only for methods that have an IL body. That table is defined
/// to be <i>parallel</i> to <c>MethodDef</c> — row <c>N</c> describes method <c>N</c> — so every
/// abstract or interface method silently shifts the rows after it and a debugger attributes line
/// information to the wrong method. SharpTS emits interfaces, so it always hits this. Emitting the
/// table here keeps the parallel invariant exact (see the emitted row-count assertion in
/// <see cref="BuildPdbMetadata"/>), and is also what lets hidden sequence points and precise local
/// scopes be expressed later.</para>
///
/// <para>Blob layouts follow the portable PDB specification's <c>Document</c> name encoding and
/// <c>SequencePoints</c> record format.</para>
/// </remarks>
internal sealed class DebugInfoCollector
{
    /// <summary>GUID identifying SHA-256 as a document hash algorithm, per the portable PDB spec.</summary>
    private static readonly Guid Sha256HashAlgorithm = new("8829d00f-11b8-4213-878b-770e8597ac16");

    /// <summary>
    /// GUID identifying C# source documents. SharpTS emits managed IL and uses the C# debugger
    /// pipeline; a zero language GUID makes managed debuggers reject otherwise valid TypeScript
    /// sequence points as having no associated target-code type.
    /// </summary>
    private static readonly Guid ManagedSourceLanguage =
        new("3f5162f8-07c6-11d3-9053-00c04fa302a1");

    /// <summary>Line number marking a hidden sequence point in the debugger's own conventions.</summary>
    internal const int HiddenLine = 0xfeefee;

    private readonly Dictionary<string, SourceFile> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<MethodBase, List<Point>> _methods = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MethodBase, MethodLocalSymbols> _locals = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MethodBase, MethodBase> _stateMachines =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<MethodBase, List<AsyncStep>> _asyncSteps =
        new(ReferenceEqualityComparer.Instance);

    private static readonly Guid AsyncMethodSteppingInformationKind =
        new("54fd2ac5-e925-401a-9c2a-f94f171072f8");

    /// <summary>
    /// Starts collecting named locals and lexical scopes for a method body, returning the sink to
    /// hand to its <see cref="LocalsManager"/>.
    /// </summary>
    internal MethodLocalSymbols BeginMethodLocals(MethodBase method, System.Reflection.Emit.ILGenerator il)
    {
        // A method body can be handed more than one emission context; all of them must report into
        // the same record or the scopes would be split across duplicate rows.
        if (_locals.TryGetValue(method, out var existing)) return existing;

        var symbols = new MethodLocalSymbols(method, il);
        symbols.Begin();
        _locals[method] = symbols;
        return symbols;
    }

    /// <summary>Whether anything worth writing to a PDB has been recorded.</summary>
    internal bool IsEmpty => _methods.Count == 0;

    /// <summary>
    /// A registered source document. Handed back from <see cref="AddDocument"/> so emitters name a
    /// document by identity rather than by re-hashing a path on every sequence point.
    /// </summary>
    internal sealed class SourceFile(string path, byte[] hash, byte[]? embeddedSource)
    {
        internal string Path { get; } = path;
        internal byte[] Hash { get; } = hash;
        internal byte[]? EmbeddedSource { get; } = embeddedSource;

        /// <summary>Row assigned when the PDB's Document table is written.</summary>
        internal DocumentHandle Handle { get; set; }
    }

    /// <summary>A sequence point. <see cref="Document"/> is null for a hidden point.</summary>
    private readonly record struct Point(int IlOffset, SourceFile? Document, int StartLine, int StartColumn, int EndLine, int EndColumn);
    private readonly record struct AsyncStep(int YieldOffset, int ResumeOffset);

    /// <summary>
    /// Associates a generated MoveNext body with the source-level kickoff method. Portable-PDB
    /// readers use this table to present the original async/iterator frame instead of raw
    /// state-machine plumbing.
    /// </summary>
    internal void RecordStateMachine(
        MethodBase kickoffMethod,
        MethodBase moveNextMethod)
    {
        _stateMachines[moveNextMethod] = kickoffMethod;
    }

    /// <summary>Records one await suspension/resume pair for async stepping.</summary>
    internal void RecordAsyncStep(
        MethodBase moveNextMethod,
        int yieldOffset,
        int resumeOffset)
    {
        if (!_asyncSteps.TryGetValue(moveNextMethod, out List<AsyncStep>? steps))
            _asyncSteps[moveNextMethod] = steps = [];
        if (steps.Count == 0 || steps[^1] != new AsyncStep(yieldOffset, resumeOffset))
            steps.Add(new AsyncStep(yieldOffset, resumeOffset));
    }

    /// <summary>
    /// Registers a source file, returning a stable key to pass to
    /// <see cref="RecordSequencePoint"/>. Safe to call repeatedly for the same path.
    /// </summary>
    /// <param name="path">
    /// Path recorded in the PDB. Callers should normalize to a full path so debuggers can locate the
    /// file; it is also the identity used to deduplicate documents.
    /// </param>
    /// <param name="sourceText">
    /// The exact text compiled. Its UTF-8 bytes are hashed with SHA-256 so a debugger can tell
    /// whether the file on disk still matches what was compiled.
    /// </param>
    /// <param name="embedSource">
    /// Embeds <paramref name="sourceText"/> in the PDB. Used for virtual documents such as the
    /// bundled stdlib, which have no file on disk for a debugger to open.
    /// </param>
    internal SourceFile AddDocument(string path, string sourceText, bool embedSource = false)
    {
        if (_documents.TryGetValue(path, out var existing))
            return existing;

        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(sourceText);
        var document = new SourceFile(path, SHA256.HashData(utf8), embedSource ? utf8 : null);
        _documents[path] = document;
        return document;
    }

    /// <summary>
    /// Marks <paramref name="ilOffset"/> in <paramref name="method"/> as the start of the statement
    /// at the given one-based source position.
    /// </summary>
    /// <param name="document">A handle previously returned by <see cref="AddDocument"/>.</param>
    /// <remarks>
    /// Points at an IL offset already recorded for the method are ignored: the portable PDB format
    /// requires strictly increasing offsets, and a duplicate would also make a debugger stop twice
    /// in one place.
    /// </remarks>
    internal void RecordSequencePoint(
        MethodBase method, SourceFile document, int ilOffset, int startLine, int startColumn, int endLine, int endColumn)
    {
        Add(method, new Point(ilOffset, document, startLine, startColumn, endLine, endColumn));
    }

    /// <summary>
    /// Marks <paramref name="ilOffset"/> as compiler-generated code with no source of its own, so a
    /// debugger steps over it instead of attributing it to whichever statement came before.
    /// </summary>
    internal void RecordHiddenSequencePoint(MethodBase method, int ilOffset)
    {
        Add(method, new Point(ilOffset, null, HiddenLine, 0, HiddenLine, 0));
    }

    private void Add(MethodBase method, Point point)
    {
        if (!_methods.TryGetValue(method, out var points))
            _methods[method] = points = [];

        // Offsets arrive in emission order, so only the last one can collide.
        if (points.Count > 0 && points[^1].IlOffset >= point.IlOffset)
            return;

        points.Add(point);
    }

    /// <summary>
    /// Builds the PDB's debug metadata tables.
    /// </summary>
    /// <param name="methodDefRowCount">
    /// Number of <c>MethodDef</c> rows in the emitted assembly. <c>MethodDebugInformation</c> is
    /// filled to exactly this many rows so that row <c>N</c> describes method <c>N</c>.
    /// </param>
    /// <param name="localSignatureRid">
    /// Maps a <c>MethodDef</c> row id to the <c>StandAloneSig</c> row id of that method's local
    /// signature, or 0 when it declares no locals. Debuggers use it to type the local slots named by
    /// the <c>LocalVariable</c> table.
    /// </param>
    /// <param name="methodIlSize">
    /// Maps a <c>MethodDef</c> row id to the byte length of its IL body. Used to close the
    /// method-body scope, whose end is the end of the method rather than an offset any emitter
    /// reports.
    /// </param>
    internal MetadataBuilder BuildPdbMetadata(
        int methodDefRowCount, Func<int, int> localSignatureRid, Func<int, int>? methodIlSize = null)
    {
        var pdb = new MetadataBuilder();

        foreach (var document in _documents.Values)
        {
            document.Handle = pdb.AddDocument(
                name: pdb.GetOrAddDocumentName(document.Path),
                hashAlgorithm: pdb.GetOrAddGuid(Sha256HashAlgorithm),
                hash: pdb.GetOrAddBlob(document.Hash),
                language: pdb.GetOrAddGuid(ManagedSourceLanguage));
        }

        var byRid = new Dictionary<int, List<Point>>(_methods.Count);
        foreach (var (method, points) in _methods)
        {
            if (points.Count > 0)
                byRid[MetadataTokens.GetRowNumber(MetadataTokens.MethodDefinitionHandle(method.MetadataToken))] = points;
        }

        for (int rid = 1; rid <= methodDefRowCount; rid++)
        {
            if (!byRid.TryGetValue(rid, out var points))
            {
                pdb.AddMethodDebugInformation(default, default);
                continue;
            }

            SourceFile? single = SingleDocument(points);
            var blob = EncodeSequencePoints(points, localSignatureRid(rid), single);
            pdb.AddMethodDebugInformation(single?.Handle ?? default, pdb.GetOrAddBlob(blob));
        }

        WriteStateMachineMetadata(pdb);
        WriteLocalScopes(pdb, methodIlSize);
        EmbedSources(pdb);
        return pdb;
    }

    private void WriteStateMachineMetadata(MetadataBuilder pdb)
    {
        foreach (var (moveNext, kickoff) in _stateMachines
            .Select(pair => (
                MoveNext: MethodHandle(pair.Key),
                Kickoff: MethodHandle(pair.Value)))
            .OrderBy(pair => MetadataTokens.GetRowNumber(pair.MoveNext)))
        {
            pdb.AddStateMachineMethod(moveNext, kickoff);
        }

        GuidHandle kind = pdb.GetOrAddGuid(AsyncMethodSteppingInformationKind);
        foreach (var (method, steps) in _asyncSteps
            .OrderBy(pair => MetadataTokens.GetRowNumber(MethodHandle(pair.Key))))
        {
            if (steps.Count == 0)
                continue;

            MethodDefinitionHandle moveNext = MethodHandle(method);
            var blob = new BlobBuilder();
            blob.WriteInt32(-1); // no catch-handler offset
            foreach (AsyncStep step in steps.OrderBy(step => step.YieldOffset))
            {
                blob.WriteInt32(step.YieldOffset);
                blob.WriteInt32(step.ResumeOffset);
                blob.WriteCompressedInteger(MetadataTokens.GetRowNumber(moveNext));
            }
            pdb.AddCustomDebugInformation(
                moveNext,
                kind,
                pdb.GetOrAddBlob(blob));
        }

        static MethodDefinitionHandle MethodHandle(MethodBase method) =>
            MetadataTokens.MethodDefinitionHandle(method.MetadataToken);
    }

    /// <summary>
    /// Writes the <c>LocalScope</c> and <c>LocalVariable</c> tables that give a debugger variable
    /// names and the ranges they are live over.
    /// </summary>
    /// <remarks>
    /// <c>LocalScope</c> must be sorted by method, then by start offset ascending, then by length
    /// descending — an enclosing scope has to precede the scopes nested inside it. Each scope's
    /// variables must occupy a contiguous run of <c>LocalVariable</c> rows, so the variables are
    /// written immediately before the scope that owns them.
    /// </remarks>
    private void WriteLocalScopes(MetadataBuilder pdb, Func<int, int>? methodIlSize)
    {
        foreach (var symbols in _locals.Values
            .Where(s => s.HasLocals)
            .Select(s => (Symbols: s, Rid: MetadataTokens.GetRowNumber(
                MetadataTokens.MethodDefinitionHandle(s.Method.MetadataToken))))
            .OrderBy(entry => entry.Rid))
        {
            int bodySize = methodIlSize?.Invoke(symbols.Rid) ?? 0;

            foreach (var scope in symbols.Symbols.Scopes
                .Where(scope => scope.Locals.Count > 0)
                .OrderBy(scope => scope.StartOffset)
                .ThenByDescending(scope => EndOf(scope, bodySize) - scope.StartOffset))
            {
                var firstVariable = MetadataTokens.LocalVariableHandle(
                    pdb.GetRowCount(TableIndex.LocalVariable) + 1);

                foreach (var (name, slot) in scope.Locals)
                {
                    pdb.AddLocalVariable(
                        IsCompilerGenerated(name) ? LocalVariableAttributes.DebuggerHidden : LocalVariableAttributes.None,
                        slot,
                        pdb.GetOrAddString(name));
                }

                int end = EndOf(scope, bodySize);
                pdb.AddLocalScope(
                    method: MetadataTokens.MethodDefinitionHandle(symbols.Rid),
                    importScope: default,
                    variableList: firstVariable,
                    constantList: MetadataTokens.LocalConstantHandle(pdb.GetRowCount(TableIndex.LocalConstant) + 1),
                    startOffset: scope.StartOffset,
                    length: Math.Max(0, end - scope.StartOffset));
            }
        }

        static int EndOf(MethodLocalSymbols.Scope scope, int bodySize) =>
            scope.IsOpen ? Math.Max(scope.StartOffset, bodySize) : scope.EndOffset;
    }

    /// <summary>
    /// Whether a binding is scaffolding rather than something the user declared. The parser and the
    /// lowerings name their temporaries distinctively, and marking them hidden keeps a debugger's
    /// locals window showing only variables that appear in the source.
    /// </summary>
    private static bool IsCompilerGenerated(string name) =>
        name.StartsWith('$') || name.StartsWith("__") || name.StartsWith("_dest");

    /// <summary>Returns the one document all points share, or null when they span several.</summary>
    private static SourceFile? SingleDocument(List<Point> points)
    {
        SourceFile? single = null;
        foreach (var point in points)
        {
            if (point.Document is null) continue;
            if (single is null) single = point.Document;
            else if (!ReferenceEquals(single, point.Document)) return null;
        }
        return single;
    }

    /// <summary>
    /// Encodes the portable PDB <c>SequencePoints</c> blob: a header, then one record per point with
    /// IL offsets and source positions stored as deltas from the previous record.
    /// </summary>
    private static byte[] EncodeSequencePoints(List<Point> points, int localSignatureRid, SourceFile? singleDocument)
    {
        var writer = new BlobBuilder();
        writer.WriteCompressedInteger(localSignatureRid);

        SourceFile? current = singleDocument;
        if (singleDocument is null)
        {
            // Multi-document method: the first document is named in the blob instead of the row.
            current = points.First(p => p.Document is not null).Document;
            writer.WriteCompressedInteger(MetadataTokens.GetRowNumber(current!.Handle));
        }

        int previousOffset = -1;
        int previousLine = -1, previousColumn = 0;

        foreach (var point in points)
        {
            if (point.Document is not null && !ReferenceEquals(point.Document, current))
            {
                // A document record: offset delta 0 (never valid for a real point) then the new document.
                writer.WriteCompressedInteger(0);
                writer.WriteCompressedInteger(MetadataTokens.GetRowNumber(point.Document.Handle));
                current = point.Document;
            }

            writer.WriteCompressedInteger(previousOffset < 0 ? point.IlOffset : point.IlOffset - previousOffset);
            previousOffset = point.IlOffset;

            if (point.Document is null)
            {
                // Hidden point: zero line and column deltas, and no position follows.
                writer.WriteCompressedInteger(0);
                writer.WriteCompressedInteger(0);
                continue;
            }

            int deltaLines = point.EndLine - point.StartLine;
            int deltaColumns = point.EndColumn - point.StartColumn;
            writer.WriteCompressedInteger(deltaLines);
            if (deltaLines == 0) writer.WriteCompressedInteger(deltaColumns);
            else writer.WriteCompressedSignedInteger(deltaColumns);

            if (previousLine < 0)
            {
                writer.WriteCompressedInteger(point.StartLine);
                writer.WriteCompressedInteger(point.StartColumn);
            }
            else
            {
                writer.WriteCompressedSignedInteger(point.StartLine - previousLine);
                writer.WriteCompressedSignedInteger(point.StartColumn - previousColumn);
            }
            previousLine = point.StartLine;
            previousColumn = point.StartColumn;
        }

        return writer.ToArray();
    }

    /// <summary>
    /// Attaches source text to documents that have no file on disk, using the
    /// <c>Embedded Source</c> custom debug information kind.
    /// </summary>
    private void EmbedSources(MetadataBuilder pdb)
    {
        var embeddedSourceKind = pdb.GetOrAddGuid(new Guid("0e8a571b-6926-466e-b4ad-8ab04611f5fe"));

        foreach (var document in _documents.Values)
        {
            if (document.EmbeddedSource is null) continue;

            // Format: int32 uncompressed-size (0 = stored verbatim) followed by the bytes.
            var blob = new BlobBuilder();
            blob.WriteInt32(0);
            blob.WriteBytes(document.EmbeddedSource);
            pdb.AddCustomDebugInformation(document.Handle, embeddedSourceKind, pdb.GetOrAddBlob(blob));
        }
    }
}
