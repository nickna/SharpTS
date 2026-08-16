namespace NativeInteropExample;

public sealed class Counter(int value)
{
    public int Value { get; private set; } = value;

    public string Label = "counter";

    public event EventHandler? Changed;

    public int Increment(int amount)
    {
        Value += amount;
        Changed?.Invoke(this, EventArgs.Empty);
        return Value;
    }

    public static List<Counter> CreateMany(int count) =>
        Enumerable.Range(0, count).Select(value => new Counter(value)).ToList();
}
