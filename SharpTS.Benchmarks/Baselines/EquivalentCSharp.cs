namespace SharpTS.Benchmarks.Baselines;

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
}
