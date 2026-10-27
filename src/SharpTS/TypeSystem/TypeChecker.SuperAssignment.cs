using SharpTS.Parsing;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private TypeInfo CheckSetSuper(Expr.SetSuper set)
    {
        var name = set.Target.Method!;
        CaptureEditorSuperReceiver(set.Target);
        using var lookup = BeginSourceMemberLookup(set, name, MemberOperation.Write);
        if (_currentClass?.Superclass is not { } superclass)
            throw new TypeCheckException("Cannot use 'super' without a superclass.", name.Line, tsCode: "TS2335");
        if (superclass is TypeInfo.MutableClass)
            return CheckExpr(set.Value);

        bool isStatic = _inStaticMethod || _inStaticBlock;
        TypeInfo? current = superclass;
        Dictionary<string, TypeInfo> substitutions = [];
        while (current != null)
        {
            if (current is TypeInfo.InstantiatedGeneric
                { GenericDefinition: TypeInfo.GenericClass generic } instantiated)
            {
                var arguments = instantiated.TypeArguments
                    .Select(argument => SubstituteConstructorMember(argument, substitutions)).ToList();
                substitutions = GenericClassSubs(generic, arguments);
            }
            var setters = isStatic ? GetStaticSetters(current) : GetSetters(current);
            TypeInfo? setter = setters?.GetValueOrDefault(name.Lexeme);
            bool accessor = isStatic
                ? TryGetAccessorSetterAccess(current, true, name.Lexeme, out _)
                : GetGetters(current)?.ContainsKey(name.Lexeme) == true || setter != null;
            TypeInfo? writeType = setter ?? (isStatic
                ? GetStaticProperties(current)?.GetValueOrDefault(name.Lexeme)
                    ?? GetStaticMethods(current)?.GetValueOrDefault(name.Lexeme)
                : GetMethods(current)?.GetValueOrDefault(name.Lexeme));
            if (accessor && setter == null)
                throw new TypeCheckException($"Cannot assign to '{name.Lexeme}' because it is a read-only property.",
                    name.Line, tsCode: "TS2540");
            if (writeType != null)
            {
                if (accessor)
                    EnforceAccessorAccess(current, isStatic ? _currentClass : new TypeInfo.Instance(_currentClass),
                        name, isStatic, isWrite: true);
                else if (isStatic)
                    EnforceStaticMemberAccess(current, name);
                else if (GetMethodAccess(current)?.GetValueOrDefault(name.Lexeme) == AccessModifier.Private)
                    throw new TypeCheckException($"Property '{name.Lexeme}' is private.", name.Line, tsCode: "TS2341");
                if (isStatic && ClassInfoAccessor.Get(current, c => c.Core.StaticReadonlyFields,
                        c => c.Core.StaticReadonlyFields)?.Contains(name.Lexeme) == true)
                    throw new TypeCheckException($"Cannot assign to '{name.Lexeme}' because it is a read-only property.",
                        name.Line, tsCode: "TS2540");
                if (substitutions.Count != 0) writeType = SubstituteConstructorMember(writeType, substitutions);
                TypeInfo? readType = set.Operator is null ? null : CheckSuper(set.Target);
                TypeInfo valueType = CheckExpr(set.Value);
                bool logical = set.Operator?.Type is TokenType.AND_AND_EQUAL or TokenType.OR_OR_EQUAL or TokenType.QUESTION_QUESTION_EQUAL;
                if (set.Operator is { } op && !logical)
                    valueType = op.Type switch
                    {
                        TokenType.PLUS_EQUAL => CheckPlusOperator(readType!, valueType, op.Line),
                        TokenType.GREATER_GREATER_GREATER_EQUAL => CheckUnsignedShiftBinary(readType!, valueType, op.Line),
                        _ => CheckArithmeticBinary(readType!, valueType, op.Line),
                    };
                if (!IsCompatible(writeType, valueType))
                    throw new TypeCheckException($"Cannot assign '{valueType}' to property '{name.Lexeme}' of type '{writeType}'.",
                        name.Line, tsCode: "TS2322");
                ObserveSourceMemberSelection(name, current, isStatic ? MemberFacet.Static : MemberFacet.Instance);
                lookup?.Succeed();
                return logical ? CreateUnion(set.Operator!.Type == TokenType.QUESTION_QUESTION_EQUAL
                    ? ExcludeNullUndefined(readType!) : readType!, valueType) : valueType;
            }
            if (!isStatic && GetFieldTypes(current)?.ContainsKey(name.Lexeme) == true)
                throw new TypeCheckException($"Class field '{name.Lexeme}' is not accessible via super.",
                    name.Line, tsCode: "TS2855");
            current = GetSuperclass(current);
        }
        throw new TypeCheckException($"Property '{name.Lexeme}' does not exist on the superclass.", name.Line, tsCode: "TS2339");
    }
}
