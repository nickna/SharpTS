using System.Reflection;
using System.Reflection.Emit;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitJsonParse(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "JsonParse",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );
        runtime.JsonParse = method;

        var il = method.GetILGenerator();
        var resultLocal = il.DeclareLocal(_types.Object);
        var endLabel = il.DefineLabel();

        // try { return JsonParseHelper(arg); }
        // catch (Exception ex) {
        //   if (ex.Data.Contains("__tsValue")) rethrow;
        //   throw $SyntaxError(ex.Message); // ECMA-262 24.5.1.1: parse failures throw SyntaxError
        // }
        il.BeginExceptionBlock();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, EmitJsonParseHelper(typeBuilder, runtime));
        il.Emit(OpCodes.Stloc, resultLocal);
        il.Emit(OpCodes.Leave, endLabel);

        il.BeginCatchBlock(_types.Exception);
        var exLocal = il.DeclareLocal(_types.Exception);
        il.Emit(OpCodes.Stloc, exLocal);
        var rethrowLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Data").GetGetMethod()!);
        il.Emit(OpCodes.Ldstr, "__tsValue");
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.IDictionary, "Contains", _types.Object));
        il.Emit(OpCodes.Brtrue, rethrowLabel);

        il.Emit(OpCodes.Ldloc, exLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.Exception, "Message").GetGetMethod()!);
        GuestErrorEmitter.ThrowErrorFromStack(il, runtime, runtime.TSSyntaxErrorCtor);

        il.MarkLabel(rethrowLabel);
        il.Emit(OpCodes.Rethrow);
        il.EndExceptionBlock();

        il.MarkLabel(endLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);
    }

    private MethodBuilder EmitJsonParseHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        // Parse JSON using RuntimeTypes helper
        var method = typeBuilder.DefineMethod(
            "JsonParseHelper",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();
        var shapeLocal = il.DeclareLocal(_types.Object);
        var (_, tryGetShape) = EmitJsonShapeAssociationHelpers(typeBuilder);

        // Carry a weakly associated closed shape only when the exact string
        // instance came from the guarded shaped serializer.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca, shapeLocal);
        il.Emit(OpCodes.Call, tryGetShape);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Call, EmitJsonParseStaticHelper(typeBuilder, runtime));
        il.Emit(OpCodes.Ret);

        return method;
    }

    private MethodBuilder EmitJsonParseStaticHelper(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var validateControlChars = EmitJsonValidateControlChars(typeBuilder);
        var parseValue = EmitParseValueFromReaderHelper(typeBuilder, runtime);

        var method = typeBuilder.DefineMethod(
            "ParseJsonValue",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Object]
        );
        method.SetImplementationFlags(MethodImplAttributes.AggressiveOptimization);

        var il = method.GetILGenerator();

        // One-pass parse: transcode the text to UTF-8 and walk it with a
        // System.Text.Json.Utf8JsonReader straight into our runtime graph
        // (Dictionary<string,object?> / List<object?> / boxed double|bool|string|null),
        // instead of building a throwaway JsonDocument DOM and then re-walking it.
        // Skipping the intermediate DOM removes a full second pass over the data and
        // its allocations. Utf8JsonReader and Encoding live in the BCL, so the emitted
        // token references System.Text.Json / System.Private.CoreLib, never SharpTS.dll
        // — standalone DLLs stay standalone. The token decoders (GetString/GetDouble)
        // are the SAME engine JsonDocument used, so the produced values are identical.
        var readerType = typeof(System.Text.Json.Utf8JsonReader);
        var encodingType = typeof(System.Text.Encoding);
        var bytePoolType = typeof(System.Buffers.ArrayPool<byte>);
        var propertyNamesType = typeof(List<string>);
        var strLocal = il.DeclareLocal(_types.String);
        var bytesLocal = il.DeclareLocal(typeof(byte[]));
        var byteCountLocal = il.DeclareLocal(_types.Int32);
        var encodingLocal = il.DeclareLocal(encodingType);
        var optionsLocal = il.DeclareLocal(typeof(System.Text.Json.JsonReaderOptions));
        var readerLocal = il.DeclareLocal(readerType);
        var propertyNamesLocal = il.DeclareLocal(propertyNamesType);
        var resultLocal = il.DeclareLocal(_types.Object);
        var notNullLabel = il.DefineLabel();
        var gotTokenLabel = il.DefineLabel();
        var parsedLabel = il.DefineLabel();
        var okEndLabel = il.DefineLabel();

        // if (arg == null) return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, notNullLabel);

        // str = arg.ToString();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, strLocal);

        // ECMA-262 25.5.1: control chars (U+0000–U+001F except \t \n \r whitespace) are
        // forbidden in the JSON grammar. Kept as an explicit pre-pass so behavior is
        // identical to before; the throw converts to SyntaxError via EmitJsonParse's catch.
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Call, validateControlChars);

        // encoding = Encoding.UTF8;
        // byteCount = encoding.GetByteCount(str);
        // bytes = ArrayPool<byte>.Shared.Rent(Math.Max(byteCount, 1));
        il.Emit(OpCodes.Call, encodingType.GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Stloc, encodingLocal);
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, encodingType.GetMethod("GetByteCount", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, byteCountLocal);
        il.Emit(OpCodes.Call, bytePoolType.GetProperty("Shared")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, byteCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(Math).GetMethod("Max", [typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Callvirt, bytePoolType.GetMethod("Rent", [typeof(int)])!);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // Return the bounded shared buffer even when transcoding or parsing throws.
        il.BeginExceptionBlock();

        // encoding.GetBytes(str, 0, str.Length, bytes, 0)
        il.Emit(OpCodes.Ldloc, encodingLocal);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, encodingType.GetMethod(
            "GetBytes",
            [typeof(string), typeof(int), typeof(int), typeof(byte[]), typeof(int)])!);
        il.Emit(OpCodes.Pop);

        // reader = new Utf8JsonReader(new ReadOnlySpan<byte>(bytes, 0, byteCount), default)
        il.Emit(OpCodes.Ldloca, optionsLocal);
        il.Emit(OpCodes.Initobj, typeof(System.Text.Json.JsonReaderOptions));
        il.Emit(OpCodes.Ldloca, readerLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldloc, byteCountLocal);
        il.Emit(OpCodes.Newobj, typeof(ReadOnlySpan<byte>).GetConstructor(
            [typeof(byte[]), typeof(int), typeof(int)])!);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Call, readerType.GetConstructor(
            [typeof(ReadOnlySpan<byte>), typeof(System.Text.Json.JsonReaderOptions)])!);

        // Reuse repeated property-name strings within this document. The cache is
        // deliberately small and parse-scoped so it cannot retain user data.
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Newobj, propertyNamesType.GetConstructor([typeof(int)])!);
        il.Emit(OpCodes.Stloc, propertyNamesLocal);

        // if (!reader.Read()) throw  — empty input is not a valid JSON document.
        il.Emit(OpCodes.Ldloca, readerLocal);
        il.Emit(OpCodes.Call, readerType.GetMethod("Read", Type.EmptyTypes)!);
        il.Emit(OpCodes.Brtrue, gotTokenLabel);
        il.Emit(OpCodes.Ldstr, "Unexpected end of JSON input");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(gotTokenLabel);
        // result = ParseValueFromReader(ref reader, propertyNames)
        il.Emit(OpCodes.Ldloca, readerLocal);
        il.Emit(OpCodes.Ldloc, propertyNamesLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, parseValue);
        il.Emit(OpCodes.Stloc, resultLocal);

        // A single JSON document must hold exactly one value: any non-whitespace
        // trailing token is a SyntaxError (matches JsonDocument.Parse).
        il.Emit(OpCodes.Ldloca, readerLocal);
        il.Emit(OpCodes.Call, readerType.GetMethod("Read", Type.EmptyTypes)!);
        il.Emit(OpCodes.Brfalse, parsedLabel);
        il.Emit(OpCodes.Ldstr, "Unexpected non-whitespace character after JSON");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(parsedLabel);
        il.Emit(OpCodes.Leave, okEndLabel);

        il.BeginFinallyBlock();
        il.Emit(OpCodes.Call, bytePoolType.GetProperty("Shared")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, bytePoolType.GetMethod(
            "Return",
            [typeof(byte[]), typeof(bool)])!);
        il.EndExceptionBlock();

        il.MarkLabel(okEndLabel);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(notNullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits a string-validator helper that walks the input JSON text and
    /// throws Exception on any control-char violation per ECMA-262 25.5.1
    /// JSON grammar:
    ///   - Inside string literals ("..."): U+0000–U+001F are forbidden
    ///     (must be escaped as \u00XX, or via \b\t\n\f\r mnemonics).
    ///   - Outside string literals: only \t \n \r are allowed as whitespace
    ///     in the U+0000–U+001F range. Other control chars are forbidden.
    ///
    /// State machine: tracks in-string flag and a one-char escape lookahead.
    /// The outer JsonParse catch converts the thrown Exception to SyntaxError.
    /// </summary>
    private MethodBuilder EmitJsonValidateControlChars(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "ValidateJsonControlChars",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.String]
        );

        var il = method.GetILGenerator();
        var iLocal = il.DeclareLocal(_types.Int32);
        var lenLocal = il.DeclareLocal(_types.Int32);
        var inStringLocal = il.DeclareLocal(_types.Boolean);
        var afterEscapeLocal = il.DeclareLocal(_types.Boolean);
        var cLocal = il.DeclareLocal(_types.Char);

        var nullRetLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, nullRetLabel);

        // len = s.Length
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, lenLocal);
        // i = 0; inString = false; afterEscape = false;
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, inStringLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, afterEscapeLocal);

        var loopStart = il.DefineLabel();
        var loopEnd = il.DefineLabel();
        var advanceLabel = il.DefineLabel();
        var throwLabel = il.DefineLabel();

        il.MarkLabel(loopStart);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldloc, lenLocal);
        il.Emit(OpCodes.Bge, loopEnd);

        // c = s[i]
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.String, "get_Chars", [_types.Int32])!);
        il.Emit(OpCodes.Stloc, cLocal);

        // if (afterEscape) { afterEscape = false; goto advance; }
        var notAfterEscapeLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, afterEscapeLocal);
        il.Emit(OpCodes.Brfalse, notAfterEscapeLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, afterEscapeLocal);
        il.Emit(OpCodes.Br, advanceLabel);
        il.MarkLabel(notAfterEscapeLabel);

        // if (inString) { ... } else { ... }
        var elseBranchLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, inStringLocal);
        il.Emit(OpCodes.Brfalse, elseBranchLabel);

        // INSIDE STRING: any U+0000–U+001F is invalid (must be escaped).
        var notControlInStrLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0x20);
        il.Emit(OpCodes.Bge, notControlInStrLabel);
        il.Emit(OpCodes.Br, throwLabel);
        il.MarkLabel(notControlInStrLabel);
        // if (c == '\\') afterEscape = true; goto advance;
        var notBackslashLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'\\');
        il.Emit(OpCodes.Bne_Un, notBackslashLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, afterEscapeLocal);
        il.Emit(OpCodes.Br, advanceLabel);
        il.MarkLabel(notBackslashLabel);
        // if (c == '"') inString = false;
        var notQuoteLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'"');
        il.Emit(OpCodes.Bne_Un, notQuoteLabel);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, inStringLocal);
        il.MarkLabel(notQuoteLabel);
        il.Emit(OpCodes.Br, advanceLabel);

        // OUTSIDE STRING:
        il.MarkLabel(elseBranchLabel);
        // c < 0x20 and c != \t \n \r → invalid.
        var notControlOutLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0x20);
        il.Emit(OpCodes.Bge, notControlOutLabel);
        // c == '\t' (0x09)?
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0x09);
        il.Emit(OpCodes.Beq, notControlOutLabel);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0x0A);
        il.Emit(OpCodes.Beq, notControlOutLabel);
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, 0x0D);
        il.Emit(OpCodes.Beq, notControlOutLabel);
        il.Emit(OpCodes.Br, throwLabel);
        il.MarkLabel(notControlOutLabel);
        // if (c == '"') inString = true;
        var notOpenQuoteLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, cLocal);
        il.Emit(OpCodes.Ldc_I4, (int)'"');
        il.Emit(OpCodes.Bne_Un, notOpenQuoteLabel);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, inStringLocal);
        il.MarkLabel(notOpenQuoteLabel);

        il.MarkLabel(advanceLabel);
        il.Emit(OpCodes.Ldloc, iLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, iLocal);
        il.Emit(OpCodes.Br, loopStart);

        il.MarkLabel(throwLabel);
        il.Emit(OpCodes.Ldstr, "Bad control character in string literal in JSON");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(loopEnd);
        il.MarkLabel(nullRetLabel);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits <c>object? ParseValueFromReader(ref Utf8JsonReader reader,
    /// List&lt;string&gt; propertyNames)</c>: a recursive
    /// descent that consumes the value at the reader's current position and returns the
    /// runtime graph node for it — <c>Dictionary&lt;string,object?&gt;</c> for objects,
    /// <c>List&lt;object?&gt;</c> for arrays, a boxed double / bool, a string, or null.
    /// On entry the reader is positioned ON the value's first token; on return it is
    /// positioned on the value's last token (the matching End for containers), so the
    /// caller's next <c>Read()</c> advances past it. Mirrors the value kinds the old
    /// JsonDocument walker produced, so the resulting graph (consumed by the reviver and
    /// everything downstream) is byte-for-byte the same.
    /// </summary>
    private MethodBuilder EmitParseValueFromReaderHelper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime)
    {
        var readerType = typeof(System.Text.Json.Utf8JsonReader);
        var propertyNamesType = typeof(List<string>);
        var readMethod = readerType.GetMethod("Read", Type.EmptyTypes)!;
        var tokenTypeGetter = readerType.GetProperty("TokenType")!.GetGetMethod()!;
        var getStringMethod = readerType.GetMethod("GetString", Type.EmptyTypes)!;
        var getDoubleMethod = readerType.GetMethod("GetDouble", Type.EmptyTypes)!;
        var getPropertyName = EmitJsonPropertyNameHelper(typeBuilder);

        var method = typeBuilder.DefineMethod(
            "ParseValueFromReader",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Object,
            [readerType.MakeByRefType(), propertyNamesType, _types.Object]
        );
        method.SetImplementationFlags(MethodImplAttributes.AggressiveOptimization);

        var il = method.GetILGenerator();

        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var nameLocal = il.DeclareLocal(_types.String);
        var shapeLocal = il.DeclareLocal(_types.ObjectArray);
        var childShapeLocal = il.DeclareLocal(_types.Object);
        var parsedValueLocal = il.DeclareLocal(_types.Object);
        var shapeIndexLocal = il.DeclareLocal(_types.Int32);
        var valueIndexLocal = il.DeclareLocal(_types.Int32);
        var valueCountLocal = il.DeclareLocal(_types.Int32);
        var overflowValuesLocal = il.DeclareLocal(_types.ObjectArray);
        var valueLocals = Enumerable.Range(0, 4)
            .Select(_ => il.DeclareLocal(_types.Object))
            .ToArray();

        var objectLabel = il.DefineLabel();
        var arrayLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var numberLabel = il.DefineLabel();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();
        var nullLabel = il.DefineLabel();
        var shapedObjectLabel = il.DefineLabel();

        // switch (reader.TokenType) — same dup/Beq ladder shape as the old DOM walker.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, tokenTypeGetter);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonTokenType.StartObject);
        il.Emit(OpCodes.Beq, objectLabel);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonTokenType.StartArray);
        il.Emit(OpCodes.Beq, arrayLabel);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonTokenType.String);
        il.Emit(OpCodes.Beq, stringLabel);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonTokenType.Number);
        il.Emit(OpCodes.Beq, numberLabel);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonTokenType.True);
        il.Emit(OpCodes.Beq, trueLabel);

        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonTokenType.False);
        il.Emit(OpCodes.Beq, falseLabel);

        il.Emit(OpCodes.Pop); // None / Null / anything else → null
        il.Emit(OpCodes.Br, nullLabel);

        // --- Object: { (PropertyName value)* } ---
        il.MarkLabel(objectLabel);
        il.Emit(OpCodes.Pop); // pop tokenType
        var genericObjectLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.ObjectArray);
        il.Emit(OpCodes.Stloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Brfalse, genericObjectLabel);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldstr, "$O");
        // Shape tags are emitted ldstr literals in this module. Reference
        // identity is therefore exact, verifier-safe, and avoids a checked
        // cast plus string equality for every parsed record.
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brtrue, shapedObjectLabel);

        il.MarkLabel(genericObjectLabel);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        il.Emit(OpCodes.Stloc, dictLocal);

        var objLoop = il.DefineLabel();
        var objEnd = il.DefineLabel();
        il.MarkLabel(objLoop);
        // Read() → PropertyName or EndObject (throws on malformed/incomplete input).
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, readMethod);
        il.Emit(OpCodes.Brfalse, objEnd);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, tokenTypeGetter);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonTokenType.EndObject);
        il.Emit(OpCodes.Beq, objEnd);
        // name = GetJsonPropertyName(ref reader, propertyNames)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, getPropertyName);
        il.Emit(OpCodes.Stloc, nameLocal);
        // reader.Read() → the value's first token
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, readMethod);
        il.Emit(OpCodes.Pop);
        // dict[name] = ParseValueFromReader(ref reader)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, method); // recursive
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object]));
        il.Emit(OpCodes.Br, objLoop);

        il.MarkLabel(objEnd);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);

        // --- Closed shaped object: parse directly into fixed scalar slots. ---
        il.MarkLabel(shapedObjectLabel);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldlen);
        il.Emit(OpCodes.Conv_I4);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stloc, valueCountLocal);
        var fixedValues = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, valueCountLocal);
        il.Emit(OpCodes.Ldc_I4_4);
        il.Emit(OpCodes.Ble, fixedValues);
        il.Emit(OpCodes.Ldloc, valueCountLocal);
        il.Emit(OpCodes.Newarr, _types.Object);
        il.Emit(OpCodes.Stloc, overflowValuesLocal);
        il.MarkLabel(fixedValues);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, shapeIndexLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, valueIndexLocal);

        var shapedLoop = il.DefineLabel();
        var shapedEnd = il.DefineLabel();
        var shapedMismatch = il.DefineLabel();
        il.MarkLabel(shapedLoop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, readMethod);
        il.Emit(OpCodes.Brfalse, shapedEnd);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, tokenTypeGetter);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonTokenType.EndObject);
        il.Emit(OpCodes.Beq, shapedEnd);
        // The shape association is identity-based on the immutable string, but
        // retain an allocation-free key check as a corruption guard.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, shapeIndexLocal);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Castclass, _types.String);
        il.Emit(OpCodes.Call, readerType.GetMethod("ValueTextEquals", [typeof(string)])!);
        il.Emit(OpCodes.Brfalse, shapedMismatch);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, readMethod);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, shapeIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Call, method);
        il.Emit(OpCodes.Stloc, parsedValueLocal);

        var overflowStore = il.DefineLabel();
        var storedValue = il.DefineLabel();
        var storeLabels = valueLocals.Select(_ => il.DefineLabel()).ToArray();
        il.Emit(OpCodes.Ldloc, overflowValuesLocal);
        il.Emit(OpCodes.Brtrue, overflowStore);
        il.Emit(OpCodes.Ldloc, valueIndexLocal);
        il.Emit(OpCodes.Switch, storeLabels);
        il.Emit(OpCodes.Br, shapedMismatch);
        for (int index = 0; index < valueLocals.Length; index++)
        {
            il.MarkLabel(storeLabels[index]);
            il.Emit(OpCodes.Ldloc, parsedValueLocal);
            il.Emit(OpCodes.Stloc, valueLocals[index]);
            il.Emit(OpCodes.Br, storedValue);
        }
        il.MarkLabel(overflowStore);
        il.Emit(OpCodes.Ldloc, overflowValuesLocal);
        il.Emit(OpCodes.Ldloc, valueIndexLocal);
        il.Emit(OpCodes.Ldloc, parsedValueLocal);
        il.Emit(OpCodes.Stelem_Ref);
        il.MarkLabel(storedValue);
        il.Emit(OpCodes.Ldloc, shapeIndexLocal);
        il.Emit(OpCodes.Ldc_I4_2);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, shapeIndexLocal);
        il.Emit(OpCodes.Ldloc, valueIndexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, valueIndexLocal);
        il.Emit(OpCodes.Br, shapedLoop);

        il.MarkLabel(shapedEnd);
        il.Emit(OpCodes.Ldloc, valueIndexLocal);
        il.Emit(OpCodes.Ldloc, valueCountLocal);
        il.Emit(OpCodes.Bne_Un, shapedMismatch);
        var overflowConstruct = il.DefineLabel();
        var constructLabels = Enumerable.Range(0, 4)
            .Select(_ => il.DefineLabel()).ToArray();
        il.Emit(OpCodes.Ldloc, overflowValuesLocal);
        il.Emit(OpCodes.Brtrue, overflowConstruct);
        il.Emit(OpCodes.Ldloc, valueCountLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Switch, constructLabels);
        il.Emit(OpCodes.Br, shapedMismatch);
        for (int arity = 1; arity <= 4; arity++)
        {
            il.MarkLabel(constructLabels[arity - 1]);
            il.Emit(OpCodes.Ldloc, shapeLocal);
            for (int index = 0; index < arity; index++)
                il.Emit(OpCodes.Ldloc, valueLocals[index]);
            il.Emit(OpCodes.Newobj, runtime.JsonScalarRecordInlineCtors[arity]);
            il.Emit(OpCodes.Ret);
        }
        il.MarkLabel(overflowConstruct);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, overflowValuesLocal);
        il.Emit(OpCodes.Newobj, runtime.JsonScalarRecordCtor);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(shapedMismatch);
        il.Emit(OpCodes.Ldstr, "JSON shape association mismatch");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        // --- Array: [ value* ] ---
        il.MarkLabel(arrayLabel);
        il.Emit(OpCodes.Pop); // pop tokenType
        var arrayShapeReady = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Isinst, _types.ObjectArray);
        il.Emit(OpCodes.Stloc, shapeLocal);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Brfalse, arrayShapeReady);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Ldstr, "$A");
        il.Emit(OpCodes.Ceq);
        il.Emit(OpCodes.Brfalse, arrayShapeReady);
        il.Emit(OpCodes.Ldloc, shapeLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldelem_Ref);
        il.Emit(OpCodes.Stloc, childShapeLocal);
        il.MarkLabel(arrayShapeReady);
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.ListOfObject));
        il.Emit(OpCodes.Stloc, listLocal);

        var arrLoop = il.DefineLabel();
        var arrEnd = il.DefineLabel();
        il.MarkLabel(arrLoop);
        // Read() → the element's first token or EndArray.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, readMethod);
        il.Emit(OpCodes.Brfalse, arrEnd);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, tokenTypeGetter);
        il.Emit(OpCodes.Ldc_I4, (int)System.Text.Json.JsonTokenType.EndArray);
        il.Emit(OpCodes.Beq, arrEnd);
        // list.Add(ParseValueFromReader(ref reader))
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, childShapeLocal);
        il.Emit(OpCodes.Call, method); // recursive
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.ListOfObject, "Add", [_types.Object]));
        il.Emit(OpCodes.Br, arrLoop);

        il.MarkLabel(arrEnd);
        il.Emit(OpCodes.Ldloc, listLocal);
        il.Emit(OpCodes.Ret);

        // --- String ---
        il.MarkLabel(stringLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, getStringMethod);
        il.Emit(OpCodes.Ret);

        // --- Number → boxed double ---
        il.MarkLabel(numberLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, getDoubleMethod);
        il.Emit(OpCodes.Box, _types.Double);
        il.Emit(OpCodes.Ret);

        // --- True / False → boxed bool ---
        il.MarkLabel(trueLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(falseLabel);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Box, _types.Boolean);
        il.Emit(OpCodes.Ret);

        // --- Null / None / unhandled ---
        il.MarkLabel(nullLabel);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        return method;
    }

    /// <summary>
    /// Emits a bounded, per-parse property-name cache. <c>ValueTextEquals</c>
    /// compares the reader's UTF-8 token without allocating a candidate string,
    /// so record arrays reuse the first materialized "id" / "label" instances.
    /// </summary>
    private MethodBuilder EmitJsonPropertyNameHelper(TypeBuilder typeBuilder)
    {
        var readerType = typeof(System.Text.Json.Utf8JsonReader);
        var propertyNamesType = typeof(List<string>);
        var method = typeBuilder.DefineMethod(
            "GetJsonPropertyName",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [readerType.MakeByRefType(), propertyNamesType]
        );

        var il = method.GetILGenerator();
        var indexLocal = il.DeclareLocal(_types.Int32);
        var candidateLocal = il.DeclareLocal(_types.String);
        var nameLocal = il.DeclareLocal(_types.String);
        var loopLabel = il.DefineLabel();
        var missLabel = il.DefineLabel();
        var returnCandidateLabel = il.DefineLabel();
        var returnNameLabel = il.DefineLabel();
        var countGetter = propertyNamesType.GetProperty("Count")!.GetGetMethod()!;

        // for (int i = 0; i < propertyNames.Count; i++)
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.MarkLabel(loopLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Bge, missLabel);

        // candidate = propertyNames[i];
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Callvirt, propertyNamesType.GetMethod("get_Item", [typeof(int)])!);
        il.Emit(OpCodes.Stloc, candidateLocal);

        // if (reader.ValueTextEquals(candidate)) return candidate;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, candidateLocal);
        il.Emit(OpCodes.Call, readerType.GetMethod("ValueTextEquals", [typeof(string)])!);
        il.Emit(OpCodes.Brtrue, returnCandidateLabel);
        il.Emit(OpCodes.Ldloc, indexLocal);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, indexLocal);
        il.Emit(OpCodes.Br, loopLabel);

        // string name = reader.GetString()!;
        // if (propertyNames.Count < 64) propertyNames.Add(name);
        il.MarkLabel(missLabel);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, readerType.GetMethod("GetString", Type.EmptyTypes)!);
        il.Emit(OpCodes.Stloc, nameLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, countGetter);
        il.Emit(OpCodes.Ldc_I4, 64);
        il.Emit(OpCodes.Bge, returnNameLabel);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Callvirt, propertyNamesType.GetMethod("Add", [typeof(string)])!);

        il.MarkLabel(returnNameLabel);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(returnCandidateLabel);
        il.Emit(OpCodes.Ldloc, candidateLocal);
        il.Emit(OpCodes.Ret);

        return method;
    }
}

