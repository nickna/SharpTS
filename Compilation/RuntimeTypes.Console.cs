namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region Console

    public static void ConsoleLog(object? value)
    {
        Console.WriteLine(Stringify(value));
    }

    public static void ConsoleLogMultiple(object?[] values)
    {
        Console.WriteLine(string.Join(" ", values.Select(Stringify)));
    }

    #endregion
}
