# GC profile matrix

This harness measures the deployment-time GC policies available to SharpTS-compiled applications.
It compiles the existing cross-runtime corpus once, then launches each output under workstation,
adaptive server (DATAS), and fixed server GC. Node is measured as the cross-runtime reference.

Run the default three-launch Windows and Ubuntu matrix from the repository root:

```powershell
.\benchmarks\gc-profiles\run-gc-profile-matrix.ps1
```

Ubuntu runs in Docker using `Dockerfile`; workloads execute as the image's non-root `app` user
against read-only source mounts. Restrict platforms, workloads, or repetitions when iterating:

```powershell
.\benchmarks\gc-profiles\run-gc-profile-matrix.ps1 `
  -Platforms windows -Workloads json,binary-trees -Launches 5 -NoBuild
```

Generated raw measurements and summaries are written under the ignored `.perf-gc-profiles`
directory. The harness rejects a dirty Git worktree before building so its recorded commit fully
identifies the compiler and workloads that were measured. The checked-in
[decision record](decision.md) explains the product policy, and `results/` preserves the evidence
used for issue #1462.
