namespace SharpTS.Compilation;

public static partial class RuntimeTypes
{
    #region Exceptions

    public static Exception CreateException(object? value)
    {
        return new Exception(Stringify(value));
    }

    public static object WrapException(Exception ex)
    {
        return new Dictionary<string, object?>
        {
            ["message"] = ex.Message,
            ["name"] = ex.GetType().Name
        };
    }

    #endregion
}
