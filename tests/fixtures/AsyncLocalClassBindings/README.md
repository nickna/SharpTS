# Async local class bindings (#1805)

These three files preserve the issue's original displayed sources. Each prints
`5` with empty stderr and clean exit within 30 seconds on fresh main and current.
The repair already exists in `ca9dec46` (#1849), which preserves named class
storage across suspension and scoped renaming.

The original isolated executable checks retain the original CLI options and
deadline. Hosted variants verify module IL and deployment without invoking exports.
Existing `AsyncLocalClassDeclarationTests`, class-expression and native class
initializer tests retain ordinary, async, generator, declaration/expression,
shadowing, definition-order and exception-identity controls.

See [the outcome record](../../../docs/epic-1866-outcomes.md).
