[CmdletBinding()]
param(
    [string[]]$Layer = @(),
    [string]$ConfigPath = (Join-Path $PSScriptRoot '../tests/test-layers.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$config = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json -AsHashtable
foreach ($name in $Layer) {
    if ($name -cnotin $config.layers) { throw "Unknown test layer: $name" }
}
$categories = ($config.excludedCategories | ForEach-Object { "Category!=$_" }) -join '&'
if ($Layer.Count -eq 0) { return $categories }

# Namespace defaults use the longest matching prefix. Type overrides always win.
# Include the trailing dot so FooTests does not accidentally match FooTestsExtra.
$clauses = [Collections.Generic.List[string]]::new()
foreach ($namespace in ($config.namespaces.Keys | Sort-Object)) {
    if ($config.namespaces[$namespace] -cnotin $Layer) { continue }
    $terms = [Collections.Generic.List[string]]::new()
    $terms.Add("FullyQualifiedName~$namespace.")
    foreach ($child in ($config.namespaces.Keys | Sort-Object)) {
        if ($child.StartsWith("$namespace.", [StringComparison]::Ordinal)) {
            $terms.Add("FullyQualifiedName!~$child.")
        }
    }
    foreach ($type in ($config.types.Keys | Sort-Object)) {
        if ($type.StartsWith("$namespace.", [StringComparison]::Ordinal)) {
            $terms.Add("FullyQualifiedName!~$type.")
        }
    }
    $clauses.Add('(' + ($terms -join '&') + ')')
}
foreach ($type in ($config.types.Keys | Sort-Object)) {
    if ($config.types[$type] -cin $Layer) { $clauses.Add("FullyQualifiedName~$type.") }
}
if ($clauses.Count -eq 0) { throw 'The selected layers contain no selection rules.' }
return '(' + ($clauses -join '|') + ")&$categories"
