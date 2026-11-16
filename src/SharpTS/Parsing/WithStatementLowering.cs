namespace SharpTS.Parsing;

internal sealed record EvalObjectEnvironment(Token Storage, HashSet<string> MaskedNames);

// Keeping the object environment in a lexical temporary makes its lifetime follow the
// existing closure and suspension machinery. Rewrite references, rather than values:
// choosing a binding must precede the RHS/arguments and calls retain their receiver.
internal sealed class WithStatementLowering(Token storage, SpanTable spans, Func<Token> fresh)
{
    private static long _evalRewriteId;

    internal static List<Stmt> ApplyEvalEnvironments(List<Stmt> statements,
        IReadOnlyList<EvalObjectEnvironment>? environments)
    {
        if (environments is null) return statements;
        long rewriteId = Interlocked.Increment(ref _evalRewriteId);
        int temporary = 0;
        foreach (var environment in environments)
        {
            var lowering = new WithStatementLowering(environment.Storage, new SpanTable(), () =>
                new Token(TokenType.IDENTIFIER, $"<with:eval:{rewriteId}:{temporary++}>", null, environment.Storage.Line)
                { IsGeneratedTemporary = true });
            lowering._scopes.Push(environment.MaskedNames);
            statements = statements.Select(lowering.Rewrite).ToList();
        }
        return statements;
    }
    internal const string ToObject = "<with:object>";
    internal const string HasBinding = "<with:has>";
    internal const string CheckGet = "<with:get>";
    internal const string SetBinding = "<with:set>";
    internal const string CaptureCall = "<with:call-reference>";
    internal const string IsIntrinsicEval = "<with:is-intrinsic-eval>";
    internal const string CaptureLexicalCall = "<with:lexical-call-reference>";
    private readonly Stack<HashSet<string>> _scopes = new();
    private bool _strict;

    internal static Expr.Call Helper(string name, Token origin, params Expr[] args) => new(
        new Expr.Variable(new Token(TokenType.IDENTIFIER, name, null, origin.Line)
            { IsGeneratedTemporary = true }), origin, null, [.. args]);

    private bool IsDynamic(Token name) => !name.IsGeneratedTemporary
        && !_scopes.Any(scope => scope.Contains(name.Lexeme));

    private Expr Resolve(Token name, Func<Expr.Get, Expr> property, Expr fallback) =>
        !IsDynamic(name) ? fallback : new Expr.Ternary(
            Helper(HasBinding, name, new Expr.Variable(storage), new Expr.Literal(name.Lexeme)),
            property(new Expr.Get(new Expr.Variable(storage), name)), fallback);

    private Expr Read(Expr.Get property, Func<Expr.Get, Expr>? operation = null, Expr? missing = null) =>
        new Expr.Ternary(Helper(CheckGet, property.Name, property.Object,
            new Expr.Literal(property.Name.Lexeme), new Expr.Literal(_strict)),
            operation is null ? property : operation(property),
            missing ?? new Expr.Literal(SharpTS.Runtime.Types.SharpTSUndefined.Instance));

    private Expr Write(Expr.Get property, Expr value)
    {
        // The RHS can suspend. Keep the receiver/name off the evaluation stack
        // until it has completed; the chosen environment already lives in storage.
        Token assigned = fresh();
        return new Expr.DestructuringAssign([
            new Stmt.Var(assigned, null, value),
            // Keep the completed store separate from the suspended value; the
            // helper emitter guards setter exceptions after resumption.
            new Stmt.Expression(Helper(SetBinding, property.Name, property.Object,
                new Expr.Literal(property.Name.Lexeme), new Expr.Variable(assigned), new Expr.Literal(_strict)))
        ], new Expr.Variable(assigned));
    }

    private T InStrictContext<T>(bool strict, Func<T> rewrite)
    {
        bool previous = _strict;
        _strict |= strict;
        try { return rewrite(); }
        finally { _strict = previous; }
    }

    private static bool HasStrictDirective(List<Stmt>? body) =>
        body?.Any(statement => statement is Stmt.Directive { Value: "use strict" }) == true;

    private static Expr.Variable? Reference(Expr expression) => expression switch
    {
        Expr.Variable variable => variable,
        Expr.Grouping grouping => Reference(grouping.Expression),
        Expr.NonNullAssertion assertion => Reference(assertion.Expression),
        _ => null
    };

    private T InScope<T>(HashSet<string> names, Func<T> rewrite)
    {
        _scopes.Push(names);
        try { return rewrite(); }
        finally { _scopes.Pop(); }
    }

    private static HashSet<string> Bindings(IEnumerable<Stmt> statements, bool includeVar = false)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (Stmt statement in statements)
        {
            switch (statement)
            {
                case Stmt.Var variable when includeVar || !variable.IsVar: names.Add(variable.Name.Lexeme); break;
                case Stmt.Const constant: names.Add(constant.Name.Lexeme); break;
                case Stmt.Function function: names.Add(function.Name.Lexeme); break;
                case Stmt.Class klass: names.Add(klass.Name.Lexeme); break;
                case Stmt.Using resource:
                    foreach (var binding in resource.Bindings)
                        if (binding.Name is { } name) names.Add(name.Lexeme);
                    break;
                case Stmt.Sequence sequence: names.UnionWith(Bindings(sequence.Statements, includeVar)); break;
            }
        }
        return names;
    }

    private List<Stmt> Statements(List<Stmt> statements, Token? extra = null)
    {
        var names = Bindings(statements);
        if (extra is not null) names.Add(extra.Lexeme);
        return InScope(names, () => statements.Select(Rewrite).ToList());
    }

    public Stmt Rewrite(Stmt statement)
    {
        Stmt result = statement switch
        {
            Stmt.Expression s => s with { Expr = Expression(s.Expr) },
            Stmt.Block s => s with { Statements = Statements(s.Statements) },
            Stmt.Sequence s => s with { Statements = s.Statements.Select(Rewrite).ToList() },
            Stmt.Var { IsVar: true, Initializer: not null } s when IsDynamic(s.Name) => new Stmt.Sequence([
                s with { Initializer = null },
                new Stmt.Expression(Expression(new Expr.Assign(s.Name, s.Initializer))) ]),
            Stmt.Var s => s with { Initializer = Optional(s.Initializer) },
            Stmt.Const s => s with { Initializer = Expression(s.Initializer) },
            Stmt.Return s => s with { Value = Optional(s.Value) },
            Stmt.Throw s => s with { Value = Expression(s.Value) },
            Stmt.If s => s with { Condition = Expression(s.Condition), ThenBranch = Rewrite(s.ThenBranch), ElseBranch = s.ElseBranch is null ? null : Rewrite(s.ElseBranch) },
            Stmt.While s => s with { Condition = Expression(s.Condition), Body = Rewrite(s.Body) },
            Stmt.DoWhile s => s with { Condition = Expression(s.Condition), Body = Rewrite(s.Body) },
            Stmt.For s => InScope(Bindings(s.Initializer is null ? [] : [s.Initializer]), () => s with {
                Initializer = s.Initializer is null ? null : Rewrite(s.Initializer),
                Condition = Optional(s.Condition), Increment = Optional(s.Increment), Body = Rewrite(s.Body) }),
            Stmt.ForOf s => ForOf(s),
            Stmt.ForIn s => ForIn(s),
            Stmt.LabeledStatement s => s with { Statement = Rewrite(s.Statement) },
            Stmt.Switch s => Switch(s),
            Stmt.TryCatch s => s with { TryBlock = Statements(s.TryBlock),
                CatchBlock = s.CatchBlock is null ? null : Statements(s.CatchBlock, s.CatchParam),
                FinallyBlock = s.FinallyBlock is null ? null : Statements(s.FinallyBlock) },
            Stmt.Function s => Function(s),
            Stmt.Class s => Class(s),
            Stmt.StaticBlock s => StaticBlock(s),
            Stmt.Field s => s with { Initializer = Optional(s.Initializer), ComputedKey = Optional(s.ComputedKey) },
            Stmt.AutoAccessor s => s with { Initializer = Optional(s.Initializer) },
            Stmt.Accessor s => Accessor(s),
            Stmt.Using s => s with { Bindings = s.Bindings.Select(binding => binding with {
                Initializer = Expression(binding.Initializer) }).ToList() },
            _ => statement
        };
        spans.CopySpan(statement, result);
        return result;
    }

    private Stmt Switch(Stmt.Switch statement)
    {
        Expr subject = Expression(statement.Subject);
        return InScope(Bindings(statement.Clauses.SelectMany(c => c.Body)), () => statement with {
            Subject = subject, Clauses = statement.Clauses.Select(c => c with {
                Value = Optional(c.Value), Body = c.Body.Select(Rewrite).ToList() }).ToList() });
    }

    private Stmt ForOf(Stmt.ForOf loop)
    {
        if (loop.IsDeclaration && !loop.IsVar)
            return InScope(new(StringComparer.Ordinal) { loop.Variable.Lexeme },
                () => loop with { Iterable = Expression(loop.Iterable), Body = Rewrite(loop.Body) });
        Expr source = Expression(loop.Iterable);
        if (!IsDynamic(loop.Variable)) return loop with { Iterable = source, Body = Rewrite(loop.Body) };
        Token value = fresh();
        Stmt rewritten = loop with { Variable = value, Iterable = source, IsDeclaration = true, IsVar = false,
            Body = new Stmt.Block([new Stmt.Expression(Expression(new Expr.Assign(loop.Variable, new Expr.Variable(value)))), Rewrite(loop.Body)]) };
        return loop.IsVar ? new Stmt.Sequence([new Stmt.Var(loop.Variable, null, null, IsVar: true), rewritten]) : rewritten;
    }

    private Stmt ForIn(Stmt.ForIn loop)
    {
        if (loop.IsDeclaration && !loop.IsVar)
            return InScope(new(StringComparer.Ordinal) { loop.Variable.Lexeme },
                () => loop with { Object = Expression(loop.Object), Body = Rewrite(loop.Body) });
        Expr source = Expression(loop.Object);
        if (!IsDynamic(loop.Variable)) return loop with { Object = source, Body = Rewrite(loop.Body) };
        Token value = fresh();
        Stmt rewritten = loop with { Variable = value, Object = source, IsDeclaration = true, IsVar = false,
            Body = new Stmt.Block([new Stmt.Expression(Expression(new Expr.Assign(loop.Variable, new Expr.Variable(value)))), Rewrite(loop.Body)]) };
        return loop.IsVar ? new Stmt.Sequence([new Stmt.Var(loop.Variable, null, null, IsVar: true), rewritten]) : rewritten;
    }

    private HashSet<string> FunctionBindings(List<Stmt.Parameter> parameters, List<Stmt>? body, Token? name, bool ownArguments)
    {
        var names = Bindings(body ?? [], includeVar: true);
        names.UnionWith(parameters.Select(p => p.Name.Lexeme));
        if (name is not null) names.Add(name.Lexeme);
        if (ownArguments) names.Add("arguments");
        return names;
    }

    private List<Stmt.Parameter> Parameters(List<Stmt.Parameter> parameters) =>
        parameters.Select(p => p with { DefaultValue = Optional(p.DefaultValue) }).ToList();

    private Stmt.Function Function(Stmt.Function function, bool bindName = true)
    {
        Expr? key = Optional(function.ComputedKey);
        return InStrictContext(HasStrictDirective(function.Body), () => InScope(FunctionBindings(function.Parameters, null, bindName ? function.Name : null, true), () => function with {
            ComputedKey = key, Parameters = Parameters(function.Parameters),
            Body = function.Body is null ? null : InScope(Bindings(function.Body, includeVar: true), () => function.Body.Select(Rewrite).ToList()) }));
    }

    private Expr.ArrowFunction Arrow(Expr.ArrowFunction function) =>
        InStrictContext(HasStrictDirective(function.BlockBody), () => InScope(FunctionBindings(function.Parameters, null, function.Name, function.HasOwnThis), () => function with {
            Parameters = Parameters(function.Parameters),
            BlockBody = function.BlockBody is null ? null : InScope(Bindings(function.BlockBody, includeVar: true), () => function.BlockBody.Select(Rewrite).ToList()),
            ExpressionBody = Optional(function.ExpressionBody) }));

    private Stmt.Accessor Accessor(Stmt.Accessor accessor)
    {
        Expr? key = Optional(accessor.ComputedKey);
        return InScope(FunctionBindings(accessor.SetterParam is null ? [] : [accessor.SetterParam], accessor.Body, null, true),
            () => accessor with { ComputedKey = key, Body = accessor.Body.Select(Rewrite).ToList() });
    }

    private Stmt.StaticBlock StaticBlock(Stmt.StaticBlock block)
    {
        List<Stmt> body = VarHoister.Hoist(block.Body, spans);
        return InScope(Bindings(body, includeVar: true), () => block with { Body = body.Select(Rewrite).ToList() });
    }

    private Stmt.Class Class(Stmt.Class klass) => InStrictContext(true, () => InScope(new(StringComparer.Ordinal) { klass.Name.Lexeme }, () => klass with {
        SuperclassExpr = Optional(klass.SuperclassExpr), Methods = klass.Methods.Select(method => Function(method, bindName: false)).ToList(),
        Fields = klass.Fields.Select(f => (Stmt.Field)Rewrite(f)).ToList(),
        Accessors = klass.Accessors?.Select(Accessor).ToList(),
        AutoAccessors = klass.AutoAccessors?.Select(a => (Stmt.AutoAccessor)Rewrite(a)).ToList(),
        StaticInitializers = klass.StaticInitializers?.Select(Rewrite).ToList() }));

    private Expr.ClassExpr Class(Expr.ClassExpr klass) => InStrictContext(true, () => InScope(
        klass.Name is null ? new(StringComparer.Ordinal) : new(StringComparer.Ordinal) { klass.Name.Lexeme }, () => klass with {
        SuperclassExpr = Optional(klass.SuperclassExpr), Methods = klass.Methods.Select(method => Function(method, bindName: false)).ToList(),
        Fields = klass.Fields.Select(f => (Stmt.Field)Rewrite(f)).ToList(),
        Accessors = klass.Accessors?.Select(Accessor).ToList(),
        AutoAccessors = klass.AutoAccessors?.Select(a => (Stmt.AutoAccessor)Rewrite(a)).ToList(),
        StaticInitializers = klass.StaticInitializers?.Select(Rewrite).ToList() }));

    private Expr? Optional(Expr? expression) => expression is null ? null : Expression(expression);

    private Expr Expression(Expr expression)
    {
        Expr result = expression switch
        {
            Expr.Variable e => Resolve(e.Name, property => Read(property), e),
            Expr.Assign e => Assign(e),
            Expr.CompoundAssign e => Compound(e),
            Expr.LogicalAssign e => Logical(e),
            Expr.Call e => Call(e),
            Expr.TaggedTemplateLiteral e => Tagged(e),
            Expr.Delete e when Reference(e.Operand) is { } v => Resolve(v.Name, p => e with { Operand = p }, e),
            Expr.Unary e when e.Operator.Type == TokenType.TYPEOF && Reference(e.Right) is { } v => Resolve(v.Name, p => e with { Right = Read(p) }, e),
            Expr.PrefixIncrement e when Reference(e.Operand) is { } v && IsDynamic(v.Name) => Resolve(v.Name, p => Update(p, e.Operator, false), UpdateLexical(v.Name, e.Operator, false)),
            Expr.PostfixIncrement e when Reference(e.Operand) is { } v && IsDynamic(v.Name) => Resolve(v.Name, p => Update(p, e.Operator, true), UpdateLexical(v.Name, e.Operator, true)),
            Expr.Comma e => e with { Left = Expression(e.Left), Right = Expression(e.Right) },
            Expr.Binary e => e with { Left = Expression(e.Left), Right = Expression(e.Right) },
            Expr.Logical e => e with { Left = Expression(e.Left), Right = Expression(e.Right) },
            Expr.NullishCoalescing e => e with { Left = Expression(e.Left), Right = Expression(e.Right) },
            Expr.Ternary e => e with { Condition = Expression(e.Condition), ThenBranch = Expression(e.ThenBranch), ElseBranch = Expression(e.ElseBranch) },
            Expr.Grouping e => e with { Expression = Expression(e.Expression) },
            Expr.Unary e => e with { Right = Expression(e.Right) },
            Expr.Delete e => e with { Operand = Expression(e.Operand) },
            Expr.PrefixIncrement e => e with { Operand = Expression(e.Operand) },
            Expr.PostfixIncrement e => e with { Operand = Expression(e.Operand) },
            Expr.Get e => e with { Object = Expression(e.Object) },
            Expr.Set e => e with { Object = Expression(e.Object), Value = Expression(e.Value) },
            Expr.GetIndex e => e with { Object = Expression(e.Object), Index = Expression(e.Index) },
            Expr.SetIndex e => e with { Object = Expression(e.Object), Index = Expression(e.Index), Value = Expression(e.Value) },
            Expr.GetPrivate e => e with { Object = Expression(e.Object) },
            Expr.SetPrivate e => e with { Object = Expression(e.Object), Value = Expression(e.Value) },
            Expr.PrivateIn e => e with { Object = Expression(e.Object) },
            Expr.CallPrivate e => e with { Object = Expression(e.Object), Arguments = e.Arguments.Select(Expression).ToList() },
            Expr.SetSuper e => e with { Value = Expression(e.Value) },
            Expr.CompoundSet e => e with { Object = Expression(e.Object), Value = Expression(e.Value) },
            Expr.CompoundSetPrivate e => e with { Object = Expression(e.Object), Value = Expression(e.Value) },
            Expr.CompoundSetIndex e => e with { Object = Expression(e.Object), Index = Expression(e.Index), Value = Expression(e.Value) },
            Expr.LogicalSet e => e with { Object = Expression(e.Object), Value = Expression(e.Value) },
            Expr.LogicalSetPrivate e => e with { Object = Expression(e.Object), Value = Expression(e.Value) },
            Expr.LogicalSetIndex e => e with { Object = Expression(e.Object), Index = Expression(e.Index), Value = Expression(e.Value) },
            Expr.ArrayLiteral e => e with { Elements = e.Elements.Select(Expression).ToList() },
            Expr.ObjectLiteral e => e with { Properties = e.Properties.Select(p => p with {
                Key = p.Key is Expr.ComputedKey k ? k with { Expression = Expression(k.Expression) } : p.Key,
                Value = Expression(p.Value) }).ToList() },
            Expr.ArrowFunction e => Arrow(e),
            Expr.ClassExpr e => Class(e),
            Expr.New e => e with { Callee = Expression(e.Callee), Arguments = e.Arguments.Select(Expression).ToList() },
            Expr.TemplateLiteral e => e with { Expressions = e.Expressions.Select(Expression).ToList() },
            Expr.Spread e => e with { Expression = Expression(e.Expression) },
            Expr.Await e => e with { Expression = Expression(e.Expression) },
            Expr.Yield e => e with { Value = Optional(e.Value) },
            Expr.Instantiation e => e with { Expression = Expression(e.Expression) },
            Expr.TypeAssertion e => e with { Expression = Expression(e.Expression) },
            Expr.Satisfies e => e with { Expression = Expression(e.Expression) },
            Expr.NonNullAssertion e => e with { Expression = Expression(e.Expression) },
            Expr.DynamicImport e => e with { PathExpression = Expression(e.PathExpression) },
            Expr.DestructuringAssign e => e with { Assignments = e.Assignments.Select(Rewrite).ToList(), ResultValue = Expression(e.ResultValue) },
            Expr.AssertedAssignment e => e with { LoweredAssignment = Expression(e.LoweredAssignment) },
            _ => expression
        };
        spans.CopySpan(expression, result);
        return result;
    }

    private Expr Assign(Expr.Assign expression)
    {
        var rewritten = expression with { Value = Expression(expression.Value) };
        return Resolve(expression.Name, p => Write(p, Expression(expression.Value)), rewritten);
    }

    private Expr Compound(Expr.CompoundAssign expression)
    {
        if (!IsDynamic(expression.Name)) return expression with { Value = Expression(expression.Value) };
        Token lexicalValue = fresh();
        Token lexicalRight = fresh();
        var rewritten = new Expr.DestructuringAssign([
            new Stmt.Var(lexicalValue, null, new Expr.Variable(expression.Name)),
            new Stmt.Var(lexicalRight, null, Expression(expression.Value)),
            new Stmt.Expression(new Expr.CompoundAssign(lexicalValue, expression.Operator, new Expr.Variable(lexicalRight)))
        ], new Expr.Assign(expression.Name, new Expr.Variable(lexicalValue)));
        return Resolve(expression.Name, p => {
            Token value = fresh();
            Token right = fresh();
            return new Expr.DestructuringAssign([
                new Stmt.Var(value, null, Read(p)),
                new Stmt.Var(right, null, Expression(expression.Value)),
                new Stmt.Expression(new Expr.CompoundAssign(value, expression.Operator, new Expr.Variable(right)))
            ], Write(p, new Expr.Variable(value)));
        }, rewritten);
    }

    private Expr Logical(Expr.LogicalAssign expression)
    {
        if (!IsDynamic(expression.Name)) return expression with { Value = Expression(expression.Value) };
        Expr Select(Expr current, Expr assigned) => expression.Operator.Type switch {
            TokenType.AND_AND_EQUAL => new Expr.Logical(current, new Token(TokenType.AND_AND, "&&", null, expression.Name.Line), assigned),
            TokenType.OR_OR_EQUAL => new Expr.Logical(current, new Token(TokenType.OR_OR, "||", null, expression.Name.Line), assigned),
            _ => new Expr.NullishCoalescing(current, assigned)
        };
        Token lexicalValue = fresh();
        var rewritten = new Expr.DestructuringAssign([
            new Stmt.Var(lexicalValue, null, new Expr.Variable(expression.Name))
        ], Select(new Expr.Variable(lexicalValue), new Expr.Assign(expression.Name, Expression(expression.Value))));
        return Resolve(expression.Name, p => {
            Token value = fresh();
            Expr current = new Expr.Variable(value);
            Expr assigned = Write(p, Expression(expression.Value));
            Expr result = Select(current, assigned);
            return new Expr.DestructuringAssign([new Stmt.Var(value, null, Read(p))], result);
        }, rewritten);
    }

    private Expr Call(Expr.Call expression)
    {
        // Keep an inner with's fallback as a Reference until every enclosing
        // object environment has had the chance to supply its call receiver.
        if (expression.Callee is Expr.Variable { Name.Lexeme: CaptureLexicalCall }
            && Reference(expression.Arguments[0]) is { } fallbackReference)
            return Resolve(fallbackReference.Name, p => Helper(CaptureCall, fallbackReference.Name,
                p.Object, new Expr.Literal(fallbackReference.Name.Lexeme), new Expr.Literal(_strict)), expression);
        // The parser encodes console intrinsics as dotted pseudo-identifiers;
        // they are complete calls, not identifier References that can be captured.
        if (Reference(expression.Callee) is { } intrinsic && intrinsic.Name.Lexeme.Contains('.'))
            return expression with { Arguments = expression.Arguments.Select(Expression).ToList() };
        // Direct eval is syntactic: moving its fallback callee into a value
        // expression would turn it into indirect eval, lose caller bindings and
        // introduce a runtime dependency for otherwise standalone literal eval.
        if (!expression.Optional && Reference(expression.Callee) is { Name.Lexeme: "eval" } eval)
        {
            // An outer with adds its environment to a branch whose intrinsic
            // identity was already captured by an inner with before the arguments.
            if (expression.IsCapturedIntrinsicEval)
                return PrepareDirectEval(expression, expression.Arguments.Select(Expression).ToList());
            Token directFlag = fresh();
            Token selected = fresh();
            Token lexical = fresh();
            var fallback = new Expr.DestructuringAssign([
                new Stmt.Var(lexical, null, Helper(CaptureLexicalCall, eval.Name, expression.Callee)),
                new Stmt.Expression(new Expr.Assign(directFlag,
                    Helper(IsIntrinsicEval, eval.Name, new Expr.Variable(lexical))))
            ], new Expr.Variable(lexical));
            List<Stmt> prelude = [
                new Stmt.Var(directFlag, null, new Expr.Literal(false)),
                new Stmt.Var(selected, null, Resolve(eval.Name,
                    p => Helper(CaptureCall, eval.Name, p.Object,
                        new Expr.Literal(eval.Name.Lexeme), new Expr.Literal(_strict)), fallback))
            ];
            List<Expr> arguments = [];
            foreach (var argument in expression.Arguments)
            {
                // Constants have no effects and must stay literal for standalone eval.
                if (argument is Expr.Literal) { arguments.Add(argument); continue; }
                Token value = fresh();
                if (argument is Expr.Spread spread)
                {
                    prelude.Add(new Stmt.Var(value, null,
                        new Expr.ArrayLiteral([new Expr.Spread(Expression(spread.Expression))])));
                    arguments.Add(new Expr.Spread(new Expr.Variable(value)) { IsMaterializedArguments = true });
                }
                else
                {
                    prelude.Add(new Stmt.Var(value, null, Expression(argument)));
                    arguments.Add(new Expr.Variable(value));
                }
            }
            var direct = PrepareDirectEval(expression with { Callee = new Expr.Variable(eval.Name) }, arguments);
            return new Expr.DestructuringAssign(prelude, new Expr.Ternary(new Expr.Variable(directFlag),
                direct, expression with { Callee = new Expr.Variable(selected), Arguments = arguments }));
        }
        return expression with { Callee = Callable(expression.Callee),
            Arguments = expression.Arguments.Select(Expression).ToList() };
    }

    private Expr.Call PrepareDirectEval(Expr.Call expression, List<Expr> arguments)
    {
        var environments = new List<EvalObjectEnvironment>(expression.EvalObjectEnvironments ?? []);
        environments.Add(new(storage, _scopes.SelectMany(scope => scope).ToHashSet(StringComparer.Ordinal)));
        var direct = expression with { Arguments = arguments,
            EvalObjectEnvironments = environments, PreparedEvalStatements = null, IsCapturedIntrinsicEval = true };
        if (arguments.FirstOrDefault() is Expr.Literal { Value: string source })
        {
            try
            {
                direct = direct with { PreparedEvalStatements = ApplyEvalEnvironments(
                    new Parser(new Lexer(source).ScanTokens()).ParseOrThrow(), environments) };
            }
            catch
            {
                // Eval owns syntax errors when this branch executes.
            }
        }
        return direct;
    }

    private Expr Tagged(Expr.TaggedTemplateLiteral expression)
    {
        return expression with { Tag = Callable(expression.Tag),
            Expressions = expression.Expressions.Select(Expression).ToList() };
    }

    // Resolve the call Reference before evaluating one argument/interpolation tree.
    // A captured callable retains the object receiver without reading guest .bind,
    // .call, name or length properties, and survives argument suspension.
    private Expr Callable(Expr expression) => Reference(expression) is { } variable
        ? Resolve(variable.Name, p => Helper(CaptureCall, variable.Name, p.Object,
            new Expr.Literal(variable.Name.Lexeme), new Expr.Literal(_strict)), expression)
        : Expression(expression);

    private Expr Update(Expr.Get property, Token operation, bool postfix)
    {
        Token value = fresh();
        Token previous = fresh();
        return new Expr.DestructuringAssign([
            new Stmt.Var(value, null, Read(property)),
            new Stmt.Var(previous, null, new Expr.PostfixIncrement(new Expr.Variable(value), operation)),
            new Stmt.Expression(Write(property, new Expr.Variable(value)))
        ], new Expr.Variable(postfix ? previous : value));
    }

    private Expr UpdateLexical(Token name, Token operation, bool postfix)
    {
        Token value = fresh();
        Token previous = fresh();
        return new Expr.DestructuringAssign([
            new Stmt.Var(value, null, new Expr.Variable(name)),
            new Stmt.Var(previous, null, new Expr.PostfixIncrement(new Expr.Variable(value), operation)),
            new Stmt.Expression(new Expr.Assign(name, new Expr.Variable(value)))
        ], new Expr.Variable(postfix ? previous : value));
    }
}
