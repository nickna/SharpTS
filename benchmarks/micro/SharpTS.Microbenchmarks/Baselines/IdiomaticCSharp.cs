namespace SharpTS.Microbenchmarks.Baselines;

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

    /// <summary>
    /// JSON round-trip - typed serialize, parse via JsonDocument, sum a field.
    /// </summary>
    public static int JsonRoundTrip(int n)
    {
        var items = new List<object>(n);
        for (int i = 0; i < n; i++)
        {
            items.Add(new { id = i, name = "item-" + i, value = i * 3 - 1 });
        }
        string json = System.Text.Json.JsonSerializer.Serialize(new { items });
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        int sum = 0;
        foreach (var el in doc.RootElement.GetProperty("items").EnumerateArray())
        {
            sum += el.GetProperty("value").GetInt32();
        }
        return sum;
    }

    /// <summary>
    /// Typed-array numeric kernel - native double[] fill + 3-point stencil sweep.
    /// </summary>
    public static double TypedArrayKernel(int n)
    {
        var a = new double[n];
        for (int i = 0; i < n; i++)
        {
            a[i] = i * 1.5 + (i % 7);
        }
        double sum = 0;
        for (int i = 1; i < n - 1; i++)
        {
            sum += a[i - 1] - 2 * a[i] + a[i + 1];
        }
        return sum;
    }

    /// <summary>
    /// binary-trees (CLBG) - build a node tree to `depth`, then checksum it.
    /// </summary>
    public static int BinaryTrees(int depth) => ItemCheck(BuildTree(depth));

    private sealed class TreeNode
    {
        public TreeNode? Left;
        public TreeNode? Right;
    }

    private static TreeNode BuildTree(int depth)
    {
        if (depth <= 0) return new TreeNode();
        return new TreeNode { Left = BuildTree(depth - 1), Right = BuildTree(depth - 1) };
    }

    private static int ItemCheck(TreeNode? node)
    {
        if (node is null) return 1;
        return 1 + ItemCheck(node.Left) + ItemCheck(node.Right);
    }
}
