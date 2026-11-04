using SharpTS.Parsing;

namespace SharpTS.Compilation;

internal static class FieldClrNames
{
    internal static string Instance(IEnumerable<Stmt.Field> fields, string sourceName)
    {
        string conventional = NamingConventions.ToPascalCase(sourceName);
        // A colliding group has no unique PascalCase projection. Preserve its
        // exact spellings in CLR metadata; ordinary fields retain their ABI.
        return fields.Any(field => !field.IsStatic && !field.IsPrivate && !field.IsDeclare
            && field.ComputedKey == null && field.Name.Lexeme != sourceName
            && NamingConventions.ToPascalCase(field.Name.Lexeme) == conventional)
            ? sourceName : conventional;
    }
}
