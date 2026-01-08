using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncArrowMoveNextEmitter
{
    private void EmitExpression(Expr expr)
    {
        switch (expr)
        {
            case Expr.Literal lit:
                EmitLiteral(lit);
                break;

            case Expr.Variable v:
                LoadVariable(v.Name.Lexeme);
                break;

            case Expr.Assign a:
                EmitExpression(a.Value);
                EnsureBoxed();
                _il.Emit(OpCodes.Dup);
                StoreVariable(a.Name.Lexeme);
                break;

            case Expr.Binary b:
                EmitBinary(b);
                break;

            case Expr.Call c:
                EmitCall(c);
                break;

            case Expr.Await aw:
                EmitAwait(aw);
                break;

            case Expr.Get g:
                EmitGet(g);
                break;

            case Expr.Grouping grp:
                EmitExpression(grp.Expression);
                break;

            case Expr.Ternary t:
                EmitTernary(t);
                break;

            case Expr.This:
                EmitThis();
                break;

            case Expr.ArrowFunction af:
                EmitArrowFunction(af);
                break;

            case Expr.DynamicImport di:
                EmitDynamicImport(di);
                break;

            default:
                // For unhandled expressions, push null
                _il.Emit(OpCodes.Ldnull);
                SetStackUnknown();
                break;
        }
    }

    private void EmitLiteral(Expr.Literal lit)
    {
        if (lit.Value == null)
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackType(StackType.Null);
        }
        else if (lit.Value is double d)
        {
            _il.Emit(OpCodes.Ldc_R8, d);
            _il.Emit(OpCodes.Box, typeof(double));
            SetStackUnknown();
        }
        else if (lit.Value is string s)
        {
            _il.Emit(OpCodes.Ldstr, s);
            SetStackType(StackType.String);
        }
        else if (lit.Value is bool b)
        {
            _il.Emit(b ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
            _il.Emit(OpCodes.Box, typeof(bool));
            SetStackUnknown();
        }
        else
        {
            _il.Emit(OpCodes.Ldnull);
            SetStackType(StackType.Null);
        }
    }

    private void EmitGet(Expr.Get g)
    {
        EmitExpression(g.Object);
        EnsureBoxed();
        _il.Emit(OpCodes.Ldstr, g.Name.Lexeme);
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.GetProperty);
        SetStackUnknown();
    }

    private void EmitTernary(Expr.Ternary t)
    {
        var elseLabel = _il.DefineLabel();
        var endLabel = _il.DefineLabel();

        EmitExpression(t.Condition);
        EnsureBoxed();
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.IsTruthy);
        _il.Emit(OpCodes.Brfalse, elseLabel);

        EmitExpression(t.ThenBranch);
        EnsureBoxed();
        _il.Emit(OpCodes.Br, endLabel);

        _il.MarkLabel(elseLabel);
        EmitExpression(t.ElseBranch);
        EnsureBoxed();

        _il.MarkLabel(endLabel);
        SetStackUnknown();
    }

    private void EmitThis()
    {
        // Load 'this' from outer state machine if captured
        if (_builder.Captures.Contains("this"))
        {
            // Get outer state machine's ThisField
            if (_ctx?.AsyncArrowOuterBuilders?.TryGetValue(_builder.Arrow, out var outerBuilder) == true &&
                outerBuilder.ThisField != null)
            {
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldfld, _builder.OuterStateMachineField);
                _il.Emit(OpCodes.Unbox, _builder.OuterStateMachineType);
                _il.Emit(OpCodes.Ldfld, outerBuilder.ThisField);
                SetStackUnknown();
                return;
            }
        }

        _il.Emit(OpCodes.Ldnull);
        SetStackType(StackType.Null);
    }

    private void EmitDynamicImport(Expr.DynamicImport di)
    {
        // Emit the path expression
        EmitExpression(di.PathExpression);
        EnsureBoxed();

        // Convert to string
        _il.Emit(OpCodes.Call, typeof(Convert).GetMethod("ToString", [typeof(object)])!);

        // Push current module path (or empty string if not in module context)
        _il.Emit(OpCodes.Ldstr, _ctx?.CurrentModulePath ?? "");

        // Call DynamicImportModule(path, currentModulePath) -> Task<object?>
        _il.Emit(OpCodes.Call, _ctx!.Runtime!.DynamicImportModule);

        // Wrap Task<object?> in SharpTSPromise
        _il.Emit(OpCodes.Call, _ctx.Runtime.WrapTaskAsPromise);

        SetStackUnknown();
    }
}
