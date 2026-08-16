namespace SharpTS.Microbenchmarks.Baselines;

/// <summary>
/// C# implementations using object/dynamic types to simulate SharpTS runtime overhead.
/// Uses object? for numbers (requires boxing/unboxing), List&lt;object?&gt; for arrays.
/// This reveals the performance cost of dynamic typing separate from TypeScript compilation.
/// </summary>
public static class EquivalentCSharp
{
    /// <summary>
    /// Fibonacci (recursive) - Uses object? and Convert.ToDouble for dynamic typing overhead
    /// </summary>
    public static object? Fibonacci(object? n)
    {
        double nVal = Convert.ToDouble(n);
        if (nVal <= 1) return nVal;

        object? n1 = Fibonacci(nVal - 1);
        object? n2 = Fibonacci(nVal - 2);
        return Convert.ToDouble(n1) + Convert.ToDouble(n2);
    }

    /// <summary>
    /// Factorial (iterative) - Uses object? with boxing/unboxing overhead
    /// </summary>
    public static object? Factorial(object? n)
    {
        double nVal = Convert.ToDouble(n);
        double result = 1;

        for (double i = 2; i <= nVal; i++)
        {
            result *= i;
        }

        return result;
    }

    /// <summary>
    /// Count Primes (Sieve of Eratosthenes) - Uses List&lt;object?&gt; to match SharpTS array representation
    /// </summary>
    public static object? CountPrimes(object? n)
    {
        double nVal = Convert.ToDouble(n);
        if (nVal <= 2) return 0.0;

        int nInt = (int)nVal;
        var isPrime = new List<object?>(nInt);
        for (int i = 0; i < nInt; i++)
        {
            isPrime.Add(true);
        }
        isPrime[0] = false;
        isPrime[1] = false;

        for (double i = 2; i * i < nVal; i++)
        {
            int idx = (int)i;
            if ((bool)isPrime[idx]!)
            {
                for (double j = i * i; j < nVal; j += i)
                {
                    isPrime[(int)j] = false;
                }
            }
        }

        double count = 0;
        foreach (var p in isPrime)
        {
            if ((bool)p!) count++;
        }
        return count;
    }

    /// <summary>
    /// JSON round-trip using Dictionary&lt;string, object?&gt; nodes - mirrors the
    /// boxed object/array representation the SharpTS runtime produces.
    /// </summary>
    public static object? JsonRoundTrip(object? n)
    {
        double nVal = Convert.ToDouble(n);
        int nInt = (int)nVal;
        var items = new List<object?>(nInt);
        for (int i = 0; i < nInt; i++)
        {
            items.Add(new Dictionary<string, object?>
            {
                { "id", (double)i },
                { "name", "item-" + i },
                { "value", (double)(i * 3 - 1) },
            });
        }
        var root = new Dictionary<string, object?> { { "items", items } };
        string json = System.Text.Json.JsonSerializer.Serialize(root);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        double sum = 0;
        foreach (var el in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            sum += el.GetProperty("value").GetDouble();
        }
        return sum;
    }

    /// <summary>
    /// Typed-array kernel using List&lt;object?&gt; with boxed doubles - the dynamic
    /// representation tax SharpTS AVOIDS by compiling Float64Array to a real buffer.
    /// </summary>
    public static object? TypedArrayKernel(object? n)
    {
        double nVal = Convert.ToDouble(n);
        int nInt = (int)nVal;
        var a = new List<object?>(nInt);
        for (int i = 0; i < nInt; i++)
        {
            a.Add(i * 1.5 + (i % 7));
        }
        double sum = 0;
        for (int i = 1; i < nInt - 1; i++)
        {
            sum += Convert.ToDouble(a[i - 1]) - 2 * Convert.ToDouble(a[i]) + Convert.ToDouble(a[i + 1]);
        }
        return sum;
    }

    /// <summary>
    /// binary-trees using Dictionary&lt;string, object?&gt; nodes with dynamic
    /// property lookups - mirrors a boxed SharpTS object graph.
    /// </summary>
    public static object? BinaryTrees(object? depth)
    {
        double d = Convert.ToDouble(depth);
        return (double)ItemCheckBoxed(BuildTreeBoxed((int)d));
    }

    private static Dictionary<string, object?> BuildTreeBoxed(int depth)
    {
        if (depth <= 0)
        {
            return new Dictionary<string, object?> { { "left", null }, { "right", null } };
        }
        return new Dictionary<string, object?>
        {
            { "left", BuildTreeBoxed(depth - 1) },
            { "right", BuildTreeBoxed(depth - 1) },
        };
    }

    private static int ItemCheckBoxed(Dictionary<string, object?>? node)
    {
        if (node is null) return 1;
        return 1 + ItemCheckBoxed(node["left"] as Dictionary<string, object?>)
                 + ItemCheckBoxed(node["right"] as Dictionary<string, object?>);
    }
}
