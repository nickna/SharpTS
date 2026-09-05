namespace SharpTS.Microbenchmarks.Baselines;

public static class ArrayQueueBaselines
{
    public static double DequeShiftDrain(int n)
    {
        var values = new SharpTS.Runtime.Deque<double>();
        for (int i = 0; i < n; i++) values.AddLast(i);
        double checksum = 0;
        while (values.Count > 0) checksum += values.RemoveFirst();
        return checksum;
    }

    public static double DequeUnshiftBuild(int n)
    {
        var values = new SharpTS.Runtime.Deque<double>();
        for (int i = 0; i < n; i++) values.AddFirst(i);
        return values.Count + values[0] + values[n - 1];
    }

    public static double ShiftDrain(int n)
    {
        var values = new List<double>(n);
        for (int i = 0; i < n; i++) values.Add(i);
        double checksum = 0;
        while (values.Count > 0)
        {
            checksum += values[0];
            values.RemoveAt(0);
        }
        return checksum;
    }

    public static double UnshiftBuild(int n)
    {
        var values = new List<double>(n);
        for (int i = 0; i < n; i++) values.Insert(0, i);
        return values.Count + values[0] + values[n - 1];
    }
}
