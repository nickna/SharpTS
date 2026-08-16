# npm package compatibility

SharpTS runs a pinned smoke matrix against real packages in
`tests/SharpTS.Tests/IntegrationTests/RealPackageSmokeTests.cs`. Each maintained scenario executes in
the interpreter and compiled mode; the UUID package also covers ESM named imports.

| Package | Tested version | Exercised surface |
| --- | --- | --- |
| `ms` | 2.1.3 | Parse and format durations |
| `uuid` | 9.0.1 | CommonJS `v4`; ESM `v4`, `validate`, and `NIL` |
| `debug` | 4.3.4 | Load the factory and create a namespaced logger |
| `semver` | 7.6.0 | `valid`, `gt`, and `satisfies` |
| `minimatch` | 9.0.4 | Basic positive and negative glob matching |
| `yaml` | 2.4.1 | Parse a mapping and load the stringify function |
| `lodash` | 4.17.21 | Load the callable export, `chunk`, and `flatten` |

The table is a tested API slice, not a guarantee that every export or transitive dependency in a
package is compatible.

## Run the matrix

Install Node.js/npm, then run:

```bash
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj --filter "Category=npm"
```

The fixture installs the exact versions above into a temporary package directory. Tests skip when
`npm` is not on `PATH`; a skipped local run is not compatibility evidence. Network access and a
usable npm registry are required when the packages are not already cached.

When extending the matrix, pin the package version in the fixture, exercise observable behavior in
both modes, and update this table in the same change. Historical fixes and closed gaps belong in
Git history rather than this current compatibility reference.
