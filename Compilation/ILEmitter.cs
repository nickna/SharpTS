using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Emits IL instructions for AST statements and expressions.
/// </summary>
/// <remarks>
/// Core code generation component used by <see cref="ILCompiler"/>. Traverses AST nodes
/// and emits corresponding IL opcodes via <see cref="ILGenerator"/>. Handles all expression
/// types (literals, binary ops, calls, property access) and statement types (if, while,
/// try/catch, return). Uses <see cref="CompilationContext"/> to track locals, parameters,
/// and the current <see cref="ILGenerator"/>. Supports closures via display class field access.
/// </remarks>
/// <seealso cref="ILCompiler"/>
/// <seealso cref="CompilationContext"/>
public partial class ILEmitter
{
    private readonly CompilationContext _ctx;
    private ILGenerator IL => _ctx.IL;

    /// <summary>
    /// Current type on top of the IL evaluation stack.
    /// Used for unboxed numeric optimization.
    /// </summary>
    private StackType _stackType = StackType.Unknown;

    public ILEmitter(CompilationContext ctx)
    {
        _ctx = ctx;
    }

    #region Stack Type Tracking

    /// <summary>
    /// Returns the stack type that an expression will produce based on TypeMap.
    /// </summary>
    private StackType GetExpressionStackType(Expr expr)
    {
        var type = _ctx.TypeMap?.Get(expr);
        return type switch
        {
            TypeSystem.TypeInfo.Primitive { Type: TokenType.TYPE_NUMBER } => StackType.Double,
            TypeSystem.TypeInfo.Primitive { Type: TokenType.TYPE_BOOLEAN } => StackType.Boolean,
            TypeSystem.TypeInfo.Primitive { Type: TokenType.TYPE_STRING } => StackType.String,
            TypeSystem.TypeInfo.Null => StackType.Null,
            _ => StackType.Unknown
        };
    }

    /// <summary>
    /// Ensures the value on stack is boxed as object.
    /// Only emits boxing IL if current stack type is a value type.
    /// </summary>
    private void EnsureBoxed()
    {
        switch (_stackType)
        {
            case StackType.Double:
                IL.Emit(OpCodes.Box, typeof(double));
                _stackType = StackType.Unknown;
                break;
            case StackType.Boolean:
                IL.Emit(OpCodes.Box, typeof(bool));
                _stackType = StackType.Unknown;
                break;
            // String, Null, Unknown are already reference types - no boxing needed
        }
    }

    /// <summary>
    /// Ensures the value on stack is an unboxed double.
    /// Only emits unboxing IL if stack is not already a double.
    /// </summary>
    private void EnsureDouble()
    {
        if (_stackType != StackType.Double)
        {
            IL.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
            _stackType = StackType.Double;
        }
    }

    /// <summary>
    /// Ensures the value on stack is an unboxed boolean (int32 0 or 1).
    /// Only emits conversion IL if stack is not already a boolean.
    /// </summary>
    private void EnsureBoolean()
    {
        if (_stackType != StackType.Boolean)
        {
            IL.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToBoolean", [typeof(object)])!);
            _stackType = StackType.Boolean;
        }
    }

    /// <summary>
    /// Emits a boxed double constant and sets _stackType.
    /// Use this helper to avoid forgetting to set _stackType after boxing.
    /// </summary>
    private void EmitBoxedDoubleConstant(double value)
    {
        IL.Emit(OpCodes.Ldc_R8, value);
        IL.Emit(OpCodes.Box, typeof(double));
        _stackType = StackType.Unknown;
    }

    /// <summary>
    /// Emits a string constant and sets _stackType.
    /// Use this helper to avoid forgetting to set _stackType after loading a string.
    /// </summary>
    private void EmitStringConstant(string value)
    {
        IL.Emit(OpCodes.Ldstr, value);
        _stackType = StackType.String;
    }

    #endregion

    /// <summary>
    /// Finalize return handling for methods that had returns inside exception blocks.
    /// Must be called after emitting the method body but before the final Ret.
    /// </summary>
    public void FinalizeReturns()
    {
        if (_ctx.ReturnValueLocal != null)
        {
            // Mark the return label and emit the actual return
            IL.MarkLabel(_ctx.ReturnLabel);
            IL.Emit(OpCodes.Ldloc, _ctx.ReturnValueLocal);
            IL.Emit(OpCodes.Ret);
        }
    }

    /// <summary>
    /// Check if the method had returns inside exception blocks that need finalization.
    /// </summary>
    public bool HasDeferredReturns => _ctx.ReturnValueLocal != null;

    /// <summary>
    /// Emit default parameter value checks at function entry.
    /// For each parameter with a default value, checks if arg is null and assigns default.
    /// </summary>
    public void EmitDefaultParameters(List<Stmt.Parameter> parameters, bool isInstanceMethod)
    {
        int argOffset = isInstanceMethod ? 1 : 0;

        for (int i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];
            if (param.DefaultValue == null) continue;

            int argIndex = i + argOffset;

            // if (arg == null) { arg = <default>; }
            var skipDefault = IL.DefineLabel();

            // Load argument and check if null
            IL.Emit(OpCodes.Ldarg, argIndex);
            IL.Emit(OpCodes.Brtrue, skipDefault);

            // Argument is null, emit default value and store
            EmitExpression(param.DefaultValue);
            EmitBoxIfNeeded(param.DefaultValue);
            IL.Emit(OpCodes.Starg, argIndex);

            IL.MarkLabel(skipDefault);
        }
    }

    public void EmitStatement(Stmt stmt)
    {
        // Skip dead statements (unreachable code)
        if (_ctx.DeadCode?.IsDead(stmt) == true)
            return;

        switch (stmt)
        {
            case Stmt.Expression e:
                EmitExpression(e.Expr);
                // All expressions leave a value on the stack, so pop when used as a statement
                IL.Emit(OpCodes.Pop);
                break;

            case Stmt.Var v:
                EmitVarDeclaration(v);
                break;

            case Stmt.If i:
                EmitIf(i);
                break;

            case Stmt.While w:
                EmitWhile(w);
                break;

            case Stmt.DoWhile dw:
                EmitDoWhile(dw);
                break;

            case Stmt.ForOf f:
                EmitForOf(f);
                break;

            case Stmt.ForIn fi:
                EmitForIn(fi);
                break;

            case Stmt.Block b:
                EmitBlock(b);
                break;

            case Stmt.Sequence seq:
                // Execute in current scope (no new environment)
                foreach (var s in seq.Statements)
                    EmitStatement(s);
                break;

            case Stmt.Return r:
                EmitReturn(r);
                break;

            case Stmt.Break breakStmt:
                EmitBreak(breakStmt.Label?.Lexeme);
                break;

            case Stmt.Continue continueStmt:
                EmitContinue(continueStmt.Label?.Lexeme);
                break;

            case Stmt.LabeledStatement labeledStmt:
                EmitLabeledStatement(labeledStmt);
                break;

            case Stmt.Switch s:
                EmitSwitch(s);
                break;

            case Stmt.TryCatch t:
                EmitTryCatch(t);
                break;

            case Stmt.Throw t:
                EmitThrow(t);
                break;

            case Stmt.Print p:
                EmitPrint(p);
                break;

            case Stmt.Function:
            case Stmt.Class:
            case Stmt.Interface:
            case Stmt.TypeAlias:
            case Stmt.Enum:
                // Handled at top level / compile-time only
                break;

            case Stmt.Namespace ns:
                EmitNamespace(ns);
                break;

            case Stmt.Import import:
                EmitImport(import);
                break;

            case Stmt.Export export:
                EmitExport(export);
                break;
        }
    }

    public void EmitExpression(Expr expr)
    {
        switch (expr)
        {
            case Expr.Literal lit:
                EmitLiteral(lit);
                break;

            case Expr.Variable v:
                EmitVariable(v);
                break;

            case Expr.Assign a:
                EmitAssign(a);
                break;

            case Expr.Binary b:
                EmitBinary(b);
                break;

            case Expr.Logical l:
                EmitLogical(l);
                break;

            case Expr.Unary u:
                EmitUnary(u);
                break;

            case Expr.Grouping g:
                EmitExpression(g.Expression);
                break;

            case Expr.Call c:
                EmitCall(c);
                break;

            case Expr.New n:
                EmitNew(n);
                break;

            case Expr.Get g:
                EmitGet(g);
                break;

            case Expr.Set s:
                EmitSet(s);
                break;

            case Expr.GetIndex gi:
                EmitGetIndex(gi);
                break;

            case Expr.SetIndex si:
                EmitSetIndex(si);
                break;

            case Expr.This:
                EmitThis();
                break;

            case Expr.ArrayLiteral a:
                EmitArrayLiteral(a);
                break;

            case Expr.ObjectLiteral o:
                EmitObjectLiteral(o);
                break;

            case Expr.Ternary t:
                EmitTernary(t);
                break;

            case Expr.NullishCoalescing nc:
                EmitNullishCoalescing(nc);
                break;

            case Expr.TemplateLiteral tl:
                EmitTemplateLiteral(tl);
                break;

            case Expr.CompoundAssign ca:
                EmitCompoundAssign(ca);
                break;

            case Expr.CompoundSet cs:
                EmitCompoundSet(cs);
                break;

            case Expr.CompoundSetIndex csi:
                EmitCompoundSetIndex(csi);
                break;

            case Expr.PrefixIncrement pi:
                EmitPrefixIncrement(pi);
                break;

            case Expr.PostfixIncrement pi:
                EmitPostfixIncrement(pi);
                break;

            case Expr.ArrowFunction af:
                EmitArrowFunction(af);
                break;

            case Expr.Super s:
                EmitSuper(s);
                break;

            case Expr.Spread sp:
                // Spread expressions are handled in context (arrays, objects, calls)
                // If we get here directly, just emit the inner expression
                EmitExpression(sp.Expression);
                break;

            case Expr.TypeAssertion ta:
                // Type assertions are compile-time only, just emit the inner expression
                EmitExpression(ta.Expression);
                break;

            case Expr.RegexLiteral re:
                EmitRegexLiteral(re);
                break;

            case Expr.DynamicImport di:
                EmitDynamicImport(di);
                break;

            default:
                // Fallback: push null
                IL.Emit(OpCodes.Ldnull);
                break;
        }
    }

    public void EmitBoxIfNeeded(Expr expr)
    {
        // First, check if we already have an unboxed value type on the stack
        // This handles typed locals and other cases where _stackType is known
        if (_stackType == StackType.Double)
        {
            IL.Emit(OpCodes.Box, typeof(double));
            _stackType = StackType.Unknown;
            return;
        }
        if (_stackType == StackType.Boolean)
        {
            IL.Emit(OpCodes.Box, typeof(bool));
            _stackType = StackType.Unknown;
            return;
        }

        // Optimization: Use TypeMap to skip boxing check for known reference types
        // This avoids the pattern match overhead for expressions that definitely don't need boxing
        TypeSystem.TypeInfo? type = _ctx.TypeMap?.Get(expr);
        if (type != null)
        {
            // Reference types never need boxing - skip the literal check entirely
            if (type is TypeSystem.TypeInfo.Primitive { Type: TokenType.TYPE_STRING }
                or TypeSystem.TypeInfo.Array
                or TypeSystem.TypeInfo.Instance
                or TypeSystem.TypeInfo.Record
                or TypeSystem.TypeInfo.Class
                or TypeSystem.TypeInfo.Interface
                or TypeSystem.TypeInfo.Function
                or TypeSystem.TypeInfo.Void
                or TypeSystem.TypeInfo.Null)
            {
                return;
            }
            // For primitives (number/boolean) and other types (Any, Union, etc.),
            // fall through to the literal check - only literals produce unboxed values
        }

        // Only Expr.Literal with double/bool produces unboxed value types on the stack.
        // All other expressions (Variable, Call, Binary, etc.) already produce boxed objects.
        // IMPORTANT: Never add boxing for non-literals - their results are already boxed,
        // and Box(double) on an object reference causes garbage output.
        if (expr is Expr.Literal lit)
        {
            if (lit.Value is double)
            {
                IL.Emit(OpCodes.Box, typeof(double));
                _stackType = StackType.Unknown;
            }
            else if (lit.Value is bool)
            {
                IL.Emit(OpCodes.Box, typeof(bool));
                _stackType = StackType.Unknown;
            }
        }
    }

    private void EmitExpressionAsDouble(Expr expr)
    {
        // Emit expression and ensure result is a double on the stack
        if (expr is Expr.Literal lit && lit.Value is double d)
        {
            // Literal double - push directly
            IL.Emit(OpCodes.Ldc_R8, d);
            _stackType = StackType.Double;
        }
        else if (expr is Expr.Literal intLit && intLit.Value is int i)
        {
            IL.Emit(OpCodes.Ldc_R8, (double)i);
            _stackType = StackType.Double;
        }
        else
        {
            // Other expressions - emit and convert if needed
            EmitExpression(expr);
            EnsureDouble();
        }
    }

    private void EmitUnboxToDouble()
    {
        // Convert object to double using Convert.ToDouble
        IL.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToDouble", [typeof(object)])!);
        _stackType = StackType.Double;
    }

    private bool IsStringExpression(Expr expr)
    {
        return expr switch
        {
            Expr.Literal lit => lit.Value is string,
            Expr.TemplateLiteral => true,
            Expr.Binary bin when bin.Operator.Type == TokenType.PLUS =>
                IsStringExpression(bin.Left) || IsStringExpression(bin.Right),
            _ => false
        };
    }

    private void EmitTruthyCheck()
    {
        // Truthy check for boxed value:
        // - null => false
        // - boxed false => false
        // - everything else => true
        var checkBoolLabel = IL.DefineLabel();
        var falseLabel = IL.DefineLabel();
        var endLabel = IL.DefineLabel();

        // Check for null
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Brfalse, falseLabel);

        // Check if it's a boolean
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Isinst, typeof(bool));
        IL.Emit(OpCodes.Brfalse, checkBoolLabel);

        // It's a boxed bool - unbox and use the value
        IL.Emit(OpCodes.Unbox_Any, typeof(bool));
        IL.Emit(OpCodes.Br, endLabel);

        IL.MarkLabel(checkBoolLabel);
        // Not null and not bool - always truthy
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Ldc_I4_1);
        IL.Emit(OpCodes.Br, endLabel);

        IL.MarkLabel(falseLabel);
        // Null - false
        IL.Emit(OpCodes.Pop);
        IL.Emit(OpCodes.Ldc_I4_0);

        IL.MarkLabel(endLabel);
    }

    private static bool IsComparisonOp(TokenType op) =>
        op is TokenType.LESS or TokenType.GREATER or TokenType.LESS_EQUAL or TokenType.GREATER_EQUAL
            or TokenType.EQUAL_EQUAL or TokenType.BANG_EQUAL
            or TokenType.EQUAL_EQUAL_EQUAL or TokenType.BANG_EQUAL_EQUAL;

    #region Module Support

    /// <summary>
    /// Emits code for an import statement.
    /// Imports bind local variables to module export fields.
    /// </summary>
    private void EmitImport(Stmt.Import import)
    {
        if (_ctx.CurrentModulePath == null || _ctx.ModuleResolver == null ||
            _ctx.ModuleExportFields == null || _ctx.ModuleTypes == null)
        {
            // Not in module context - imports are no-ops for single-file compilation
            return;
        }

        string importedPath = _ctx.ModuleResolver.ResolveModulePath(import.ModulePath, _ctx.CurrentModulePath);

        if (!_ctx.ModuleExportFields.TryGetValue(importedPath, out var exportFields) ||
            !_ctx.ModuleTypes.TryGetValue(importedPath, out var moduleType))
        {
            // Module not found - skip (type checker should have caught this)
            return;
        }

        // Default import: bind local variable to $default field
        if (import.DefaultImport != null)
        {
            string localName = import.DefaultImport.Lexeme;
            if (exportFields.TryGetValue("$default", out var defaultField))
            {
                var local = _ctx.Locals.GetLocal(localName) ?? _ctx.Locals.DeclareLocal(localName, typeof(object));
                IL.Emit(OpCodes.Ldsfld, defaultField);
                IL.Emit(OpCodes.Stloc, local);
            }
        }

        // Named imports: bind local variables to named export fields
        if (import.NamedImports != null)
        {
            foreach (var spec in import.NamedImports)
            {
                string importedName = spec.Imported.Lexeme;
                string localName = spec.LocalName?.Lexeme ?? importedName;

                if (exportFields.TryGetValue(importedName, out var field))
                {
                    var local = _ctx.Locals.GetLocal(localName) ?? _ctx.Locals.DeclareLocal(localName, typeof(object));
                    IL.Emit(OpCodes.Ldsfld, field);
                    IL.Emit(OpCodes.Stloc, local);
                }
            }
        }

        // Namespace import: create a SharpTSObject with all exports
        if (import.NamespaceImport != null)
        {
            string localName = import.NamespaceImport.Lexeme;
            var local = _ctx.Locals.GetLocal(localName) ?? _ctx.Locals.DeclareLocal(localName, typeof(object));

            // Create new Dictionary<string, object?>
            var dictType = typeof(Dictionary<string, object?>);
            var dictCtor = dictType.GetConstructor(Type.EmptyTypes)!;
            var addMethod = dictType.GetMethod("Add", [typeof(string), typeof(object)])!;

            IL.Emit(OpCodes.Newobj, dictCtor);

            // Add each export to the dictionary
            foreach (var (exportName, field) in exportFields)
            {
                if (exportName == "$default") continue; // Skip default export in namespace import

                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldstr, exportName);
                IL.Emit(OpCodes.Ldsfld, field);
                IL.Emit(OpCodes.Callvirt, addMethod);
            }

            // Call CreateObject to wrap the dictionary
            IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateObject);
            IL.Emit(OpCodes.Stloc, local);
        }
    }

    /// <summary>
    /// Emits code for an export statement.
    /// Exports store values into module export fields.
    /// </summary>
    private void EmitExport(Stmt.Export export)
    {
        if (_ctx.CurrentModulePath == null || _ctx.ModuleExportFields == null)
        {
            // Not in module context
            return;
        }

        if (!_ctx.ModuleExportFields.TryGetValue(_ctx.CurrentModulePath, out var exportFields))
        {
            return;
        }

        if (export.IsDefaultExport)
        {
            if (export.Declaration != null)
            {
                // export default class/function - execute declaration and store value
                EmitStatement(export.Declaration);

                string? name = GetDeclarationName(export.Declaration);
                if (name != null && exportFields.TryGetValue("$default", out var defaultField))
                {
                    // Load the declared value and store in export field
                    var local = _ctx.Locals.GetLocal(name);
                    if (local != null)
                    {
                        EmitStoreLocalToExportField(local, defaultField);
                    }
                    else if (_ctx.Functions.TryGetValue(name, out var funcBuilder))
                    {
                        // Create TSFunction for function
                        EmitFunctionReference(name, funcBuilder);
                        IL.Emit(OpCodes.Stsfld, defaultField);
                    }
                    else if (_ctx.Classes.TryGetValue(name, out var classBuilder))
                    {
                        // Store class type token
                        IL.Emit(OpCodes.Ldtoken, classBuilder);
                        IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
                        IL.Emit(OpCodes.Stsfld, defaultField);
                    }
                    else if (_ctx.EnumMembers?.TryGetValue(name, out var enumMembers) == true)
                    {
                        // Create SharpTSObject with enum members
                        EmitEnumAsObject(enumMembers);
                        IL.Emit(OpCodes.Stsfld, defaultField);
                    }
                }
            }
            else if (export.DefaultExpr != null)
            {
                // export default <expression>
                if (exportFields.TryGetValue("$default", out var defaultField))
                {
                    EmitExpression(export.DefaultExpr);
                    EnsureBoxed();
                    IL.Emit(OpCodes.Stsfld, defaultField);
                }
            }
        }
        else if (export.Declaration != null)
        {
            // export const/let/function/class - execute declaration and store in named field
            EmitStatement(export.Declaration);

            string? name = GetDeclarationName(export.Declaration);
            if (name != null && exportFields.TryGetValue(name, out var field))
            {
                var local = _ctx.Locals.GetLocal(name);
                if (local != null)
                {
                    EmitStoreLocalToExportField(local, field);
                }
                else if (_ctx.Functions.TryGetValue(name, out var funcBuilder))
                {
                    EmitFunctionReference(name, funcBuilder);
                    IL.Emit(OpCodes.Stsfld, field);
                }
                else if (_ctx.Classes.TryGetValue(name, out var classBuilder))
                {
                    IL.Emit(OpCodes.Ldtoken, classBuilder);
                    IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
                    IL.Emit(OpCodes.Stsfld, field);
                }
                else if (_ctx.EnumMembers?.TryGetValue(name, out var enumMembers) == true)
                {
                    // Create SharpTSObject with enum members
                    EmitEnumAsObject(enumMembers);
                    IL.Emit(OpCodes.Stsfld, field);
                }
            }
        }
        else if (export.NamedExports != null && export.FromModulePath == null)
        {
            // export { x, y as z }
            foreach (var spec in export.NamedExports)
            {
                string localName = spec.LocalName.Lexeme;
                string exportedName = spec.ExportedName?.Lexeme ?? localName;

                if (exportFields.TryGetValue(exportedName, out var field))
                {
                    var local = _ctx.Locals.GetLocal(localName);
                    if (local != null)
                    {
                        EmitStoreLocalToExportField(local, field);
                    }
                    else if (_ctx.Functions.TryGetValue(localName, out var funcBuilder))
                    {
                        EmitFunctionReference(localName, funcBuilder);
                        IL.Emit(OpCodes.Stsfld, field);
                    }
                    else if (_ctx.Classes.TryGetValue(localName, out var classBuilder))
                    {
                        IL.Emit(OpCodes.Ldtoken, classBuilder);
                        IL.Emit(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle")!);
                        IL.Emit(OpCodes.Stsfld, field);
                    }
                    else if (_ctx.EnumMembers?.TryGetValue(localName, out var enumMembers) == true)
                    {
                        // Create SharpTSObject with enum members
                        EmitEnumAsObject(enumMembers);
                        IL.Emit(OpCodes.Stsfld, field);
                    }
                }
            }
        }
        else if (export.FromModulePath != null && _ctx.ModuleResolver != null)
        {
            // Re-export: export { x } from './module' or export * from './module'
            string sourcePath = _ctx.ModuleResolver.ResolveModulePath(export.FromModulePath, _ctx.CurrentModulePath);

            if (_ctx.ModuleExportFields.TryGetValue(sourcePath, out var sourceFields))
            {
                if (export.NamedExports != null)
                {
                    // Re-export specific names
                    foreach (var spec in export.NamedExports)
                    {
                        string importedName = spec.LocalName.Lexeme;
                        string exportedName = spec.ExportedName?.Lexeme ?? importedName;

                        if (sourceFields.TryGetValue(importedName, out var sourceField) &&
                            exportFields.TryGetValue(exportedName, out var targetField))
                        {
                            IL.Emit(OpCodes.Ldsfld, sourceField);
                            IL.Emit(OpCodes.Stsfld, targetField);
                        }
                    }
                }
                else
                {
                    // Re-export all: export * from './module'
                    foreach (var (name, sourceField) in sourceFields)
                    {
                        if (name == "$default") continue; // Don't re-export default
                        if (exportFields.TryGetValue(name, out var targetField))
                        {
                            IL.Emit(OpCodes.Ldsfld, sourceField);
                            IL.Emit(OpCodes.Stsfld, targetField);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets the name declared by a statement.
    /// </summary>
    private static string? GetDeclarationName(Stmt decl) => decl switch
    {
        Stmt.Function f => f.Name.Lexeme,
        Stmt.Class c => c.Name.Lexeme,
        Stmt.Var v => v.Name.Lexeme,
        Stmt.Enum e => e.Name.Lexeme,
        _ => null
    };

    /// <summary>
    /// Stores a value from a local variable to an export field, boxing if necessary.
    /// </summary>
    private void EmitStoreLocalToExportField(LocalBuilder local, FieldBuilder field)
    {
        IL.Emit(OpCodes.Ldloc, local);
        if (local.LocalType.IsValueType)
        {
            IL.Emit(OpCodes.Box, local.LocalType);
        }
        IL.Emit(OpCodes.Stsfld, field);
    }

    /// <summary>
    /// Emits a TSFunction reference for a method (used for function exports).
    /// Creates: new TSFunction(null, methodInfo)
    /// </summary>
    private void EmitFunctionReference(string name, MethodBuilder method)
    {
        // Create TSFunction(null, methodInfo) - same pattern as arrow functions
        IL.Emit(OpCodes.Ldnull);  // target (null for static methods)
        IL.Emit(OpCodes.Ldtoken, method);
        IL.Emit(OpCodes.Call, typeof(System.Reflection.MethodBase).GetMethod(
            "GetMethodFromHandle", [typeof(RuntimeMethodHandle)])!);
        IL.Emit(OpCodes.Castclass, typeof(System.Reflection.MethodInfo));
        IL.Emit(OpCodes.Newobj, _ctx.Runtime!.TSFunctionCtor);
    }

    /// <summary>
    /// Emits an enum as a SharpTSObject with its member values.
    /// </summary>
    private void EmitEnumAsObject(Dictionary<string, object> members)
    {
        // Create new Dictionary<string, object?>()
        var dictType = typeof(Dictionary<string, object?>);
        var dictCtor = dictType.GetConstructor(Type.EmptyTypes)!;
        var addMethod = dictType.GetMethod("Add", [typeof(string), typeof(object)])!;

        IL.Emit(OpCodes.Newobj, dictCtor);

        foreach (var (memberName, value) in members)
        {
            IL.Emit(OpCodes.Dup);  // Keep dictionary on stack
            IL.Emit(OpCodes.Ldstr, memberName);
            if (value is double d)
            {
                IL.Emit(OpCodes.Ldc_R8, d);
                IL.Emit(OpCodes.Box, typeof(double));
            }
            else if (value is string s)
            {
                IL.Emit(OpCodes.Ldstr, s);
            }
            else
            {
                IL.Emit(OpCodes.Ldnull);
            }
            IL.Emit(OpCodes.Call, addMethod);
        }

        // Wrap in SharpTSObject using the CreateObject helper
        IL.Emit(OpCodes.Call, _ctx.Runtime!.CreateObject);
    }

    /// <summary>
    /// Emits a dynamic import expression.
    /// Dynamic import returns a Promise that resolves to the module namespace.
    /// </summary>
    private void EmitDynamicImport(Expr.DynamicImport di)
    {
        // Emit the path expression
        EmitExpression(di.PathExpression);
        EmitBoxIfNeeded(di.PathExpression);

        // Convert to string
        IL.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToString", [typeof(object)])!);

        // Push current module path (or empty string if not in module context)
        IL.Emit(OpCodes.Ldstr, _ctx.CurrentModulePath ?? "");

        // Call DynamicImportModule(path, currentModulePath) -> Task<object?>
        IL.Emit(OpCodes.Call, _ctx.Runtime!.DynamicImportModule);

        // Wrap Task<object?> in SharpTSPromise
        IL.Emit(OpCodes.Call, _ctx.Runtime!.WrapTaskAsPromise);

        _stackType = StackType.Unknown;
    }

    #endregion
}
