# GC profile benchmark matrix

Generated from commit `4189deb66260242c2bb448af8d3cfd185bc3d390` with 5 launches per cell.

## Largest-input benchmark results

| Platform | Benchmark | Input | Runtime/profile | Median mean | Median minimum | Median process | Median peak RSS |
|---|---|---:|---|---:|---:|---:|---:|
| windows | json | 10000 | adaptive | 2.0046 ms | 0.8920 ms | 1119.3 ms | 93.0 MB |
| windows | json | 10000 | node | 2.5675 ms | 2.1356 ms | 1220.4 ms | 77.8 MB |
| windows | json | 10000 | throughput | 1.6351 ms | 0.9314 ms | 1172.7 ms | 712.1 MB |
| windows | json | 10000 | workstation | 3.8056 ms | 0.8596 ms | 1117.0 ms | 62.2 MB |

## Cold startup

| Platform | Runtime/profile | Median elapsed | Median peak RSS |
|---|---|---:|---:|
| windows | adaptive | 82.22 ms | 24.7 MB |
| windows | throughput | 74.01 ms | 22.9 MB |
| windows | workstation | 69.89 ms | 20.3 MB |
| windows | node | 93.92 ms | 50.9 MB |

