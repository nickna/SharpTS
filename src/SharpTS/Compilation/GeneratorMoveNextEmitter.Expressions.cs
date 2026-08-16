namespace SharpTS.Compilation;

public partial class GeneratorMoveNextEmitter
{
    // Expression emission is fully inherited from ExpressionEmitterBase.
    //
    // EmitRegexLiteral, EmitSuper, and EmitDynamicImport used to be overridden
    // here (or inherited as a null-pushing base stub) so that a regex literal,
    // `super.x`, or `import()` inside a generator body silently evaluated to
    // `null` at runtime (#1105). The shared base now carries working
    // implementations for all three — regex builds a $RegExp, super loads the
    // hoisted `this` and resolves through GetSuperMethod, and dynamic import goes
    // through the module registry — so the generator emitter inherits them
    // unchanged. EmitterSyncTests enforces that no silent override creeps back.
}
