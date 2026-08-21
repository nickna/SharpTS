using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using SharpTS.Runtime.BuiltIns;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits a UTF-16 parser for the exact immutable string instances produced
    /// by the guarded closed-shape serializer. The association is an identity
    /// proof: unlike ordinary JSON.parse input, these strings have fixed member
    /// order, no insignificant whitespace, and a descriptor that names every
    /// value slot. Unsupported descriptors remain on the Utf8JsonReader path.
    /// </summary>
    private MethodBuilder EmitJsonAssociatedParseHelper(
        TypeBuilder typeBuilder,
        EmittedRuntime runtime)
    {
        var consumeLiteral = EmitJsonAssociatedConsumeLiteral(typeBuilder);
        var readNumber = EmitJsonAssociatedReadNumber(typeBuilder);
        var readString = EmitJsonAssociatedReadString(typeBuilder);

        var supportedShapes = _features.JsonScalarRecordShapes
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Where(pair => runtime.JsonTypedScalarRecordCtors.ContainsKey(pair.Key))
            .Where(pair => IsDirectlyParseable(pair.Value))
            .Select((pair, ordinal) => (
                Fingerprint: pair.Key,
                Shape: pair.Value,
                Method: typeBuilder.DefineMethod(
                    $"ParseJsonAssociatedRecord{ordinal}",
                    MethodAttributes.Private | MethodAttributes.Static,
                    _types.Object,
                    [_types.String, _types.Int32.MakeByRefType(), _types.Object])))
            .ToArray();

        foreach (var parser in supportedShapes)
        {
            parser.Method.SetImplementationFlags(
                MethodImplAttributes.AggressiveOptimization);
        }

        var parsersByFingerprint = supportedShapes.ToDictionary(
            parser => parser.Fingerprint,
            parser => parser.Method,
            StringComparer.Ordinal);

        foreach (var parser in supportedShapes)
        {
            EmitRecordParser(
                parser.Fingerprint,
                parser.Shape,
                parser.Method);
        }

        var method = typeBuilder.DefineMethod(
            "TryParseJsonAssociated",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Boolean,
            [_types.String, _types.Object, _types.Object.MakeByRefType()]);
        method.SetImplementationFlags(MethodImplAttributes.AggressiveOptimization);

        var il = method.GetILGenerator();
        var indexLocal = il.DeclareLocal(_types.Int32);
        var resultLocal = il.DeclareLocal(_types.Object);
        var miss = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stind_Ref);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, miss);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, miss);

        foreach (var parser in supportedShapes)
        {
            var next = il.DefineLabel();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldsfld,
                runtime.JsonTypedScalarRecordShapeFields[parser.Fingerprint]);
            il.Emit(OpCodes.Bne_Un, next);

            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldloca, indexLocal);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, parser.Method);
            il.Emit(OpCodes.Stloc, resultLocal);

            // The root parser must consume the complete associated string.
            il.Emit(OpCodes.Ldloc, indexLocal);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt,
                _types.GetProperty(_types.String, "Length").GetGetMethod()!);
            var complete = il.DefineLabel();
            il.Emit(OpCodes.Beq, complete);
            EmitAssociatedMismatch(il);
            il.MarkLabel(complete);

            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldloc, resultLocal);
            il.Emit(OpCodes.Stind_Ref);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
            il.MarkLabel(next);
        }

        il.MarkLabel(miss);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
        return method;

        bool IsDirectlyParseable(JsonSerializationShape shape) => shape switch
        {
            JsonSerializationShape.Number => true,
            JsonSerializationShape.String => true,
            JsonSerializationShape.Boolean => true,
            JsonSerializationShape.Record record =>
                record.Fields.All(field => IsDirectlyParseable(field.Value)),
            JsonSerializationShape.Array { Element: JsonSerializationShape.Record record } =>
                IsDirectlyParseable(record),
            _ => false
        };

        void EmitRecordParser(
            string fingerprint,
            JsonSerializationShape.Record shape,
            MethodBuilder parserMethod)
        {
            var parserIl = parserMethod.GetILGenerator();
            var descriptorLocal = parserIl.DeclareLocal(_types.ObjectArray);
            Type[] fieldTypes = shape.Fields
                .Select(field => GetJsonScalarRecordFieldType(field.Value))
                .ToArray();
            LocalBuilder[] fieldLocals = fieldTypes
                .Select(parserIl.DeclareLocal)
                .ToArray();

            parserIl.Emit(OpCodes.Ldarg_2);
            parserIl.Emit(OpCodes.Castclass, _types.ObjectArray);
            parserIl.Emit(OpCodes.Stloc, descriptorLocal);

            if (shape.Fields.Count == 0)
            {
                EmitConsume(parserIl, "{}");
            }
            else
            {
                for (int index = 0; index < shape.Fields.Count; index++)
                {
                    var field = shape.Fields[index];
                    string prefix = (index == 0 ? "{" : ",") +
                        JsonStringEscaper.Quote(field.Key) + ":";
                    EmitConsume(parserIl, prefix);
                    EmitValue(field.Value, fieldLocals[index], index);
                }

                EmitConsume(parserIl, "}");
            }

            parserIl.Emit(OpCodes.Ldarg_2);
            foreach (var fieldLocal in fieldLocals)
                parserIl.Emit(OpCodes.Ldloc, fieldLocal);
            parserIl.Emit(OpCodes.Newobj,
                runtime.JsonTypedScalarRecordCtors[fingerprint]);
            parserIl.Emit(OpCodes.Ret);

            void EmitConsume(ILGenerator target, string literal)
            {
                target.Emit(OpCodes.Ldarg_0);
                target.Emit(OpCodes.Ldarg_1);
                target.Emit(OpCodes.Ldstr, literal);
                target.Emit(OpCodes.Call, consumeLiteral);
            }

            void EmitValue(
                JsonSerializationShape valueShape,
                LocalBuilder destination,
                int fieldIndex)
            {
                switch (valueShape)
                {
                    case JsonSerializationShape.Number:
                        parserIl.Emit(OpCodes.Ldarg_0);
                        parserIl.Emit(OpCodes.Ldarg_1);
                        parserIl.Emit(OpCodes.Call, readNumber);
                        parserIl.Emit(OpCodes.Stloc, destination);
                        break;

                    case JsonSerializationShape.String:
                        parserIl.Emit(OpCodes.Ldarg_0);
                        parserIl.Emit(OpCodes.Ldarg_1);
                        parserIl.Emit(OpCodes.Call, readString);
                        parserIl.Emit(OpCodes.Stloc, destination);
                        break;

                    case JsonSerializationShape.Boolean:
                    {
                        var parseFalse = parserIl.DefineLabel();
                        var parsed = parserIl.DefineLabel();
                        EmitLoadCurrentChar(parserIl);
                        parserIl.Emit(OpCodes.Ldc_I4, (int)'t');
                        parserIl.Emit(OpCodes.Bne_Un, parseFalse);
                        EmitConsume(parserIl, "true");
                        parserIl.Emit(OpCodes.Ldc_I4_1);
                        parserIl.Emit(OpCodes.Stloc, destination);
                        parserIl.Emit(OpCodes.Br, parsed);
                        parserIl.MarkLabel(parseFalse);
                        EmitConsume(parserIl, "false");
                        parserIl.Emit(OpCodes.Ldc_I4_0);
                        parserIl.Emit(OpCodes.Stloc, destination);
                        parserIl.MarkLabel(parsed);
                        break;
                    }

                    case JsonSerializationShape.Record record:
                    {
                        string childFingerprint =
                            JsonSerializationShapeAnalyzer.Fingerprint(record);
                        parserIl.Emit(OpCodes.Ldarg_0);
                        parserIl.Emit(OpCodes.Ldarg_1);
                        EmitLoadFieldDescriptor(parserIl, fieldIndex);
                        parserIl.Emit(OpCodes.Call,
                            parsersByFingerprint[childFingerprint]);
                        parserIl.Emit(OpCodes.Stloc, destination);
                        break;
                    }

                    case JsonSerializationShape.Array
                    {
                        Element: JsonSerializationShape.Record elementRecord
                    }:
                        EmitRecordArray(elementRecord, destination, fieldIndex);
                        break;

                    default:
                        throw new InvalidOperationException(
                            $"Unsupported associated JSON shape {valueShape}.");
                }
            }

            void EmitRecordArray(
                JsonSerializationShape.Record elementRecord,
                LocalBuilder destination,
                int fieldIndex)
            {
                string elementFingerprint =
                    JsonSerializationShapeAnalyzer.Fingerprint(elementRecord);
                MethodBuilder elementParser = parsersByFingerprint[elementFingerprint];
                var arrayDescriptor = parserIl.DeclareLocal(_types.ObjectArray);
                var elementDescriptor = parserIl.DeclareLocal(_types.Object);
                var elements = parserIl.DeclareLocal(_types.ListOfObject);
                var loop = parserIl.DefineLabel();
                var nonEmpty = parserIl.DefineLabel();
                var separator = parserIl.DefineLabel();
                var done = parserIl.DefineLabel();

                EmitConsume(parserIl, "[");
                EmitLoadFieldDescriptor(parserIl, fieldIndex);
                parserIl.Emit(OpCodes.Castclass, _types.ObjectArray);
                parserIl.Emit(OpCodes.Stloc, arrayDescriptor);
                parserIl.Emit(OpCodes.Ldloc, arrayDescriptor);
                parserIl.Emit(OpCodes.Ldc_I4_1);
                parserIl.Emit(OpCodes.Ldelem_Ref);
                parserIl.Emit(OpCodes.Stloc, elementDescriptor);
                parserIl.Emit(OpCodes.Newobj,
                    _types.GetDefaultConstructor(_types.ListOfObject));
                parserIl.Emit(OpCodes.Stloc, elements);

                EmitLoadCurrentChar(parserIl);
                parserIl.Emit(OpCodes.Ldc_I4, (int)']');
                parserIl.Emit(OpCodes.Bne_Un, nonEmpty);
                EmitAdvance(parserIl);
                parserIl.Emit(OpCodes.Br, done);

                parserIl.MarkLabel(nonEmpty);
                parserIl.MarkLabel(loop);
                parserIl.Emit(OpCodes.Ldloc, elements);
                parserIl.Emit(OpCodes.Ldarg_0);
                parserIl.Emit(OpCodes.Ldarg_1);
                parserIl.Emit(OpCodes.Ldloc, elementDescriptor);
                parserIl.Emit(OpCodes.Call, elementParser);
                parserIl.Emit(OpCodes.Callvirt,
                    _types.GetMethod(_types.ListOfObject, "Add", [_types.Object]));

                EmitLoadCurrentChar(parserIl);
                parserIl.Emit(OpCodes.Dup);
                parserIl.Emit(OpCodes.Ldc_I4, (int)',');
                parserIl.Emit(OpCodes.Beq, separator);
                parserIl.Emit(OpCodes.Ldc_I4, (int)']');
                var close = parserIl.DefineLabel();
                parserIl.Emit(OpCodes.Beq, close);
                EmitAssociatedMismatch(parserIl);

                parserIl.MarkLabel(separator);
                parserIl.Emit(OpCodes.Pop);
                EmitAdvance(parserIl);
                parserIl.Emit(OpCodes.Br, loop);

                parserIl.MarkLabel(close);
                EmitAdvance(parserIl);
                parserIl.MarkLabel(done);
                parserIl.Emit(OpCodes.Ldloc, elements);
                parserIl.Emit(OpCodes.Stloc, destination);
            }

            void EmitLoadFieldDescriptor(ILGenerator target, int fieldIndex)
            {
                target.Emit(OpCodes.Ldloc, descriptorLocal);
                target.Emit(OpCodes.Ldc_I4, 2 + fieldIndex * 2);
                target.Emit(OpCodes.Ldelem_Ref);
            }

            void EmitLoadCurrentChar(ILGenerator target)
            {
                target.Emit(OpCodes.Ldarg_0);
                target.Emit(OpCodes.Ldarg_1);
                target.Emit(OpCodes.Ldind_I4);
                target.Emit(OpCodes.Callvirt,
                    _types.GetProperty(_types.String, "Chars").GetGetMethod()!);
            }

            void EmitAdvance(ILGenerator target)
            {
                target.Emit(OpCodes.Ldarg_1);
                target.Emit(OpCodes.Dup);
                target.Emit(OpCodes.Ldind_I4);
                target.Emit(OpCodes.Ldc_I4_1);
                target.Emit(OpCodes.Add);
                target.Emit(OpCodes.Stind_I4);
            }
        }
    }

    private MethodBuilder EmitJsonAssociatedConsumeLiteral(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "ConsumeJsonAssociatedLiteral",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Void,
            [_types.String, _types.Int32.MakeByRefType(), _types.String]);
        method.SetImplementationFlags(
            MethodImplAttributes.AggressiveInlining |
            MethodImplAttributes.AggressiveOptimization);

        var il = method.GetILGenerator();
        var start = il.DeclareLocal(_types.Int32);
        var matched = il.DefineLabel();
        var mismatch = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldind_I4);
        il.Emit(OpCodes.Stloc, start);
        il.Emit(OpCodes.Ldloc, start);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt,
            _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt,
            _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Bgt, mismatch);

        // CompareOrdinal performs the fixed prefix comparison in optimized BCL
        // code instead of issuing one virtual string indexer call per character.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, start);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt,
            _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Call, _types.GetMethod(
            _types.String,
            "CompareOrdinal",
            [_types.String, _types.Int32, _types.String, _types.Int32, _types.Int32]));
        il.Emit(OpCodes.Brfalse, matched);
        il.Emit(OpCodes.Br, mismatch);

        il.MarkLabel(matched);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, start);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Callvirt,
            _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stind_I4);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(mismatch);
        EmitAssociatedMismatch(il);
        return method;
    }

    private MethodBuilder EmitJsonAssociatedReadNumber(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "ReadJsonAssociatedNumber",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.Double,
            [_types.String, _types.Int32.MakeByRefType()]);
        method.SetImplementationFlags(
            MethodImplAttributes.AggressiveInlining |
            MethodImplAttributes.AggressiveOptimization);

        var il = method.GetILGenerator();
        var start = il.DeclareLocal(_types.Int32);
        var index = il.DeclareLocal(_types.Int32);
        var c = il.DeclareLocal(_types.Char);
        var negative = il.DeclareLocal(_types.Boolean);
        var digitCount = il.DeclareLocal(_types.Int32);
        var value = il.DeclareLocal(_types.Double);
        var digitLoop = il.DefineLabel();
        var positiveStart = il.DefineLabel();
        var integerDone = il.DefineLabel();
        var positiveResult = il.DefineLabel();
        var fallbackScan = il.DefineLabel();
        var fallbackDone = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldind_I4);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, start);
        il.Emit(OpCodes.Stloc, index);

        // The shaped serializer emits the benchmark's id/value fields as short
        // integers. Accumulate up to 15 digits directly (the exact decimal
        // integer range); decimal/exponent or longer tokens retain the fully
        // rounded invariant double.Parse fallback.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt,
            _types.GetProperty(_types.String, "Chars").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)'-');
        il.Emit(OpCodes.Bne_Un, positiveStart);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, negative);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.MarkLabel(positiveStart);

        il.MarkLabel(digitLoop);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt,
            _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, integerDone);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt,
            _types.GetProperty(_types.String, "Chars").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, c);
        foreach (char delimiter in new[] { ',', '}', ']' })
        {
            il.Emit(OpCodes.Ldloc, c);
            il.Emit(OpCodes.Ldc_I4, (int)delimiter);
            il.Emit(OpCodes.Beq, integerDone);
        }
        il.Emit(OpCodes.Ldloc, c);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Blt, fallbackScan);
        il.Emit(OpCodes.Ldloc, c);
        il.Emit(OpCodes.Ldc_I4, (int)'9');
        il.Emit(OpCodes.Bgt, fallbackScan);

        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ldc_R8, 10.0);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Ldloc, c);
        il.Emit(OpCodes.Ldc_I4, (int)'0');
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Conv_R8);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Ldloc, digitCount);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, digitCount);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, digitLoop);

        il.MarkLabel(integerDone);
        il.Emit(OpCodes.Ldloc, digitCount);
        il.Emit(OpCodes.Brfalse, fallbackDone);
        il.Emit(OpCodes.Ldloc, digitCount);
        il.Emit(OpCodes.Ldc_I4, 15);
        il.Emit(OpCodes.Bgt, fallbackDone);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Stind_I4);
        il.Emit(OpCodes.Ldloc, negative);
        il.Emit(OpCodes.Brfalse, positiveResult);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Neg);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(positiveResult);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(fallbackScan);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Callvirt,
            _types.GetProperty(_types.String, "Length").GetGetMethod()!);
        il.Emit(OpCodes.Bge, fallbackDone);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt,
            _types.GetProperty(_types.String, "Chars").GetGetMethod()!);
        il.Emit(OpCodes.Stloc, c);
        foreach (char delimiter in new[] { ',', '}', ']' })
        {
            il.Emit(OpCodes.Ldloc, c);
            il.Emit(OpCodes.Ldc_I4, (int)delimiter);
            il.Emit(OpCodes.Beq, fallbackDone);
        }
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Br, fallbackScan);

        il.MarkLabel(fallbackDone);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Stind_I4);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, start);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, start);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Call, typeof(MemoryExtensions).GetMethod(
            "AsSpan", [_types.String, _types.Int32, _types.Int32])!);
        il.Emit(OpCodes.Ldc_I4, (int)NumberStyles.Float);
        il.Emit(OpCodes.Call,
            typeof(CultureInfo).GetProperty("InvariantCulture")!.GetGetMethod()!);
        il.Emit(OpCodes.Call, typeof(double).GetMethod(
            "Parse",
            [typeof(ReadOnlySpan<char>), typeof(NumberStyles), typeof(IFormatProvider)])!);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private MethodBuilder EmitJsonAssociatedReadString(TypeBuilder typeBuilder)
    {
        var method = typeBuilder.DefineMethod(
            "ReadJsonAssociatedString",
            MethodAttributes.Private | MethodAttributes.Static,
            _types.String,
            [_types.String, _types.Int32.MakeByRefType()]);
        method.SetImplementationFlags(
            MethodImplAttributes.AggressiveInlining |
            MethodImplAttributes.AggressiveOptimization);

        MethodInfo deserializeString = typeof(JsonSerializer)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(candidate =>
                candidate.Name == "Deserialize" &&
                candidate.IsGenericMethodDefinition &&
                candidate.GetParameters() is var parameters &&
                parameters.Length == 2 &&
                parameters[0].ParameterType == typeof(string) &&
                parameters[1].ParameterType == typeof(JsonSerializerOptions))
            .MakeGenericMethod(typeof(string));

        var il = method.GetILGenerator();
        var quoteStart = il.DeclareLocal(_types.Int32);
        var contentStart = il.DeclareLocal(_types.Int32);
        var index = il.DeclareLocal(_types.Int32);
        var c = il.DeclareLocal(_types.Char);
        var escaped = il.DeclareLocal(_types.Boolean);
        var loop = il.DefineLabel();
        var escapedLoop = il.DefineLabel();
        var escapedAdvance = il.DefineLabel();
        var setEscaped = il.DefineLabel();
        var escapedDone = il.DefineLabel();
        var plainDone = il.DefineLabel();
        var mismatch = il.DefineLabel();

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldind_I4);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, quoteStart);
        il.Emit(OpCodes.Stloc, index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Callvirt,
            _types.GetProperty(_types.String, "Chars").GetGetMethod()!);
        il.Emit(OpCodes.Ldc_I4, (int)'"');
        il.Emit(OpCodes.Bne_Un, mismatch);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Stloc, contentStart);
        il.Emit(OpCodes.Stloc, index);

        il.MarkLabel(loop);
        EmitReadCharOrMismatch(il, index, c, mismatch);
        il.Emit(OpCodes.Ldloc, c);
        il.Emit(OpCodes.Ldc_I4, (int)'"');
        il.Emit(OpCodes.Beq, plainDone);
        il.Emit(OpCodes.Ldloc, c);
        il.Emit(OpCodes.Ldc_I4, (int)'\\');
        il.Emit(OpCodes.Beq, setEscaped);
        EmitIncrement(il, index);
        il.Emit(OpCodes.Br, loop);

        il.MarkLabel(plainDone);
        EmitStoreNextIndex(il, index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, contentStart);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, contentStart);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Callvirt,
            _types.GetMethod(_types.String, "Substring", [_types.Int32, _types.Int32]));
        il.Emit(OpCodes.Ret);

        // Escapes are uncommon in the measured workload. Scan to the closing
        // quote here, then delegate only that token's unescaping to the BCL.
        il.MarkLabel(escapedLoop);
        EmitReadCharOrMismatch(il, index, c, mismatch);
        il.Emit(OpCodes.Ldloc, escaped);
        il.Emit(OpCodes.Brtrue, escapedAdvance);
        il.Emit(OpCodes.Ldloc, c);
        il.Emit(OpCodes.Ldc_I4, (int)'\\');
        il.Emit(OpCodes.Beq, setEscaped);
        il.Emit(OpCodes.Ldloc, c);
        il.Emit(OpCodes.Ldc_I4, (int)'"');
        il.Emit(OpCodes.Beq, escapedDone);
        il.Emit(OpCodes.Br, escapedAdvance);
        il.MarkLabel(setEscaped);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stloc, escaped);
        EmitIncrement(il, index);
        il.Emit(OpCodes.Br, escapedLoop);
        il.MarkLabel(escapedAdvance);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stloc, escaped);
        EmitIncrement(il, index);
        il.Emit(OpCodes.Br, escapedLoop);

        il.MarkLabel(escapedDone);
        EmitStoreNextIndex(il, index);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, quoteStart);
        il.Emit(OpCodes.Ldloc, index);
        il.Emit(OpCodes.Ldloc, quoteStart);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Callvirt,
            _types.GetMethod(_types.String, "Substring", [_types.Int32, _types.Int32]));
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Call, deserializeString);
        il.Emit(OpCodes.Ret);

        il.MarkLabel(mismatch);
        EmitAssociatedMismatch(il);
        return method;

        void EmitReadCharOrMismatch(
            ILGenerator target,
            LocalBuilder position,
            LocalBuilder destination,
            Label fail)
        {
            target.Emit(OpCodes.Ldloc, position);
            target.Emit(OpCodes.Ldarg_0);
            target.Emit(OpCodes.Callvirt,
                _types.GetProperty(_types.String, "Length").GetGetMethod()!);
            target.Emit(OpCodes.Bge, fail);
            target.Emit(OpCodes.Ldarg_0);
            target.Emit(OpCodes.Ldloc, position);
            target.Emit(OpCodes.Callvirt,
                _types.GetProperty(_types.String, "Chars").GetGetMethod()!);
            target.Emit(OpCodes.Stloc, destination);
        }

        static void EmitIncrement(ILGenerator target, LocalBuilder position)
        {
            target.Emit(OpCodes.Ldloc, position);
            target.Emit(OpCodes.Ldc_I4_1);
            target.Emit(OpCodes.Add);
            target.Emit(OpCodes.Stloc, position);
        }

        static void EmitStoreNextIndex(ILGenerator target, LocalBuilder position)
        {
            target.Emit(OpCodes.Ldarg_1);
            target.Emit(OpCodes.Ldloc, position);
            target.Emit(OpCodes.Ldc_I4_1);
            target.Emit(OpCodes.Add);
            target.Emit(OpCodes.Stind_I4);
        }
    }

    private void EmitAssociatedMismatch(ILGenerator il)
    {
        il.Emit(OpCodes.Ldstr, "JSON shape association mismatch");
        il.Emit(OpCodes.Newobj,
            _types.GetConstructor(_types.Exception, _types.String));
        il.Emit(OpCodes.Throw);
    }
}
