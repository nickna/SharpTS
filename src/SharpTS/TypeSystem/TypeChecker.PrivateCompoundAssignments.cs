using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem.Exceptions;

namespace SharpTS.TypeSystem;

public partial class TypeChecker
{
    private Dictionary<Expr.CompoundSetPrivate, object?> _privateCompoundConstructors =
        new(ReferenceEqualityComparer.Instance);

    private TypeInfo CheckCompoundSetPrivate(Expr.CompoundSetPrivate compound)
    {
        using var memberLookup = BeginSourceMemberLookup(compound, compound.Name, MemberOperation.Read | MemberOperation.Write);
        if (_currentClass is null)
            throw new TypeCheckException($"Cannot access private field '{compound.Name.Lexeme}' outside of a class body.", compound.Name.Line, tsCode: "TS18013");

        TypeInfo receiver = CheckExpr(compound.Object);
        string name = compound.Name.Lexeme;
        var owner = FindLexicalPrivateOwner(name);
        if (owner is null)
            throw new TypeCheckException($"Private field '{name}' does not exist on class '{_currentClass.Name}'.", compound.Name.Line, tsCode: "TS2339");

        bool isStatic = false;
        TypeInfo? fieldType = owner.PrivateFieldTypes.GetValueOrDefault(name);
        if (fieldType is null)
        {
            fieldType = owner.StaticPrivateFieldTypes.GetValueOrDefault(name);
            isStatic = fieldType is not null;
        }
        if (fieldType is null)
        {
            if (owner.PrivateMethodTypes.ContainsKey(name) || owner.StaticPrivateMethodTypes.ContainsKey(name))
                throw new TypeCheckException($"Cannot assign to private method '{name}'. Private methods are not writable.", compound.Name.Line, tsCode: "TS2803");
            throw new TypeCheckException($"Private field '{name}' does not exist on class '{_currentClass.Name}'.", compound.Name.Line, tsCode: "TS2339");
        }

        ValidatePrivateReceiver(receiver, owner, isStatic, compound.Name);

        // Readonly initialization is a syntax/context permission: an alias of
        // this, or an arrow nested in a constructor, does not receive it.
        bool isReadonly = isStatic
            ? owner.Core.StaticReadonlyFields?.Contains(name) == true
            : owner.ReadonlyFields.Contains(name);
        if (isReadonly && (isStatic || !CanInitializeReadonlyPrivateCompound(compound, owner)))
            throw new TypeCheckException($"Cannot assign to '{name}' because it is a read-only property.", compound.Name.Line, tsCode: "TS2540");

        fieldType = InstantiatePrivateCompoundFieldType(fieldType, receiver, owner);
        TypeInfo right = CheckExpr(compound.Value);
        int line = compound.Operator.Line;
        TypeInfo result = compound.Operator.Type switch
        {
            TokenType.PLUS_EQUAL => CheckPlusOperator(fieldType, right, line),
            TokenType.AMPERSAND_EQUAL or TokenType.PIPE_EQUAL or TokenType.CARET_EQUAL
                or TokenType.LESS_LESS_EQUAL or TokenType.GREATER_GREATER_EQUAL => CheckBitwiseBinary(fieldType, right, line),
            TokenType.GREATER_GREATER_GREATER_EQUAL => CheckUnsignedShiftBinary(fieldType, right, line),
            _ => CheckArithmeticBinary(fieldType, right, line)
        };
        if (!IsCompatible(fieldType, result))
            throw new TypeCheckException($"Type '{result}' is not assignable to private field '{name}' of type '{fieldType}'.", line, tsCode: "TS2322");

        ObservePrivateSourceMemberSelection(compound.Name, receiver, owner,
            isStatic ? MemberFacet.PrivateStatic : MemberFacet.PrivateInstance);
        memberLookup?.Succeed();
        return result;
    }

    private TypeInfo InstantiatePrivateCompoundFieldType(TypeInfo fieldType, TypeInfo receiver, TypeInfo.Class owner)
    {
        TypeInfo? current = receiver is TypeInfo.Instance instance ? instance.ResolvedClassType : receiver;
        Dictionary<string, TypeInfo> substitutions = [];
        while (current is not null)
        {
            substitutions = ComposeInheritedClassSubs(current, substitutions);
            if (SelectedSourceClassId(current) == owner.Core.DeclarationId)
                return substitutions.Count == 0 ? fieldType : Substitute(fieldType, substitutions);
            current = GetSuperclass(current);
        }
        return fieldType;
    }

    private bool CanInitializeReadonlyPrivateCompound(Expr.CompoundSetPrivate compound, TypeInfo.Class owner)
    {
        Expr receiver = compound.Object;
        while (receiver is Expr.Grouping grouping) receiver = grouping.Expression;
        if (receiver is not Expr.This) return false;
        if (!_privateCompoundConstructors.TryGetValue(compound, out var classOwner) && _currentModule is not null)
        {
            foreach (var entry in PrivateCompoundConstructorIndex.Collect(_currentModule.Statements))
                _privateCompoundConstructors[entry.Key] = entry.Value;
            _privateCompoundConstructors.TryGetValue(compound, out classOwner);
        }
        if (classOwner is null) return false;
        TypeInfo? constructorClass = classOwner switch
        {
            Stmt.Class declaration => _typeMap.GetClassType(declaration),
            Expr.ClassExpr expression => _typeMap.GetClassExprType(expression),
            _ => null
        };
        constructorClass ??= _newTargetSourceTypes.GetValueOrDefault(classOwner);
        return constructorClass is not null && SelectedSourceClassId(constructorClass) == owner.Core.DeclarationId;
    }

    /// <summary>Constructor permissions do not flow into nested callables or initializers.</summary>
    private sealed class PrivateCompoundConstructorIndex : AstVisitorBase
    {
        private readonly Dictionary<Stmt.Function, object> _constructors = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<Expr.CompoundSetPrivate, object?> _contexts = new(ReferenceEqualityComparer.Instance);
        private object? _constructor;

        internal static Dictionary<Expr.CompoundSetPrivate, object?> Collect(IEnumerable<Stmt> statements)
        {
            var visitor = new PrivateCompoundConstructorIndex();
            foreach (var statement in statements) visitor.Visit(statement);
            return visitor._contexts;
        }

        private void InConstructor(object? owner, Action visit)
        {
            var previous = _constructor;
            _constructor = owner;
            try { visit(); }
            finally { _constructor = previous; }
        }

        protected override void VisitCompoundSetPrivate(Expr.CompoundSetPrivate expr)
        {
            _contexts[expr] = _constructor;
            base.VisitCompoundSetPrivate(expr);
        }

        protected override void VisitFunction(Stmt.Function stmt)
            => InConstructor(_constructors.GetValueOrDefault(stmt), () => base.VisitFunction(stmt));

        protected override void VisitArrowFunction(Expr.ArrowFunction expr)
            => InConstructor(null, () => base.VisitArrowFunction(expr));

        protected override void VisitField(Stmt.Field stmt)
        {
            if (stmt.ComputedKey is { } key) Visit(key);
            if (stmt.Initializer is { } initializer) InConstructor(null, () => Visit(initializer));
        }

        protected override void VisitAccessor(Stmt.Accessor stmt)
            => InConstructor(null, () => base.VisitAccessor(stmt));
        protected override void VisitAutoAccessor(Stmt.AutoAccessor stmt)
            => InConstructor(null, () => base.VisitAutoAccessor(stmt));
        protected override void VisitStaticBlock(Stmt.StaticBlock stmt)
            => InConstructor(null, () => base.VisitStaticBlock(stmt));

        protected override void VisitClass(Stmt.Class stmt)
        {
            foreach (var decorator in stmt.Decorators ?? []) Visit(decorator.Expression);
            VisitClassMembers(stmt, stmt.SuperclassExpr, stmt.Methods, stmt.Fields, stmt.Accessors, stmt.AutoAccessors, stmt.StaticInitializers);
        }

        protected override void VisitClassExpr(Expr.ClassExpr expr)
            => VisitClassMembers(expr, expr.SuperclassExpr, expr.Methods, expr.Fields, expr.Accessors, expr.AutoAccessors, expr.StaticInitializers);

        private void VisitClassMembers(object owner, Expr? parent, List<Stmt.Function> methods,
            List<Stmt.Field> fields, List<Stmt.Accessor>? accessors, List<Stmt.AutoAccessor>? autoAccessors, List<Stmt>? initializers)
        {
            if (parent is not null) Visit(parent);
            foreach (var method in methods)
                if (!method.IsStatic && method.ComputedKey is null && method.Name.Lexeme == "constructor")
                    _constructors[method] = owner;
            foreach (var field in fields) VisitField(field);
            foreach (var method in methods) VisitFunction(method);
            foreach (var accessor in accessors ?? []) VisitAccessor(accessor);
            foreach (var accessor in autoAccessors ?? []) VisitAutoAccessor(accessor);
            foreach (var block in (initializers ?? []).OfType<Stmt.StaticBlock>()) VisitStaticBlock(block);
        }
    }
}
