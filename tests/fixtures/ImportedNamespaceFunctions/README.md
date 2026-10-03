# Imported namespace function values (#1776)

`original/lib.ts` and `original/main.ts` preserve the unchanged two-file issue
source. Fresh unchanged main and pre-repair current verify the saved IL, then
report the null namespace receiver with empty stdout. The unchanged
constant-only control prints `8 true`. Fresh runs finish within 30 seconds;
historical Windows lifetime observations remain separately owned by #1772.

Namespace declaration exports now create a module export field and store the
populated namespace object in it. Named exports use the same namespace value.
Merged declarations reuse one field. All nine pairs have exact full
TypeScript/CommonJS and Node references: original, constant control, direct,
named, nested, generator, async, merged and renamed exports.

All nine references pass compiled execution and isolated saved standalone
execution, requiring verified IL, exact stdout, empty stderr, clean exit within
30 seconds, and no SharpTS assembly reference or copy. Existing hosted checks
verify declaration/deployment only; no exported hosted function execution is
claimed. No same-named namespaces across separate modules, class identity,
mutation or process-lifetime repair is claimed.
