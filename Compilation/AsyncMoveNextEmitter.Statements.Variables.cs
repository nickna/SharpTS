using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class AsyncMoveNextEmitter
{
    private void EmitVarDeclaration(Stmt.Var v)
    {
        string name = v.Name.Lexeme;

        // Check if this variable is hoisted to state machine
        var field = _builder.GetVariableField(name);
        if (field != null)
        {
            // Hoisted variable - store to field
            if (v.Initializer != null)
            {
                // Emit expression first (important: may contain await which clears stack)
                EmitExpression(v.Initializer);
                EnsureBoxed();
                // Now store to field - load 'this' after await completes
                var temp = _il.DeclareLocal(typeof(object));
                _il.Emit(OpCodes.Stloc, temp);
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldloc, temp);
                _il.Emit(OpCodes.Stfld, field);
            }
            else
            {
                // Initialize to null
                _il.Emit(OpCodes.Ldarg_0);
                _il.Emit(OpCodes.Ldnull);
                _il.Emit(OpCodes.Stfld, field);
            }
        }
        else
        {
            // Not hoisted - use local variable
            var local = _il.DeclareLocal(typeof(object));
            _ctx!.Locals.RegisterLocal(name, local);

            if (v.Initializer != null)
            {
                EmitExpression(v.Initializer);
                EnsureBoxed();
                _il.Emit(OpCodes.Stloc, local);
            }
            else
            {
                _il.Emit(OpCodes.Ldnull);
                _il.Emit(OpCodes.Stloc, local);
            }
        }
    }
}
