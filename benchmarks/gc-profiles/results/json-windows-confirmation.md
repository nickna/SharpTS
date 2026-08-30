# GC profile benchmark matrix

Generated from commit `5c5333d53baa046fac6013188ebbb32c32851928` with 5 launches per cell.
Peak memory is peak working set from System.Diagnostics.Process on Windows and maximum RSS from GNU time on Ubuntu.

## Largest-input benchmark results

| Platform | Benchmark | Input | Runtime/profile | Median mean | Median minimum | Median stdev | Median process | Median peak memory |
|---|---|---:|---|---:|---:|---:|---:|---:|
| windows | json | 10000 | adaptive | 2.1110 ms | 0.9976 ms | 3.1925 ms | 1142.7 ms | 95.1 MB |
| windows | json | 10000 | node | 2.7534 ms | 2.3799 ms | 0.4480 ms | 1242.1 ms | 96.4 MB |
| windows | json | 10000 | throughput | 1.6760 ms | 1.0741 ms | 2.9456 ms | 1180.8 ms | 727.1 MB |
| windows | json | 10000 | workstation | 3.8156 ms | 0.9324 ms | 4.4128 ms | 1133.8 ms | 62.3 MB |

## Cold startup

| Platform | Runtime/profile | Median elapsed | Median peak memory |
|---|---|---:|---:|
| windows | adaptive | 90.41 ms | 24.1 MB |
| windows | throughput | 85.04 ms | 23.3 MB |
| windows | workstation | 84.62 ms | 21.4 MB |
| windows | node | 97.57 ms | 50.8 MB |
