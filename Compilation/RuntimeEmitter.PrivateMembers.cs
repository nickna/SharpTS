using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

/// <summary>
/// Emits runtime helpers for private field/method access in async contexts.
/// </summary>
/// <remarks>
/// Async/generator functions compile to state machine structs. The state machine's
/// MoveNext() method doesn't have direct access to the class's private field storage
/// (ConditionalWeakTable). These helpers use reflection to:
/// 1. Get the __privateFields static field from the declaring class
/// 2. Perform brand checking via TryGetValue on the ConditionalWeakTable
/// 3. Access/modify the private field dictionary
/// </remarks>
public partial class RuntimeEmitter
{
    /// <summary>
    /// Emits all private member access helper methods.
    /// </summary>
    private void EmitPrivateMemberHelpers(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        EmitGetPrivateField(typeBuilder, runtime);
        EmitSetPrivateField(typeBuilder, runtime);
        EmitCallPrivateMethod(typeBuilder, runtime);
        EmitGetStaticPrivateField(typeBuilder, runtime);
        EmitSetStaticPrivateField(typeBuilder, runtime);
        EmitCallStaticPrivateMethod(typeBuilder, runtime);
    }

    /// <summary>
    /// Emits: public static object GetPrivateField(object instance, Type declaringClass, string fieldName)
    /// </summary>
    /// <remarks>
    /// Implementation:
    /// 1. Get __privateFields FieldInfo via reflection on declaringClass
    /// 2. Get the ConditionalWeakTable value
    /// 3. Call TryGetValue for brand check
    /// 4. If brand check fails, throw TypeError
    /// 5. Return dict[fieldName]
    /// </remarks>
    private void EmitGetPrivateField(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetPrivateField",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Type, _types.String]
        );
        runtime.GetPrivateField = method;

        var il = method.GetILGenerator();

        // Local variables
        var fieldInfoLocal = il.DeclareLocal(_types.FieldInfo);
        var cwtLocal = il.DeclareLocal(_types.Object);
        var dictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));

        // Get __privateFields field via reflection
        // FieldInfo fieldInfo = declaringClass.GetField("__privateFields", BindingFlags.NonPublic | BindingFlags.Static);
        il.Emit(OpCodes.Ldarg_1); // declaringClass
        il.Emit(OpCodes.Ldstr, "__privateFields");
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.NonPublic | BindingFlags.Static));
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetField", [typeof(string), typeof(BindingFlags)])!);
        il.Emit(OpCodes.Stloc, fieldInfoLocal);

        // If fieldInfo is null, the class has no private fields - throw
        var hasFieldLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Brtrue, hasFieldLabel);

        // No __privateFields - throw TypeError
        il.Emit(OpCodes.Ldstr, "TypeError: Cannot read private member - class has no private fields");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(hasFieldLabel);

        // Get the ConditionalWeakTable value: object cwt = fieldInfo.GetValue(null);
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, typeof(FieldInfo).GetMethod("GetValue", [typeof(object)])!);
        il.Emit(OpCodes.Stloc, cwtLocal);

        // We need to use reflection to call TryGetValue since we don't know the exact generic type
        // Get the TryGetValue method via reflection
        // The ConditionalWeakTable is ConditionalWeakTable<object, Dictionary<string, object?>>
        var cwtType = typeof(ConditionalWeakTable<,>).MakeGenericType(typeof(object), typeof(Dictionary<string, object>));
        var tryGetValueMethod = cwtType.GetMethod("TryGetValue", [typeof(object), typeof(Dictionary<string, object>).MakeByRefType()])!;

        // bool found = cwt.TryGetValue(instance, out dict);
        il.Emit(OpCodes.Ldloc, cwtLocal);
        il.Emit(OpCodes.Castclass, cwtType);
        il.Emit(OpCodes.Ldarg_0); // instance
        il.Emit(OpCodes.Ldloca, dictLocal);
        il.Emit(OpCodes.Callvirt, tryGetValueMethod);

        // If not found, throw TypeError (brand check failed)
        var brandCheckPassedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, brandCheckPassedLabel);

        // Brand check failed
        il.Emit(OpCodes.Ldstr, "TypeError: Cannot read private member from an object whose class did not declare it");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(brandCheckPassedLabel);

        // Return dict[fieldName]
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_2); // fieldName
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("get_Item", [typeof(string)])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void SetPrivateField(object instance, Type declaringClass, string fieldName, object value)
    /// </summary>
    private void EmitSetPrivateField(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetPrivateField",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Object, _types.Type, _types.String, _types.Object]
        );
        runtime.SetPrivateField = method;

        var il = method.GetILGenerator();

        // Local variables
        var fieldInfoLocal = il.DeclareLocal(_types.FieldInfo);
        var cwtLocal = il.DeclareLocal(_types.Object);
        var dictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));

        // Get __privateFields field via reflection
        il.Emit(OpCodes.Ldarg_1); // declaringClass
        il.Emit(OpCodes.Ldstr, "__privateFields");
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.NonPublic | BindingFlags.Static));
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetField", [typeof(string), typeof(BindingFlags)])!);
        il.Emit(OpCodes.Stloc, fieldInfoLocal);

        // If fieldInfo is null, throw
        var hasFieldLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Brtrue, hasFieldLabel);

        il.Emit(OpCodes.Ldstr, "TypeError: Cannot write private member - class has no private fields");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(hasFieldLabel);

        // Get the ConditionalWeakTable value
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, typeof(FieldInfo).GetMethod("GetValue", [typeof(object)])!);
        il.Emit(OpCodes.Stloc, cwtLocal);

        // TryGetValue for brand check
        var cwtType = typeof(ConditionalWeakTable<,>).MakeGenericType(typeof(object), typeof(Dictionary<string, object>));
        var tryGetValueMethod = cwtType.GetMethod("TryGetValue", [typeof(object), typeof(Dictionary<string, object>).MakeByRefType()])!;

        il.Emit(OpCodes.Ldloc, cwtLocal);
        il.Emit(OpCodes.Castclass, cwtType);
        il.Emit(OpCodes.Ldarg_0); // instance
        il.Emit(OpCodes.Ldloca, dictLocal);
        il.Emit(OpCodes.Callvirt, tryGetValueMethod);

        // Brand check
        var brandCheckPassedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, brandCheckPassedLabel);

        il.Emit(OpCodes.Ldstr, "TypeError: Cannot write private member to an object whose class did not declare it");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(brandCheckPassedLabel);

        // dict[fieldName] = value
        il.Emit(OpCodes.Ldloc, dictLocal);
        il.Emit(OpCodes.Ldarg_2); // fieldName
        il.Emit(OpCodes.Ldarg_3); // value
        il.Emit(OpCodes.Callvirt, typeof(Dictionary<string, object>).GetMethod("set_Item", [typeof(string), typeof(object)])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CallPrivateMethod(object instance, Type declaringClass, string methodName, object[] args)
    /// </summary>
    private void EmitCallPrivateMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CallPrivateMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Object, _types.Type, _types.String, _types.ObjectArray]
        );
        runtime.CallPrivateMethod = method;

        var il = method.GetILGenerator();

        // Local variables
        var fieldInfoLocal = il.DeclareLocal(_types.FieldInfo);
        var cwtLocal = il.DeclareLocal(_types.Object);
        var dictLocal = il.DeclareLocal(typeof(Dictionary<string, object>));
        var methodInfoLocal = il.DeclareLocal(_types.MethodInfo);

        // First, perform brand check (same as GetPrivateField but just for validation)
        // Get __privateFields field via reflection
        il.Emit(OpCodes.Ldarg_1); // declaringClass
        il.Emit(OpCodes.Ldstr, "__privateFields");
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.NonPublic | BindingFlags.Static));
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetField", [typeof(string), typeof(BindingFlags)])!);
        il.Emit(OpCodes.Stloc, fieldInfoLocal);

        // Skip brand check if no private fields (class might only have private methods)
        var skipBrandCheckLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Brfalse, skipBrandCheckLabel);

        // Perform brand check
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, typeof(FieldInfo).GetMethod("GetValue", [typeof(object)])!);
        il.Emit(OpCodes.Stloc, cwtLocal);

        var cwtType = typeof(ConditionalWeakTable<,>).MakeGenericType(typeof(object), typeof(Dictionary<string, object>));
        var tryGetValueMethod = cwtType.GetMethod("TryGetValue", [typeof(object), typeof(Dictionary<string, object>).MakeByRefType()])!;

        il.Emit(OpCodes.Ldloc, cwtLocal);
        il.Emit(OpCodes.Castclass, cwtType);
        il.Emit(OpCodes.Ldarg_0); // instance
        il.Emit(OpCodes.Ldloca, dictLocal);
        il.Emit(OpCodes.Callvirt, tryGetValueMethod);

        var brandCheckPassedLabel = il.DefineLabel();
        il.Emit(OpCodes.Brtrue, brandCheckPassedLabel);

        il.Emit(OpCodes.Ldstr, "TypeError: Cannot call private method on an object whose class did not declare it");
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(brandCheckPassedLabel);
        il.MarkLabel(skipBrandCheckLabel);

        // Get the private method: "__private_" + methodName
        // MethodInfo methodInfo = declaringClass.GetMethod("__private_" + methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        il.Emit(OpCodes.Ldarg_1); // declaringClass
        il.Emit(OpCodes.Ldstr, "__private_");
        il.Emit(OpCodes.Ldarg_2); // methodName
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.NonPublic | BindingFlags.Instance));
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetMethod", [typeof(string), typeof(BindingFlags)])!);
        il.Emit(OpCodes.Stloc, methodInfoLocal);

        // If method not found, throw
        var methodFoundLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, methodInfoLocal);
        il.Emit(OpCodes.Brtrue, methodFoundLabel);

        il.Emit(OpCodes.Ldstr, "TypeError: Private method not found: ");
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(methodFoundLabel);

        // Invoke the method: methodInfo.Invoke(instance, args)
        il.Emit(OpCodes.Ldloc, methodInfoLocal);
        il.Emit(OpCodes.Ldarg_0); // instance
        il.Emit(OpCodes.Ldarg_3); // args
        il.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object GetStaticPrivateField(Type declaringClass, string fieldName)
    /// </summary>
    private void EmitGetStaticPrivateField(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "GetStaticPrivateField",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Type, _types.String]
        );
        runtime.GetStaticPrivateField = method;

        var il = method.GetILGenerator();

        var fieldInfoLocal = il.DeclareLocal(_types.FieldInfo);

        // Get "__private_" + fieldName static field
        il.Emit(OpCodes.Ldarg_0); // declaringClass
        il.Emit(OpCodes.Ldstr, "__private_");
        il.Emit(OpCodes.Ldarg_1); // fieldName
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.NonPublic | BindingFlags.Static));
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetField", [typeof(string), typeof(BindingFlags)])!);
        il.Emit(OpCodes.Stloc, fieldInfoLocal);

        // If not found, throw
        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Brtrue, foundLabel);

        il.Emit(OpCodes.Ldstr, "TypeError: Static private field not found: ");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(foundLabel);

        // Return fieldInfo.GetValue(null)
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, typeof(FieldInfo).GetMethod("GetValue", [typeof(object)])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static void SetStaticPrivateField(Type declaringClass, string fieldName, object value)
    /// </summary>
    private void EmitSetStaticPrivateField(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "SetStaticPrivateField",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Void,
            [_types.Type, _types.String, _types.Object]
        );
        runtime.SetStaticPrivateField = method;

        var il = method.GetILGenerator();

        var fieldInfoLocal = il.DeclareLocal(_types.FieldInfo);

        // Get "__private_" + fieldName static field
        il.Emit(OpCodes.Ldarg_0); // declaringClass
        il.Emit(OpCodes.Ldstr, "__private_");
        il.Emit(OpCodes.Ldarg_1); // fieldName
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.NonPublic | BindingFlags.Static));
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetField", [typeof(string), typeof(BindingFlags)])!);
        il.Emit(OpCodes.Stloc, fieldInfoLocal);

        // If not found, throw
        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Brtrue, foundLabel);

        il.Emit(OpCodes.Ldstr, "TypeError: Static private field not found: ");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(foundLabel);

        // fieldInfo.SetValue(null, value)
        il.Emit(OpCodes.Ldloc, fieldInfoLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldarg_2); // value
        il.Emit(OpCodes.Callvirt, typeof(FieldInfo).GetMethod("SetValue", [typeof(object), typeof(object)])!);
        il.Emit(OpCodes.Ret);
    }

    /// <summary>
    /// Emits: public static object CallStaticPrivateMethod(Type declaringClass, string methodName, object[] args)
    /// </summary>
    private void EmitCallStaticPrivateMethod(TypeBuilder typeBuilder, EmittedRuntime runtime)
    {
        var method = typeBuilder.DefineMethod(
            "CallStaticPrivateMethod",
            MethodAttributes.Public | MethodAttributes.Static,
            _types.Object,
            [_types.Type, _types.String, _types.ObjectArray]
        );
        runtime.CallStaticPrivateMethod = method;

        var il = method.GetILGenerator();

        var methodInfoLocal = il.DeclareLocal(_types.MethodInfo);

        // Get "__private_" + methodName static method
        il.Emit(OpCodes.Ldarg_0); // declaringClass
        il.Emit(OpCodes.Ldstr, "__private_");
        il.Emit(OpCodes.Ldarg_1); // methodName
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Ldc_I4, (int)(BindingFlags.NonPublic | BindingFlags.Static));
        il.Emit(OpCodes.Callvirt, typeof(Type).GetMethod("GetMethod", [typeof(string), typeof(BindingFlags)])!);
        il.Emit(OpCodes.Stloc, methodInfoLocal);

        // If not found, throw
        var foundLabel = il.DefineLabel();
        il.Emit(OpCodes.Ldloc, methodInfoLocal);
        il.Emit(OpCodes.Brtrue, foundLabel);

        il.Emit(OpCodes.Ldstr, "TypeError: Static private method not found: ");
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", [typeof(string), typeof(string)])!);
        il.Emit(OpCodes.Newobj, typeof(Exception).GetConstructor([typeof(string)])!);
        il.Emit(OpCodes.Throw);

        il.MarkLabel(foundLabel);

        // Invoke: methodInfo.Invoke(null, args)
        il.Emit(OpCodes.Ldloc, methodInfoLocal);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldarg_2); // args
        il.Emit(OpCodes.Callvirt, typeof(MethodInfo).GetMethod("Invoke", [typeof(object), typeof(object[])])!);
        il.Emit(OpCodes.Ret);
    }
}
