using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public partial class ILEmitter
{
    private const long MaxSafeInteger = 9_007_199_254_740_991L;

    private sealed record StableObjectDestructureReduction(
        Expr.Variable Source,
        Expr.Variable Bound,
        string Accumulator,
        IReadOnlyList<FieldBuilder> Fields);

    /// <summary>
    /// Versions a side-effect-free stable-record reduction such as
    /// <c>const { x, y } = point; sum = sum + x + y</c>. V8 speculates this
    /// common integer case; retaining two dependent floating-point additions
    /// otherwise leaves the object-destructure benchmark behind Node even after
    /// its property loads are fully typed.
    /// </summary>
    /// <remarks>
    /// The fast path accepts only non-negative safe integers and proves the
    /// complete result remains a safe integer before entering the loop. It can
    /// therefore combine the invariant destructured terms once and use one
    /// native integer addition per iteration without changing Number rounding.
    /// Every other value or receiver shape branches to the ordinary emitted
    /// loop, preserving fractional arithmetic, getters, proxies, NaN/infinity,
    /// negative zero, overflow rounding, and runtime cancellation.
    /// </remarks>
    private bool TryEmitStableObjectDestructureReduction(Stmt.For loop, string counterName)
    {
        if (_ctx.ExceptionBlockDepth != 0
            || _ctx.RuntimeFeatures?.UsesDynamicPropertyDescriptors == true
            || _ctx.RuntimeFeatures is not { } features
            || _ctx.Runtime is not { } runtime
            || loop.Initializer is not Stmt.Var counterDeclaration
            || counterDeclaration.Name.Lexeme != counterName
            || !TryGetIntegerCounterInit(counterDeclaration.Initializer, out long initialCounter)
            || initialCounter != 0
            || loop.Increment is not Expr.PostfixIncrement
            {
                Operator.Type: TokenType.PLUS_PLUS,
                Operand: Expr.Variable incrementCounter
            }
            || incrementCounter.Name.Lexeme != counterName
            || loop.Condition is not Expr.Binary
            {
                Operator.Type: TokenType.LESS,
                Left: Expr.Variable conditionCounter,
                Right: Expr.Variable bound
            }
            || conditionCounter.Name.Lexeme != counterName
            || !_ctx.TryGetParameterType(bound.Name.Lexeme, out var boundType)
            || boundType != _ctx.Types.Double
            || !TryAnalyzeStableObjectDestructureReduction(
                loop.Body, bound, out var reduction)
            || _ctx.Locals.GetLocal(reduction.Source.Name.Lexeme) is not { } sourceLocal
            || _ctx.Locals.GetLocal(reduction.Accumulator) is not { } accumulatorDouble
            || accumulatorDouble.LocalType != _ctx.Types.Double)
        {
            return false;
        }

        _ctx.Locals.EnterScope();
        EmitStatement(loop.Initializer);
        var counter = _ctx.Locals.GetLocal(counterName)!;
        var exactSource = IL.DeclareLocal(reduction.Fields[0].DeclaringType!);
        var boundDouble = IL.DeclareLocal(_ctx.Types.Double);
        var boundInteger = IL.DeclareLocal(_ctx.Types.Int64);
        var accumulatorInteger = IL.DeclareLocal(_ctx.Types.Int64);
        var incrementInteger = IL.DeclareLocal(_ctx.Types.Int64);
        var termDouble = IL.DeclareLocal(_ctx.Types.Double);

        var slowStart = IL.DefineLabel();
        var fastStart = IL.DefineLabel();
        var fastContinue = IL.DefineLabel();
        var fastEnd = IL.DefineLabel();
        var incrementReady = IL.DefineLabel();
        var end = IL.DefineLabel();

        _ctx.EnterLoop(end, fastContinue);

        IL.Emit(OpCodes.Ldloc, sourceLocal);
        IL.Emit(OpCodes.Isinst, reduction.Fields[0].DeclaringType!);
        IL.Emit(OpCodes.Stloc, exactSource);
        IL.Emit(OpCodes.Ldloc, exactSource);
        IL.Emit(OpCodes.Brfalse, slowStart);

        EmitExpressionAsDouble(reduction.Bound);
        IL.Emit(OpCodes.Stloc, boundDouble);
        EmitExactIntegerGuard(boundDouble, 0d, MaxSafeInteger, slowStart);
        IL.Emit(OpCodes.Ldloc, boundDouble);
        IL.Emit(OpCodes.Conv_I8);
        IL.Emit(OpCodes.Stloc, boundInteger);

        EmitExactIntegerGuard(accumulatorDouble, 0d, MaxSafeInteger, slowStart);
        var accumulatorNotZero = IL.DefineLabel();
        IL.Emit(OpCodes.Ldloc, accumulatorDouble);
        IL.Emit(OpCodes.Ldc_R8, 0d);
        IL.Emit(OpCodes.Bne_Un, accumulatorNotZero);
        IL.Emit(OpCodes.Ldloc, accumulatorDouble);
        IL.Emit(OpCodes.Call, typeof(BitConverter).GetMethod(
            nameof(BitConverter.DoubleToInt64Bits), [_ctx.Types.Double])!);
        IL.Emit(OpCodes.Ldc_I8, 0L);
        IL.Emit(OpCodes.Blt, slowStart);
        IL.MarkLabel(accumulatorNotZero);
        IL.Emit(OpCodes.Ldloc, accumulatorDouble);
        IL.Emit(OpCodes.Conv_I8);
        IL.Emit(OpCodes.Stloc, accumulatorInteger);

        IL.Emit(OpCodes.Ldc_I4_0);
        IL.Emit(OpCodes.Conv_I8);
        IL.Emit(OpCodes.Stloc, incrementInteger);
        foreach (var field in reduction.Fields)
        {
            IL.Emit(OpCodes.Ldloc, exactSource);
            IL.Emit(OpCodes.Ldfld, field);
            IL.Emit(OpCodes.Stloc, termDouble);
            EmitExactIntegerGuard(termDouble, 0d, MaxSafeInteger, slowStart);
            IL.Emit(OpCodes.Ldloc, incrementInteger);
            IL.Emit(OpCodes.Ldloc, termDouble);
            IL.Emit(OpCodes.Conv_I8);
            IL.Emit(OpCodes.Add);
            IL.Emit(OpCodes.Stloc, incrementInteger);
            IL.Emit(OpCodes.Ldloc, incrementInteger);
            IL.Emit(OpCodes.Ldc_I8, MaxSafeInteger);
            IL.Emit(OpCodes.Bgt, slowStart);
        }

        // Prove every integer addition in the loop remains exactly representable.
        // A zero increment needs no division and leaves the accumulator unchanged.
        IL.Emit(OpCodes.Ldloc, incrementInteger);
        IL.Emit(OpCodes.Brfalse, incrementReady);
        IL.Emit(OpCodes.Ldc_I8, MaxSafeInteger);
        IL.Emit(OpCodes.Ldloc, accumulatorInteger);
        IL.Emit(OpCodes.Sub);
        IL.Emit(OpCodes.Ldloc, incrementInteger);
        IL.Emit(OpCodes.Div);
        IL.Emit(OpCodes.Ldloc, boundInteger);
        IL.Emit(OpCodes.Blt, slowStart);
        IL.MarkLabel(incrementReady);

        IL.Emit(OpCodes.Br, fastStart);
        IL.MarkLabel(fastStart);
        EmitCancellationCheckWithInt64AccumulatorFlush(
            accumulatorDouble, accumulatorInteger);
        IL.Emit(OpCodes.Ldloc, counter);
        IL.Emit(OpCodes.Ldloc, boundInteger);
        IL.Emit(OpCodes.Bge, fastEnd);
        IL.Emit(OpCodes.Ldloc, accumulatorInteger);
        IL.Emit(OpCodes.Ldloc, incrementInteger);
        IL.Emit(OpCodes.Add);
        IL.Emit(OpCodes.Stloc, accumulatorInteger);

        IL.MarkLabel(fastContinue);
        IL.Emit(OpCodes.Ldloc, counter);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Conv_I8);
        IL.Emit(OpCodes.Add);
        IL.Emit(OpCodes.Stloc, counter);
        IL.Emit(OpCodes.Br, fastStart);

        IL.MarkLabel(fastEnd);
        EmitInt64AccumulatorStore(accumulatorDouble, accumulatorInteger);
        IL.Emit(OpCodes.Br, end);

        IL.MarkLabel(slowStart);
        EmitCancellationCheck();
        EmitConditionCheck(loop.Condition);
        IL.Emit(OpCodes.Brfalse, end);
        EmitStatement(loop.Body);
        EmitExpression(loop.Increment);
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Br, slowStart);

        IL.MarkLabel(end);
        _ctx.ExitLoop();
        _ctx.Locals.ExitScope();
        SetStackUnknown();
        return true;

        bool TryAnalyzeStableObjectDestructureReduction(
            Stmt body,
            Expr.Variable loopBound,
            out StableObjectDestructureReduction result)
        {
            result = null!;
            if (body is not Stmt.Block
                {
                    Statements:
                    [
                        Stmt.Sequence { Statements: var destructure },
                        Stmt.Expression
                        {
                            Expr: Expr.Assign
                            {
                                Name: var accumulator,
                                Value: var addition
                            }
                        }
                    ]
                }
                || destructure.Count < 2
                || destructure[0] is not Stmt.Var
                {
                    Initializer: Expr.Variable source,
                    DestructuringSource: DestructuringSourceKind.Object
                } sourceDeclaration
                || !TryFlattenAddition(addition, out var terms)
                || terms.Count != destructure.Count
                || terms[0].Name.Lexeme != accumulator.Lexeme)
            {
                return false;
            }

            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int index = 1; index < destructure.Count; index++)
            {
                if (destructure[index] is not Stmt.Var
                    {
                        Name: var binding,
                        Initializer: Expr.Get
                        {
                            Object: Expr.Variable receiver,
                            Name: var property,
                            Optional: false,
                            Defaulted: false
                        }
                    }
                    || receiver.Name.Lexeme != sourceDeclaration.Name.Lexeme
                    || !properties.TryAdd(binding.Lexeme, property.Lexeme))
                {
                    return false;
                }
            }

            if (_ctx.TypeMap?.Get(sourceDeclaration.Initializer!) is not { } sourceType
                || !ILCompiler.TryGetCompactRecordShape(sourceType, out var shape))
            {
                return false;
            }

            string fingerprint = JsonSerializationShapeAnalyzer.Fingerprint(shape);
            if (!features.CanAssumeCompactObjectRecordIsUnmaterialized(fingerprint)
                || !runtime.CompactObjectRecordTypes.TryGetValue(
                    fingerprint, out var carrierType))
            {
                return false;
            }

            var fields = new List<FieldBuilder>(terms.Count - 1);
            var usedBindings = new HashSet<string>(StringComparer.Ordinal);
            for (int termIndex = 1; termIndex < terms.Count; termIndex++)
            {
                string binding = terms[termIndex].Name.Lexeme;
                if (!properties.TryGetValue(binding, out string? property)
                    || !usedBindings.Add(binding))
                {
                    return false;
                }

                int fieldIndex = -1;
                for (int index = 0; index < shape.Fields.Count; index++)
                {
                    if (shape.Fields[index].Key == property)
                    {
                        fieldIndex = index;
                        break;
                    }
                }

                if (fieldIndex < 0
                    || !runtime.CompactObjectRecordValueFields.TryGetValue(
                        (fingerprint, fieldIndex), out var field)
                    || field.DeclaringType != carrierType
                    || field.FieldType != _ctx.Types.Double)
                {
                    return false;
                }
                fields.Add(field);
            }

            if (usedBindings.Count != properties.Count)
                return false;

            result = new StableObjectDestructureReduction(
                source, loopBound, accumulator.Lexeme, fields);
            return true;
        }
    }

    private static bool TryFlattenAddition(
        Expr expression,
        out List<Expr.Variable> terms)
    {
        var flattened = new List<Expr.Variable>();
        bool success = Visit(expression);
        terms = flattened;
        return success;

        bool Visit(Expr current)
        {
            if (current is Expr.Binary
                {
                    Operator.Type: TokenType.PLUS,
                    Left: var left,
                    Right: var right
                })
            {
                return Visit(left) && Visit(right);
            }

            if (current is not Expr.Variable variable)
                return false;
            flattened.Add(variable);
            return true;
        }
    }
}
