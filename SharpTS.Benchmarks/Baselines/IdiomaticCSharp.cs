namespace SharpTS.Benchmarks.Baselines;

/// <summary>
/// Idiomatic C# implementations using native types (int, long, bool[], etc.).
/// This represents best-case .NET performance - the baseline to compare against.
/// Uses optimized C# patterns and types for maximum performance.
/// </summary>
public static class IdiomaticCSharp
{
    /// <summary>
    /// Fibonacci (recursive) - Uses native int for best performance
    /// </summary>
    public static int Fibonacci(int n)
    {
        if (n <= 1) return n;
        return Fibonacci(n - 1) + Fibonacci(n - 2);
    }

    /// <summary>
    /// Factorial (iterative) - Uses long to avoid overflow, native int for loop
    /// </summary>
    public static long Factorial(int n)
    {
        long result = 1;
        for (int i = 2; i <= n; i++)
        {
            result *= i;
        }
        return result;
    }

    /// <summary>
    /// Count Primes (Sieve of Eratosthenes) - Uses bool[] for optimal memory and performance
    /// </summary>
    public static int CountPrimes(int n)
    {
        if (n <= 2) return 0;

        var isPrime = new bool[n];
        Array.Fill(isPrime, true);
        isPrime[0] = false;
        isPrime[1] = false;

        for (int i = 2; i * i < n; i++)
        {
            if (isPrime[i])
            {
                for (int j = i * i; j < n; j += i)
                {
                    isPrime[j] = false;
                }
            }
        }

        int count = 0;
        for (int i = 0; i < n; i++)
        {
            if (isPrime[i]) count++;
        }
        return count;
    }
}
