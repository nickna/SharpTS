using System.Reflection;

namespace SharpTS.Compilation;

// An output-local source-class data definition endpoint. This is separate from
// ordinary SetProperty: it never dispatches a guest accessor setter.
internal sealed record EmittedSourceDataDefinitionRuntime(Type Contract, MethodInfo DefineValue);
