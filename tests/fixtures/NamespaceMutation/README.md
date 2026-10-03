# Namespace alias writes (#1777)

`original.ts` preserves the unchanged dynamic-properties probe. Full
TypeScript/CommonJS compilation and Node print `8 9`. Fresh unchanged main and
pre-repair current verify the saved IL and print `null 3`, with empty stderr and
clean exit within the original 30 seconds.

The eight references cover added/existing dot and computed properties, strict
mode, nested values, function replacement, separate namespace objects, and
exported mutable bindings. The exact live-binding and dynamic-write originals
retained from #1774 are included with their full expected results.

Emitted namespace property/index dispatch stores values through its scoped
namespace owner. Exported variable properties bind to the actual generated
public static backing field, so member-body updates and alias writes share one
value. Native saved-helper tests preserve Set/Get identity and isolation, verify
the new Bind metadata and ABI, and observe updates in both directions. Private
variable publication and same-named namespaces across modules are outside this
repair. Missing-value and deletion behavior remain separate #1778/#1779 tasks.

Compiled references and isolated saved standalone outputs require verified IL,
exact stdout, empty stderr, clean exit within 30 seconds, and no SharpTS assembly
reference or copy. Reused native emitters verify minimal/optional/minimal and
hosted ownership/deployment; hosted guest execution is not claimed.
