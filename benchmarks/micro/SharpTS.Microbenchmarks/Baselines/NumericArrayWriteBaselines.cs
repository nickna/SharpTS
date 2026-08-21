namespace SharpTS.Microbenchmarks.Baselines;

/// <summary>
/// Direct ceiling and boxed-representation controls for the escaping
/// <c>number[]</c> indexed-write workload.
/// </summary>
public static class NumericArrayWriteBaselines
{
    public static double Idiomatic(int n)
    {
        var values = new double[n];
        for (int i = 0; i < n; i++)
            values[i] = i * 3.0;

        double sum = 0;
        for (int i = 0; i < n; i++)
            sum += values[i];
        return sum;
    }

    public static double BoxedEquivalent(int n)
    {
        var values = new List<object?>(n);
        for (int i = 0; i < n; i++)
            values.Add(i * 3.0);

        double sum = 0;
        for (int i = 0; i < n; i++)
            sum += (double)values[i]!;
        return sum;
    }
}
