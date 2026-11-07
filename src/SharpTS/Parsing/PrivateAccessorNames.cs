namespace SharpTS.Parsing;

internal static class PrivateAccessorNames
{
    // The colon cannot appear in a source private identifier.
    public static string Getter(string name) => $"{name}:get";
    public static string Setter(string name) => $"{name}:set";
    public static bool IsFacet(string name) => name.Contains(':');

    public static bool Contains<T>(IReadOnlyDictionary<string, T> methods, string name)
        => methods.ContainsKey(Getter(name)) || methods.ContainsKey(Setter(name));

    public static bool TryGet<T>(IReadOnlyDictionary<string, T> instanceMethods,
        IReadOnlyDictionary<string, T> staticMethods, string name, out bool isStatic, out T? getter, out T? setter)
        where T : class
    {
        string get = Getter(name);
        string set = Setter(name);
        getter = instanceMethods.GetValueOrDefault(get);
        setter = instanceMethods.GetValueOrDefault(set);
        isStatic = false;
        if (getter is not null || setter is not null) return true;

        getter = staticMethods.GetValueOrDefault(get);
        setter = staticMethods.GetValueOrDefault(set);
        isStatic = getter is not null || setter is not null;
        return isStatic;
    }
}
