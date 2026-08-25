using SharpTS.Parsing;
using SharpTS.Parsing.Visitors;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

public readonly record struct TypedArrayElementLayout(
    int BytesPerElement,
    bool Signed,
    bool IsFloat)
{
    public static bool TryGet(string elementType, out TypedArrayElementLayout layout)
    {
        layout = elementType switch
        {
            "Int8" => new(1, Signed: true, IsFloat: false),
            "Uint8" => new(1, Signed: false, IsFloat: false),
            "Int16" => new(2, Signed: true, IsFloat: false),
            "Uint16" => new(2, Signed: false, IsFloat: false),
            "Int32" => new(4, Signed: true, IsFloat: false),
            "Uint32" => new(4, Signed: false, IsFloat: false),
            "Float32" => new(4, Signed: false, IsFloat: true),
            "Float64" => new(8, Signed: false, IsFloat: true),
            _ => default
        };
        return layout.BytesPerElement != 0;
    }

    public static bool IsSupportedConstructor(string name) =>
        name.EndsWith("Array", StringComparison.Ordinal)
        && TryGet(name[..^5], out _);
}

public readonly record struct TypedArrayHoistCandidate(
    string ElementType,
    bool CanHoistBacking);

/// <summary>
/// Proves the exact local lifetime needed to cache a numeric TypedArray's backing storage, and
/// separately finds loop-invariant receivers whose concrete cast can still be hoisted (#1481).
/// Backing hoisting is deliberately narrower: the receiver must be a function-local value created
/// from a numeric length and used only through numeric index operations or <c>.length</c>. Any escape,
/// alias/view exposure, capture, reassignment, dynamic access, direct eval, or observable constructor
/// use keeps the existing concrete-accessor path.
/// </summary>
public static class TypedArrayHoistAnalyzer
{
    /// <summary>
    /// Marks receiver expressions belonging to exact, locally constructed, non-escaping numeric
    /// TypedArrays. This whole-program proof runs after closure analysis and before IL emission.
    /// </summary>
    public static void Analyze(List<Stmt> program, TypeMap? typeMap, ClosureAnalyzer? closures)
    {
        if (typeMap == null) return;

        var visitor = new StableBackingVisitor(typeMap);
        foreach (var statement in program)
            visitor.Visit(statement);

        if (visitor.ContainsDirectEval || visitor.IntrinsicConstructorIsObservable)
            return;

        foreach (var (key, candidate) in visitor.Candidates)
        {
            if (key.Scope == 0
                || visitor.Disqualified.Contains(key)
                || visitor.DeclarationCounts.GetValueOrDefault(key) != 1
                || closures?.IsVariableCaptured(key.Name) == true)
            {
                continue;
            }

            foreach (var receiver in candidate.PermittedReceivers)
                typeMap.MarkStableTypedArrayBackingReceiver(receiver);
        }
    }

    public static Dictionary<string, TypedArrayHoistCandidate> AnalyzeFor(
        Stmt body, Expr? condition, Expr? increment, TypeMap? typeMap)
    {
        if (typeMap == null) return new();

        var visitor = new TypedArrayAccessVisitor(typeMap);
        visitor.Visit(body);
        if (condition != null) visitor.VisitExpr(condition);
        if (increment != null) visitor.VisitExpr(increment);

        // eval can replace a lexical binding without an Assign node in this AST.
        if (visitor.ContainsDirectEval)
            return new();

        foreach (var reassigned in visitor.Reassigned)
            visitor.Candidates.Remove(reassigned);

        return visitor.Candidates;
    }

    private static bool IsNumber(TypeInfo? type) =>
        type is TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER }
            or TypeInfo.NumberLiteral;

    private sealed class StableBackingCandidate(string elementType)
    {
        public string ElementType { get; } = elementType;
        public HashSet<Expr.Variable> PermittedReceivers { get; } =
            new(ReferenceEqualityComparer.Instance);
    }

    private sealed class StableBackingVisitor(TypeMap typeMap) : AstVisitorBase
    {
        private readonly TypeMap _typeMap = typeMap;
        private int _scope;
        private int _nextScope;

        public Dictionary<(int Scope, string Name), StableBackingCandidate> Candidates { get; } = [];
        public Dictionary<(int Scope, string Name), int> DeclarationCounts { get; } = [];
        public HashSet<(int Scope, string Name)> Disqualified { get; } = [];
        public bool ContainsDirectEval { get; private set; }
        public bool IntrinsicConstructorIsObservable { get; private set; }

        protected override void VisitFunction(Stmt.Function statement)
        {
            if (TypedArrayElementLayout.IsSupportedConstructor(statement.Name.Lexeme))
                IntrinsicConstructorIsObservable = true;
            InScope(() => base.VisitFunction(statement));
        }

        protected override void VisitArrowFunction(Expr.ArrowFunction expression) =>
            InScope(() => base.VisitArrowFunction(expression));

        private void InScope(Action visit)
        {
            int saved = _scope;
            _scope = ++_nextScope;
            visit();
            _scope = saved;
        }

        protected override void VisitVar(Stmt.Var statement) =>
            HandleDeclaration(statement.Name, statement.Initializer);

        protected override void VisitConst(Stmt.Const statement) =>
            HandleDeclaration(statement.Name, statement.Initializer);

        private void HandleDeclaration(Token name, Expr? initializer)
        {
            var key = (_scope, name.Lexeme);
            DeclarationCounts[key] = DeclarationCounts.GetValueOrDefault(key) + 1;

            if (TypedArrayElementLayout.IsSupportedConstructor(name.Lexeme))
                IntrinsicConstructorIsObservable = true;

            if (initializer is Expr.New
                {
                    Callee: Expr.Variable constructor,
                    Arguments: [var length]
                }
                && _typeMap.Get(initializer) is TypeInfo.TypedArray typedArray
                && TypedArrayElementLayout.TryGet(typedArray.ElementType, out _)
                && constructor.Name.Lexeme == typedArray.ElementType + "Array"
                && IsNumber(_typeMap.Get(length)))
            {
                Candidates.TryAdd(key, new StableBackingCandidate(typedArray.ElementType));
            }

            if (initializer != null)
                Visit(initializer);
        }

        protected override void VisitNew(Expr.New expression)
        {
            // Constructing an intrinsic is not itself an observation of the constructor binding.
            if (expression.Callee is Expr.Variable constructor
                && TypedArrayElementLayout.IsSupportedConstructor(constructor.Name.Lexeme))
            {
                foreach (var argument in expression.Arguments)
                    Visit(argument);
                return;
            }

            base.VisitNew(expression);
        }

        protected override void VisitGetIndex(Expr.GetIndex expression)
        {
            if (!expression.Optional
                && expression.Object is Expr.Variable receiver
                && IsNumber(_typeMap.Get(expression.Index))
                && TryPermitReceiver(receiver))
            {
                Visit(expression.Index);
                return;
            }

            base.VisitGetIndex(expression);
        }

        protected override void VisitSetIndex(Expr.SetIndex expression)
        {
            if (expression.Object is Expr.Variable receiver
                && IsNumber(_typeMap.Get(expression.Index))
                && IsNumber(_typeMap.Get(expression.Value))
                && TryPermitReceiver(receiver))
            {
                Visit(expression.Index);
                Visit(expression.Value);
                return;
            }

            base.VisitSetIndex(expression);
        }

        protected override void VisitCompoundSetIndex(Expr.CompoundSetIndex expression)
        {
            if (expression.Object is Expr.Variable receiver
                && IsNumber(_typeMap.Get(expression.Index))
                && IsNumber(_typeMap.Get(expression.Value))
                && IsDirectCompoundOperator(expression.Operator.Type)
                && TryPermitReceiver(receiver))
            {
                Visit(expression.Index);
                Visit(expression.Value);
                return;
            }

            base.VisitCompoundSetIndex(expression);
        }

        protected override void VisitGet(Expr.Get expression)
        {
            if (!expression.Optional
                && expression.Name.Lexeme == "length"
                && expression.Object is Expr.Variable receiver
                && TryPermitReceiver(receiver))
            {
                return;
            }

            base.VisitGet(expression);
        }

        protected override void VisitCall(Expr.Call expression)
        {
            if (!expression.Optional
                && expression.Callee is Expr.Variable { Name.Lexeme: "eval" })
            {
                ContainsDirectEval = true;
            }
            base.VisitCall(expression);
        }

        private bool TryPermitReceiver(Expr.Variable receiver)
        {
            if (_typeMap.Get(receiver) is not TypeInfo.TypedArray typedArray
                || !TypedArrayElementLayout.TryGet(typedArray.ElementType, out _))
            {
                return false;
            }

            var key = (_scope, receiver.Name.Lexeme);
            if (Candidates.TryGetValue(key, out var candidate)
                && candidate.ElementType == typedArray.ElementType)
            {
                candidate.PermittedReceivers.Add(receiver);
            }
            return true;
        }

        protected override void VisitVariable(Expr.Variable expression)
        {
            if (TypedArrayElementLayout.IsSupportedConstructor(expression.Name.Lexeme))
                IntrinsicConstructorIsObservable = true;
            Disqualified.Add((_scope, expression.Name.Lexeme));
        }

        protected override void VisitAssign(Expr.Assign expression)
        {
            NoteAssignment(expression.Name.Lexeme);
            base.VisitAssign(expression);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expression)
        {
            NoteAssignment(expression.Name.Lexeme);
            base.VisitCompoundAssign(expression);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expression)
        {
            NoteAssignment(expression.Name.Lexeme);
            base.VisitLogicalAssign(expression);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expression)
        {
            if (expression.Operand is Expr.Variable variable)
                NoteAssignment(variable.Name.Lexeme);
            base.VisitPrefixIncrement(expression);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expression)
        {
            if (expression.Operand is Expr.Variable variable)
                NoteAssignment(variable.Name.Lexeme);
            base.VisitPostfixIncrement(expression);
        }

        private void NoteAssignment(string name)
        {
            if (TypedArrayElementLayout.IsSupportedConstructor(name))
                IntrinsicConstructorIsObservable = true;
            Disqualified.Add((_scope, name));
        }

        private static bool IsDirectCompoundOperator(TokenType op) => op is
            TokenType.PLUS_EQUAL or TokenType.MINUS_EQUAL or TokenType.STAR_EQUAL or
            TokenType.SLASH_EQUAL or TokenType.PERCENT_EQUAL or TokenType.AMPERSAND_EQUAL or
            TokenType.PIPE_EQUAL or TokenType.CARET_EQUAL or TokenType.LESS_LESS_EQUAL or
            TokenType.GREATER_GREATER_EQUAL;
    }

    private sealed class TypedArrayAccessVisitor(TypeMap typeMap) : AstVisitorBase
    {
        private readonly TypeMap _typeMap = typeMap;

        public Dictionary<string, TypedArrayHoistCandidate> Candidates { get; } = [];
        public HashSet<string> Reassigned { get; } = [];
        public bool ContainsDirectEval { get; private set; }

        public void VisitExpr(Expr expr) => Visit(expr);

        protected override void VisitGetIndex(Expr.GetIndex expr)
        {
            TryRegister(expr.Object);
            base.VisitGetIndex(expr);
        }

        protected override void VisitSetIndex(Expr.SetIndex expr)
        {
            TryRegister(expr.Object);
            base.VisitSetIndex(expr);
        }

        protected override void VisitCompoundSetIndex(Expr.CompoundSetIndex expr)
        {
            TryRegister(expr.Object);
            base.VisitCompoundSetIndex(expr);
        }

        protected override void VisitAssign(Expr.Assign expr)
        {
            Reassigned.Add(expr.Name.Lexeme);
            base.VisitAssign(expr);
        }

        protected override void VisitCompoundAssign(Expr.CompoundAssign expr)
        {
            Reassigned.Add(expr.Name.Lexeme);
            base.VisitCompoundAssign(expr);
        }

        protected override void VisitLogicalAssign(Expr.LogicalAssign expr)
        {
            Reassigned.Add(expr.Name.Lexeme);
            base.VisitLogicalAssign(expr);
        }

        protected override void VisitPrefixIncrement(Expr.PrefixIncrement expr)
        {
            if (expr.Operand is Expr.Variable variable)
                Reassigned.Add(variable.Name.Lexeme);
            base.VisitPrefixIncrement(expr);
        }

        protected override void VisitPostfixIncrement(Expr.PostfixIncrement expr)
        {
            if (expr.Operand is Expr.Variable variable)
                Reassigned.Add(variable.Name.Lexeme);
            base.VisitPostfixIncrement(expr);
        }

        protected override void VisitCall(Expr.Call expr)
        {
            if (!expr.Optional && expr.Callee is Expr.Variable { Name.Lexeme: "eval" })
                ContainsDirectEval = true;
            base.VisitCall(expr);
        }

        protected override void VisitVar(Stmt.Var stmt)
        {
            Reassigned.Add(stmt.Name.Lexeme);
            base.VisitVar(stmt);
        }

        protected override void VisitConst(Stmt.Const stmt)
        {
            Reassigned.Add(stmt.Name.Lexeme);
            base.VisitConst(stmt);
        }

        private void TryRegister(Expr receiver)
        {
            if (receiver is not Expr.Variable variable
                || _typeMap.Get(receiver) is not TypeInfo.TypedArray typedArray)
            {
                return;
            }

            bool stableBacking = _typeMap.IsStableTypedArrayBackingReceiver(variable);
            if (Candidates.TryGetValue(variable.Name.Lexeme, out var current))
            {
                // Every access in the loop must belong to the same whole-program proof.
                Candidates[variable.Name.Lexeme] = current with
                {
                    CanHoistBacking = current.CanHoistBacking && stableBacking
                };
                return;
            }

            Candidates[variable.Name.Lexeme] = new(typedArray.ElementType, stableBacking);
        }
    }
}
