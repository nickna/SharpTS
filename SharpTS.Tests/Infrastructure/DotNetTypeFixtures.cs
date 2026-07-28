namespace SharpTS.Tests.Infrastructure;

/// <summary>
/// Controlled .NET surface used by <c>@DotNetType</c> interpreter tests for
/// delegate parameters and event subscription. Separate from production code so
/// we can exercise exact signatures (Action, Func, Predicate, EventHandler) without
/// depending on volatile BCL API shapes.
/// </summary>
public class CallbackFixture
{
    public string LastReceived { get; private set; } = string.Empty;

    /// <summary>Passes a hard-coded string to the callback; verifies Action&lt;string&gt;.</summary>
    public void InvokeWithGreeting(Action<string> callback) => callback("hello");

    /// <summary>Invokes a Func and returns its result doubled; verifies Func&lt;int,int&gt; return flow.</summary>
    public int DoubleOf(Func<int, int> callback, int input) => callback(input) * 2;

    /// <summary>Filters a list via predicate; verifies Predicate&lt;int&gt; + boolean return.</summary>
    public int CountMatching(int[] values, Predicate<int> predicate)
    {
        int count = 0;
        foreach (var v in values)
        {
            if (predicate(v)) count++;
        }
        return count;
    }

    /// <summary>
    /// No-args void delegate; verifies the zero-parameter <c>Action</c> path,
    /// which has no boxing to worry about.
    /// </summary>
    public void InvokeNoArgs(Action callback) => callback();

    /// <summary>Fires the <see cref="StringReceived"/> event with the supplied payload.</summary>
    public void FireStringEvent(string payload)
    {
        LastReceived = payload;
        StringReceived?.Invoke(this, payload);
    }

    /// <summary>Fires the <see cref="Ping"/> event (no payload beyond EventArgs).</summary>
    public void FirePing() => Ping?.Invoke(this, EventArgs.Empty);

    /// <summary>Generic event with a string payload.</summary>
    public event EventHandler<string>? StringReceived;

    /// <summary>Classic EventHandler with no payload.</summary>
    public event EventHandler? Ping;
}

/// <summary>Controlled nullable-value surface for dual-mode CLR interop tests.</summary>
public class NullableFixture
{
    public int? Echo(int? value) => value;

    public int OrDefault(int? value, int fallback) => value ?? fallback;
}

/// <summary>Controlled ref/out/in surface for tuple-lowered CLR interop tests.</summary>
public class ByRefFixture
{
    public bool TryDouble(string text, out int value) =>
        int.TryParse(text, out value);

    public void Increment(ref int value) => value++;

    public string Mix(int addend, ref int current, out bool changed)
    {
        current += addend;
        changed = addend != 0;
        return $"value={current}";
    }

    public int ReadOnlyAdd(in int value, int addend) => value + addend;
}

public delegate T GenericTransformer<T>(T value);

/// <summary>Controlled ordinary generic-method surface for inference and explicit-type tests.</summary>
public class GenericMethodFixture
{
    public T Echo<T>(T value) => value;

    public T[] Copy<T>(T[] values) => values.ToArray();

    public T Transform<T>(T value, GenericTransformer<T> transform) =>
        transform(value);

    public T FromFactory<T>(Func<T> factory) => factory();

    public void Tap<T>(T value, Action<T> callback) => callback(value);

    public T DefaultValue<T>() => default!;

    public T[] EmptyArray<T>() => [];

    public static T StaticEcho<T>(T value) => value;

    public string TypeName<T>() => typeof(T).Name;

    public T Constrained<T>(T value) where T : struct => value;
}

/// <summary>Unsupported managed-reference boundaries used to pin discovery diagnostics.</summary>
public class ByRefBoundaryFixture
{
    private int _value;

    public ByRefBoundaryFixture(ref int value)
    {
        _value = value;
    }

    public ref int ValueRef() => ref _value;
}

/// <summary>Controlled user-defined operator surface for dual-mode CLR interop tests.</summary>
public sealed class OperatorFixture(int value)
{
    private OperatorFixture? _slot;

    public int Value { get; } = value;

    public OperatorFixture Current
    {
        get => _slot ?? this;
        set => _slot = value;
    }

    public OperatorFixture this[int index]
    {
        get => _slot ?? this;
        set => _slot = value;
    }

    public static OperatorFixture operator +(OperatorFixture left, OperatorFixture right) =>
        new(left.Value + right.Value);

    public static OperatorFixture operator *(OperatorFixture left, int factor) =>
        new(left.Value * factor);

    public static OperatorFixture operator -(OperatorFixture value) =>
        new(-value.Value);

    public static OperatorFixture operator ++(OperatorFixture value) =>
        new(value.Value + 1);

    public static OperatorFixture operator --(OperatorFixture value) =>
        new(value.Value - 1);

    public static bool operator !(OperatorFixture value) =>
        value.Value == 0;

    public static bool operator >(OperatorFixture left, OperatorFixture right) =>
        left.Value > right.Value;

    public static bool operator <(OperatorFixture left, OperatorFixture right) =>
        left.Value < right.Value;

    public static bool operator ==(OperatorFixture? left, OperatorFixture? right) =>
        ReferenceEquals(left, right) ||
        (left is not null && right is not null && left.Value == right.Value);

    public static bool operator !=(OperatorFixture? left, OperatorFixture? right) =>
        !(left == right);

    public override bool Equals(object? obj) =>
        obj is OperatorFixture other && this == other;

    public override int GetHashCode() => Value;
}

/// <summary>Controlled generic extension-method surface for module-scoping tests.</summary>
public static class EnumerableExtensionFixture
{
    public static int CountItems<T>(this IEnumerable<T> values) =>
        values.Count();

    public static T FirstItem<T>(this IEnumerable<T> values) =>
        values.First();
}

/// <summary>
/// Fixture for testing static event subscription via <see cref="DotNetTypeFixtures"/>-style
/// declarations. Lives here so the static event state can be reset between tests.
/// </summary>
public static class StaticCallbackFixture
{
    public static int LastValue { get; private set; }

    public static event EventHandler<int>? ValueChanged;

    public static void Fire(int value)
    {
        LastValue = value;
        ValueChanged?.Invoke(null, value);
    }

    /// <summary>Test-only reset so event subscribers from a prior test don't leak across tests.</summary>
    public static void Reset()
    {
        LastValue = 0;
        ValueChanged = null;
    }
}
