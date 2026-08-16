using System.Reflection.Emit;

namespace SharpTS.TypeSystem;

/// <summary>
/// Categories of binary operators based on their semantic behavior.
/// </summary>
public enum OperatorCategory
{
    /// <summary>Arithmetic operators: -, *, /, %, **</summary>
    Arithmetic,
    /// <summary>Plus operator (special: handles string concatenation)</summary>
    Plus,
    /// <summary>Comparison operators: &lt;, &lt;=, &gt;, &gt;=</summary>
    Comparison,
    /// <summary>Equality operators: ==, ===, !=, !==</summary>
    Equality,
    /// <summary>Bitwise operators: &amp;, |, ^, &lt;&lt;, &gt;&gt;</summary>
    Bitwise,
    /// <summary>Unsigned right shift: &gt;&gt;&gt; (no bigint support)</summary>
    UnsignedShift,
    /// <summary>Special operators: in, instanceof</summary>
    Special
}

/// <summary>
/// Discriminated union describing binary operator semantics including IL opcodes.
/// Used to centralize operator classification across TypeChecker, Interpreter, and ILEmitter.
/// </summary>
public abstract record OperatorDescriptor
{
    private OperatorDescriptor() { }

    /// <summary>Arithmetic operators (-, *, /, %) that map directly to IL opcodes.</summary>
    public sealed record Arithmetic(OpCode Opcode) : OperatorDescriptor;

    /// <summary>Plus operator - special case for string concatenation.</summary>
    public sealed record Plus() : OperatorDescriptor;

    /// <summary>Power operator (**) - requires Math.Pow call.</summary>
    public sealed record Power() : OperatorDescriptor;

    /// <summary>Comparison operators (&lt;, &gt;, &lt;=, &gt;=). Negated is true for &lt;= and &gt;=.</summary>
    public sealed record Comparison(OpCode Opcode, bool Negated = false) : OperatorDescriptor;

    /// <summary>Equality operators (==, ===, !=, !==).</summary>
    public sealed record Equality(bool IsStrict, bool IsNegated) : OperatorDescriptor;

    /// <summary>Bitwise operators (&amp;, |, ^) that map directly to IL opcodes.</summary>
    public sealed record Bitwise(OpCode Opcode) : OperatorDescriptor;

    /// <summary>Bitwise shift operators (&lt;&lt;, &gt;&gt;) with 5-bit shift amount mask.</summary>
    public sealed record BitwiseShift(OpCode Opcode) : OperatorDescriptor;

    /// <summary>Unsigned right shift (&gt;&gt;&gt;) - not supported for bigint.</summary>
    public sealed record UnsignedRightShift() : OperatorDescriptor;

    /// <summary>The 'in' operator for property existence check.</summary>
    public sealed record In() : OperatorDescriptor;

    /// <summary>The 'instanceof' operator for type checking.</summary>
    public sealed record InstanceOf() : OperatorDescriptor;

    /// <summary>Unknown operator - fallback case.</summary>
    public sealed record Unknown() : OperatorDescriptor;
}
