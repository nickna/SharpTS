# SharpTS.Hosting

`SharpTS.Hosting` builds a Native AOT SharpTS executable with a closed, generated
.NET interop catalog. It is for applications that need selected BCL or
application-library types in `dotnet:` imports without enabling open-world
reflection.

Declare each allowed C# type as an MSBuild item. Set `Assembly` for types from a
class library so its managed DLL and copy-local dependency closure are embedded
for SharpTS `--compile` output:

```xml
<ItemGroup>
  <SharpTSNativeInteropType Include="MyCompany.Widget"
                            Assembly="MyCompany.Library"
                            Alias="Widget" />
</ItemGroup>
```

Start SharpTS from the generated catalog:

```csharp
return SharpTSCli.Run(args, SharpTS.Generated.GeneratedNativeDotNetCatalog.Instance);
```

Only the declared runtime types are bindable. Open-ended DLL loading, arbitrary
generic construction, `--gen-decl`, and IL verification remain managed-only.
