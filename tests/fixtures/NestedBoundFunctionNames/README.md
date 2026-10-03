# Repeated bound-function names (#1717)

`original.ts` preserves the issue's unchanged source and Node output:

```text
bound sample 2 bound bound sample 1
7 true true 6
```

Unchanged `0ad37b57` and pre-repair `f020187` compiled outputs verify but print
an empty second name; baseline interpretation drops one `bound` prefix instead.
Neither baseline result is normalized into a pass. The compiled generic bound
wrapper now resolves the immediate target name with its own additional prefix;
the interpreter retains the immediate BoundFunction.Name when rebinding.

`repeated.ts` is a labelled new control for four binding levels, the zero length
floor, argument prepending, independent expandos and unchanged invocation.
Separate TypeScript 7.0.2 and Node v25.5.0 references accept both sources.
Both API modes and two isolated default-library CLI programs match Node, pass
IL verification, and execute with zero exit and empty stderr. Isolated outputs
use `--standalone` and have no SharpTS assembly reference. Their new limits are
60 seconds to compile and 30 seconds to execute; the original issue did not
specify a deadline. Hosted execution and unrelated function-name mutation
semantics are not claimed.
