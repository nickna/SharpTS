namespace ExternalSignatureFixture;

public sealed class Api
{
    private static int _number = 7;
    public string Value;
    public volatile int Volatile;
    public string Property { get; set; }
    public Api(string value) { Value = value; Property = value; }
    public string InstanceEcho(string value) => value;
    public string this[int index] { get => Value + index; set => Value = value; }
    public static string Echo(string value) => value;
    public static string Params(params string[] values) => string.Join("|", values);
    public static bool Boolean(bool value) => value;
    public static int Integer(int value) => value;
    public static double Number(double value) => value;
    public static object ObjectValue() => 42;
    public static object ObjectEcho(object value) => value;
    public static void Nothing() { }
    public static string[] Strings(string[] values) => values;
    public static int[][] Jagged(int[][] values) => values;
    public static string[,] Matrix(string[,] values) => values;
    public static string[] ArrayValue() => ["a", "b"];
    public static void OutString(out string value) => value = "out";
    public static string RefString(ref string value) { value += "!"; return value; }
    public static ref readonly int ReadOnlyRef() => ref _number;
    public static T Identity<T>(T value) => value;
    public static List<string> List(List<string> value) => value;
}

public sealed class Box<T>
{
    public T Value;
    public T Property { get; set; }
    public Box(T value) { Value = value; Property = value; }
    public T Echo(T value) => value;
}

public interface IReadOnlyInput { int Read(in int value); }
