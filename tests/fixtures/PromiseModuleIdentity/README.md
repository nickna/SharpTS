# Promise module declaration controls (#1910)

The #1821/#1822 notes retain duplicate `$Module_promises` metadata, but no exact
colliding import source or deadline was recovered. These are labelled new controls,
not reconstructed historical sources. Both import orders expect:

```text
function function function
ready
```

Node v25.5.0 and interpretation produce that output. On unchanged `0ad37b57` and
pre-repair `acae1bdd`, compilation and IL verification pass, and the artifacts
execute successfully, while each saved assembly contains six distinct TypeDef
rows named `$Module_promises`. The observation is a declaration defect, not a
claimed runtime failure in these controls. Node's package-type warning is retained
in the logs; it does not change stdout or exit status.

The repair allocates unique module type names across ESM and CommonJS, preserving
canonical path lookup and export-field ownership. The current metadata contains
`$Module_promises` and `$Module_promises$1` through `$5` exactly once each.
Import-order tests assert uniqueness and execute the exports. Additional isolated
controls cover same filename modules, a name resembling an allocated suffix,
both orders and ESM/CommonJS, with `left right third` output.

The built-in controls use default CLI deployment because DNS records its existing
optional SharpTS runtime requirement. They do not establish runtime-independent
DNS execution. Local-module controls also pass `--standalone` and contain no
SharpTS assembly reference. Compilation/execution limits are 60/30 seconds for
the new isolated regressions; the unrecovered historical deadline is not invented.
No hosted execution or broad class/function/module-name rewrite is claimed.
