# Windows redirected Unicode output (#1743)

The collection Unicode and numeric code-unit controls preserve the displayed
originals, complete Node expectations and 30-second deadlines. Fresh unchanged
main and pre-repair current reproduce the Unicode discrepancy with the same
verified assembly: normal processes emit UTF-8 emoji; CreateNoWindow emits `??`.
The numeric code-unit control passes in both contexts.

A minimal .NET 10 console reproduces the cause: Console.OutputEncoding is code
page 65001 normally and 437 under CreateNoWindow. Explicit UTF-8 produces the
same complete UTF-8 bytes in both. Executable entry points now initialize
redirected stdout to UTF-8 before guest writes. An assembly-entry-point guard
preserves embedding hosts' writers/encoding. Hosted initialization does not
change host console policy.

Raw pipe-byte regressions cover the original pair, multilingual text, module
output and an async user-main compiler entry point, in both process contexts.
They use no launcher encoding override and require verified IL, valid UTF-8,
complete expected stdout, empty stderr and clean exit within 30 seconds.
TypeScript 7.0.2 and Node 25.5.0 establish the reference outputs.

An additional synchronous void-main compiler-API control encountered an
independent StackUnderflow at the unconditional result pop. Its source and
failure log are retained locally and excluded from passing counts; this repair
does not claim general user-main IL correctness. The async user-main control
passes. Original issue sources and expectations are unchanged.
