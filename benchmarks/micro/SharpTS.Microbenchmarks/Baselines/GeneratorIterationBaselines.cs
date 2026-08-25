namespace SharpTS.Microbenchmarks.Baselines;

/// <summary>
/// Native-value and boxed-value controls for the numeric generator range
/// workload. Both retain iterator state-machine overhead; only the element
/// representation differs.
/// </summary>
public static class GeneratorIterationBaselines
{
    public static double Idiomatic(int n)
    {
        double sum = 0;
        foreach (double value in NumericRange(n))
            sum += value;
        return sum;
    }

    public static double BoxedEquivalent(int n)
    {
        double sum = 0;
        foreach (object? value in BoxedNumericRange(n))
            sum += (double)value!;
        return sum;
    }

    private static IEnumerable<double> NumericRange(int n)
    {
        for (int i = 0; i < n; i++)
            yield return i;
    }

    private static IEnumerable<object?> BoxedNumericRange(int n)
    {
        for (int i = 0; i < n; i++)
            yield return (double)i;
    }
}
