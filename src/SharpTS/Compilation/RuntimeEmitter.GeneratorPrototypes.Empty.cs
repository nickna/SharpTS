using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SharpTS.Compilation;

public partial class RuntimeEmitter
{
    private void EmitEmptyGeneratorConstructor(EmittedGeneratorPrototypeRuntime prototypes,
        EmittedGeneratorPrototypeKindRuntime kind, Type generatorInterface,
        MethodInfo next, MethodInfo returnMethod, MethodInfo throwMethod, bool isAsync,
        GeneratorPrototypeGraphInputs graph, EmittedErrorRuntime errors, FieldInfo undefined, MethodInfo? coerceAwaitable)
    {
        // Empty source needs no parser or runtime compiler. Its iterator implements
        // the same emitted protocol as ordinary generators and has no body state.
        var iterator = EmitTypeDefinitions.DefineNestedType(prototypes.Type, isAsync ? "EmptyAsyncGenerator" : "EmptyGenerator",
            TypeAttributes.NestedPublic | TypeAttributes.Sealed, _types.Object, [generatorInterface]);
        var ctor = iterator.DefineDefaultConstructor(MethodAttributes.Public);
        const MethodAttributes implementation = MethodAttributes.Public | MethodAttributes.Virtual
            | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot;

        MethodBuilder Implement(string name, Type result, Type[] parameters, params MethodInfo[] contracts)
        {
            var method = iterator.DefineMethod(name, implementation, result, parameters);
            foreach (var contract in contracts) iterator.DefineMethodOverride(method, contract);
            return method;
        }

        var nextBody = Implement("next", isAsync ? _types.TaskOfObject : _types.Object, [_types.Object], next);
        var returnBody = Implement("return", isAsync ? _types.TaskOfObject : _types.Object, [_types.Object], returnMethod);
        var throwBody = Implement("throw", isAsync ? _types.TaskOfObject : _types.Object, [_types.Object], throwMethod);
        ILGenerator il;
        if (isAsync) EmitAsyncRequests();
        else
        {
            EmitResult(nextBody.GetILGenerator(), false);
            EmitResult(returnBody.GetILGenerator(), true);
            il = throwBody.GetILGenerator();
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, errors.CreateException); il.Emit(OpCodes.Throw);
        }

        if (isAsync)
        {
            var current = Implement("get_Current", _types.Object, [],
                _types.GetProperty(_types.IAsyncEnumeratorOfObject, "Current")!.GetGetMethod()!);
            il = current.GetILGenerator(); il.Emit(OpCodes.Ldsfld, undefined); il.Emit(OpCodes.Ret);
            var move = Implement("MoveNextAsync", _types.ValueTaskOfBool, [],
                _types.GetMethodNoParams(_types.IAsyncEnumeratorOfObject, "MoveNextAsync"));
            il = move.GetILGenerator(); il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Newobj, _types.GetConstructor(_types.ValueTaskOfBool, _types.Boolean)!); il.Emit(OpCodes.Ret);
            var dispose = Implement("DisposeAsync", _types.ValueTask, [],
                _types.GetMethodNoParams(_types.IAsyncDisposable, "DisposeAsync"));
            il = dispose.GetILGenerator(); var completed = il.DeclareLocal(_types.ValueTask);
            il.Emit(OpCodes.Ldloca, completed); il.Emit(OpCodes.Initobj, _types.ValueTask);
            il.Emit(OpCodes.Ldloc, completed); il.Emit(OpCodes.Ret);
            var enumerate = Implement("GetAsyncEnumerator", _types.IAsyncEnumeratorOfObject, [_types.CancellationToken],
                _types.GetMethod(_types.IAsyncEnumerableOfObject, "GetAsyncEnumerator", _types.CancellationToken));
            il = enumerate.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
        }
        else
        {
            var current = Implement("get_Current", _types.Object, [],
                _types.GetProperty(_types.IEnumeratorOfObject, "Current")!.GetGetMethod()!,
                _types.GetProperty(_types.IEnumerator, "Current")!.GetGetMethod()!);
            il = current.GetILGenerator(); il.Emit(OpCodes.Ldsfld, undefined); il.Emit(OpCodes.Ret);
            var move = Implement("MoveNext", _types.Boolean, [], _types.GetMethodNoParams(_types.IEnumerator, "MoveNext"));
            il = move.GetILGenerator(); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
            Implement("Reset", _types.Void, [], _types.GetMethodNoParams(_types.IEnumerator, "Reset")).GetILGenerator().Emit(OpCodes.Ret);
            Implement("Dispose", _types.Void, [], _types.GetMethodNoParams(_types.IDisposable, "Dispose")).GetILGenerator().Emit(OpCodes.Ret);
            var enumerate = Implement("GetEnumerator", _types.IEnumeratorOfObject, [],
                _types.GetMethodNoParams(_types.IEnumerableOfObject, "GetEnumerator"));
            il = enumerate.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
            enumerate = Implement("System.Collections.IEnumerable.GetEnumerator", _types.IEnumerator, [],
                _types.GetMethodNoParams(_types.IEnumerable, "GetEnumerator"));
            il = enumerate.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
            var identity = Implement("iterator", _types.Object, [],
                _types.GetMethodNoParams(generatorInterface, "iterator"));
            il = identity.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ret);
        }

        var marker = isAsync ? typeof(AsyncIteratorStateMachineAttribute) : typeof(IteratorStateMachineAttribute);
        var markerCtor = marker.GetConstructor([typeof(Type)])!;
        kind.EmptyFunction.SetCustomAttribute(markerCtor, CustomAttributeEncoder.Encode(markerCtor, iterator));
        kind.EmptyFunction.SetCustomAttribute(graph.Attributes.NonConstructibleCtor, CustomAttributeEncoder.EmptyBlob);
        il = kind.EmptyFunction.GetILGenerator();
        il.Emit(OpCodes.Newobj, ctor); il.Emit(OpCodes.Dup);
        _types.EmitLoadMethodInfo(il, kind.EmptyFunction); il.Emit(OpCodes.Call, prototypes.Associate);
        il.Emit(OpCodes.Ret); prototypes.MarkBodyEmitted(kind.EmptyFunction);
        iterator.CreateType();

        il = kind.ConstructorMethod.GetILGenerator();
        var empty = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Brfalse, empty);
        GuestErrorEmitter.ThrowError(il, errors.CreateException, errors.TypeErrorConstructor,
            "Dynamic generator function source construction is not supported");
        il.MarkLabel(empty); il.Emit(OpCodes.Ldnull);
        var parameterProducer = EmitReflectedMethodLiteral(il, kind.EmptyFunction);
        il.Emit(OpCodes.Ldstr, "anonymous"); il.Emit(OpCodes.Ldc_I4_0);
        EmitReflectedFunctionConstructor(il, graph.Construction.CachedConstructor, parameterProducer, cached: true); il.Emit(OpCodes.Ret);
        prototypes.MarkBodyEmitted(kind.ConstructorMethod);

        void EmitResult(ILGenerator body, bool returned)
        {
            EmitResultObject(body, () =>
            {
                if (returned) body.Emit(OpCodes.Ldarg_1); else body.Emit(OpCodes.Ldsfld, undefined);
            });
            body.Emit(OpCodes.Ret);
        }

        void EmitResultObject(ILGenerator body, Action value)
        {
            body.Emit(OpCodes.Newobj, _types.DictionaryStringObjectCtor);
            body.Emit(OpCodes.Dup); body.Emit(OpCodes.Ldstr, "value");
            value();
            body.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
            body.Emit(OpCodes.Dup); body.Emit(OpCodes.Ldstr, "done"); body.Emit(OpCodes.Ldc_I4_1);
            body.Emit(OpCodes.Box, _types.Boolean);
            body.Emit(OpCodes.Callvirt, _types.GetMethod(_types.DictionaryStringObject, "set_Item", _types.String, _types.Object));
        }

        void EmitAsyncRequests()
        {
            var tail = iterator.DefineField("_pending", _types.Task, FieldAttributes.Private);
            var fromResult = EmitGenerics.MakeGenericMethod(
                _types.GetMethods(_types.Task).Single(method => method.Name == "FromResult" && method.IsGenericMethodDefinition), _types.Object);
            var fromException = EmitGenerics.MakeGenericMethod(
                _types.GetMethods(_types.Task).Single(method => method.Name == "FromException" && method.IsGenericMethodDefinition), _types.Object);
            var completeReturn = iterator.DefineMethod("CompleteReturn", MethodAttributes.Private | MethodAttributes.Static,
                _types.Object, [_types.TaskOfObject]);
            var body = completeReturn.GetILGenerator();
            var awaiter = body.DeclareLocal(_types.TaskAwaiterOfObject);
            body.Emit(OpCodes.Ldarg_0);
            body.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.TaskOfObject, "GetAwaiter"));
            body.Emit(OpCodes.Stloc, awaiter);
            EmitResultObject(body, () =>
            {
                body.Emit(OpCodes.Ldloca, awaiter);
                body.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.TaskAwaiterOfObject, "GetResult"));
            });
            body.Emit(OpCodes.Ret);
            var resultCallbackType = _types.MakeGenericType(typeof(Func<,>), _types.TaskOfObject, _types.Object);
            var completeResult = EmitGenerics.MakeGenericMethod(_types.GetMethods(_types.TaskOfObject).Single(method => method.Name == "ContinueWith"
                && method.IsGenericMethodDefinition && method.GetParameters() is { Length: 2 } parameters
                && parameters[0].ParameterType.IsGenericType
                && parameters[0].ParameterType.GetGenericTypeDefinition() == typeof(Func<,>)
                && parameters[0].ParameterType.GetGenericArguments()[0] == _types.TaskOfObject
                && parameters[1].ParameterType == typeof(TaskContinuationOptions)), _types.Object);
            var queueCallbackType = _types.MakeGenericType(typeof(Func<,,>), _types.Task, _types.Object, _types.TaskOfObject);
            var queue = EmitGenerics.MakeGenericMethod(_types.GetMethods(_types.Task).Single(method => method.Name == "ContinueWith"
                && method.IsGenericMethodDefinition && method.GetParameters() is { Length: 3 } parameters
                && parameters[0].ParameterType.IsGenericType
                && parameters[0].ParameterType.GetGenericTypeDefinition() == typeof(Func<,,>)
                && parameters[1].ParameterType == _types.Object
                && parameters[2].ParameterType == typeof(TaskContinuationOptions)), _types.TaskOfObject);
            var unwrap = EmitGenerics.MakeGenericMethod(
                _types.GetMethods(typeof(TaskExtensions)).Single(method => method.Name == "Unwrap" && method.IsGenericMethodDefinition), _types.Object);
            var completionType = _types.MakeGenericType(typeof(TaskCompletionSource<>), _types.Object);
            var completionTask = _types.GetPropertyGetter(completionType, "Task");
            var settle = iterator.DefineMethod("SettleRequest", MethodAttributes.Private | MethodAttributes.Static,
                _types.Void, [_types.TaskOfObject, _types.Object]);
            body = settle.GetILGenerator();
            var settleAwaiter = body.DeclareLocal(_types.TaskAwaiterOfObject);
            var settledValue = body.DeclareLocal(_types.Object);
            var settledError = body.DeclareLocal(_types.Exception);
            var settled = body.DefineLabel();
            body.BeginExceptionBlock();
            body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Callvirt, _types.GetMethodNoParams(_types.TaskOfObject, "GetAwaiter"));
            body.Emit(OpCodes.Stloc, settleAwaiter); body.Emit(OpCodes.Ldloca, settleAwaiter);
            body.Emit(OpCodes.Call, _types.GetMethodNoParams(_types.TaskAwaiterOfObject, "GetResult")); body.Emit(OpCodes.Stloc, settledValue);
            body.Emit(OpCodes.Ldarg_1); body.Emit(OpCodes.Castclass, completionType); body.Emit(OpCodes.Ldloc, settledValue);
            body.Emit(OpCodes.Callvirt, _types.GetMethod(completionType, "TrySetResult", _types.Object));
            body.Emit(OpCodes.Pop); body.Emit(OpCodes.Leave, settled);
            body.BeginCatchBlock(_types.Exception); body.Emit(OpCodes.Stloc, settledError);
            body.Emit(OpCodes.Ldarg_1); body.Emit(OpCodes.Castclass, completionType); body.Emit(OpCodes.Ldloc, settledError);
            body.Emit(OpCodes.Callvirt, _types.GetMethod(completionType, "TrySetException", _types.Exception));
            body.Emit(OpCodes.Pop); body.Emit(OpCodes.Leave, settled);
            body.EndExceptionBlock(); body.MarkLabel(settled); body.Emit(OpCodes.Ret);
            var settleCallbackType = _types.MakeGenericType(typeof(Action<,>), _types.TaskOfObject, _types.Object);
            var settleContinuation = _types.GetMethods(_types.TaskOfObject).Single(method => method.Name == "ContinueWith"
                && !method.IsGenericMethodDefinition && method.GetParameters() is { Length: 3 } parameters
                && parameters[0].ParameterType.IsGenericType
                && parameters[0].ParameterType.GetGenericTypeDefinition() == typeof(Action<,>)
                && parameters[0].ParameterType.GetGenericArguments()[0] == _types.TaskOfObject
                && parameters[1].ParameterType == _types.Object
                && parameters[2].ParameterType == typeof(TaskContinuationOptions));
            foreach (var (request, operation) in new[] { (nextBody, "Next"), (returnBody, "Return"), (throwBody, "Throw") })
            {
                var drive = iterator.DefineMethod("Drive" + operation, MethodAttributes.Private | MethodAttributes.Static,
                    _types.TaskOfObject, [_types.Object]);
                body = drive.GetILGenerator();
                if (operation == "Return")
                {
                    // Promise/thenable adoption may throw during Get(then). Convert
                    // that abrupt completion to a rejected async-generator request.
                    var result = body.DeclareLocal(_types.TaskOfObject);
                    var done = body.DefineLabel();
                    body.BeginExceptionBlock();
                    body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Call, coerceAwaitable!);
                    body.Emit(OpCodes.Ldnull); body.Emit(OpCodes.Ldftn, completeReturn);
                    body.Emit(OpCodes.Newobj, _types.GetConstructor(resultCallbackType, _types.Object, _types.IntPtr)!);
                    body.Emit(OpCodes.Ldc_I4, (int)TaskContinuationOptions.ExecuteSynchronously);
                    body.Emit(OpCodes.Callvirt, completeResult); body.Emit(OpCodes.Stloc, result); body.Emit(OpCodes.Leave, done);
                    body.BeginCatchBlock(_types.Exception);
                    body.Emit(OpCodes.Call, fromException); body.Emit(OpCodes.Stloc, result); body.Emit(OpCodes.Leave, done);
                    body.EndExceptionBlock(); body.MarkLabel(done); body.Emit(OpCodes.Ldloc, result); body.Emit(OpCodes.Ret);
                }
                else if (operation == "Throw")
                {
                    body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Call, errors.CreateException);
                    body.Emit(OpCodes.Call, fromException); body.Emit(OpCodes.Ret);
                }
                else
                {
                    EmitResultObject(body, () => body.Emit(OpCodes.Ldsfld, undefined));
                    body.Emit(OpCodes.Call, fromResult); body.Emit(OpCodes.Ret);
                }
                var continuation = iterator.DefineMethod("Queued" + operation, MethodAttributes.Private | MethodAttributes.Static,
                    _types.TaskOfObject, [_types.Task, _types.Object]);
                body = continuation.GetILGenerator(); body.Emit(OpCodes.Ldarg_1); body.Emit(OpCodes.Call, drive); body.Emit(OpCodes.Ret);
                body = request.GetILGenerator();
                var previous = body.DeclareLocal(_types.Task);
                var response = body.DeclareLocal(_types.TaskOfObject);
                var completion = body.DeclareLocal(completionType);
                var immediate = body.DefineLabel(); var save = body.DefineLabel();
                body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Ldfld, tail); body.Emit(OpCodes.Stloc, previous);
                // Publish this request before Get(then) can run guest code and
                // enqueue another request on the same iterator reentrantly.
                body.Emit(OpCodes.Newobj, _types.GetDefaultConstructor(completionType)); body.Emit(OpCodes.Stloc, completion);
                body.Emit(OpCodes.Ldarg_0); body.Emit(OpCodes.Ldloc, completion);
                body.Emit(OpCodes.Callvirt, completionTask); body.Emit(OpCodes.Stfld, tail);
                body.Emit(OpCodes.Ldloc, previous); body.Emit(OpCodes.Brfalse, immediate);
                body.Emit(OpCodes.Ldloc, previous); body.Emit(OpCodes.Callvirt, _types.GetPropertyGetter(_types.Task, "IsCompleted"));
                body.Emit(OpCodes.Brtrue, immediate);
                body.Emit(OpCodes.Ldloc, previous); body.Emit(OpCodes.Ldnull); body.Emit(OpCodes.Ldftn, continuation);
                body.Emit(OpCodes.Newobj, _types.GetConstructor(queueCallbackType, _types.Object, _types.IntPtr)!);
                body.Emit(OpCodes.Ldarg_1); body.Emit(OpCodes.Ldc_I4, (int)TaskContinuationOptions.ExecuteSynchronously);
                body.Emit(OpCodes.Callvirt, queue); body.Emit(OpCodes.Call, unwrap); body.Emit(OpCodes.Stloc, response); body.Emit(OpCodes.Br, save);
                body.MarkLabel(immediate); body.Emit(OpCodes.Ldarg_1); body.Emit(OpCodes.Call, drive); body.Emit(OpCodes.Stloc, response);
                body.MarkLabel(save); body.Emit(OpCodes.Ldloc, response); body.Emit(OpCodes.Ldnull); body.Emit(OpCodes.Ldftn, settle);
                body.Emit(OpCodes.Newobj, _types.GetConstructor(settleCallbackType, _types.Object, _types.IntPtr)!);
                body.Emit(OpCodes.Ldloc, completion); body.Emit(OpCodes.Ldc_I4, (int)TaskContinuationOptions.ExecuteSynchronously);
                body.Emit(OpCodes.Callvirt, settleContinuation); body.Emit(OpCodes.Pop);
                body.Emit(OpCodes.Ldloc, completion); body.Emit(OpCodes.Callvirt, completionTask); body.Emit(OpCodes.Ret);
            }
        }
    }
}
