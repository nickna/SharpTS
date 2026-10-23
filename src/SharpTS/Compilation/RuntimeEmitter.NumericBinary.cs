using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    // Uses only required coercion/error metadata and BCL BigInteger methods.
    // Dynamic numeric operations must also compile when the optional BigInt
    // implementation family is not selected by the source feature detector.
    private void EmitNumericBinary(
        TypeBuilder typeBuilder, EmittedOperatorRuntime operators,
        EmittedNumericCoercionRuntime numeric, EmittedErrorRuntime errors)
    {
        var method = typeBuilder.DefineMethod(
            "NumericBinary", MethodAttributes.Public | MethodAttributes.Static,
            _types.Object, [_types.Object, _types.Object, _types.Int32]);
        operators.NumericBinary = method;
        var il = method.GetILGenerator();
        var left = il.DeclareLocal(_types.Object);
        var right = il.DeclareLocal(_types.Object);

        // Callers have already evaluated both expressions. Finish the left
        // ToNumeric before starting the right, then compare primitive kinds.
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, numeric.ToNumeric);
        il.Emit(OpCodes.Stloc, left);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, numeric.ToNumeric);
        il.Emit(OpCodes.Stloc, right);

        var bigint = il.DefineLabel();
        var mixedKinds = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, left);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brtrue, bigint);
        il.Emit(OpCodes.Ldloc, right);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brtrue, mixedKinds);

        TokenType[] operations =
        [
            TokenType.MINUS, TokenType.STAR, TokenType.SLASH, TokenType.PERCENT,
            TokenType.STAR_STAR, TokenType.AMPERSAND, TokenType.PIPE, TokenType.CARET,
            TokenType.LESS_LESS, TokenType.GREATER_GREATER, TokenType.GREATER_GREATER_GREATER
        ];
        var numberLabels = operations.ToDictionary(op => op, _ => il.DefineLabel());
        EmitDispatch(numberLabels);
        foreach (var op in operations)
        {
            il.MarkLabel(numberLabels[op]);
            bool bitwise = op is TokenType.AMPERSAND or TokenType.PIPE or TokenType.CARET
                or TokenType.LESS_LESS or TokenType.GREATER_GREATER or TokenType.GREATER_GREATER_GREATER;
            EmitNumber(left, bitwise);
            EmitNumber(right, bitwise);
            if (op is TokenType.LESS_LESS or TokenType.GREATER_GREATER or TokenType.GREATER_GREATER_GREATER)
            {
                il.Emit(OpCodes.Ldc_I4, 0x1f);
                il.Emit(OpCodes.And);
            }
            switch (op)
            {
                case TokenType.MINUS: il.Emit(OpCodes.Sub); break;
                case TokenType.STAR: il.Emit(OpCodes.Mul); break;
                case TokenType.SLASH: il.Emit(OpCodes.Div); break;
                case TokenType.PERCENT: il.Emit(OpCodes.Rem); break;
                case TokenType.STAR_STAR:
                    il.Emit(OpCodes.Call, _types.GetMethod(_types.Math, "Pow", _types.Double, _types.Double));
                    break;
                case TokenType.AMPERSAND: il.Emit(OpCodes.And); break;
                case TokenType.PIPE: il.Emit(OpCodes.Or); break;
                case TokenType.CARET: il.Emit(OpCodes.Xor); break;
                case TokenType.LESS_LESS: il.Emit(OpCodes.Shl); break;
                case TokenType.GREATER_GREATER: il.Emit(OpCodes.Shr); break;
                case TokenType.GREATER_GREATER_GREATER:
                    il.Emit(OpCodes.Shr_Un);
                    il.Emit(OpCodes.Conv_U8);
                    break;
            }
            if (bitwise) il.Emit(OpCodes.Conv_R8);
            il.Emit(OpCodes.Box, _types.Double);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(bigint);
        il.Emit(OpCodes.Ldloc, right);
        il.Emit(OpCodes.Isinst, _types.BigInteger);
        il.Emit(OpCodes.Brfalse, mixedKinds);
        var leftBig = il.DeclareLocal(_types.BigInteger);
        var rightBig = il.DeclareLocal(_types.BigInteger);
        il.Emit(OpCodes.Ldloc, left);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        il.Emit(OpCodes.Stloc, leftBig);
        il.Emit(OpCodes.Ldloc, right);
        il.Emit(OpCodes.Unbox_Any, _types.BigInteger);
        il.Emit(OpCodes.Stloc, rightBig);
        var bigintLabels = operations.ToDictionary(op => op, _ => il.DefineLabel());
        var explicitToInt = _types.GetMethods(_types.BigInteger).First(candidate =>
            candidate.Name == "op_Explicit" && candidate.ReturnType == _types.Int32 &&
            candidate.GetParameters().Length == 1 &&
            candidate.GetParameters()[0].ParameterType == _types.BigInteger);
        var sign = _types.GetProperty(_types.BigInteger, "Sign").GetGetMethod()!;
        EmitDispatch(bigintLabels);
        foreach (var op in operations)
        {
            il.MarkLabel(bigintLabels[op]);
            if (op == TokenType.GREATER_GREATER_GREATER)
            {
                GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor,
                    "BigInts have no unsigned right shift");
                continue;
            }
            if (op is TokenType.SLASH or TokenType.PERCENT)
            {
                var nonzero = il.DefineLabel();
                il.Emit(OpCodes.Ldloca, rightBig);
                il.Emit(OpCodes.Call, sign);
                il.Emit(OpCodes.Brtrue, nonzero);
                GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.RangeErrorConstructor, "Division by zero");
                il.MarkLabel(nonzero);
            }
            if (op is TokenType.STAR_STAR or TokenType.LESS_LESS or TokenType.GREATER_GREATER)
                EmitBigIntCountGuard(il, op, leftBig, rightBig, errors.CreateException, errors.RangeErrorConstructor);
            il.Emit(OpCodes.Ldloc, leftBig);
            il.Emit(OpCodes.Ldloc, rightBig);
            bool intRight = op is TokenType.STAR_STAR or TokenType.LESS_LESS or TokenType.GREATER_GREATER;
            if (intRight) il.Emit(OpCodes.Call, explicitToInt);
            string name = op switch
            {
                TokenType.MINUS => "op_Subtraction",
                TokenType.STAR => "op_Multiply",
                TokenType.SLASH => "op_Division",
                TokenType.PERCENT => "op_Modulus",
                TokenType.STAR_STAR => "Pow",
                TokenType.AMPERSAND => "op_BitwiseAnd",
                TokenType.PIPE => "op_BitwiseOr",
                TokenType.CARET => "op_ExclusiveOr",
                TokenType.LESS_LESS => "op_LeftShift",
                TokenType.GREATER_GREATER => "op_RightShift",
                _ => throw new InvalidOperationException("Unsupported numeric operator")
            };
            il.Emit(OpCodes.Call, _types.GetMethod(_types.BigInteger, name,
                _types.BigInteger, intRight ? _types.Int32 : _types.BigInteger));
            il.Emit(OpCodes.Box, _types.BigInteger);
            il.Emit(OpCodes.Ret);
        }

        il.MarkLabel(mixedKinds);
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor,
            "Cannot mix BigInt and other types in operations");

        void EmitDispatch(Dictionary<TokenType, Label> labels)
        {
            foreach (var op in operations)
            {
                il.Emit(OpCodes.Ldarg_2);
                il.Emit(OpCodes.Ldc_I4, (int)op);
                il.Emit(OpCodes.Beq, labels[op]);
            }
            GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor,
                "Unsupported numeric operator");
        }

        void EmitNumber(LocalBuilder operand, bool toInt32)
        {
            il.Emit(OpCodes.Ldloc, operand);
            il.Emit(OpCodes.Unbox_Any, _types.Double);
            if (toInt32) il.Emit(OpCodes.Call, numeric.JsNumberToInt32);
        }
    }

    private void EmitBigIntCountGuard(
        ILGenerator il, TokenType operation, LocalBuilder leftBig, LocalBuilder rightBig,
        MethodInfo createException, ConstructorInfo rangeErrorCtor)
    {
        var sign = _types.GetProperty(_types.BigInteger, "Sign").GetGetMethod()!;
        if (operation == TokenType.STAR_STAR)
        {
            var nonnegative = il.DefineLabel();
            il.Emit(OpCodes.Ldloca, rightBig);
            il.Emit(OpCodes.Call, sign);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Bge, nonnegative);
            GuestErrorEmitter.ThrowError(il, createException, rangeErrorCtor,
                "BigInt exponent must be non-negative");
            il.MarkLabel(nonnegative);
        }
        EmitLargeCountGuard(operation);

        // BCL power/shift methods take an Int32 count. Handle unrepresentable
        // counts before casting, including the finite results of huge shifts
        // toward zero and powers of 0, 1 and -1.
        void EmitLargeCountGuard(TokenType op)
        {
            var upperBound = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, rightBig);
            EmitBigIntInt32(int.MaxValue);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.BigInteger, "op_GreaterThan",
                _types.BigInteger, _types.BigInteger));
            il.Emit(OpCodes.Brfalse, upperBound);
            if (op == TokenType.STAR_STAR)
                EmitHugePower();
            else if (op == TokenType.GREATER_GREATER)
                EmitSaturatedShift();
            else
                EmitHugeLeftShift();
            il.MarkLabel(upperBound);

            if (op == TokenType.STAR_STAR) return; // Negative exponents already rejected.
            var lowerBound = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, rightBig);
            EmitBigIntInt32(-int.MaxValue);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.BigInteger, "op_LessThan",
                _types.BigInteger, _types.BigInteger));
            il.Emit(OpCodes.Brfalse, lowerBound);
            if (op == TokenType.LESS_LESS)
                EmitSaturatedShift();
            else
                EmitHugeLeftShift();
            il.MarkLabel(lowerBound);
        }

        void EmitBigIntInt32(int value)
        {
            il.Emit(OpCodes.Ldc_I4, value);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.BigInteger, _types.Int32));
        }

        void EmitReturnBigInt(string property)
        {
            il.Emit(OpCodes.Call, _types.GetProperty(_types.BigInteger, property).GetGetMethod()!);
            il.Emit(OpCodes.Box, _types.BigInteger);
            il.Emit(OpCodes.Ret);
        }

        void EmitSaturatedShift()
        {
            var negative = il.DefineLabel();
            il.Emit(OpCodes.Ldloca, leftBig);
            il.Emit(OpCodes.Call, sign);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Blt, negative);
            EmitReturnBigInt("Zero");
            il.MarkLabel(negative);
            EmitReturnBigInt("MinusOne");
        }

        void EmitHugeLeftShift()
        {
            var zero = il.DefineLabel();
            il.Emit(OpCodes.Ldloca, leftBig);
            il.Emit(OpCodes.Call, sign);
            il.Emit(OpCodes.Brfalse, zero);
            GuestErrorEmitter.ThrowError(il, createException, rangeErrorCtor,
                "Maximum BigInt size exceeded");
            il.MarkLabel(zero);
            EmitReturnBigInt("Zero");
        }

        void EmitHugePower()
        {
            var nonzero = il.DefineLabel();
            il.Emit(OpCodes.Ldloca, leftBig);
            il.Emit(OpCodes.Call, sign);
            il.Emit(OpCodes.Brtrue, nonzero);
            EmitReturnBigInt("Zero");
            il.MarkLabel(nonzero);
            var notOne = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, leftBig);
            il.Emit(OpCodes.Call, _types.GetProperty(_types.BigInteger, "One").GetGetMethod()!);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.BigInteger, "op_Equality", _types.BigInteger, _types.BigInteger));
            il.Emit(OpCodes.Brfalse, notOne);
            EmitReturnBigInt("One");
            il.MarkLabel(notOne);
            var negativeOne = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, leftBig);
            il.Emit(OpCodes.Call, _types.GetProperty(_types.BigInteger, "MinusOne").GetGetMethod()!);
            il.Emit(OpCodes.Call, _types.GetMethod(_types.BigInteger, "op_Equality", _types.BigInteger, _types.BigInteger));
            il.Emit(OpCodes.Brtrue, negativeOne);
            GuestErrorEmitter.ThrowError(il, createException, rangeErrorCtor,
                "Maximum BigInt size exceeded");
            il.MarkLabel(negativeOne);
            var odd = il.DefineLabel();
            il.Emit(OpCodes.Ldloca, rightBig);
            il.Emit(OpCodes.Call, _types.GetProperty(_types.BigInteger, "IsEven").GetGetMethod()!);
            il.Emit(OpCodes.Brfalse, odd);
            EmitReturnBigInt("One");
            il.MarkLabel(odd);
            EmitReturnBigInt("MinusOne");
        }
    }
}
