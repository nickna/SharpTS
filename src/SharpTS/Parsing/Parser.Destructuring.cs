namespace SharpTS.Parsing;

public partial class Parser
{
    // ============== DESTRUCTURING PATTERN PARSING ==============

    private DestructurePattern ParseDestructurePattern()
    {
        if (Match(TokenType.LEFT_BRACKET))
            return ParseArrayPattern();
        if (Match(TokenType.LEFT_BRACE))
            return ParseObjectPattern();
        if (Match(TokenType.DOT_DOT_DOT))
        {
            Token restName = ConsumeIdentifierName("Expect identifier after '...'.");
            return new RestPattern(restName);
        }

        Token patternName = ConsumeIdentifierName("Expect identifier in pattern.");
        Expr? defaultValue = Match(TokenType.EQUAL) ? Expression() : null;
        return new IdentifierPattern(patternName, defaultValue);
    }

    private ArrayPattern ParseArrayPattern()
    {
        int line = Previous().Line;
        List<ArrayPatternElement> elements = [];

        while (!Check(TokenType.RIGHT_BRACKET) && !IsAtEnd())
        {
            if (Check(TokenType.COMMA))
            {
                // Hole in array: [a, , c]
                elements.Add(new ArrayPatternElement(null, IsHole: true));
            }
            else
            {
                elements.Add(new ArrayPatternElement(ParseDestructurePattern(), IsHole: false));
            }

            if (!Check(TokenType.RIGHT_BRACKET))
            {
                Consume(TokenType.COMMA, "Expect ',' between array pattern elements.");
            }
        }

        Consume(TokenType.RIGHT_BRACKET, "Expect ']' after array pattern.");
        return new ArrayPattern(elements, line);
    }

    private ObjectPattern ParseObjectPattern()
    {
        int line = Previous().Line;
        List<ObjectPatternProperty> properties = [];

        while (!Check(TokenType.RIGHT_BRACE) && !IsAtEnd())
        {
            // Handle rest pattern: { ...rest }
            if (Match(TokenType.DOT_DOT_DOT))
            {
                Token restName = ConsumeIdentifierName("Expect identifier after '...'.");
                properties.Add(new ObjectPatternProperty(restName, new RestPattern(restName), null));
                // Rest must be last, so break out of loop
                break;
            }

            // Property-key position accepts any keyword (JS semantics).
            Token key = ConsumePropertyName("Expect property name.");
            DestructurePattern value;
            Expr? defaultValue = null;

            if (Match(TokenType.COLON))
            {
                // Rename or nested: { x: newName } or { x: { nested } }
                if (Check(TokenType.LEFT_BRACE) || Check(TokenType.LEFT_BRACKET))
                {
                    value = ParseDestructurePattern();
                }
                else
                {
                    Token rename = ConsumeIdentifierName("Expect identifier after ':'.");
                    if (Match(TokenType.EQUAL))
                        defaultValue = Expression();
                    value = new IdentifierPattern(rename, defaultValue);
                }
            }
            else
            {
                // Shorthand: { x } or { x = default }
                if (Match(TokenType.EQUAL))
                    defaultValue = Expression();
                Token bindingName = key.Type == TokenType.IDENTIFIER
                    ? key
                    : new Token(TokenType.IDENTIFIER, key.Lexeme, key.Literal, key.Line, key.Start);
                value = new IdentifierPattern(bindingName, defaultValue);
            }

            properties.Add(new ObjectPatternProperty(key, value, defaultValue));

            if (!Check(TokenType.RIGHT_BRACE))
            {
                Consume(TokenType.COMMA, "Expect ',' between object pattern properties.");
            }
        }

        Consume(TokenType.RIGHT_BRACE, "Expect '}' after object pattern.");
        return new ObjectPattern(properties, line);
    }

    // ============== DESTRUCTURING DECLARATIONS ==============

    private Stmt DestructureArrayDeclaration()
    {
        Consume(TokenType.LEFT_BRACKET, "Expect '[' for array destructuring.");
        ArrayPattern pattern = ParseArrayPattern();

        // Optional type annotation (ignored for now, inferred from initializer)
        if (Match(TokenType.COLON))
            ParseTypeAnnotation();

        Consume(TokenType.EQUAL, "Expect '=' after destructuring pattern.");
        Expr initializer = Expression();
        ConsumeSemicolon("Expect ';' after variable declaration.");

        return DesugarArrayPattern(pattern, initializer);
    }

    private Stmt DestructureObjectDeclaration()
    {
        Consume(TokenType.LEFT_BRACE, "Expect '{' for object destructuring.");
        ObjectPattern pattern = ParseObjectPattern();

        // Optional type annotation (ignored for now, inferred from initializer)
        if (Match(TokenType.COLON))
            ParseTypeAnnotation();

        Consume(TokenType.EQUAL, "Expect '=' after destructuring pattern.");
        Expr initializer = Expression();
        ConsumeSemicolon("Expect ';' after variable declaration.");

        return DesugarObjectPattern(pattern, initializer);
    }

    // ============== DESUGARING METHODS ==============

    // underDefault: true when this pattern is reached through an enclosing default, so its
    // property reads must tolerate a missing property (typed as `undefined`) rather than report
    // TS2339. Array index reads are already OOB-tolerant in the checker, so the flag only needs
    // to ride through to nested object patterns. #796
    private Stmt DesugarArrayPattern(ArrayPattern pattern, Expr initializer, bool underDefault = false)
    {
        List<Stmt> statements = [];

        // const _dest0 = __arrayDestructure(initializer);
        // JS array destructuring follows the iterator protocol, not positional
        // indexing. The wrapper normalizes non-indexable iterables (generators,
        // Set, Map, typed arrays, Buffers, strings, objects with [Symbol.iterator])
        // into an array so the index access below is spec-correct (#685/#1288);
        // arrays/tuples pass through unchanged to keep the fast index path.
        Token temp = GenerateTempVar(pattern.Line);
        Expr normalizedInit = new Expr.Call(
            new Expr.Variable(new Token(TokenType.IDENTIFIER, "__arrayDestructure", null, pattern.Line)),
            new Token(TokenType.RIGHT_PAREN, ")", null, pattern.Line),
            null,
            [initializer]
        );
        // Carry the pattern's shape as a contextual type so a mixed array-literal source (`[[7], 8]`)
        // infers as a tuple instead of an array — otherwise `_dest[0]` is a non-indexable union and a
        // nested pattern (`[m]`) fails to type-check (#783). Erased at runtime.
        statements.Add(new Stmt.Var(temp, null, normalizedInit,
            InitializerContext: BuildArrayPatternShape(pattern),
            DestructuringSource: DestructuringSourceKind.Array));

        int index = 0;
        foreach (var element in pattern.Elements)
        {
            if (element.IsHole)
            {
                index++;
                continue;
            }

            if (element.Pattern is RestPattern rest)
            {
                // const rest = _dest0.slice(index);
                Expr sliceCall = new Expr.Call(
                    new Expr.Get(new Expr.Variable(temp),
                        new Token(TokenType.IDENTIFIER, "slice", null, pattern.Line)),
                    new Token(TokenType.RIGHT_PAREN, ")", null, pattern.Line),
                    null,
                    [new Expr.Literal((double)index)]
                );
                statements.Add(new Stmt.Var(rest.Name, null, sliceCall));
                break; // Rest must be last
            }

            // Access expression: _dest0[index]
            Expr accessExpr = new Expr.GetIndex(
                new Expr.Variable(temp),
                new Expr.Literal((double)index)
            );

            if (element.Pattern is IdentifierPattern id)
            {
                // Apply default value if present (undefined-only, #784)
                if (id.DefaultValue != null)
                {
                    accessExpr = DefaultIfUndefined(accessExpr, id.DefaultValue, statements, pattern.Line);
                }
                statements.Add(new Stmt.Var(id.Name, null, accessExpr));
            }
            else if (element.Pattern is ArrayPattern nestedArray)
            {
                statements.Add(DesugarArrayPattern(nestedArray, accessExpr, underDefault));
            }
            else if (element.Pattern is ObjectPattern nestedObj)
            {
                statements.Add(DesugarObjectPattern(nestedObj, accessExpr, underDefault));
            }

            index++;
        }

        return new Stmt.Sequence(statements);
    }

    // underDefault: see DesugarArrayPattern. A property whose own value carries a default also
    // makes its read (and any nested pattern beneath it) tolerant. #796
    private Stmt DesugarObjectPattern(ObjectPattern pattern, Expr initializer, bool underDefault = false)
    {
        List<Stmt> statements = [];
        List<string> usedKeys = [];

        // const _dest0 = initializer;
        Token temp = GenerateTempVar(pattern.Line);
        statements.Add(new Stmt.Var(temp, null, initializer,
            DestructuringSource: DestructuringSourceKind.Object));

        foreach (var prop in pattern.Properties)
        {
            // Handle rest pattern: const { x, ...rest } = obj
            if (prop.Value is RestPattern rest)
            {
                // Generate: const rest = __objectRest(_dest0, ["x", "y", ...usedKeys]);
                var excludeKeysExpr = new Expr.ArrayLiteral(
                    usedKeys.Select(k => new Expr.Literal(k) as Expr).ToList()
                );
                var restCall = new Expr.Call(
                    new Expr.Variable(new Token(TokenType.IDENTIFIER, "__objectRest", null, pattern.Line)),
                    new Token(TokenType.RIGHT_PAREN, ")", null, pattern.Line),
                    null,
                    [new Expr.Variable(temp), excludeKeysExpr]
                );
                statements.Add(new Stmt.Var(rest.Name, null, restCall));
                break; // Rest must be last
            }

            usedKeys.Add(prop.Key.Lexeme);

            // This read is tolerant if an enclosing pattern is defaulted, or this property itself
            // carries a default (its read may be missing on the source — the default covers it). #796
            bool propDefaulted = underDefault || prop.DefaultValue != null;

            // Access expression: _dest0.key
            Expr accessExpr = new Expr.Get(new Expr.Variable(temp), prop.Key, Defaulted: propDefaulted);

            if (prop.Value is IdentifierPattern id)
            {
                // Apply default value if present (undefined-only, #784)
                Expr? defaultVal = prop.DefaultValue ?? id.DefaultValue;
                if (defaultVal != null)
                {
                    accessExpr = DefaultIfUndefined(accessExpr, defaultVal, statements, pattern.Line);
                }
                statements.Add(new Stmt.Var(id.Name, null, accessExpr));
            }
            else if (prop.Value is ArrayPattern nestedArray)
            {
                statements.Add(DesugarArrayPattern(nestedArray, accessExpr, propDefaulted));
            }
            else if (prop.Value is ObjectPattern nestedObj)
            {
                statements.Add(DesugarObjectPattern(nestedObj, accessExpr, propDefaulted));
            }
        }

        return new Stmt.Sequence(statements);
    }

    // ============== CONTEXTUAL SHAPE FROM BINDING PATTERN (#783) ==============
    // A mixed array literal (`[[7], 8]`) infers as the array `(number[] | number)[]` rather than the
    // tuple `[number[], number]`, so the desugared positional read `_dest[0]` is a non-indexable union
    // and a nested pattern (`[m]`) fails to type-check. tsc avoids this by using the binding pattern as a
    // contextual type. These helpers reconstruct that shape as a synthetic `TupleTypeNode` attached to
    // the source temp's `InitializerContext`: only positions whose binding is itself a nested array get a
    // recursive tuple context; every other position gets `unknown`, which never triggers tuple inference
    // (so uniform-nested literals like `[[1,2],[3,4]]` stay arrays, matching tsc). A trailing rest becomes
    // a `...unknown[]` element so arity filtering still admits the literal.

    private static TypeNode BuildArrayPatternShape(ArrayPattern pattern)
    {
        var elements = new List<TupleElementNode>(pattern.Elements.Count);
        foreach (var element in pattern.Elements)
        {
            if (!element.IsHole && element.Pattern is RestPattern)
            {
                elements.Add(new TupleElementNode(null, UnknownArrayShape(pattern.Line), IsOptional: false, IsRest: true, pattern.Line));
                break;
            }
            TypeNode shape = !element.IsHole && element.Pattern is ArrayPattern nested
                ? BuildArrayPatternShape(nested)
                : UnknownShape(pattern.Line);
            elements.Add(new TupleElementNode(null, shape, IsOptional: false, IsRest: false, pattern.Line));
        }
        return new TupleTypeNode(elements, pattern.Line);
    }

    private static TypeNode BuildArrayLiteralShape(Expr.ArrayLiteral arr, int line)
    {
        var elements = new List<TupleElementNode>(arr.Elements.Count);
        for (int i = 0; i < arr.Elements.Count; i++)
        {
            if (arr.IsHole(i))
            {
                elements.Add(new TupleElementNode(null, UnknownShape(line), IsOptional: false, IsRest: false, line));
                continue;
            }
            Expr element = Unwrap(arr.Elements[i]);
            if (element is Expr.Spread)
            {
                elements.Add(new TupleElementNode(null, UnknownArrayShape(line), IsOptional: false, IsRest: true, line));
                break;
            }
            // A nested array target — directly (`[[m], n]`) or with a default that the eager parser
            // captured as a DestructuringAssign carrying the raw target (`[[a] = []]`, #779).
            Expr.ArrayLiteral? nested = element switch
            {
                Expr.ArrayLiteral lit => lit,
                Expr.DestructuringAssign { RawTarget: { } raw } when Unwrap(raw) is Expr.ArrayLiteral rawLit => rawLit,
                _ => null
            };
            TypeNode shape = nested != null ? BuildArrayLiteralShape(nested, line) : UnknownShape(line);
            elements.Add(new TupleElementNode(null, shape, IsOptional: false, IsRest: false, line));
        }
        return new TupleTypeNode(elements, line);
    }

    private static TypeNode UnknownShape(int line) => new NamedTypeNode("unknown", null, line);
    private static TypeNode UnknownArrayShape(int line) => new ArrayTypeNode(UnknownShape(line), line);

    // ============== ASSIGNMENT DESTRUCTURING (#754) ==============
    // `[a, b] = rhs` / `({a, b} = rhs)` assign to EXISTING l-values (not new bindings). The target has
    // already been parsed as an array/object literal (the eager-parse cover grammar); here it is
    // reinterpreted as a pattern and lowered into assignment statements that reuse the #685
    // iterator-protocol normalization (`__arrayDestructure`) and `__objectRest`. The rhs is evaluated
    // once into a temp and yielded as the expression's value (ECMA-262: an assignment evaluates to its
    // right-hand side, the ORIGINAL rhs — not the normalized array).

    /// <summary>True when an `=` target parsed as an array/object literal is an assignment-destructuring
    /// pattern (#754) rather than an ordinary assignment target.</summary>
    private static bool IsDestructuringAssignmentTarget(Expr target) =>
        Unwrap(target) is Expr.ArrayLiteral or Expr.ObjectLiteral;

    private static Expr Unwrap(Expr e) => e is Expr.Grouping g ? Unwrap(g.Expression) : e;

    private Expr BuildDestructuringAssignment(Expr pattern, Expr value, int line)
    {
        var stmts = new List<Stmt>();
        // _destN = rhs — evaluate the source exactly once; it is also the expression's result value.
        // Carry the target pattern's shape as a contextual type so a mixed array-literal rhs infers as
        // a tuple, not an array union — keeping nested targets (`[[m], n] = [[7], 8]`) indexable (#783).
        Token rhsTemp = GenerateTempVar(line);
        TypeNode? rhsShape = Unwrap(pattern) is Expr.ArrayLiteral patternArr ? BuildArrayLiteralShape(patternArr, line) : null;
        stmts.Add(new Stmt.Var(rhsTemp, null, value, InitializerContext: rhsShape));
        LowerAssignmentTarget(pattern, new Expr.Variable(rhsTemp), stmts, line);
        // Retain the raw (pattern, default) so this node can be re-lowered if it turns out to be a nested
        // element WITH a default (`[[a] = []]`) — see LowerElementWithDefault (#779).
        return new Expr.DestructuringAssign(stmts, new Expr.Variable(rhsTemp), RawTarget: pattern, RawDefault: value);
    }

    // underDefault: true when this target sits beneath an enclosing default (`{p: {x} = {}}`), so
    // its property reads must tolerate a missing property (typed as `undefined`) rather than report
    // TS2339. Threaded through to MakePropertyAccess; array index reads are already OOB-tolerant. #796
    private void LowerAssignmentTarget(Expr target, Expr source, List<Stmt> stmts, int line, bool underDefault = false)
    {
        switch (Unwrap(target))
        {
            case Expr.ArrayLiteral arr: LowerArrayAssignmentTarget(arr, source, stmts, line, underDefault); break;
            case Expr.ObjectLiteral obj: LowerObjectAssignmentTarget(obj, source, stmts, line, underDefault); break;
            default: stmts.Add(new Stmt.Expression(MakeAssignmentExpr(target, source))); break;
        }
    }

    private void LowerArrayAssignmentTarget(Expr.ArrayLiteral arr, Expr source, List<Stmt> stmts, int line, bool underDefault = false)
    {
        // _srcN = __arrayDestructure(source) — normalize any iterable to an indexable array (#685).
        Token srcTemp = GenerateTempVar(line);
        stmts.Add(new Stmt.Var(srcTemp, null, MakeArrayDestructureCall(source, line),
            DestructuringSource: DestructuringSourceKind.Array));
        Expr src = new Expr.Variable(srcTemp);

        for (int i = 0; i < arr.Elements.Count; i++)
        {
            if (arr.IsHole(i)) continue;
            Expr element = Unwrap(arr.Elements[i]);

            if (element is Expr.Spread spread)
            {
                // Rest: target = _srcN.slice(i). Must be the final element.
                LowerAssignmentTarget(spread.Expression, MakeSliceCall(src, i, line), stmts, line, underDefault);
                break;
            }

            LowerElementWithDefault(element, new Expr.GetIndex(src, new Expr.Literal((double)i)), stmts, line, underDefault);
        }
    }

    private void LowerObjectAssignmentTarget(Expr.ObjectLiteral obj, Expr source, List<Stmt> stmts, int line, bool underDefault = false)
    {
        // _srcN = source — object patterns read properties directly (no iterator normalization).
        Token srcTemp = GenerateTempVar(line);
        stmts.Add(new Stmt.Var(srcTemp, null, source,
            DestructuringSource: DestructuringSourceKind.Object));
        Expr src = new Expr.Variable(srcTemp);
        var usedKeys = new List<Expr>();

        foreach (var prop in obj.Properties)
        {
            if (prop.IsSpread)
            {
                // Rest: target = __objectRest(_srcN, [usedKeys]). Must be last.
                LowerAssignmentTarget(prop.Value, MakeObjectRestCall(src, usedKeys, line), stmts, line, underDefault);
                break;
            }
            if (prop.Kind is not Expr.ObjectPropertyKind.Value || prop.Key is null)
                throw new Exception("Invalid assignment target.");  // getters/setters/methods aren't patterns

            Expr access = MakePropertyAccess(src, prop.Key, usedKeys, underDefault);
            LowerElementWithDefault(prop.Value, access, stmts, line, underDefault);
        }
    }

    // Lowers one pattern element / property value to an assignment, peeling off a default (`= expr`)
    // that the eager parser captured as an Assign (variable target) or Set/SetIndex/SetPrivate (member
    // target). The default is applied only when the read is `undefined` (ECMA-262 AssignmentElement),
    // via the shared DefaultIfUndefined helper (#784).
    private void LowerElementWithDefault(Expr element, Expr access, List<Stmt> stmts, int line, bool underDefault = false)
    {
        switch (Unwrap(element))
        {
            case Expr.Assign def:
                LowerAssignmentTarget(new Expr.Variable(def.Name),
                    DefaultIfUndefined(access, def.Value, stmts, line), stmts, line, underDefault);
                break;
            case Expr.Set def:
                LowerAssignmentTarget(new Expr.Get(def.Object, def.Name),
                    DefaultIfUndefined(access, def.Value, stmts, line), stmts, line, underDefault);
                break;
            case Expr.SetIndex def:
                LowerAssignmentTarget(new Expr.GetIndex(def.Object, def.Index),
                    DefaultIfUndefined(access, def.Value, stmts, line), stmts, line, underDefault);
                break;
            case Expr.SetPrivate def:
                LowerAssignmentTarget(new Expr.GetPrivate(def.Object, def.Name),
                    DefaultIfUndefined(access, def.Value, stmts, line), stmts, line, underDefault);
                break;
            case Expr.DestructuringAssign { RawTarget: { } rawTarget, RawDefault: { } rawDefault }:
                // A nested pattern WITH a default (`[[a] = []]`, `{p: {x} = {}}`): the eager parser lowered
                // the inner `[a] = []` to a DestructuringAssign but kept its raw (target, default), so
                // re-lower the inner pattern against the defaulted access — discarding the inner node's
                // pre-built statements, which bound against the wrong (inner-rhs) source (#779). The inner
                // pattern sits beneath this default, so its reads must tolerate a missing property (#796).
                LowerAssignmentTarget(rawTarget, DefaultIfUndefined(access, rawDefault, stmts, line), stmts, line, underDefault: true);
                break;
            default:
                LowerAssignmentTarget(element, access, stmts, line, underDefault);
                break;
        }
    }

    // Destructuring defaults are applied ONLY when the matched value is `undefined`, never `null`
    // (ECMA-262 ArrayBindingPattern / ObjectBindingPattern / AssignmentElement) — unlike `??`, which
    // also replaces null. The access is spilled into a temp so it is evaluated exactly once (a property
    // read may invoke a getter) and the ternary's repeated operand is a side-effect-free temp read.
    // Shared by both the declaration desugaring and the #754 assignment-destructuring lowering. #784
    private Expr DefaultIfUndefined(Expr access, Expr defaultValue, List<Stmt> stmts, int line)
    {
        // The read is now covered by a default, so a missing property must type as `undefined`
        // (the ternary below supplies the default) rather than reporting TS2339. #796
        if (access is Expr.Get g && !g.Defaulted) access = g with { Defaulted = true };
        Token valueTemp = GenerateTempVar(line);
        stmts.Add(new Stmt.Var(valueTemp, null, access));
        Expr isUndefined = new Expr.Binary(
            new Expr.Variable(valueTemp),
            new Token(TokenType.EQUAL_EQUAL_EQUAL, "===", null, line),
            new Expr.Literal(SharpTS.Runtime.Types.SharpTSUndefined.Instance));
        return new Expr.Ternary(isUndefined, defaultValue, new Expr.Variable(valueTemp));
    }

    private static Expr MakeAssignmentExpr(Expr target, Expr value) => Unwrap(target) switch
    {
        Expr.Variable v => new Expr.Assign(v.Name, value),
        Expr.Get g => new Expr.Set(g.Object, g.Name, value),
        Expr.GetIndex gi => new Expr.SetIndex(gi.Object, gi.Index, value),
        Expr.GetPrivate gp => new Expr.SetPrivate(gp.Object, gp.Name, value),
        _ => throw new Exception("Invalid assignment target.")
    };

    private Expr MakePropertyAccess(Expr src, Expr.PropertyKey key, List<Expr> usedKeys, bool underDefault = false)
    {
        switch (key)
        {
            case Expr.IdentifierKey ik:
                usedKeys.Add(new Expr.Literal(ik.Name.Lexeme));
                return new Expr.Get(src, ik.Name, Defaulted: underDefault);
            case Expr.LiteralKey lk:
                string name = lk.Literal.Type == TokenType.STRING
                    ? (string)lk.Literal.Literal!
                    : lk.Literal.Literal!.ToString()!;
                usedKeys.Add(new Expr.Literal(name));
                return new Expr.GetIndex(src, new Expr.Literal(name));
            case Expr.ComputedKey ck:
                // A computed key is dynamic, so it cannot be added to the static rest-exclusion list
                // (a `{[k]: v, ...rest}` pattern would over-include — a narrow, rare edge).
                return new Expr.GetIndex(src, ck.Expression);
            default:
                throw new Exception("Invalid assignment target.");
        }
    }

    private static Expr MakeArrayDestructureCall(Expr source, int line) => new Expr.Call(
        new Expr.Variable(new Token(TokenType.IDENTIFIER, "__arrayDestructure", null, line)),
        new Token(TokenType.RIGHT_PAREN, ")", null, line), null, [source]);

    private static Expr MakeSliceCall(Expr src, int index, int line) => new Expr.Call(
        new Expr.Get(src, new Token(TokenType.IDENTIFIER, "slice", null, line)),
        new Token(TokenType.RIGHT_PAREN, ")", null, line), null, [new Expr.Literal((double)index)]);

    private static Expr MakeObjectRestCall(Expr src, List<Expr> usedKeys, int line) => new Expr.Call(
        new Expr.Variable(new Token(TokenType.IDENTIFIER, "__objectRest", null, line)),
        new Token(TokenType.RIGHT_PAREN, ")", null, line), null,
        [src, new Expr.ArrayLiteral(usedKeys)]);
}
