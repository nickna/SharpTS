namespace SharpTS.TypeSystem.Exceptions;

/// <summary>
/// Exception thrown when accessing a property or method that doesn't exist on a type.
/// </summary>
public class UndefinedMemberException : TypeCheckException
{
    /// <summary>
    /// The name of the undefined member.
    /// </summary>
    public string MemberName { get; init; }

    /// <summary>
    /// The type on which the member was accessed.
    /// </summary>
    public TypeInfo Type { get; init; }

    public UndefinedMemberException(string memberName, TypeInfo type, int? line = null, int? column = null)
        : base($"Property '{memberName}' does not exist on type '{type}'", line, column)
    {
        MemberName = memberName;
        Type = type;
    }

    public UndefinedMemberException(string customMessage, string memberName, TypeInfo type, int? line = null, int? column = null)
        : base(customMessage, line, column)
    {
        MemberName = memberName;
        Type = type;
    }
}
