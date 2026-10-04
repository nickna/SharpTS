# Class own-key order (#1765)

The four underscore-named files preserve the issue's displayed original sources.
The remaining passing references cover later writes, multiple fields, class
expressions, inherited dynamic writes, generic storage, numeric indices, symbols
and nonenumerable fields. `ClassOwnKeyOrderTests` provides exact expectations;
the isolated tests verify saved IL and clean standalone execution within 30 seconds.
Hosted variants verify declarations without invoking exports.

`independent/` contains diagnostics excluded from passing coverage. Unchanged
main and current both omit an inherited field from enumeration when no dynamic
write has represented it in the derived store. Both also disagree with Node on
the chronology of properties created inside a field initializer. These require
tracking actual property creation across initialization and inheritance; the
declared-field merge repair does not claim that broader storage redesign.

See [the outcome record](../../../docs/epic-1866-outcomes.md).
