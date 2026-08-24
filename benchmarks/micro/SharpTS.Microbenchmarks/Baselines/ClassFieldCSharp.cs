using System.Runtime.CompilerServices;

namespace SharpTS.Microbenchmarks.Baselines;

public static class ClassFieldCSharp
{
    private sealed class Counter(double value)
    {
        public double Value = value;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public double Step() => ++Value;
    }

    private sealed class BoxedCounter
    {
        private readonly Dictionary<string, object?> _fields = [];
        private object? _value;

        public int DynamicFieldCount => _fields.Count;

        public BoxedCounter(object? value) => SetValue(value);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public object? GetValue() => _value;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public object? SetValue(object? value)
        {
            _value = value;
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public object? Step()
        {
            object? next = Convert.ToDouble(GetValue()) + 1;
            SetValue(next);
            return GetValue();
        }
    }

    public static double FieldReuse(int n)
    {
        var counter = new Counter(0);
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            counter.Value++;
            sum += counter.Value;
        }
        return sum;
    }

    public static double MethodReuse(int n)
    {
        var counter = new Counter(0);
        double sum = 0;
        for (int i = 0; i < n; i++)
            sum += counter.Step();
        return sum;
    }

    public static double Construction(int n)
    {
        double sum = 0;
        for (int i = 0; i < n; i++)
            sum += new Counter(i).Value;
        return sum;
    }

    public static object? BoxedFieldReuse(int n)
    {
        var counter = new BoxedCounter(0.0);
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            counter.SetValue(Convert.ToDouble(counter.GetValue()) + 1);
            sum += Convert.ToDouble(counter.GetValue());
        }
        return sum;
    }

    public static object? BoxedMethodReuse(int n)
    {
        var counter = new BoxedCounter(0.0);
        double sum = 0;
        for (int i = 0; i < n; i++)
            sum += Convert.ToDouble(counter.Step());
        return sum;
    }

    public static object? BoxedConstruction(int n)
    {
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            var counter = new BoxedCounter((double)i);
            sum += Convert.ToDouble(counter.GetValue()) + counter.DynamicFieldCount;
        }
        return sum;
    }
}
