[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NativeTypeDefinitionPolicy.psm1') -Force
$root = Split-Path $PSScriptRoot -Parent
$cases = @(
    @{ Name = 'untracked instance'; Body = 'module.DefineType("C", TypeAttributes.Public);'; Unsafe = 1 },
    @{ Name = 'static helper'; Body = 'module.DefineType("C", TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed);'; Unsafe = 0 },
    @{ Name = 'unknown attributes'; Body = 'var attr = ReadFlags(); module.DefineType("C", attr);'; Unsafe = 1 },
    @{ Name = 'conditional instance'; Body = 'module.DefineType("C", ReadFlag() ? TypeAttributes.Abstract : TypeAttributes.Public);'; Unsafe = 1 },
    @{ Name = 'nested instance'; Body = 'owner.DefineNestedType("C", TypeAttributes.NestedPublic);'; Unsafe = 1 },
    @{ Name = 'nested contract'; Body = 'owner.DefineNestedType("C", TypeAttributes.Interface | TypeAttributes.Abstract);'; Unsafe = 1 },
    @{ Name = 'tracked factory'; Body = 'EmitTypeDefinitions.DefineType(module, "C", ReadFlags());'; Unsafe = 0 },
    @{ Name = 'untracked enum'; Body = 'module.DefineEnum("C", TypeAttributes.Public, typeof(int));'; Unsafe = 1 },
    @{ Name = 'method group'; Body = 'var callback = module.DefineType;'; Unsafe = 1 },
    @{ Name = 'non-code'; Body = '// module.DefineType("C", flags);' + "`n" + 'var text = "module.DefineType";'; Unsafe = 0 }
)
foreach ($case in $cases) {
    $source = 'using System; using System.Reflection; using System.Reflection.Emit; namespace SharpTS.Compilation { class Probe { void Run(ModuleBuilder module, TypeBuilder owner) { ' + $case.Body + ' } } }'
    $unsafe = @(Get-NativeTypeDefinitionCalls -Text $source | Where-Object { -not $_.Safe })
    if ($unsafe.Count -ne $case.Unsafe) { throw "Definition policy control failed: $($case.Name)." }
}
$shadow = 'using System.Reflection; using System.Reflection.Emit; namespace SharpTS.Compilation { class Probe { void Run(ModuleBuilder EmitTypeDefinitions) { EmitTypeDefinitions.DefineType("C", TypeAttributes.Public); } } }'
if (@(Get-NativeTypeDefinitionCalls -Text $shadow | Where-Object { -not $_.Safe }).Count -ne 1) { throw 'A shadowed factory name bypassed the policy.' }
$factoryPath = Join-Path $root 'src/SharpTS/Compilation/EmitTypeDefinitions.cs'
$untrackedFactory = 'using System.Reflection; using System.Reflection.Emit; namespace SharpTS.Compilation { static class EmitTypeDefinitions { static TypeBuilder DefineType(ModuleBuilder module) { return module.DefineType("C", TypeAttributes.Public); } } }'
if (@(Get-NativeTypeDefinitionCalls -Text $untrackedFactory -Path $factoryPath -FactorySource $untrackedFactory | Where-Object { -not $_.Safe }).Count -ne 1) { throw 'An unrecorded central factory bypassed the policy.' }
$shadowedLedger = 'using System.Reflection; using System.Reflection.Emit; namespace SharpTS.Compilation { static class EmitTypeDefinitions { static TypeBuilder DefineType(ModuleBuilder module) { dynamic NativeOwnedTypeDeclarations = null; return NativeOwnedTypeDeclarations.Record(module, module.DefineType("C", TypeAttributes.Public)); } } }'
if (@(Get-NativeTypeDefinitionCalls -Text $shadowedLedger -Path $factoryPath -FactorySource $shadowedLedger | Where-Object { -not $_.Safe }).Count -ne 1) { throw 'A shadowed ledger name bypassed the central factory policy.' }
$errors = @(Get-ChildItem (Join-Path $root 'src/SharpTS/Compilation') -Recurse -File -Filter '*.cs' | ForEach-Object {
    Get-NativeTypeDefinitionCalls -Text ([IO.File]::ReadAllText($_.FullName)) -Path $_.FullName | Where-Object { -not $_.Safe }
})
if ($errors.Count) {
    $errors | ForEach-Object { Write-Host "$($_.Path):$($_.Line): untracked $($_.Method). Use EmitTypeDefinitions." }
    throw "Native type declaration coverage has $($errors.Count) untracked definition paths."
}
Write-Host "Native type definition architecture: 13 controls passed; all instance and nested definitions are tracked."
