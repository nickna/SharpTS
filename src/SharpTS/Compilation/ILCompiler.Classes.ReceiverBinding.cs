using System.Reflection;
using System.Reflection.Emit;
using SharpTS.Compilation.Emitters;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

public partial class ILCompiler
{
    private static bool IsConstructorParameterProperty(Stmt statement, Stmt.Function? constructor)
        => statement is Stmt.Expression { Expr: Expr.Set { Object: Expr.This, Value: Expr.Variable value } set }
            && constructor?.Parameters.Any(parameter => parameter.IsParameterProperty
                && ReferenceEquals(parameter.Name, set.Name) && ReferenceEquals(parameter.Name, value.Name)) == true;

    private void EmitEffectiveClassElements(CompilationContext context, IEnumerable<Stmt.Field> fields,
        IReadOnlyDictionary<string, FieldBuilder>? backingFields, IReadOnlyDictionary<string, Type>? propertyTypes,
        Action<Stmt.Field> loadComputedKey, IEnumerable<Stmt.AutoAccessor>? autoAccessors = null)
    {
        var il = context.IL;
        var emitter = new ILEmitter(context);
        bool previous = context.SuppressNewTarget;
        context.SuppressNewTarget = true;
        var slots = EmitPrivateFieldStorage(il, context.CurrentClassName!, context, emitter);
        var elements = fields.Where(field => !field.IsStatic && !field.IsDeclare).Cast<Stmt>()
            .Concat((autoAccessors ?? []).Where(accessor => !accessor.IsStatic))
            .OrderBy(element => element is Stmt.Field field ? field.Name.SourceOrderOffset : ((Stmt.AutoAccessor)element).Name.SourceOrderOffset);
        foreach (var element in elements)
        {
            if (element is Stmt.AutoAccessor accessor)
            {
                EmitReceiverAutoAccessorInitializer(context, accessor, context.CurrentClassName!);
                continue;
            }
            var field = (Stmt.Field)element;
            if (field.IsPrivate)
            {
                EmitPrivateFieldInitializer(il, field, slots!, emitter);
                continue;
            }
            var value = il.DeclareLocal(_types.Object);
            if (field.Initializer is { } initializer)
            {
                emitter.EmitExpression(initializer);
                emitter.EmitBoxIfNeeded(initializer);
            }
            else il.Emit(OpCodes.Ldsfld, _runtime.Sentinels.UndefinedInstance);
            il.Emit(OpCodes.Stloc, value);
            var done = il.DefineLabel();
            string property = NamingConventions.ToPascalCase(field.Name.Lexeme);
            bool nativeOwn = context.CurrentClassBuilder is TypeBuilder nativeOwnOwner && _nativeOwnAdapters.ContainsKey(nativeOwnOwner);
            if (field.ComputedKey == null && (field.Initializer != null || nativeOwn)
                && backingFields?.TryGetValue(property, out var backing) == true)
            {
                var external = il.DefineLabel();
                il.Emit(OpCodes.Ldloc, context.ClassConstructorReceiver!);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Bne_Un, external);
                GetClassRegistry().TryGetOwnGuestDataField((TypeBuilder)backing.DeclaringType!, field.Name.Lexeme, out var guestField);
                void Store(bool ownStorage)
                {
                    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldloc, value);
                    if (guestField != null && (guestField.IsPrimitive || ownStorage))
                        il.Emit(OpCodes.Call, EmitterTypeHelpers.SelfMethodReference(guestField.GuestSetter));
                    else
                    {
                        EmitBoxedFieldConversion(il, propertyTypes![property]);
                        il.Emit(OpCodes.Stfld, backing);
                    }
                }
                if (nativeOwn)
                {
                    var legacy = EmitNativeOwnReceiverGuard(il);
                    // Complete the throwing public field definition before selecting
                    // the exact owner-local native/raw storage for its value.
                    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, field.Name.Lexeme);
                    EmitOwnDataDefinition(il, value);
                    Store(true);
                    // This internal overlay removal does not delete the property.
                    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, field.Name.Lexeme);
                    il.Emit(OpCodes.Call, _runtime.DescriptorStorage.DeleteProperty); il.Emit(OpCodes.Pop);
                    il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldstr, field.Name.Lexeme);
                    il.Emit(OpCodes.Ldstr, NativeOwnFieldIdentity(guestField!)); il.Emit(OpCodes.Ldc_I4_1);
                    il.Emit(OpCodes.Call, _runtime.GuestOwnProperties!.CommitDefinition);
                    il.Emit(OpCodes.Br, done); il.MarkLabel(legacy);
                }
                if (field.Initializer == null) il.Emit(OpCodes.Br, external);
                else Store(false);
                il.Emit(OpCodes.Br, done);
                il.MarkLabel(external);
            }
            il.Emit(OpCodes.Ldloc, context.ClassConstructorReceiver!);
            if (field.ComputedKey == null) il.Emit(OpCodes.Ldstr, field.Name.Lexeme);
            else loadComputedKey(field);
            EmitOwnDataDefinition(il, value);
            il.MarkLabel(done);
        }
        context.SuppressNewTarget = previous;
    }

    private void EmitBoxedFieldConversion(ILGenerator il, Type target)
    {
        // This value was already boxed and spilled. Do not reuse the source
        // expression's primitive stack proof after loading its object local.
        if (target == _types.Object) return;
        if (target == _types.Double) il.Emit(OpCodes.Call, _runtime.NumericCoercion.ConvertToNumber);
        else if (target == _types.Boolean) il.Emit(OpCodes.Call, _runtime.Booleans.IsTruthy);
        else if (target.IsValueType || target.IsGenericParameter) il.Emit(OpCodes.Unbox_Any, target);
        else il.Emit(OpCodes.Castclass, target);
    }

    private void EmitOwnDataDefinition(ILGenerator il, LocalBuilder value)
    {
        // receiver and key are on the stack. Object.defineProperty is the
        // throwing definition protocol (including Proxy traps), not [[Set]].
        il.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(_types.DictionaryStringObject));
        foreach (var name in new[] { "value", "writable", "enumerable", "configurable" })
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldstr, name);
            if (name == "value") il.Emit(OpCodes.Ldloc, value);
            else { il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Box, _types.Boolean); }
            il.Emit(OpCodes.Callvirt, _types.DictionaryStringObjectSetItem);
        }
        il.Emit(OpCodes.Newobj, _runtime.ObjectStorage.Constructor);
        il.Emit(OpCodes.Call, _runtime.ObjectDescriptors.DefineProperty);
        il.Emit(OpCodes.Pop);
    }

    private static void EmitOnceOnlyConstructorElements(CompilationContext context, Action emit)
    {
        var il = context.IL;
        var started = il.DeclareLocal(context.Types.Boolean);
        context.EmitConstructorOwnElements = () =>
        {
            var fresh = il.DefineLabel();
            il.Emit(OpCodes.Ldloc, started);
            il.Emit(OpCodes.Brfalse, fresh);
            GuestErrorEmitter.ThrowError(il, context.Runtime!.Errors.CreateException,
                context.Runtime.Errors.ReferenceErrorConstructor, "Class instance elements have already been initialized");
            il.MarkLabel(fresh);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Stloc, started);
            emit();
        };
    }

    private bool IsConstructorThisArrowParent(object? callable)
    {
        while (callable is Expr.ArrowFunction arrow)
        {
            if (arrow.HasOwnThis) return false;
            _arrowEnclosingCallable.TryGetValue(arrow, out callable);
        }
        if (callable is not Stmt.Function { IsStatic: false } function || function.Name.Lexeme != "constructor") return false;
        return _classes.Declarations.Any(source => source.Methods.Contains(function)
                && GetQualifiedClassDeclarationName(source) == _currentCollectClassName)
            || _classExprs.Names.Any(entry => entry.Value == _currentCollectClassName && entry.Key.Methods.Contains(function));
    }
}
