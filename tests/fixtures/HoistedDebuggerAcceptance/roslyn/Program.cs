internal static class Program
{
    private static async Task Main()
    {
        await Async(10);
        foreach (int result in Iterator(20))
            Console.WriteLine($"iterator result: {result}");
        await foreach (int result in AsyncIterator(30))
            Console.WriteLine($"async iterator result: {result}");
    }

    private static async Task<int> Async(int parameter)
    {
        int local = parameter + 1;
        int captured = parameter + 2;
        Func<int> reader = () => captured;
        Console.WriteLine($"async before: {parameter}, {local}, {reader()}"); // @break:async_before
        await Task.Delay(100);
        Console.WriteLine($"async after: {parameter}, {local}, {reader()}"); // @break:async_after
        {
            int blockLocal = local + 3;
            await Task.Delay(100);
            Console.WriteLine($"async block: {blockLocal}"); // @break:async_block
        }
        captured += 1;
        Console.WriteLine($"async out of block: {local}, {reader()}"); // @break:async_out_of_block
        return local + reader();
    }

    private static IEnumerable<int> Iterator(int parameter)
    {
        int local = parameter + 1;
        Console.WriteLine($"iterator before: {parameter}, {local}"); // @break:iterator_before
        yield return local;
        local += 2;
        Console.WriteLine($"iterator after: {parameter}, {local}"); // @break:iterator_after
        yield return local;
    }

    private static async IAsyncEnumerable<int> AsyncIterator(int parameter)
    {
        int local = parameter + 1;
        Console.WriteLine($"async iterator before: {parameter}, {local}"); // @break:async_iterator_before
        await Task.Delay(100);
        yield return local;
        local += 2;
        await Task.Delay(100);
        Console.WriteLine($"async iterator after: {parameter}, {local}"); // @break:async_iterator_after
        yield return local;
    }
}
