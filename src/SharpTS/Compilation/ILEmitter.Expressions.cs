using System.IO;
using System.Reflection.Emit;
using SharpTS.Parsing;
using SharpTS.TypeSystem;

namespace SharpTS.Compilation;

/// <summary>
/// Basic expression emission methods for the IL emitter.
/// </summary>
public partial class ILEmitter
{
    protected override void EmitLiteral(Expr.Literal lit)
    {
        switch (lit.Value)
        {
            case double d:
                EmitDoubleConstant(d);
                break;
            case string s:
                EmitStringConstant(s);
                break;
            case bool b:
                EmitBoolConstant(b);
                break;
            case System.Numerics.BigInteger bi:
                if (bi >= long.MinValue && bi <= long.MaxValue)
                {
                    // Optimization: Use BigInteger(long) constructor for small values
                    IL.Emit(OpCodes.Ldc_I8, (long)bi);
                    IL.Emit(OpCodes.Newobj, _ctx.Types.GetConstructor(_ctx.Types.BigInteger, _ctx.Types.Int64));
                }
                else
                {
                    // Fallback: Parse from string for large values
                    IL.Emit(OpCodes.Ldstr, bi.ToString());
                    IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.BigInteger, "Parse", _ctx.Types.String));
                }
                IL.Emit(OpCodes.Box, _ctx.Types.BigInteger);
                SetStackUnknown();
                break;
            case Runtime.Types.SharpTSUndefined:
                EmitUndefinedConstant();
                break;
            case null:
                EmitNullConstant();
                break;
            default:
                EmitNullConstant();
                break;
        }
    }

    protected override void EmitVariable(Expr.Variable v)
    {
        var name = v.Name.Lexeme;

        if (TryEmitDefaultParameterTdz(name))
            return;

        if (_ctx.LexicalInitializerTdzName == name)
        {
            IL.Emit(OpCodes.Ldstr, name);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.ThrowUndefinedVariable);
            EmitNullConstant();
            return;
        }

        // Block-scoped class locals start as undefined and are initialized at
        // the declaration statement. Observe the class TDZ before ordinary
        // local resolution.
        if (_ctx.Locals.TryGetTag(name, out var classTag) && classTag is Stmt.Class)
        {
            var local = _ctx.Locals.GetLocal(name)!;
            var initializedLabel = IL.DefineLabel();
            IL.Emit(OpCodes.Ldloc, local);
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Isinst, _ctx.Runtime!.UndefinedType);
            IL.Emit(OpCodes.Brfalse, initializedLabel);
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Ldstr, name);
            IL.Emit(OpCodes.Call, _ctx.Runtime.ThrowUndefinedVariable);
            IL.Emit(OpCodes.Ldnull);
            IL.MarkLabel(initializedLabel);
            SetStackUnknown();
            return;
        }

        // A class body's lexical self-binding shadows every outer binding with the same
        // spelling. Class expressions are emitted as Types rather than ordinary locals, so
        // resolve that binding before the general variable/capture resolver.
        if (_ctx.CurrentClassBuilder != null
            && (name == _ctx.CurrentClassName || name == _ctx.CurrentClassShortName))
        {
            IL.Emit(OpCodes.Ldtoken, _ctx.CurrentClassBuilder);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(
                _ctx.Types.Type, "GetTypeFromHandle", _ctx.Types.RuntimeTypeHandle));
            SetStackUnknown();
            return;
        }

        // Try resolver first (user-defined variables: parameters, locals, captured)
        var stackType = _resolver.TryLoadVariable(name);
        if (stackType.HasValue)
        {
            SetStackType(stackType.Value);
            return;
        }

        // CommonJS: bare `exports` resolves to the current module's $exports static field.
        if (TryEmitCjsVariable(name)) return;

        // Fallback: pseudo-variables (Math, process, classes, functions, namespaces)
        if (name == "Math")
        {
            // Bare `Math` resolves to a shared Dictionary<string, object>
            // singleton so `Math.length = 1; Math[0] = 1` and iteration via
            // `Array.prototype.X.call(Math, cb)` work per ECMA-262 (Math is an
            // ordinary extensible object). `Math.PI`/`Math.floor`/etc. still
            // route through MathStaticEmitter's compile-time interception
            // *before* this bare-reference path, so static-member dispatch is
            // unaffected.
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.MathSingletonField);
            SetStackUnknown();
            return;
        }

        if (name == "JSON")
        {
            // Stage 4z3: bare `JSON` resolves to a singleton Dictionary so
            // `typeof JSON === "object"` per ECMA-262 24.5. Compile-time
            // static dispatch (JSON.parse / JSON.stringify) intercepts before
            // this bare-reference path so behavior is preserved.
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.JsonSingletonField);
            SetStackUnknown();
            return;
        }

        // AbortSignal / Intl value-position namespace singletons (#224) —
        // shared with the state-machine emitters via the base helper.
        if (TryEmitNamespaceSingleton(name)) return;

        if (name == "process")
        {
            // Value-position process resolves to the live $Process singleton
            // (epic #1078): `const p = process; p.on(...)` and the module
            // facade's default export share one object. The syntactic
            // `process.X` form still takes ProcessStaticEmitter's fast path.
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProcessObject);
            SetStackUnknown();
            return;
        }

        // globalThis / global (a Node alias) in value position resolve to the
        // runtime sentinel (#271) so `var root = globalThis || global` is a real
        // object whose dynamic GetProperty routes to GlobalThisGetProperty —
        // lodash's runInContext reads `context.Object`/`context.Math` off it.
        // The syntactic `globalThis.X` path is still intercepted at compile time.
        if (name == "globalThis" || name == "global")
        {
            IL.Emit(OpCodes.Ldsfld, _ctx.Runtime!.GlobalThisSingletonField);
            SetStackUnknown();
            return;
        }

        if (name == "Symbol")
        {
            // Bare `Symbol` resolves to the $TSSymbol Type token (#234) — the
            // same value-form pattern as Array/Number/String. typeof → "function",
            // aliased member access hits GetProperty's Type branch (well-known
            // symbols are public static fields with their JS names; for/keyFor
            // route through LookupBuiltInStaticMember), and the call form
            // dispatches via InvokeValue's Type-callee branch. Direct
            // `Symbol.iterator` / `Symbol(...)` sites still compile through
            // SymbolStaticEmitter / BuiltInConstructorHandler first.
            IL.Emit(OpCodes.Ldtoken, _ctx.Runtime!.TSSymbolType);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Type, "GetTypeFromHandle", _ctx.Types.RuntimeTypeHandle));
            SetStackUnknown();
            return;
        }

        if (name == "BigInt")
        {
            // BigInt is callable but not constructible. Its value-form Type
            // token gives aliases normal function identity/property lookup;
            // direct BigInt(x) calls still use BuiltInConstructorHandler.
            IL.Emit(OpCodes.Ldtoken, _ctx.Types.BigInteger);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Type, "GetTypeFromHandle", _ctx.Types.RuntimeTypeHandle));
            SetStackUnknown();
            return;
        }

        if (name == "Proxy")
        {
            // Value form of the %Proxy% constructor. Direct `new Proxy(...)`
            // remains handled by the constructor emitter; this identity-stable
            // wrapper supplies function branding and standard name/length
            // metadata for reflection and aliases.
            _ctx.Types.EmitLoadMethodInfo(IL, _ctx.Runtime!.CreateProxy);
            IL.Emit(OpCodes.Ldstr, "Proxy");
            IL.Emit(OpCodes.Ldc_I4_2);
            IL.Emit(OpCodes.Call, _ctx.Runtime.TSFunctionGetOrCreate);
            SetStackUnknown();
            return;
        }

        // JavaScript global constants (NaN/Infinity/undefined)
        if (TryEmitJsGlobalConstant(name)) return;

        // Global fetch function - use cached TSFunction for reference equality with globalThis.fetch
        if (name == "fetch")
        {
            IL.Emit(OpCodes.Ldstr, "fetch");
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GlobalThisGetProperty);
            SetStackUnknown();
            return;
        }

        // Other global functions routable through globalThis (they have fast-path call handlers
        // but must ALSO be addressable as values — lodash caches them: `var freeParseFloat = parseFloat`
        // then calls the alias later). Matches how `fetch` resolves the bare reference above.
        if (name is "eval" or "parseFloat" or "parseInt" or "isNaN" or "isFinite"
            or "encodeURIComponent" or "decodeURIComponent"
            or "setTimeout" or "clearTimeout" or "setInterval" or "clearInterval"
            or "queueMicrotask" or "structuredClone")
        {
            IL.Emit(OpCodes.Ldstr, name);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GlobalThisGetProperty);
            SetStackUnknown();
            return;
        }

        // Check for Node.js module globals (__dirname, __filename)
        if (name == "__filename")
        {
            IL.Emit(OpCodes.Ldstr, _ctx.CurrentModulePath ?? "");
            SetStackUnknown();
            return;
        }

        if (name == "__dirname")
        {
            string dirname = string.IsNullOrEmpty(_ctx.CurrentModulePath)
                ? ""
                : Path.GetDirectoryName(_ctx.CurrentModulePath) ?? "";
            IL.Emit(OpCodes.Ldstr, dirname);
            SetStackUnknown();
            return;
        }

        // Check if it's a class - load the Type object
        if (_ctx.Classes.TryGetValue(_ctx.ResolveClassName(name), out var classType))
        {
            IL.Emit(OpCodes.Ldtoken, classType);
            IL.Emit(OpCodes.Call, _ctx.Types.GetMethod(_ctx.Types.Type, "GetTypeFromHandle", _ctx.Types.RuntimeTypeHandle));
            SetStackUnknown();
            return;
        }

        // Check if it's an imported/captured value. Class declarations must win
        // above: closure analysis can include a class name referenced by a later
        // callback, but class declarations are represented by their Type token and
        // are not initialized into a top-level value field.
        if (_ctx.TopLevelStaticVars?.TryGetValue(name, out var topLevelField) == true)
        {
            IL.Emit(OpCodes.Ldsfld, topLevelField);
            SetStackUnknown();
            return;
        }

        // Check if it's a top-level function - wrap as TSFunction.
        // Stage 6r: route through GetOrCreate (MethodInfo-keyed instance cache)
        // so multiple references to the same function decl produce the SAME
        // $TSFunction wrapper. Without this, `e.constructor === ErrorClass`
        // and `instanceof ErrorClass` checks across separately-loaded
        // references fail with reference inequality even though the
        // underlying MethodInfo is the same.
        if (_ctx.Functions.TryGetValue(_ctx.ResolveFunctionName(name), out var methodBuilder))
        {
            IL.Emit(OpCodes.Ldtoken, methodBuilder);
            // Use two-parameter GetMethodFromHandle with declaring type for proper token resolution in persisted assemblies
            if (_ctx.ProgramType != null)
            {
                IL.Emit(OpCodes.Ldtoken, _ctx.ProgramType);
                IL.Emit(OpCodes.Call, _ctx.Types.MethodBaseGetMethodFromHandleWithType);
            }
            else
            {
                IL.Emit(OpCodes.Call, _ctx.Types.MethodBaseGetMethodFromHandle);
            }
            IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);

            // Compute function arity at compile time. name/length are used only
            // on first create (subsequent cache hits return the existing wrapper
            // whose name/length are already set).
            int arity = _ctx.GetFunctionLength(methodBuilder);
            IL.Emit(OpCodes.Ldstr, _ctx.GetFunctionName(methodBuilder, name));
            IL.Emit(OpCodes.Ldc_I4, arity);  // function length
            IL.Emit(OpCodes.Call, _ctx.Runtime!.TSFunctionGetOrCreate);
            SetStackUnknown();
            return;
        }

        // Check if it's an inner function - wrap as TSFunction for value reference
        if (_ctx.InnerFunctionMethodsByName?.TryGetValue(name, out var innerFuncMethod) == true)
        {
            TypeBuilder? innerDC = null;
            bool isCapturing = _ctx.InnerFunctionDisplayClassesByName?.TryGetValue(name, out innerDC) == true;
            if (isCapturing)
            {
                // Capturing: TSFunction(this, invokeMethod) where this is the display class instance
                IL.Emit(OpCodes.Ldarg_0); // Load display class instance
                IL.Emit(OpCodes.Ldtoken, innerFuncMethod);
                IL.Emit(OpCodes.Ldtoken, innerDC!);
                IL.Emit(OpCodes.Call, _ctx.Types.MethodBaseGetMethodFromHandleWithType);
            }
            else
            {
                // Non-capturing: TSFunction(null, staticMethod)
                IL.Emit(OpCodes.Ldnull);
                IL.Emit(OpCodes.Ldtoken, innerFuncMethod);
                IL.Emit(OpCodes.Call, _ctx.Types.MethodBaseGetMethodFromHandle);
            }
            IL.Emit(OpCodes.Castclass, _ctx.Types.MethodInfo);
            EmitNewobjUnknown(_ctx.Runtime!.TSFunctionCtor);
            return;
        }

        // Check if it's a namespace - load the static field. ResolveNamespaceField walks enclosing
        // namespace prefixes so a nested namespace's member body can name a sibling/enclosing
        // namespace by its simple name (#665), not just a top-level namespace by full path.
        if (_ctx.ResolveNamespaceField(name) is { } nsField)
        {
            IL.Emit(OpCodes.Ldsfld, nsField);
            SetStackUnknown();
            return;
        }

        // Check if it's a built-in Error constructor — push the emitted Type object
        if (TryEmitErrorTypeToken(name))
            return;

        // Built-in classes referenced as values (e.g. `instanceof Date`,
        // `x === Map`, passing Date as an arg). Emit the .NET Type object for
        // the runtime class so InstanceOf's IsAssignableFrom check matches
        // instances produced by `new Date()` / `new Map()` / etc.
        if (TryEmitBuiltInClassType(name))
            return;

        // Last resort for JS globals (Object, globalThis, etc.): fall through to
        // globalThis.<name>. Positioned AFTER TryEmitBuiltInClassType so existing
        // IsAssignableFrom-based instanceof checks keep their Type-token emissions.
        // Runs for `Object` (coercion/identity — lodash `root.Object === Object`),
        // `Function`, and anything else the resolver/classes/functions paths didn't claim.
        if (name is "Object" or "Function" or "Number" or "String" or "Boolean")
        {
            // Bare reference to a built-in constructor. Number/String/Boolean
            // are added here (issue #62) so that patterns like
            // `var isInt = Number.isInteger` and `typeof Number === "function"`
            // don't throw ReferenceError — matches how Object and Function
            // already resolve via globalThis.
            IL.Emit(OpCodes.Ldstr, name);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GlobalThisGetProperty);
            SetStackUnknown();
            return;
        }

        // Bare `crypto` (unimported) → the WebCrypto global (#1063), via
        // GlobalThisGetProperty so the gating lives in one place. Positioned
        // after every other resolution so imports/locals/classes always win.
        if (name == "crypto")
        {
            IL.Emit(OpCodes.Ldstr, "crypto");
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GlobalThisGetProperty);
            SetStackUnknown();
            return;
        }

        // Unknown variable - throw ReferenceError at runtime
        IL.Emit(OpCodes.Ldstr, name);
        IL.Emit(OpCodes.Call, _ctx.Runtime!.ThrowUndefinedVariable);
        // Emit unreachable null to satisfy IL verification (method never returns but stack must balance)
        EmitNullConstant();
    }

    // TryEmitBuiltInClassType and TryEmitErrorTypeToken are inherited from
    // ExpressionEmitterBase so state-machine emitters resolve built-in
    // constructor identifiers identically (#232).

    // IsKnownVariable is inherited from ExpressionEmitterBase

    protected override void EmitAssign(Expr.Assign a)
    {
        // CommonJS: `exports = X` → stsfld $exports (mirrors TryEmitCjsSet for module.exports).
        // Must run before EmitExpression(a.Value) so we can own the box+dup+stsfld sequence
        // cleanly — otherwise the generic "Unknown target" fallback at the bottom of this method
        // emits Box+Dup and leaves a dangling value on the stack, which propagates through the
        // rest of the module body and ultimately trips PathStackDepth at the final ret.
        if (TryEmitCjsAssign(a)) return;

        // Promoted string-accumulator append (#857): `s = s + E` where `s` is a StringBuilder slot.
        // Emit `sb.Append(E)` instead of evaluating `s + E` (String.Concat) and storing — turning the
        // O(n²) accumulation into O(n). The analyzer promotes `s` only when every such append is in
        // statement position, so the Append-returned builder left on the stack is the single value
        // `Stmt.Expression` pops (the discarded assignment-expression result).
        if (a.Value is Expr.Binary { Operator.Type: TokenType.PLUS, Left: Expr.Variable accLeft } accBin
            && accLeft.Name.Lexeme == a.Name.Lexeme
            && _ctx.TryGetPromotedStringAccumulator(a.Name.Lexeme) is { } accSb)
        {
            IL.Emit(OpCodes.Ldloc, accSb);
            EmitExpression(accBin.Right);
            EnsureString();
            IL.Emit(OpCodes.Callvirt, _ctx.Types.StringBuilderAppendString);
            SetStackUnknown();
            return;
        }

        // A nested integer loop may advance as `j = j + i`, where both j and
        // the outer i are proven native Int64 counters. Own the assignment before
        // the general path materializes both operands as doubles and attempts to
        // store that double in the Int64 slot.
        if (TryEmitIntegerCounterAssignment(a))
            return;

        var assignmentDCName = _ctx.ResolveFunctionDCFieldName(a.Name.Lexeme);
        FieldBuilder? assignmentDCField = null;
        if (_ctx.CapturedFunctionLocals?.Contains(assignmentDCName) == true)
            _ctx.FunctionDisplayClassFields?.TryGetValue(assignmentDCName, out assignmentDCField);

        // Direct numeric locals and proven numeric function-capture slots coerce
        // their RHS to double up front, avoiding a box/unbox round trip.
        // expression emitter for that representation up front for number[] reads,
        // allowing the guarded GetDouble arm to avoid a box/unbox round trip.
        var assignmentLocal = _ctx.Locals.GetLocal(a.Name.Lexeme);
        if ((assignmentDCField?.FieldType == _ctx.Types.Double) ||
            (a.Value is Expr.GetIndex && assignmentLocal != null &&
             _ctx.Types.IsDouble(assignmentLocal.LocalType)))
        {
            EmitExpressionAsDouble(a.Value);
        }
        else
        {
            EmitExpression(a.Value);
        }

        if (_ctx.LexicalInitializerTdzName == a.Name.Lexeme)
        {
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Pop);
            IL.Emit(OpCodes.Ldstr, a.Name.Lexeme);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.ThrowUndefinedVariable);
            EmitNullConstant();
            return;
        }

        EmitLexicalAssignmentTdzGuard(a.Name.Lexeme);

        // 0. Per-iteration loop-binding cell (#650): write through the StrongBox so the
        //    mutation is visible to closures that captured this iteration's cell. Mirrors
        //    LocalVariableResolver's cell store; this hand-rolled assignment path does not
        //    route through TryStoreVariable, so the cell case must be handled here too.
        if (_ctx.CellBindingLocals.TryGetValue(a.Name.Lexeme, out var assignCell))
        {
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup); // leave the assigned value on the stack (expression result)
            var temp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, temp);
            IL.Emit(OpCodes.Ldloc, assignCell);
            IL.Emit(OpCodes.Ldloc, temp);
            IL.Emit(OpCodes.Stfld, _ctx.Types.StrongBoxOfObjectValueField);
            SetStackUnknown();
            return;
        }

        // 1. Function display class fields (captured function-local vars)
        // Check this BEFORE regular locals to ensure we use the shared storage.
        // #838: remap a write-captured block-scope shadow to its renamed DC storage key in an arrow body.
        if (assignmentDCField is { } funcDCField)
        {
            if (funcDCField.FieldType == _ctx.Types.Double)
            {
                EnsureDouble();
                IL.Emit(OpCodes.Dup);
                var numericTemp = IL.DeclareLocal(_ctx.Types.Double);
                IL.Emit(OpCodes.Stloc, numericTemp);
                if (_ctx.FunctionDisplayClassLocal != null)
                {
                    IL.Emit(OpCodes.Ldloc, _ctx.FunctionDisplayClassLocal);
                }
                else if (_ctx.CurrentArrowFunctionDCField != null)
                {
                    IL.Emit(OpCodes.Ldarg_0);
                    IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowFunctionDCField);
                }
                else
                {
                    IL.Emit(OpCodes.Pop);
                    SetStackType(StackType.Double);
                    return;
                }
                IL.Emit(OpCodes.Ldloc, numericTemp);
                IL.Emit(OpCodes.Stfld, funcDCField);
                if (_ctx.FunctionDisplayClassLocal != null &&
                    _ctx.TryGetParameter(a.Name.Lexeme, out var numericParamSync))
                {
                    IL.Emit(OpCodes.Ldloc, numericTemp);
                    IL.Emit(OpCodes.Starg, numericParamSync);
                }
                SetStackType(StackType.Double);
                return;
            }

            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup);
            // Store to field: need temp since value is on top of stack
            var temp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, temp);

            if (_ctx.FunctionDisplayClassLocal != null)
            {
                // Direct access from function body
                IL.Emit(OpCodes.Ldloc, _ctx.FunctionDisplayClassLocal);
            }
            else if (_ctx.CurrentArrowFunctionDCField != null)
            {
                // Access from arrow body - go through $functionDC field
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowFunctionDCField);
            }
            else
            {
                // Fallback - just discard the temp and leave value on stack
                IL.Emit(OpCodes.Pop);
                SetStackUnknown();
                return;
            }

            IL.Emit(OpCodes.Ldloc, temp);
            IL.Emit(OpCodes.Stfld, funcDCField);
            // Captured PARAMETER of this function: also sync the arg slot —
            // reads resolve parameters before the DC, so a DC-only store would
            // leave later same-body reads seeing the stale argument.
            if (_ctx.FunctionDisplayClassLocal != null &&
                _ctx.TryGetParameter(a.Name.Lexeme, out var funcParamSync))
            {
                IL.Emit(OpCodes.Ldloc, temp);
                _ctx.EmitConvertForParamSlot(IL, a.Name.Lexeme);
                IL.Emit(OpCodes.Starg, funcParamSync);
            }
            SetStackUnknown();
            return;
        }

        // 1b. Arrow scope display class fields (captured arrow-local vars)
        if (_ctx.CapturedArrowLocals?.Contains(a.Name.Lexeme) == true &&
            _ctx.ArrowScopeDisplayClassFields?.TryGetValue(a.Name.Lexeme, out var arrowDCField) == true)
        {
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup);
            var temp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, temp);

            if (_ctx.ArrowScopeDisplayClassLocal != null)
            {
                // Direct access from arrow body
                IL.Emit(OpCodes.Ldloc, _ctx.ArrowScopeDisplayClassLocal);
            }
            else if (_ctx.CurrentArrowScopeDCField != null)
            {
                // Access from nested arrow body - go through $arrowDC field
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowScopeDCField);
            }
            else
            {
                IL.Emit(OpCodes.Pop);
                SetStackUnknown();
                return;
            }

            IL.Emit(OpCodes.Ldloc, temp);
            IL.Emit(OpCodes.Stfld, arrowDCField);
            // Captured PARAMETER of this arrow: also sync the arg slot — reads
            // resolve parameters before the scope DC, so a DC-only store would
            // leave later same-body reads seeing the stale argument (lodash's
            // `context = context || root; ... context.Date` failure mode).
            if (_ctx.ArrowScopeDisplayClassLocal != null &&
                _ctx.TryGetParameter(a.Name.Lexeme, out var arrowParamSync))
            {
                IL.Emit(OpCodes.Ldloc, temp);
                _ctx.EmitConvertForParamSlot(IL, a.Name.Lexeme);
                IL.Emit(OpCodes.Starg, arrowParamSync);
            }
            SetStackUnknown();
            return;
        }

        // 1c. PARENT arrow's scope DC, reachable through the current closure's
        // $arrowDC / $arrowScopeDC reference field. Mirrors LocalVariableResolver
        // store path 1c; without this branch the assignment falls through to the
        // "Unknown target" tail and leaves a dangling value on the stack.
        if (_ctx.ParentArrowCapturedLocals?.Contains(a.Name.Lexeme) == true &&
            _ctx.ParentArrowScopeDisplayClassFields?.TryGetValue(a.Name.Lexeme, out var parentArrowDCField) == true &&
            _ctx.CurrentArrowScopeDCField != null)
        {
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup);
            var temp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, temp);
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowScopeDCField);
            IL.Emit(OpCodes.Ldloc, temp);
            IL.Emit(OpCodes.Stfld, parentArrowDCField);
            SetStackUnknown();
            return;
        }

        // 1d. EXTRA ancestor arrow scope DCs — mirror of LocalVariableResolver 1d.
        if (_ctx.ExtraArrowScopeBindings?.TryGetValue(a.Name.Lexeme, out var extraAssignBinding) == true)
        {
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup);
            var temp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, temp);
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, extraAssignBinding.RefField);
            IL.Emit(OpCodes.Ldloc, temp);
            IL.Emit(OpCodes.Stfld, extraAssignBinding.VarField);
            SetStackUnknown();
            return;
        }

        var local = _ctx.Locals.GetLocal(a.Name.Lexeme);
        if (local != null)
        {
            var localType = _ctx.Locals.GetLocalType(a.Name.Lexeme);
            if (localType != null && _ctx.Types.IsDouble(localType))
            {
                // Typed local - ensure unboxed double
                EnsureDouble();
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Stloc, local);
                SetStackType(StackType.Double);
            }
            else
            {
                // Object local - ensure boxed
                EmitBoxIfNeeded(a.Value);
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Stloc, local);
                SetStackUnknown();
            }
        }
        else if (_ctx.TryGetParameter(a.Name.Lexeme, out var argIndex))
        {
            // A parameter slot is not always object: a `: number` param is an unboxed `double`
            // slot, a `: boolean` param a `bool` slot, a `: string` param a `string` slot. Box the
            // value (the assignment's result, left on the stack), then convert the copy destined for
            // the arg slot to the slot's declared type before Starg — storing a boxed object straight
            // into a double/bool/string slot fails IL verification (StackUnexpected) and reads back
            // garbage (#402). EmitConvertForParamSlot is a no-op for object slots (the common case and
            // the #372-widened undefined-reachable params).
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup);
            _ctx.EmitConvertForParamSlot(IL, a.Name.Lexeme);
            IL.Emit(OpCodes.Starg, argIndex);
            SetStackUnknown();
        }
        else if (_ctx.CapturedFields?.TryGetValue(a.Name.Lexeme, out var field) == true)
        {
            // Captured field in display class (closure)
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup);
            // Store to field: need temp since value is on top of stack
            var temp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, temp);
            // Per-iteration cell capture (#650): the field holds a shared StrongBox —
            // write through Value so the loop body and sibling closures see the update,
            // rather than overwriting this closure's reference to the cell.
            if (_ctx.CellCapturedFieldNames?.Contains(a.Name.Lexeme) == true)
            {
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, field);
                IL.Emit(OpCodes.Castclass, _ctx.Types.StrongBoxOfObject);
                IL.Emit(OpCodes.Ldloc, temp);
                IL.Emit(OpCodes.Stfld, _ctx.Types.StrongBoxOfObjectValueField);
            }
            else
            {
                IL.Emit(OpCodes.Ldarg_0);  // Load display class instance
                IL.Emit(OpCodes.Ldloc, temp);
                IL.Emit(OpCodes.Stfld, field);
            }
            SetStackUnknown();
        }
        else if (_ctx.CapturedTopLevelVars?.Contains(a.Name.Lexeme) == true &&
                 _ctx.EntryPointDisplayClassFields?.TryGetValue(a.Name.Lexeme, out var entryPointField) == true)
        {
            // Captured top-level variable in entry-point display class
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup);
            _ctx.EmitTopLevelLexicalTdzCheck(IL, a.Name.Lexeme);
            // Store to field: need temp since value is on top of stack
            var temp = IL.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, temp);

            if (_ctx.EntryPointDisplayClassLocal != null)
            {
                // Direct access from entry point
                IL.Emit(OpCodes.Ldloc, _ctx.EntryPointDisplayClassLocal);
            }
            else if (_ctx.CurrentArrowEntryPointDCField != null)
            {
                // Access from arrow body - go through $entryPointDC field
                IL.Emit(OpCodes.Ldarg_0);
                IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowEntryPointDCField);
            }
            else if (_ctx.EntryPointDisplayClassStaticField != null)
            {
                // Access from module init method - use static field
                IL.Emit(OpCodes.Ldsfld, _ctx.EntryPointDisplayClassStaticField);
            }
            else
            {
                // Fallback - just discard the temp and leave value on stack
                IL.Emit(OpCodes.Pop);
                SetStackUnknown();
                return;
            }

            IL.Emit(OpCodes.Ldloc, temp);
            IL.Emit(OpCodes.Stfld, entryPointField);
            SetStackUnknown();
        }
        else if (_ctx.TopLevelStaticVars?.TryGetValue(a.Name.Lexeme, out var topLevelField) == true)
        {
            // Top-level static variable
            EmitBoxIfNeeded(a.Value);
            IL.Emit(OpCodes.Dup);
            _ctx.EmitTopLevelLexicalTdzCheck(IL, a.Name.Lexeme);
            IL.Emit(OpCodes.Stsfld, topLevelField);
            SetStackUnknown();
        }
        else
        {
            // Class bodies and other strict contexts must reject creation of an
            // unresolvable binding. Evaluate the RHS first, then throw the same
            // guest ReferenceError used by an unknown identifier read.
            EmitBoxIfNeeded(a.Value);
            if (_ctx.IsStrictMode)
            {
                IL.Emit(OpCodes.Pop);
                IL.Emit(OpCodes.Ldstr, a.Name.Lexeme);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.ThrowUndefinedVariable);
                IL.Emit(OpCodes.Ldsfld, _ctx.Runtime.UndefinedInstance);
            }
            else
            {
                // Sloppy PutValue creates a property on the global object and
                // still evaluates to the assigned value.  Preserve one copy
                // as the expression result while the other is consumed by
                // the runtime setter.
                var value = IL.DeclareLocal(_ctx.Types.Object);
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Stloc, value);
                IL.Emit(OpCodes.Ldstr, a.Name.Lexeme);
                IL.Emit(OpCodes.Ldloc, value);
                IL.Emit(OpCodes.Call, _ctx.Runtime!.GlobalThisSetProperty);
            }
            SetStackUnknown();
        }
    }

    private void EmitLexicalAssignmentTdzGuard(string name)
    {
        if (_ctx.LexicalTdzNames?.Contains(name) != true)
            return;

        var storageName = _ctx.ResolveFunctionDCFieldName(name);
        if (_ctx.CapturedFunctionLocals?.Contains(storageName) == true
            && _ctx.FunctionDisplayClassFields?.TryGetValue(storageName, out var functionField) == true
            && _ctx.FunctionDisplayClassLocal != null)
        {
            IL.Emit(OpCodes.Ldloc, _ctx.FunctionDisplayClassLocal);
            IL.Emit(OpCodes.Ldfld, functionField);
        }
        else if (_ctx.CapturedArrowLocals?.Contains(name) == true
            && _ctx.ArrowScopeDisplayClassFields?.TryGetValue(name, out var arrowField) == true
            && _ctx.ArrowScopeDisplayClassLocal != null)
        {
            IL.Emit(OpCodes.Ldloc, _ctx.ArrowScopeDisplayClassLocal);
            IL.Emit(OpCodes.Ldfld, arrowField);
        }
        else if (_ctx.ParentArrowCapturedLocals?.Contains(name) == true
            && _ctx.ParentArrowScopeDisplayClassFields?.TryGetValue(name, out var parentArrowField) == true
            && _ctx.CurrentArrowScopeDCField != null)
        {
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, _ctx.CurrentArrowScopeDCField);
            IL.Emit(OpCodes.Ldfld, parentArrowField);
        }
        else if (_ctx.ExtraArrowScopeBindings?.TryGetValue(name, out var extraArrowBinding) == true)
        {
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, extraArrowBinding.RefField);
            IL.Emit(OpCodes.Ldfld, extraArrowBinding.VarField);
        }
        else if (_ctx.CapturedFields?.TryGetValue(name, out var capturedField) == true)
        {
            IL.Emit(OpCodes.Ldarg_0);
            IL.Emit(OpCodes.Ldfld, capturedField);
        }
        else if (_ctx.Locals.GetLocal(name) is { LocalType: var localType } local
            && localType == _ctx.Types.Object)
        {
            IL.Emit(OpCodes.Ldloc, local);
        }
        else
        {
            return;
        }

        _ctx.EmitLexicalTdzValueCheck(IL, name);
        IL.Emit(OpCodes.Pop);
    }

    protected override void EmitThis()
    {
        _resolver.LoadThis();
        SetStackUnknown();
    }

    protected override void EmitSuper(Expr.Super s)
    {
        // Load this and prepare for base method call
        // Note: super() constructor calls are handled in EmitCall, not here
        IL.Emit(OpCodes.Ldarg_0);
        IL.Emit(OpCodes.Ldstr, s.Method?.Lexeme ?? "constructor");
        EmitCallUnknown(_ctx.Runtime!.GetSuperMethod);
    }

    protected override void EmitTernary(Expr.Ternary t)
    {
        var builder = _ctx.ILBuilder;
        var elseLabel = builder.DefineLabel("ternary_else");
        var endLabel = builder.DefineLabel("ternary_end");

        EmitExpression(t.Condition);
        // Handle condition based on what's actually on the stack
        if (_stackType == StackType.Boolean)
        {
            // Already have unboxed boolean - ready for branch
        }
        else if (_stackType == StackType.Unknown && IsComparisonExpr(t.Condition))
        {
            // Boxed boolean from comparison - unbox it
            IL.Emit(OpCodes.Unbox_Any, _ctx.Types.Boolean);
        }
        else
        {
            // For other expressions (including Expr.Logical which returns boxed object),
            // apply truthy check to convert to int for Brfalse
            EnsureBoxed();
            EmitTruthyCheck();
        }
        builder.Emit_Brfalse(elseLabel);

        EmitExpression(t.ThenBranch);
        EmitBoxIfNeeded(t.ThenBranch);
        builder.Emit_Br(endLabel);

        builder.MarkLabel(elseLabel);
        EmitExpression(t.ElseBranch);
        EmitBoxIfNeeded(t.ElseBranch);

        builder.MarkLabel(endLabel);
        // Both branches box, so result is Unknown (boxed object)
        SetStackUnknown();
    }

    protected override void EmitNullishCoalescing(Expr.NullishCoalescing nc)
    {
        var builder = _ctx.ILBuilder;
        var endLabel = builder.DefineLabel("nullish_end");
        var useRightLabel = builder.DefineLabel("nullish_use_right");

        EmitExpression(nc.Left);
        EmitBoxIfNeeded(nc.Left);
        IL.Emit(OpCodes.Dup);

        // If left is null, use right
        builder.Emit_Brfalse(useRightLabel);

        // If left is undefined, use right
        IL.Emit(OpCodes.Dup);
        IL.Emit(OpCodes.Isinst, _ctx.Runtime!.UndefinedType);
        builder.Emit_Brtrue(useRightLabel);

        // Left is neither null nor undefined - use it
        builder.Emit_Br(endLabel);

        builder.MarkLabel(useRightLabel);
        IL.Emit(OpCodes.Pop);
        EmitExpression(nc.Right);
        EmitBoxIfNeeded(nc.Right);

        builder.MarkLabel(endLabel);
        // Both branches box, so result is Unknown (boxed object)
        SetStackUnknown();
    }

    protected override void EmitTemplateLiteral(Expr.TemplateLiteral tl)
    {
        // Build array of parts
        var totalParts = tl.Strings.Count + tl.Expressions.Count;
        IL.Emit(OpCodes.Ldc_I4, totalParts);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);

        int partIndex = 0;
        for (int i = 0; i < tl.Strings.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, partIndex++);
            IL.Emit(OpCodes.Ldstr, tl.Strings[i]);
            IL.Emit(OpCodes.Stelem_Ref);

            if (i < tl.Expressions.Count)
            {
                IL.Emit(OpCodes.Dup);
                IL.Emit(OpCodes.Ldc_I4, partIndex++);
                EmitExpression(tl.Expressions[i]);
                EmitBoxIfNeeded(tl.Expressions[i]);
                IL.Emit(OpCodes.Stelem_Ref);
            }
        }

        EmitCallString(_ctx.Runtime!.ConcatTemplate);
    }

    protected override void EmitTaggedTemplateLiteral(Expr.TaggedTemplateLiteral ttl)
    {
        // Check for String.raw special case
        if (IsStringRawTag(ttl.Tag))
        {
            EmitStringRawTaggedTemplate(ttl);
            return;
        }

        // Detect property access tag (obj.method`...`) for this binding
        bool hasThisBinding = ttl.Tag is Expr.Get;
        LocalBuilder? receiverLocal = null;

        // 1. Emit the tag function reference (and receiver for property access tags)
        if (hasThisBinding)
        {
            var g = (Expr.Get)ttl.Tag;
            // Emit and save the receiver object
            EmitExpression(g.Object);
            EnsureBoxed();
            receiverLocal = _ctx.ILBuilder.DeclareLocal(_ctx.Types.Object);
            IL.Emit(OpCodes.Stloc, receiverLocal);
            // Get the method: GetProperty(obj, name) — handles all object types including dictionaries
            IL.Emit(OpCodes.Ldloc, receiverLocal);
            IL.Emit(OpCodes.Ldstr, g.Name.Lexeme);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.GetProperty);
            // Push thisArg (receiver) for WithThis call
            IL.Emit(OpCodes.Ldloc, receiverLocal);
        }
        else
        {
            EmitExpression(ttl.Tag);
            EmitBoxIfNeeded(ttl.Tag);
        }

        // 2. Create cooked strings array (object?[] to allow null for invalid escapes)
        IL.Emit(OpCodes.Ldc_I4, ttl.CookedStrings.Count);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
        for (int i = 0; i < ttl.CookedStrings.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            if (ttl.CookedStrings[i] != null)
            {
                IL.Emit(OpCodes.Ldstr, ttl.CookedStrings[i]!);
            }
            else
            {
                IL.Emit(OpCodes.Ldnull); // null for invalid escape sequences
            }
            IL.Emit(OpCodes.Stelem_Ref);
        }

        // 3. Create raw strings array
        IL.Emit(OpCodes.Ldc_I4, ttl.RawStrings.Count);
        IL.Emit(OpCodes.Newarr, _ctx.Types.String);
        for (int i = 0; i < ttl.RawStrings.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            IL.Emit(OpCodes.Ldstr, ttl.RawStrings[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        // 4. Create expressions array
        IL.Emit(OpCodes.Ldc_I4, ttl.Expressions.Count);
        IL.Emit(OpCodes.Newarr, _ctx.Types.Object);
        for (int i = 0; i < ttl.Expressions.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            EmitExpression(ttl.Expressions[i]);
            EmitBoxIfNeeded(ttl.Expressions[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        // 5. Call appropriate runtime helper
        if (hasThisBinding)
        {
            // Stack: tag, thisArg, cooked, raw, exprs
            IL.Emit(OpCodes.Call, _ctx.Runtime!.InvokeTaggedTemplateWithThis);
        }
        else
        {
            // Stack: tag, cooked, raw, exprs
            IL.Emit(OpCodes.Call, _ctx.Runtime!.InvokeTaggedTemplate);
        }
        SetStackUnknown();
    }

    /// <summary>
    /// Checks if the tag expression is String.raw.
    /// </summary>
    private static bool IsStringRawTag(Expr tag)
    {
        return tag is Expr.Get get
            && get.Name.Lexeme == "raw"
            && get.Object is Expr.Variable v
            && v.Name.Lexeme == "String";
    }

    /// <summary>
    /// Emits optimized code for String.raw tagged template literals.
    /// Calls the emitted $Runtime.StringRaw method directly.
    /// </summary>
    private void EmitStringRawTaggedTemplate(Expr.TaggedTemplateLiteral ttl)
    {
        // 1. Create raw strings array
        IL.Emit(OpCodes.Ldc_I4, ttl.RawStrings.Count);
        IL.Emit(OpCodes.Newarr, _ctx.Types.String);
        for (int i = 0; i < ttl.RawStrings.Count; i++)
        {
            IL.Emit(OpCodes.Dup);
            IL.Emit(OpCodes.Ldc_I4, i);
            IL.Emit(OpCodes.Ldstr, ttl.RawStrings[i]);
            IL.Emit(OpCodes.Stelem_Ref);
        }

        // 2. Build expressions list. StringRaw's second param is List<object>
        // (rest-param shape), so we need a List rather than a raw object[].
        var listLocal = IL.DeclareLocal(_ctx.Types.ListOfObject);
        IL.Emit(OpCodes.Newobj, _ctx.Types.GetDefaultConstructor(_ctx.Types.ListOfObject));
        IL.Emit(OpCodes.Stloc, listLocal);
        for (int i = 0; i < ttl.Expressions.Count; i++)
        {
            IL.Emit(OpCodes.Ldloc, listLocal);
            EmitExpression(ttl.Expressions[i]);
            EmitBoxIfNeeded(ttl.Expressions[i]);
            IL.Emit(OpCodes.Callvirt, _ctx.Types.GetMethod(_ctx.Types.ListOfObject, "Add", [_ctx.Types.Object])!);
        }
        IL.Emit(OpCodes.Ldloc, listLocal);

        // 3. Call $Runtime.StringRaw(rawStrings, expressions)
        IL.Emit(OpCodes.Call, _ctx.Runtime!.StringRaw);
        SetStackType(StackType.String);
    }

    // EmitRegexLiteral is inherited from ExpressionEmitterBase (#1105): the full
    // hoist-aware $RegExp emission now lives in the shared base so regex literals
    // compile identically in plain functions, arrows, and all state-machine bodies.

    protected override void EmitClassExpression(Expr.ClassExpr ce)
    {
        // Class expressions evaluate to the Type object at runtime.
        // The type has been pre-defined during collection phase.
        if (_ctx.ClassExprBuilders != null && _ctx.ClassExprBuilders.TryGetValue(ce, out var typeBuilder))
        {
            EmitClassHeritageExpression(ce.SuperclassExpr, ce.Name?.Lexeme);

            if (_ctx.ClassExprCaptureFields?.TryGetValue(ce, out var captureFields) == true)
            {
                foreach (var (name, field) in captureFields)
                {
                    EmitVariable(new Expr.Variable(
                        new Token(TokenType.IDENTIFIER, name, null, 0)));
                    EnsureBoxed();
                    IL.Emit(OpCodes.Stsfld, field);
                }
            }

            // Class-expression evaluation always creates its constructor prototype;
            // static elements and computed keys share the same emitted .cctor.
            IL.Emit(OpCodes.Ldtoken, typeBuilder);
            IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
            IL.Emit(OpCodes.Call, _ctx.Runtime!.RunClassDefinitionMethod);

            // Load the Type object using ldtoken + GetTypeFromHandle
            IL.Emit(OpCodes.Ldtoken, typeBuilder);
            IL.Emit(OpCodes.Call, Types.TypeGetTypeFromHandle);
            SetStackUnknown();
        }
        else
        {
            // Fallback: push null (should not happen if collection worked)
            IL.Emit(OpCodes.Ldnull);
            SetStackUnknown();
        }
    }

    protected override void EmitDelete(Expr.Delete del)
    {
        // delete operator: returns boolean
        // - delete obj.prop: removes property, returns true (or throws TypeError if frozen/sealed in strict mode)
        // - delete obj[key]: removes computed property, returns true (or throws TypeError if frozen/sealed in strict mode)
        // - delete variable: throws SyntaxError in strict mode, returns false in sloppy mode
        Expr operand = del.Operand;
        while (operand is Expr.Grouping grouping)
            operand = grouping.Expression;
        switch (operand)
        {
            case Expr.Get get:
                // delete obj.prop - use static runtime helper with strict mode
                EmitExpression(get.Object);
                EmitBoxIfNeeded(get.Object);
                IL.Emit(OpCodes.Ldstr, get.Name.Lexeme);
                if (_ctx.IsStrictMode)
                {
                    IL.Emit(OpCodes.Ldc_I4_1); // true for strict mode
                    EmitCallUnknown(_ctx.Runtime!.DeletePropertyStrict);
                }
                else
                {
                    EmitCallUnknown(_ctx.Runtime!.DeleteProperty);
                }
                SetStackType(StackType.Boolean);
                break;

            case Expr.GetIndex getIndex:
                // delete obj[key] - use DeleteIndex with strict mode
                EmitExpression(getIndex.Object);
                EmitBoxIfNeeded(getIndex.Object);
                EmitExpression(getIndex.Index);
                EmitBoxIfNeeded(getIndex.Index);
                if (_ctx.IsStrictMode)
                {
                    IL.Emit(OpCodes.Ldc_I4_1); // true for strict mode
                    EmitCallUnknown(_ctx.Runtime!.DeleteIndexStrict);
                }
                else
                {
                    EmitCallUnknown(_ctx.Runtime!.DeleteIndex);
                }
                SetStackType(StackType.Boolean);
                break;

            case Expr.Variable v:
                if (_ctx.IsStrictMode)
                {
                    // Strict mode: throw SyntaxError
                    IL.Emit(OpCodes.Ldstr, $"Delete of unqualified identifier '{v.Name.Lexeme}' in strict mode");
                    EmitCallUnknown(_ctx.Runtime!.ThrowStrictSyntaxError);
                    // ThrowStrictSyntaxError throws, but we need a value on stack for IL verification
                    EmitBoolConstant(false);
                }
                else
                {
                    // Deleting an unresolvable reference succeeds; deleting a
                    // resolvable environment binding returns false.
                    if (!IsKnownVariable(v.Name.Lexeme))
                        EmitBoolConstant(true);
                    else
                    {
                        IL.Emit(OpCodes.Ldstr, v.Name.Lexeme);
                        EmitCallUnknown(_ctx.Runtime!.WarnSloppyDeleteVariable);
                    }
                }
                SetStackType(StackType.Boolean);
                break;

            default:
                // delete on other expressions: returns true but does nothing
                // Still need to evaluate for side effects
                EmitExpression(operand);
                IL.Emit(OpCodes.Pop);
                EmitBoolConstant(true);
                break;
        }
    }
}
