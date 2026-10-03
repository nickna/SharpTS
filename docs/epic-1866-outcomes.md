# Frozen behavior backlog outcomes (#1866)

This record accompanies the consolidated implementation PR. Each child is handled
independently and committed after its verification. The starting baseline is
`0ad37b574e3c41d267300ada003d63d0f8026562` (the current main when work began).
The epic's frozen source baseline remains `83a41096108fe6de739fa7cfcb148c4bb1193121`.
Design results and prior repairs are distinguished from new behavior repairs.

## #1900 — Interpreter non-writable static descriptors

**Outcome: repaired.** The original progress note links #1861 but contains no
source text. The unchanged `NonWritableDescriptorWinsOverFieldStorage` source
retained by that PR is preserved in `ClassStaticDescriptorTests`: after defining
`Box.count` as 12 and non-writable, assigning 9 through an alias must print
`12` and `true`. On the starting baseline the interpreter printed `9` and `true`;
compiled execution already printed the expected result.

Interpreted class static properties now share storage with the ordinary property
descriptor implementation. Assignment respects own and inherited non-writable
descriptors, throws guest TypeError in strict mode, and preserves writable
inherited shadows. Dot, index and read-modify-write dispatch use the same
descriptor checks. Descriptor reads and partial redefinitions retain the value
and attributes; deletion observes configurability.

Verification on Windows ARM64, .NET SDK 10.0.401/runtime 10.0.12:

- 32 focused class/static/property tests passed, with compiled IL verification.
- 891 affected class, accessor, decorator and assignment tests passed.
- Repository code-quality gates passed (zero errors).
- Twelve selected Test262 descriptor/class metadata cases per engine matched an
  isolated build of the unchanged baseline at pinned corpus
  `d5e73fc8d2c663554fb72e2380a8c2bc1a318a33`. The ten ordinary descriptor cases
  passed in each mode; class-name metadata retained two interpreted failures,
  one compiled runtime error and one compiled pass. Those existing non-target
  failures are not counted as successful conformance tests.
- Node v25.5.0, CLI interpretation and CLI compilation followed by standalone
  execution all printed `12` and `true` for the retained source; CLI IL
  verification passed. The original 30-second test-harness deadline is retained.
- Hosted execution was not exercised: this change is an interpreter property
  dispatch repair and adds no deployment or hosted ABI behavior.

The controls uncovered independent pre-existing compiled gaps, tracked outside
the frozen epic as [#1958](https://github.com/nickna/SharpTS/issues/1958)
(inherited non-writable static descriptors) and
[#1959](https://github.com/nickna/SharpTS/issues/1959)
(attribute-only static definitions losing the existing value), and
[#1960](https://github.com/nickna/SharpTS/issues/1960)
(strict compiled read-modify-write operations ignoring rejection). Those failing
compiled observations are not counted as repaired or passing coverage here.
