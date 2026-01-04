using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Ahead-of-time compiler that generates .NET IL assemblies from the AST.
/// </summary>
/// <remarks>
/// One of two execution paths after type checking (the other being <see cref="Interpreter"/>).
/// Uses <c>System.Reflection.Emit</c> with <see cref="PersistedAssemblyBuilder"/> to emit
/// .NET assemblies. Compilation runs in multiple phases: emit runtime types, analyze closures,
/// define classes/functions, collect arrow functions, emit method bodies, and finalize.
/// Delegates IL emission to <see cref="ILEmitter"/>, closure detection to <see cref="ClosureAnalyzer"/>,
/// and type mapping to <see cref="TypeMapper"/>. Produces standalone executables via <c>--compile</c> flag.
/// </remarks>
/// <seealso cref="ILEmitter"/>
/// <seealso cref="ClosureAnalyzer"/>
/// <seealso cref="Interpreter"/>
public class ILCompiler
{
    private readonly string _assemblyName;
    private readonly PersistedAssemblyBuilder _assemblyBuilder;
    private readonly ModuleBuilder _moduleBuilder;
    private readonly TypeMapper _typeMapper;
    private readonly Dictionary<string, TypeBuilder> _classBuilders = [];
    private readonly Dictionary<string, MethodBuilder> _functionBuilders = [];
    private readonly Dictionary<string, Dictionary<string, FieldBuilder>> _staticFields = [];
    private readonly Dictionary<string, Dictionary<string, MethodBuilder>> _staticMethods = [];
    private readonly Dictionary<string, FieldBuilder> _instanceFieldsField = []; // _fields dict field per class
    private readonly Dictionary<string, ConstructorBuilder> _classConstructors = [];
    private readonly Dictionary<string, (int RestParamIndex, int RegularParamCount)> _functionRestParams = [];
    private TypeBuilder _programType = null!;

    // Closure support
    private ClosureAnalyzer _closureAnalyzer = null!;
    private readonly Dictionary<Expr.ArrowFunction, MethodBuilder> _arrowMethods = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.ArrowFunction, TypeBuilder> _displayClasses = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.ArrowFunction, Dictionary<string, FieldBuilder>> _displayClassFields = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Expr.ArrowFunction, ConstructorBuilder> _displayClassConstructors = new(ReferenceEqualityComparer.Instance);
    private int _arrowMethodCounter = 0;
    private int _displayClassCounter = 0;

    // Enum support
    private readonly Dictionary<string, Dictionary<string, object>> _enumMembers = [];
    private readonly Dictionary<string, Dictionary<double, string>> _enumReverse = [];
    private readonly Dictionary<string, EnumKind> _enumKinds = [];
    private readonly HashSet<string> _constEnums = [];
    private readonly bool _preserveConstEnums;

    // Generic type parameter support
    private readonly Dictionary<string, GenericTypeParameterBuilder[]> _classGenericParams = [];
    private readonly Dictionary<string, GenericTypeParameterBuilder[]> _functionGenericParams = [];
    private readonly Dictionary<string, bool> _isGenericFunction = [];

    // Emitted runtime (for standalone DLLs)
    private EmittedRuntime _runtime = null!;

    public ILCompiler(string assemblyName, bool preserveConstEnums = false)
    {
        _assemblyName = assemblyName;
        _preserveConstEnums = preserveConstEnums;
        _assemblyBuilder = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly
        );
        _moduleBuilder = _assemblyBuilder.DefineDynamicModule(assemblyName);
        _typeMapper = new TypeMapper(_moduleBuilder);
    }

    public void Compile(List<Stmt> statements, TypeChecker typeChecker)
    {
        // Phase 1: Emit runtime support types into the generated assembly
        // This makes compiled DLLs standalone without requiring SharpTS.dll
        _runtime = RuntimeEmitter.EmitAll(_moduleBuilder);

        // Phase 2: Analyze closures
        _closureAnalyzer = new ClosureAnalyzer();
        _closureAnalyzer.Analyze(statements);

        // Phase 3: Create the main program type for top-level code
        _programType = _moduleBuilder.DefineType(
            "$Program",
            TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Sealed
        );

        // Phase 4: Collect and define all class and function declarations
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Class classStmt)
            {
                DefineClass(classStmt);
            }
            else if (stmt is Stmt.Function funcStmt)
            {
                DefineFunction(funcStmt);
            }
            else if (stmt is Stmt.Enum enumStmt)
            {
                DefineEnum(enumStmt);
            }
        }

        // Phase 5: Collect all arrow functions and generate methods/display classes
        CollectAndDefineArrowFunctions(statements);

        // Phase 6: Emit arrow function method bodies
        EmitArrowFunctionBodies();

        // Phase 7: Emit method bodies for all classes and functions
        foreach (var stmt in statements)
        {
            if (stmt is Stmt.Class classStmt)
            {
                EmitClassMethods(classStmt);
            }
            else if (stmt is Stmt.Function funcStmt)
            {
                EmitFunctionBody(funcStmt);
            }
        }

        // Phase 8: Emit entry point (top-level statements)
        EmitEntryPoint(statements);

        // Phase 9: Finalize all types
        foreach (var tb in _displayClasses.Values)
        {
            tb.CreateType();
        }
        foreach (var tb in _classBuilders.Values)
        {
            tb.CreateType();
        }
        _programType.CreateType();
    }

    #region Arrow Function Collection

    private readonly List<(Expr.ArrowFunction Arrow, HashSet<string> Captures)> _collectedArrows = [];

    private void CollectAndDefineArrowFunctions(List<Stmt> statements)
    {
        // Walk the AST and collect all arrow functions
        foreach (var stmt in statements)
        {
            CollectArrowsFromStmt(stmt);
        }

        // Define methods and display classes
        foreach (var (arrow, captures) in _collectedArrows)
        {
            var paramTypes = arrow.Parameters.Select(_ => typeof(object)).ToArray();

            if (captures.Count == 0)
            {
                // Non-capturing: static method on $Program
                var methodBuilder = _programType.DefineMethod(
                    $"<>Arrow_{_arrowMethodCounter++}",
                    MethodAttributes.Private | MethodAttributes.Static,
                    typeof(object),
                    paramTypes
                );
                _arrowMethods[arrow] = methodBuilder;
            }
            else
            {
                // Capturing: create display class
                var displayClass = _moduleBuilder.DefineType(
                    $"<>c__DisplayClass{_displayClassCounter++}",
                    TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
                    typeof(object)
                );

                // Add fields for captured variables
                var fieldMap = new Dictionary<string, FieldBuilder>();
                foreach (var capturedVar in captures)
                {
                    var field = displayClass.DefineField(capturedVar, typeof(object), FieldAttributes.Public);
                    fieldMap[capturedVar] = field;
                }
                _displayClassFields[arrow] = fieldMap;

                // Add default constructor
                var ctorBuilder = displayClass.DefineConstructor(
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    Type.EmptyTypes
                );
                var ctorIL = ctorBuilder.GetILGenerator();
                ctorIL.Emit(OpCodes.Ldarg_0);
                ctorIL.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
                ctorIL.Emit(OpCodes.Ret);
                _displayClassConstructors[arrow] = ctorBuilder;

                // Add Invoke method
                var invokeMethod = displayClass.DefineMethod(
                    "Invoke",
                    MethodAttributes.Public,
                    typeof(object),
                    paramTypes
                );

                _displayClasses[arrow] = displayClass;
                _arrowMethods[arrow] = invokeMethod;
            }
        }
    }

    private void CollectArrowsFromStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Stmt.Expression e:
                CollectArrowsFromExpr(e.Expr);
                break;
            case Stmt.Var v:
                if (v.Initializer != null)
                    CollectArrowsFromExpr(v.Initializer);
                break;
            case Stmt.Function f:
                foreach (var s in f.Body)
                    CollectArrowsFromStmt(s);
                foreach (var p in f.Parameters)
                    if (p.DefaultValue != null)
                        CollectArrowsFromExpr(p.DefaultValue);
                break;
            case Stmt.Class c:
                foreach (var method in c.Methods)
                    CollectArrowsFromStmt(method);
                break;
            case Stmt.If i:
                CollectArrowsFromExpr(i.Condition);
                CollectArrowsFromStmt(i.ThenBranch);
                if (i.ElseBranch != null)
                    CollectArrowsFromStmt(i.ElseBranch);
                break;
            case Stmt.While w:
                CollectArrowsFromExpr(w.Condition);
                CollectArrowsFromStmt(w.Body);
                break;
            case Stmt.ForOf f:
                CollectArrowsFromExpr(f.Iterable);
                CollectArrowsFromStmt(f.Body);
                break;
            case Stmt.Block b:
                foreach (var s in b.Statements)
                    CollectArrowsFromStmt(s);
                break;
            case Stmt.Sequence seq:
                foreach (var s in seq.Statements)
                    CollectArrowsFromStmt(s);
                break;
            case Stmt.Return r:
                if (r.Value != null)
                    CollectArrowsFromExpr(r.Value);
                break;
            case Stmt.Switch s:
                CollectArrowsFromExpr(s.Subject);
                foreach (var c in s.Cases)
                {
                    CollectArrowsFromExpr(c.Value);
                    foreach (var cs in c.Body)
                        CollectArrowsFromStmt(cs);
                }
                if (s.DefaultBody != null)
                    foreach (var ds in s.DefaultBody)
                        CollectArrowsFromStmt(ds);
                break;
            case Stmt.TryCatch t:
                foreach (var ts in t.TryBlock)
                    CollectArrowsFromStmt(ts);
                if (t.CatchBlock != null)
                    foreach (var cs in t.CatchBlock)
                        CollectArrowsFromStmt(cs);
                if (t.FinallyBlock != null)
                    foreach (var fs in t.FinallyBlock)
                        CollectArrowsFromStmt(fs);
                break;
            case Stmt.Throw th:
                CollectArrowsFromExpr(th.Value);
                break;
            case Stmt.Print p:
                CollectArrowsFromExpr(p.Expr);
                break;
        }
    }

    private void CollectArrowsFromExpr(Expr expr)
    {
        switch (expr)
        {
            case Expr.ArrowFunction af:
                var captures = _closureAnalyzer.GetCaptures(af);
                _collectedArrows.Add((af, captures));
                // Also collect arrows inside this arrow's body
                if (af.ExpressionBody != null)
                    CollectArrowsFromExpr(af.ExpressionBody);
                if (af.BlockBody != null)
                    foreach (var s in af.BlockBody)
                        CollectArrowsFromStmt(s);
                break;
            case Expr.Binary b:
                CollectArrowsFromExpr(b.Left);
                CollectArrowsFromExpr(b.Right);
                break;
            case Expr.Logical l:
                CollectArrowsFromExpr(l.Left);
                CollectArrowsFromExpr(l.Right);
                break;
            case Expr.Unary u:
                CollectArrowsFromExpr(u.Right);
                break;
            case Expr.Grouping g:
                CollectArrowsFromExpr(g.Expression);
                break;
            case Expr.Call c:
                CollectArrowsFromExpr(c.Callee);
                foreach (var arg in c.Arguments)
                    CollectArrowsFromExpr(arg);
                break;
            case Expr.Get g:
                CollectArrowsFromExpr(g.Object);
                break;
            case Expr.Set s:
                CollectArrowsFromExpr(s.Object);
                CollectArrowsFromExpr(s.Value);
                break;
            case Expr.GetIndex gi:
                CollectArrowsFromExpr(gi.Object);
                CollectArrowsFromExpr(gi.Index);
                break;
            case Expr.SetIndex si:
                CollectArrowsFromExpr(si.Object);
                CollectArrowsFromExpr(si.Index);
                CollectArrowsFromExpr(si.Value);
                break;
            case Expr.Assign a:
                CollectArrowsFromExpr(a.Value);
                break;
            case Expr.New n:
                foreach (var arg in n.Arguments)
                    CollectArrowsFromExpr(arg);
                break;
            case Expr.ArrayLiteral a:
                foreach (var elem in a.Elements)
                    CollectArrowsFromExpr(elem);
                break;
            case Expr.ObjectLiteral o:
                foreach (var prop in o.Properties)
                    CollectArrowsFromExpr(prop.Value);
                break;
            case Expr.Ternary t:
                CollectArrowsFromExpr(t.Condition);
                CollectArrowsFromExpr(t.ThenBranch);
                CollectArrowsFromExpr(t.ElseBranch);
                break;
            case Expr.NullishCoalescing nc:
                CollectArrowsFromExpr(nc.Left);
                CollectArrowsFromExpr(nc.Right);
                break;
            case Expr.TemplateLiteral tl:
                foreach (var e in tl.Expressions)
                    CollectArrowsFromExpr(e);
                break;
            case Expr.CompoundAssign ca:
                CollectArrowsFromExpr(ca.Value);
                break;
            case Expr.CompoundSet cs:
                CollectArrowsFromExpr(cs.Object);
                CollectArrowsFromExpr(cs.Value);
                break;
            case Expr.CompoundSetIndex csi:
                CollectArrowsFromExpr(csi.Object);
                CollectArrowsFromExpr(csi.Index);
                CollectArrowsFromExpr(csi.Value);
                break;
            case Expr.PrefixIncrement pi:
                CollectArrowsFromExpr(pi.Operand);
                break;
            case Expr.PostfixIncrement poi:
                CollectArrowsFromExpr(poi.Operand);
                break;
        }
    }

    private void EmitArrowFunctionBodies()
    {
        foreach (var (arrow, captures) in _collectedArrows)
        {
            var methodBuilder = _arrowMethods[arrow];

            if (captures.Count == 0)
            {
                // Non-capturing: emit body into static method
                EmitArrowBody(arrow, methodBuilder, null);
            }
            else
            {
                // Capturing: emit body into display class method
                var displayClass = _displayClasses[arrow];
                EmitArrowBody(arrow, methodBuilder, displayClass);
            }
        }
    }

    private void EmitArrowBody(Expr.ArrowFunction arrow, MethodBuilder method, TypeBuilder? displayClass)
    {
        var il = method.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction
        };

        if (displayClass != null)
        {
            // Instance method on display class - this is arg 0
            ctx.IsInstanceMethod = true;

            // Use the pre-stored field mapping
            if (_displayClassFields.TryGetValue(arrow, out var fieldMap))
            {
                ctx.CapturedFields = fieldMap;
            }
            else
            {
                ctx.CapturedFields = [];
            }

            // Parameters start at index 1
            for (int i = 0; i < arrow.Parameters.Count; i++)
            {
                ctx.DefineParameter(arrow.Parameters[i].Name.Lexeme, i + 1);
            }
        }
        else
        {
            // Static method - parameters start at index 0
            for (int i = 0; i < arrow.Parameters.Count; i++)
            {
                ctx.DefineParameter(arrow.Parameters[i].Name.Lexeme, i);
            }
        }

        var emitter = new ILEmitter(ctx);

        // Emit default parameter checks
        emitter.EmitDefaultParameters(arrow.Parameters, displayClass != null);

        if (arrow.ExpressionBody != null)
        {
            // Expression body: emit expression and return
            emitter.EmitExpression(arrow.ExpressionBody);
            emitter.EmitBoxIfNeeded(arrow.ExpressionBody);
            il.Emit(OpCodes.Ret);
        }
        else if (arrow.BlockBody != null)
        {
            // Block body: emit statements
            foreach (var stmt in arrow.BlockBody)
            {
                emitter.EmitStatement(stmt);
            }
            // Finalize any deferred returns from exception blocks
            if (emitter.HasDeferredReturns)
            {
                emitter.FinalizeReturns();
            }
            else
            {
                // Default return null
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ret);
            }
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    #endregion

    private void DefineClass(Stmt.Class classStmt)
    {
        Type? baseType = null;
        if (classStmt.Superclass != null && _classBuilders.TryGetValue(classStmt.Superclass.Lexeme, out var superBuilder))
        {
            baseType = superBuilder;
        }

        // Set TypeAttributes.Abstract if the class is abstract
        TypeAttributes typeAttrs = TypeAttributes.Public | TypeAttributes.Class;
        if (classStmt.IsAbstract)
        {
            typeAttrs |= TypeAttributes.Abstract;
        }

        var typeBuilder = _moduleBuilder.DefineType(
            classStmt.Name.Lexeme,
            typeAttrs,
            baseType
        );

        // Handle generic type parameters
        if (classStmt.TypeParams != null && classStmt.TypeParams.Count > 0)
        {
            string[] typeParamNames = classStmt.TypeParams.Select(tp => tp.Name.Lexeme).ToArray();
            var genericParams = typeBuilder.DefineGenericParameters(typeParamNames);

            // Apply constraints
            for (int i = 0; i < classStmt.TypeParams.Count; i++)
            {
                var constraint = classStmt.TypeParams[i].Constraint;
                if (constraint != null)
                {
                    Type constraintType = ResolveConstraintType(constraint);
                    if (constraintType.IsInterface)
                        genericParams[i].SetInterfaceConstraints(constraintType);
                    else
                        genericParams[i].SetBaseTypeConstraint(constraintType);
                }
            }

            _classGenericParams[classStmt.Name.Lexeme] = genericParams;
        }

        // Add _fields dictionary for dynamic property storage
        var fieldsField = typeBuilder.DefineField(
            "_fields",
            typeof(Dictionary<string, object>),
            FieldAttributes.Private
        );
        _instanceFieldsField[classStmt.Name.Lexeme] = fieldsField;

        // Add static fields for static properties
        var staticFieldBuilders = new Dictionary<string, FieldBuilder>();
        foreach (var field in classStmt.Fields)
        {
            if (field.IsStatic)
            {
                var fieldBuilder = typeBuilder.DefineField(
                    field.Name.Lexeme,
                    typeof(object),
                    FieldAttributes.Public | FieldAttributes.Static
                );
                staticFieldBuilders[field.Name.Lexeme] = fieldBuilder;
            }
        }

        _classBuilders[classStmt.Name.Lexeme] = typeBuilder;
        _staticFields[classStmt.Name.Lexeme] = staticFieldBuilders;
    }

    private void DefineFunction(Stmt.Function funcStmt)
    {
        var paramTypes = funcStmt.Parameters.Select(_ => typeof(object)).ToArray();
        var methodBuilder = _programType.DefineMethod(
            funcStmt.Name.Lexeme,
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            paramTypes
        );

        // Handle generic type parameters
        bool isGeneric = funcStmt.TypeParams != null && funcStmt.TypeParams.Count > 0;
        _isGenericFunction[funcStmt.Name.Lexeme] = isGeneric;

        if (isGeneric)
        {
            string[] typeParamNames = funcStmt.TypeParams!.Select(tp => tp.Name.Lexeme).ToArray();
            var genericParams = methodBuilder.DefineGenericParameters(typeParamNames);

            // Apply constraints
            for (int i = 0; i < funcStmt.TypeParams!.Count; i++)
            {
                var constraint = funcStmt.TypeParams[i].Constraint;
                if (constraint != null)
                {
                    Type constraintType = ResolveConstraintType(constraint);
                    if (constraintType.IsInterface)
                        genericParams[i].SetInterfaceConstraints(constraintType);
                    else
                        genericParams[i].SetBaseTypeConstraint(constraintType);
                }
            }

            _functionGenericParams[funcStmt.Name.Lexeme] = genericParams;
        }

        _functionBuilders[funcStmt.Name.Lexeme] = methodBuilder;

        // Track rest parameter info
        var restParam = funcStmt.Parameters.FirstOrDefault(p => p.IsRest);
        if (restParam != null)
        {
            int restIndex = funcStmt.Parameters.IndexOf(restParam);
            int regularCount = funcStmt.Parameters.Count(p => !p.IsRest);
            _functionRestParams[funcStmt.Name.Lexeme] = (restIndex, regularCount);
        }
    }

    private void DefineEnum(Stmt.Enum enumStmt)
    {
        Dictionary<string, object> members = [];
        Dictionary<double, string> reverse = [];
        double? currentNumericValue = null;
        bool hasNumeric = false;
        bool hasString = false;

        foreach (var member in enumStmt.Members)
        {
            if (member.Value is Expr.Literal lit)
            {
                if (lit.Value is double d)
                {
                    members[member.Name.Lexeme] = d;
                    reverse[d] = member.Name.Lexeme;  // Only numeric values get reverse mapping
                    currentNumericValue = d + 1;
                    hasNumeric = true;
                }
                else if (lit.Value is string s)
                {
                    members[member.Name.Lexeme] = s;
                    // No reverse mapping for string values
                    hasString = true;
                }
            }
            else if (enumStmt.IsConst && member.Value != null)
            {
                // Const enums support computed expressions - evaluate at compile time
                var computedValue = EvaluateConstEnumExpression(member.Value, members, enumStmt.Name.Lexeme);
                if (computedValue is double d)
                {
                    members[member.Name.Lexeme] = d;
                    reverse[d] = member.Name.Lexeme;
                    currentNumericValue = d + 1;
                    hasNumeric = true;
                }
                else if (computedValue is string s)
                {
                    members[member.Name.Lexeme] = s;
                    hasString = true;
                }
            }
            else if (member.Value == null)
            {
                // Auto-increment
                currentNumericValue ??= 0;
                members[member.Name.Lexeme] = currentNumericValue.Value;
                reverse[currentNumericValue.Value] = member.Name.Lexeme;
                hasNumeric = true;
                currentNumericValue++;
            }
        }

        EnumKind kind = (hasNumeric, hasString) switch
        {
            (true, false) => EnumKind.Numeric,
            (false, true) => EnumKind.String,
            (true, true) => EnumKind.Heterogeneous,
            _ => EnumKind.Numeric
        };

        _enumMembers[enumStmt.Name.Lexeme] = members;
        _enumReverse[enumStmt.Name.Lexeme] = reverse;
        _enumKinds[enumStmt.Name.Lexeme] = kind;

        // Track const enums
        if (enumStmt.IsConst)
        {
            _constEnums.Add(enumStmt.Name.Lexeme);
        }
    }

    /// <summary>
    /// Evaluates a constant expression for const enum members during compilation.
    /// </summary>
    private object EvaluateConstEnumExpression(Expr expr, Dictionary<string, object> resolvedMembers, string enumName)
    {
        return expr switch
        {
            Expr.Literal lit => lit.Value ?? throw new Exception($"Compile Error: Const enum expression cannot be null."),

            Expr.Get g when g.Object is Expr.Variable v && v.Name.Lexeme == enumName =>
                resolvedMembers.TryGetValue(g.Name.Lexeme, out var val)
                    ? val
                    : throw new Exception($"Compile Error: Const enum member '{g.Name.Lexeme}' referenced before definition."),

            Expr.Grouping gr => EvaluateConstEnumExpression(gr.Expression, resolvedMembers, enumName),

            Expr.Unary u => EvaluateConstEnumUnary(u, resolvedMembers, enumName),

            Expr.Binary b => EvaluateConstEnumBinary(b, resolvedMembers, enumName),

            _ => throw new Exception($"Compile Error: Expression type '{expr.GetType().Name}' is not allowed in const enum initializer.")
        };
    }

    private object EvaluateConstEnumUnary(Expr.Unary unary, Dictionary<string, object> resolvedMembers, string enumName)
    {
        var operand = EvaluateConstEnumExpression(unary.Right, resolvedMembers, enumName);

        return unary.Operator.Type switch
        {
            TokenType.MINUS when operand is double d => -d,
            TokenType.PLUS when operand is double d => d,
            TokenType.TILDE when operand is double d => (double)(~(int)d),
            _ => throw new Exception($"Compile Error: Operator '{unary.Operator.Lexeme}' is not allowed in const enum expressions.")
        };
    }

    private object EvaluateConstEnumBinary(Expr.Binary binary, Dictionary<string, object> resolvedMembers, string enumName)
    {
        var left = EvaluateConstEnumExpression(binary.Left, resolvedMembers, enumName);
        var right = EvaluateConstEnumExpression(binary.Right, resolvedMembers, enumName);

        if (left is double l && right is double r)
        {
            return binary.Operator.Type switch
            {
                TokenType.PLUS => l + r,
                TokenType.MINUS => l - r,
                TokenType.STAR => l * r,
                TokenType.SLASH => l / r,
                TokenType.PERCENT => l % r,
                TokenType.STAR_STAR => Math.Pow(l, r),
                TokenType.AMPERSAND => (double)((int)l & (int)r),
                TokenType.PIPE => (double)((int)l | (int)r),
                TokenType.CARET => (double)((int)l ^ (int)r),
                TokenType.LESS_LESS => (double)((int)l << (int)r),
                TokenType.GREATER_GREATER => (double)((int)l >> (int)r),
                _ => throw new Exception($"Compile Error: Operator '{binary.Operator.Lexeme}' is not allowed in const enum expressions.")
            };
        }

        if (left is string ls && right is string rs && binary.Operator.Type == TokenType.PLUS)
        {
            return ls + rs;
        }

        throw new Exception($"Compile Error: Invalid operand types for operator '{binary.Operator.Lexeme}' in const enum expression.");
    }

    private void EmitClassMethods(Stmt.Class classStmt)
    {
        var typeBuilder = _classBuilders[classStmt.Name.Lexeme];
        var fieldsField = _instanceFieldsField[classStmt.Name.Lexeme];

        // Initialize static methods dictionary for this class
        if (!_staticMethods.ContainsKey(classStmt.Name.Lexeme))
        {
            _staticMethods[classStmt.Name.Lexeme] = new Dictionary<string, MethodBuilder>();
        }

        // Define static methods first (so we can reference them in the static constructor)
        foreach (var method in classStmt.Methods)
        {
            if (method.IsStatic && method.Name.Lexeme != "constructor")
            {
                DefineStaticMethod(typeBuilder, classStmt.Name.Lexeme, method);
            }
        }

        // Emit static constructor for static property initializers
        EmitStaticConstructor(typeBuilder, classStmt);

        // Emit constructor
        EmitConstructor(typeBuilder, classStmt, fieldsField);

        // Emit method bodies
        foreach (var method in classStmt.Methods)
        {
            if (method.Name.Lexeme != "constructor")
            {
                if (method.IsStatic)
                {
                    EmitStaticMethodBody(classStmt.Name.Lexeme, method);
                }
                else
                {
                    EmitMethod(typeBuilder, method, fieldsField);
                }
            }
        }

        // Emit accessor methods
        if (classStmt.Accessors != null)
        {
            foreach (var accessor in classStmt.Accessors)
            {
                EmitAccessor(typeBuilder, accessor, fieldsField);
            }
        }
    }

    private void EmitAccessor(TypeBuilder typeBuilder, Stmt.Accessor accessor, FieldInfo fieldsField)
    {
        // Use naming convention: get_<propertyName> or set_<propertyName>
        string methodName = accessor.Kind.Type == TokenType.GET
            ? $"get_{accessor.Name.Lexeme}"
            : $"set_{accessor.Name.Lexeme}";

        Type[] paramTypes = accessor.Kind.Type == TokenType.SET
            ? [typeof(object)]  // Setter takes one parameter
            : [];                // Getter takes no parameters

        // For abstract accessors, use Abstract | Virtual attributes
        MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.Virtual;
        if (accessor.IsAbstract)
        {
            methodAttrs |= MethodAttributes.Abstract;
        }

        var methodBuilder = typeBuilder.DefineMethod(
            methodName,
            methodAttrs,
            typeof(object),
            paramTypes
        );

        // Abstract accessors have no body
        if (accessor.IsAbstract)
        {
            return;
        }

        var il = methodBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            FieldsField = fieldsField,
            IsInstanceMethod = true,
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction
        };

        // Add class generic type parameters to context
        if (_classGenericParams.TryGetValue(typeBuilder.Name, out var classGenericParams))
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Define setter parameter if applicable
        if (accessor.Kind.Type == TokenType.SET && accessor.SetterParam != null)
        {
            ctx.DefineParameter(accessor.SetterParam.Name.Lexeme, 1);
        }

        var emitter = new ILEmitter(ctx);

        foreach (var stmt in accessor.Body)
        {
            emitter.EmitStatement(stmt);
        }

        // Finalize any deferred returns from exception blocks
        if (emitter.HasDeferredReturns)
        {
            emitter.FinalizeReturns();
        }
        else
        {
            // Default return null
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    private void DefineStaticMethod(TypeBuilder typeBuilder, string className, Stmt.Function method)
    {
        var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();
        var methodBuilder = typeBuilder.DefineMethod(
            method.Name.Lexeme,
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(object),
            paramTypes
        );

        _staticMethods[className][method.Name.Lexeme] = methodBuilder;
    }

    private void EmitStaticConstructor(TypeBuilder typeBuilder, Stmt.Class classStmt)
    {
        // Only emit if there are static fields with initializers
        var staticFieldsWithInit = classStmt.Fields.Where(f => f.IsStatic && f.Initializer != null).ToList();
        if (staticFieldsWithInit.Count == 0) return;

        var cctor = typeBuilder.DefineConstructor(
            MethodAttributes.Static | MethodAttributes.Private | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            CallingConventions.Standard,
            Type.EmptyTypes
        );

        var il = cctor.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            CurrentClassBuilder = typeBuilder,
            StaticFields = _staticFields,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction
        };

        var emitter = new ILEmitter(ctx);

        var classStaticFields = _staticFields[classStmt.Name.Lexeme];
        foreach (var field in staticFieldsWithInit)
        {
            // Emit the initializer expression
            emitter.EmitExpression(field.Initializer!);
            emitter.EmitBoxIfNeeded(field.Initializer!);

            // Store in static field using the stored FieldBuilder
            var staticField = classStaticFields[field.Name.Lexeme];
            il.Emit(OpCodes.Stsfld, staticField);
        }

        il.Emit(OpCodes.Ret);
    }

    private void EmitStaticMethodBody(string className, Stmt.Function method)
    {
        var typeBuilder = _classBuilders[className];
        var methodBuilder = _staticMethods[className][method.Name.Lexeme];

        var il = methodBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            IsInstanceMethod = false,
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            CurrentClassBuilder = typeBuilder,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction
        };

        // Define parameters (starting at index 0, not 1 since no 'this')
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i);
        }

        var emitter = new ILEmitter(ctx);

        // Emit default parameter checks (static method)
        emitter.EmitDefaultParameters(method.Parameters, false);

        foreach (var stmt in method.Body)
        {
            emitter.EmitStatement(stmt);
        }

        // Finalize any deferred returns from exception blocks
        if (emitter.HasDeferredReturns)
        {
            emitter.FinalizeReturns();
        }
        else
        {
            // Default return null
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    private void EmitConstructor(TypeBuilder typeBuilder, Stmt.Class classStmt, FieldInfo fieldsField)
    {
        var constructor = classStmt.Methods.FirstOrDefault(m => m.Name.Lexeme == "constructor");
        var paramTypes = constructor?.Parameters.Select(_ => typeof(object)).ToArray() ?? [];

        var ctorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            paramTypes
        );

        // Store constructor builder for use in EmitNew
        _classConstructors[classStmt.Name.Lexeme] = ctorBuilder;

        var il = ctorBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            CurrentSuperclassName = classStmt.Superclass?.Lexeme,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction
        };

        // Add class generic type parameters to context
        if (_classGenericParams.TryGetValue(classStmt.Name.Lexeme, out var classGenericParams))
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Initialize _fields dictionary FIRST (before calling parent constructor)
        // This allows parent constructor to access fields via SetFieldsProperty
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Newobj, typeof(Dictionary<string, object>).GetConstructor([])!);
        il.Emit(OpCodes.Stfld, fieldsField);

        // Call parent constructor
        // If the class has an explicit constructor with super(), the super() in body will handle it.
        // If the class has no explicit constructor but has a superclass, we must call the parent constructor.
        // If the class has no superclass, we call Object constructor.
        if (constructor == null && classStmt.Superclass != null && _classConstructors.TryGetValue(classStmt.Superclass.Lexeme, out var parentCtor))
        {
            // No explicit constructor but has superclass - call parent's parameterless constructor
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, parentCtor);
        }
        else
        {
            // Has explicit constructor (which should have super() call) or no superclass
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor([])!);
        }

        // Emit constructor body
        if (constructor != null)
        {
            ctx.FieldsField = fieldsField;
            ctx.IsInstanceMethod = true;

            // Define parameters
            for (int i = 0; i < constructor.Parameters.Count; i++)
            {
                ctx.DefineParameter(constructor.Parameters[i].Name.Lexeme, i + 1);
            }

            var emitter = new ILEmitter(ctx);

            // Emit default parameter checks (instance method)
            emitter.EmitDefaultParameters(constructor.Parameters, true);

            foreach (var stmt in constructor.Body)
            {
                emitter.EmitStatement(stmt);
            }
        }

        il.Emit(OpCodes.Ret);
    }

    private void EmitMethod(TypeBuilder typeBuilder, Stmt.Function method, FieldInfo fieldsField)
    {
        var paramTypes = method.Parameters.Select(_ => typeof(object)).ToArray();

        // For abstract methods, use Abstract | Virtual attributes
        MethodAttributes methodAttrs = MethodAttributes.Public | MethodAttributes.Virtual;
        if (method.IsAbstract)
        {
            methodAttrs |= MethodAttributes.Abstract;
        }

        var methodBuilder = typeBuilder.DefineMethod(
            method.Name.Lexeme,
            methodAttrs,
            typeof(object),
            paramTypes
        );

        // Abstract methods have no body
        if (method.IsAbstract)
        {
            return;
        }

        var il = methodBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            FieldsField = fieldsField,
            IsInstanceMethod = true,
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction
        };

        // Add class generic type parameters to context
        if (_classGenericParams.TryGetValue(typeBuilder.Name, out var classGenericParams))
        {
            foreach (var gp in classGenericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Define parameters
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            ctx.DefineParameter(method.Parameters[i].Name.Lexeme, i + 1);
        }

        var emitter = new ILEmitter(ctx);

        // Emit default parameter checks (instance method)
        emitter.EmitDefaultParameters(method.Parameters, true);

        foreach (var stmt in method.Body)
        {
            emitter.EmitStatement(stmt);
        }

        // Finalize any deferred returns from exception blocks
        if (emitter.HasDeferredReturns)
        {
            emitter.FinalizeReturns();
        }
        else
        {
            // Default return null
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    private void EmitFunctionBody(Stmt.Function funcStmt)
    {
        var methodBuilder = _functionBuilders[funcStmt.Name.Lexeme];
        var il = methodBuilder.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction
        };

        // Add generic type parameters to context if this is a generic function
        if (_functionGenericParams.TryGetValue(funcStmt.Name.Lexeme, out var genericParams))
        {
            foreach (var gp in genericParams)
                ctx.GenericTypeParameters[gp.Name] = gp;
        }

        // Define parameters
        for (int i = 0; i < funcStmt.Parameters.Count; i++)
        {
            ctx.DefineParameter(funcStmt.Parameters[i].Name.Lexeme, i);
        }

        var emitter = new ILEmitter(ctx);

        // Emit default parameter checks (static function, not instance method)
        emitter.EmitDefaultParameters(funcStmt.Parameters, false);

        foreach (var stmt in funcStmt.Body)
        {
            emitter.EmitStatement(stmt);
        }

        // Finalize any deferred returns from exception blocks
        if (emitter.HasDeferredReturns)
        {
            emitter.FinalizeReturns();
        }
        else
        {
            // Default return null
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ret);
        }
    }

    private MethodBuilder? _entryPoint;

    private void EmitEntryPoint(List<Stmt> statements)
    {
        var mainMethod = _programType.DefineMethod(
            "Main",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            Type.EmptyTypes
        );

        _entryPoint = mainMethod;

        var il = mainMethod.GetILGenerator();
        var ctx = new CompilationContext(il, _typeMapper, _functionBuilders, _classBuilders)
        {
            ClosureAnalyzer = _closureAnalyzer,
            ArrowMethods = _arrowMethods,
            DisplayClasses = _displayClasses,
            DisplayClassFields = _displayClassFields,
            DisplayClassConstructors = _displayClassConstructors,
            StaticFields = _staticFields,
            StaticMethods = _staticMethods,
            ClassConstructors = _classConstructors,
            FunctionRestParams = _functionRestParams,
            EnumMembers = _enumMembers,
            EnumReverse = _enumReverse,
            EnumKinds = _enumKinds,
            Runtime = _runtime,
            ClassGenericParams = _classGenericParams,
            FunctionGenericParams = _functionGenericParams,
            IsGenericFunction = _isGenericFunction
        };
        var emitter = new ILEmitter(ctx);

        foreach (var stmt in statements)
        {
            // Skip class, function, interface, and enum declarations (already handled)
            if (stmt is Stmt.Class or Stmt.Function or Stmt.Interface or Stmt.Enum)
            {
                continue;
            }
            emitter.EmitStatement(stmt);
        }

        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Resolves a constraint type name to a .NET Type.
    /// </summary>
    private Type ResolveConstraintType(string constraint)
    {
        return constraint switch
        {
            "number" => typeof(double),
            "string" => typeof(string),
            "boolean" => typeof(bool),
            _ when _classBuilders.TryGetValue(constraint, out var tb) => tb,
            _ => typeof(object)
        };
    }

    public void Save(string outputPath)
    {
        // Generate metadata for the assembly
        MetadataBuilder metadataBuilder = _assemblyBuilder.GenerateMetadata(
            out BlobBuilder ilStream,
            out BlobBuilder fieldData);

        // Create an executable with entry point
        PEHeaderBuilder peHeader = PEHeaderBuilder.CreateExecutableHeader();

        ManagedPEBuilder peBuilder = new(
            header: peHeader,
            metadataRootBuilder: new MetadataRootBuilder(metadataBuilder),
            ilStream: ilStream,
            mappedFieldData: fieldData,
            entryPoint: _entryPoint != null
                ? MetadataTokens.MethodDefinitionHandle(_entryPoint.MetadataToken)
                : default);

        BlobBuilder peBlob = new();
        peBuilder.Serialize(peBlob);

        // Write the executable
        using FileStream fileStream = new(outputPath, FileMode.Create, FileAccess.Write);
        peBlob.WriteContentTo(fileStream);
    }
}
