using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    internal static bool ClassConstructorMayReplace(List<Stmt.Function> methods)
    {
        var visitor = new ConstructorReplacementVisitor();
        foreach (var statement in methods.FirstOrDefault(method => !method.IsStatic && method.Name.Lexeme == "constructor" && method.Body != null)?.Body ?? [])
            visitor.Visit(statement);
        return visitor.HasReplacement;
    }

    private void ConfigureClassConstructorReturns(CompilationContext context, Stmt.Function? constructor, bool derived, bool forceReceiver = false)
    {
        context.AsyncArrowBuilders = _async.ArrowBuilders.Count > 0 ? _async.ArrowBuilders : null;
        context.AsyncArrowOuterBuilders = _async.ArrowOuterBuilders;
        context.AsyncArrowParentBuilders = _async.ArrowParentBuilders;
        var visitor = new ConstructorReplacementVisitor();
        foreach (var statement in constructor?.Body ?? [])
            visitor.Visit(statement);
        var sourceType = context.CurrentClassExpr is { } expression
            ? _typeMap.GetClassExprType(expression) : _typeMap.GetClassType(context.CurrentClassName ?? "");
        bool needsReceiver = forceReceiver || visitor.HasReplacement || _typeMapper.IsDynamicClassInstance(sourceType);
        context.CapturesClassConstructorReturn = needsReceiver;
        context.IsDerivedClassConstructor = derived;
        if (derived) context.ConstructorSuperBound = context.IL.DeclareLocal(context.Types.Boolean);
        if (needsReceiver)
        {
            context.ClassConstructorReceiver = context.IL.DeclareLocal(context.Types.Object);
            context.ClassConstructorReturnValue = context.IL.DeclareLocal(context.Types.Object);
            context.IL.Emit(OpCodes.Ldsfld, context.Runtime!.Sentinels.UndefinedInstance);
            context.IL.Emit(OpCodes.Stloc, context.ClassConstructorReceiver);
            context.ClassConstructorThisCell = context.IL.DeclareLocal(context.Types.ObjectArray);
            context.HasGuestReceiver = true;
            context.IL.Emit(OpCodes.Ldc_I4_1);
            context.IL.Emit(OpCodes.Newarr, context.Types.Object);
            context.IL.Emit(OpCodes.Dup);
            context.IL.Emit(OpCodes.Ldc_I4_0);
            context.IL.Emit(OpCodes.Ldsfld, context.Runtime!.Sentinels.UndefinedInstance);
            context.IL.Emit(OpCodes.Stelem_Ref);
            context.IL.Emit(OpCodes.Stloc, context.ClassConstructorThisCell);
        }
    }

    private static void InitializeClassConstructorReceiver(CompilationContext context, ILGenerator il)
    {
        if (context.ClassConstructorReceiver == null) return;
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, context.Runtime!.DynamicConstruction.ResolveClassReturn);
        il.Emit(OpCodes.Stloc, context.ClassConstructorReceiver);
        if (context.ClassConstructorThisCell is { } cell)
        {
            il.Emit(OpCodes.Ldloc, cell);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, context.ClassConstructorReceiver);
            il.Emit(OpCodes.Stelem_Ref);
        }
    }

    private bool ClassNeedsConstructorReceiver(string? name)
    {
        if (name is null) return false;
        var expression = _classExprs.Names.FirstOrDefault(entry => entry.Value == name).Key;
        var source = expression is null ? _classes.Declarations.FirstOrDefault(declaration =>
            GetQualifiedClassDeclarationName(declaration) == name) : null;
        var type = expression is not null ? _typeMap.GetClassExprType(expression)
            : source is not null ? _typeMap.GetClassType(source) : _typeMap.GetClassType(name);
        return _typeMapper.IsDynamicClassInstance(type);
    }
}
