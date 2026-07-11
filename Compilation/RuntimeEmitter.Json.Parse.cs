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
        il.Emit(OpCodes.Call, EmitJsonParseHelper(typeBuilder));
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

    private MethodBuilder EmitJsonParseHelper(TypeBuilder typeBuilder)
    {
        // Parse JSON using RuntimeTypes helper
        var method = typeBuilder.DefineMethod(
            "JsonParseHelper",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );

        var il = method.GetILGenerator();

        // Call RuntimeTypes.JsonParse directly - this method exists in the emitted assembly
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, EmitJsonParseStaticHelper(typeBuilder));
        il.Emit(OpCodes.Ret);

        return method;
    }

    private MethodBuilder EmitJsonParseStaticHelper(TypeBuilder typeBuilder)
    {
        var validateControlChars = EmitJsonValidateControlChars(typeBuilder);
        var parseValue = EmitParseValueFromReaderHelper(typeBuilder);

        var method = typeBuilder.DefineMethod(
            "ParseJsonValue",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Object,
            [_types.Object]
        );

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
        var strLocal = il.DeclareLocal(_types.String);
        var bytesLocal = il.DeclareLocal(typeof(byte[]));
        var optionsLocal = il.DeclareLocal(typeof(System.Text.Json.JsonReaderOptions));
        var readerLocal = il.DeclareLocal(readerType);
        var resultLocal = il.DeclareLocal(_types.Object);
        var notNullLabel = il.DefineLabel();
        var gotTokenLabel = il.DefineLabel();
        var okEndLabel = il.DefineLabel();

        // if (arg == null) return null;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse_S, notNullLabel);

        // str = arg.ToString();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.Object, "ToString"));
        il.Emit(OpCodes.Stloc, strLocal);

        // ECMA-262 25.5.1: control chars (U+0000–U+001F except \t \n \r whitespace) are
        // forbidden in the JSON grammar. Kept as an explicit pre-pass so behavior is
        // identical to before; the throw converts to SyntaxError via EmitJsonParse's catch.
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Call, validateControlChars);

        // bytes = Encoding.UTF8.GetBytes(str)
        il.Emit(OpCodes.Call, typeof(System.Text.Encoding).GetProperty("UTF8")!.GetGetMethod()!);
        il.Emit(OpCodes.Ldloc, strLocal);
        il.Emit(OpCodes.Callvirt, typeof(System.Text.Encoding).GetMethod("GetBytes", [typeof(string)])!);
        il.Emit(OpCodes.Stloc, bytesLocal);

        // reader = new Utf8JsonReader((ReadOnlySpan<byte>)bytes, default)
        il.Emit(OpCodes.Ldloca, optionsLocal);
        il.Emit(OpCodes.Initobj, typeof(System.Text.Json.JsonReaderOptions));
        il.Emit(OpCodes.Ldloca, readerLocal);
        il.Emit(OpCodes.Ldloc, bytesLocal);
        il.Emit(OpCodes.Call, typeof(ReadOnlySpan<byte>).GetMethod("op_Implicit", [typeof(byte[])])!);
        il.Emit(OpCodes.Ldloc, optionsLocal);
        il.Emit(OpCodes.Call, readerType.GetConstructor(
            [typeof(ReadOnlySpan<byte>), typeof(System.Text.Json.JsonReaderOptions)])!);

        // if (!reader.Read()) throw  — empty input is not a valid JSON document.
        il.Emit(OpCodes.Ldloca, readerLocal);
        il.Emit(OpCodes.Call, readerType.GetMethod("Read", Type.EmptyTypes)!);
        il.Emit(OpCodes.Brtrue, gotTokenLabel);
        il.Emit(OpCodes.Ldstr, "Unexpected end of JSON input");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

        il.MarkLabel(gotTokenLabel);
        // result = ParseValueFromReader(ref reader)
        il.Emit(OpCodes.Ldloca, readerLocal);
        il.Emit(OpCodes.Call, parseValue);
        il.Emit(OpCodes.Stloc, resultLocal);

        // A single JSON document must hold exactly one value: any non-whitespace
        // trailing token is a SyntaxError (matches JsonDocument.Parse).
        il.Emit(OpCodes.Ldloca, readerLocal);
        il.Emit(OpCodes.Call, readerType.GetMethod("Read", Type.EmptyTypes)!);
        il.Emit(OpCodes.Brfalse, okEndLabel);
        il.Emit(OpCodes.Ldstr, "Unexpected non-whitespace character after JSON");
        il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);

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
        il.Emit(OpCodes.Callvirt, _types.String.GetMethod("get_Chars", [_types.Int32])!);
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
    /// Emits <c>object? ParseValueFromReader(ref Utf8JsonReader reader)</c>: a recursive
    /// descent that consumes the value at the reader's current position and returns the
    /// runtime graph node for it — <c>Dictionary&lt;string,object?&gt;</c> for objects,
    /// <c>List&lt;object?&gt;</c> for arrays, a boxed double / bool, a string, or null.
    /// On entry the reader is positioned ON the value's first token; on return it is
    /// positioned on the value's last token (the matching End for containers), so the
    /// caller's next <c>Read()</c> advances past it. Mirrors the value kinds the old
    /// JsonDocument walker produced, so the resulting graph (consumed by the reviver and
    /// everything downstream) is byte-for-byte the same.
    /// </summary>
    private MethodBuilder EmitParseValueFromReaderHelper(TypeBuilder typeBuilder)
    {
        var readerType = typeof(System.Text.Json.Utf8JsonReader);
        var readMethod = readerType.GetMethod("Read", Type.EmptyTypes)!;
        var tokenTypeGetter = readerType.GetProperty("TokenType")!.GetGetMethod()!;
        var getStringMethod = readerType.GetMethod("GetString", Type.EmptyTypes)!;
        var getDoubleMethod = readerType.GetMethod("GetDouble", Type.EmptyTypes)!;

        var method = typeBuilder.DefineMethod(
            "ParseValueFromReader",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Object,
            [readerType.MakeByRefType()]
        );

        var il = method.GetILGenerator();

        var dictLocal = il.DeclareLocal(_types.DictionaryStringObject);
        var listLocal = il.DeclareLocal(_types.ListOfObject);
        var nameLocal = il.DeclareLocal(_types.String);

        var objectLabel = il.DefineLabel();
        var arrayLabel = il.DefineLabel();
        var stringLabel = il.DefineLabel();
        var numberLabel = il.DefineLabel();
        var trueLabel = il.DefineLabel();
        var falseLabel = il.DefineLabel();
        var nullLabel = il.DefineLabel();

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
        // name = reader.GetString() (current token is the property name)
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, getStringMethod);
        il.Emit(OpCodes.Stloc, nameLocal);
        // reader.Read() → the value's first token
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, readMethod);
        il.Emit(OpCodes.Pop);
        // dict[name] = ParseValueFromReader(ref reader)
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldloc, nameLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, method); // recursive
        il.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", [_types.String, _types.Object]));
        il.Emit(OpCodes.Br, objLoop);

        il.MarkLabel(objEnd);
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ret);

        // --- Array: [ value* ] ---
        il.MarkLabel(arrayLabel);
        il.Emit(OpCodes.Pop); // pop tokenType
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
}

