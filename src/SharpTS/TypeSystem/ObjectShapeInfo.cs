using SharpTS.Parsing;

namespace SharpTS.TypeSystem;

/// <summary>
/// Pure-data description of a compiled object-literal local that the IL compiler can promote to a
/// generated value-type "shape" struct with typed fields (#862), instead of the default
/// <c>Dictionary&lt;string, object&gt;</c>. Produced by
/// <see cref="SharpTS.Compilation.ObjectLocalPromotionAnalyzer"/> and stored on the <see cref="TypeMap"/>;
/// the Reflection.Emit side (<c>ObjectShapeRegistry</c>) maps a <see cref="CanonicalKey"/> to a
/// generated <c>$Shape_N</c> value type.
///
/// <para>Order matters: JavaScript object keys are insertion-ordered, so <see cref="Fields"/> preserves
/// the literal's property order. That order defines both the struct's field layout and the
/// <see cref="CanonicalKey"/> used to de-duplicate identical shapes across the program.</para>
///
/// <para>Lives in <c>TypeSystem/</c> and references no <c>System.Reflection.Emit</c> type, so it can ride
/// the <see cref="TypeMap"/> (which is threaded into every emit context) without coupling the type system
/// to the compiler back end.</para>
/// </summary>
public sealed record ObjectShapeInfo(string CanonicalKey, IReadOnlyList<ObjectShapeField> Fields);

/// <summary>
/// A single promotable field of an <see cref="ObjectShapeInfo"/>: its property name and primitive kind
/// (<see cref="TokenType.TYPE_NUMBER"/> → <c>double</c>, <see cref="TokenType.TYPE_BOOLEAN"/> → <c>bool</c>,
/// or <see cref="TokenType.TYPE_STRING"/> → <c>string</c>).
/// </summary>
public readonly record struct ObjectShapeField(string Name, TokenType Kind);
