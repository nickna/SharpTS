using System.Diagnostics;
using System.Runtime;
using System.Text.Json;
public class StartupHook
{
    public static void Initialize()
    {
        Console.SetOut(new PhaseWriter(Console.Out));
        long start = GC.GetTotalAllocatedBytes(true);
        TimeSpan pause = GC.GetTotalPauseDuration();
        int[] collections = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
        AppDomain.CurrentDomain.ProcessExit += (_, _) => {
            var p = Process.GetCurrentProcess();
            Console.Error.WriteLine("GC_METRICS:" + JsonSerializer.Serialize(new {
                scope = "whole process, including startup, worker compilation, warmup, preflight and samples",
                serverGc = GCSettings.IsServerGC, latency = GCSettings.LatencyMode.ToString(),
                allocatedBytes = GC.GetTotalAllocatedBytes(true) - start,
                gen0 = GC.CollectionCount(0) - collections[0], gen1 = GC.CollectionCount(1) - collections[1],
                gen2 = GC.CollectionCount(2) - collections[2],
                pauseMs = (GC.GetTotalPauseDuration() - pause).TotalMilliseconds,
                peakWorkingSetBytes = p.PeakWorkingSet64
            }));
        };
    }

    private sealed class PhaseWriter(TextWriter inner) : TextWriter
    {
        public override System.Text.Encoding Encoding => inner.Encoding;
        private readonly System.Text.StringBuilder line = new();
        private long allocated;
        private TimeSpan pause;
        private int g0, g1, g2;
        public override void Write(char value) { line.Append(value); inner.Write(value); }
        public override void Write(string? value) { line.Append(value); inner.Write(value); }
        public override void WriteLine(string? value) { Write(value); WriteLine(); }
        public override void WriteLine()
        {
            string marker = line.ToString(); line.Clear(); inner.WriteLine();
            if (marker == "GC_BEGIN")
            {
                allocated = GC.GetTotalAllocatedBytes(true); pause = GC.GetTotalPauseDuration();
                g0 = GC.CollectionCount(0); g1 = GC.CollectionCount(1); g2 = GC.CollectionCount(2);
            }
            else if (marker == "GC_END")
            {
                long bytes = GC.GetTotalAllocatedBytes(true) - allocated;
                var duration = GC.GetTotalPauseDuration() - pause;
                Console.Error.WriteLine("GC_PHASE:" + JsonSerializer.Serialize(new {
                    scope = "20 steady-state jobs across all process threads; startup/warmup excluded",
                    jobs = 20, allocatedBytes = bytes, bytesPerJob = bytes / 20.0,
                    gen0 = GC.CollectionCount(0) - g0, gen1 = GC.CollectionCount(1) - g1,
                    gen2 = GC.CollectionCount(2) - g2, pauseMs = duration.TotalMilliseconds
                }));
            }
        }
    }
}
