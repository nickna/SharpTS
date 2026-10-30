Set-StrictMode -Version Latest

# Use the Roslyn shipped with PowerShell; fall back to the selected SDK when necessary.
if (-not ('Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree' -as [type])) {
    $selectedSdk = (& dotnet --version).Trim()
    $entry = @(& dotnet --list-sdks | Where-Object { $_ -match ('^' + [regex]::Escape($selectedSdk) + '\s+\[') })
    if ($entry.Count -ne 1 -or $entry[0] -notmatch '^\S+\s+\[(.+)\]$') { throw 'Cannot locate the selected SDK Roslyn.' }
    $roslyn = Join-Path $Matches[1] "$selectedSdk/Roslyn/bincore"
    Add-Type -LiteralPath (Join-Path $roslyn 'Microsoft.CodeAnalysis.dll'), (Join-Path $roslyn 'Microsoft.CodeAnalysis.CSharp.dll')
}
$script:CoreReference = [Microsoft.CodeAnalysis.MetadataReference]::CreateFromFile([object].Assembly.Location)
$script:FactoryPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/SharpTS/Compilation/EmitTypeDefinitions.cs'))
$script:LedgerPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../src/SharpTS/Compilation/NativeOwnedTypeDeclarations.cs'))

function Get-NativeTypeDefinitionCalls {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Text, [string]$Path = 'PolicyProbe.cs', [string]$FactorySource)
    if (-not $FactorySource) { $FactorySource = [IO.File]::ReadAllText($script:FactoryPath) }
    $isFactory = [IO.Path]::GetFullPath($Path) -ceq $script:FactoryPath
    $tree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($Text, $null, $Path)
    $factoryTree = if ($isFactory) { $tree } else { [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText($FactorySource, $null, '__OwnedDefinitionFactory.cs') }
    $ledgerTree = [Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree]::ParseText([IO.File]::ReadAllText($script:LedgerPath), $null, '__OwnedDeclarationLedger.cs')
    $trees = if ($isFactory) { [Microsoft.CodeAnalysis.SyntaxTree[]]@($tree, $ledgerTree) } else { [Microsoft.CodeAnalysis.SyntaxTree[]]@($tree, $factoryTree, $ledgerTree) }
    $compilation = [Microsoft.CodeAnalysis.CSharp.CSharpCompilation]::Create('NativeDefinitionPolicy', [Microsoft.CodeAnalysis.SyntaxTree[]]$trees,
        [Microsoft.CodeAnalysis.MetadataReference[]]@($script:CoreReference), $null)
    $model = $compilation.GetSemanticModel($tree)
    foreach ($member in $tree.GetRoot().DescendantNodes()) {
        if ($member -isnot [Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax] -and
            $member -isnot [Microsoft.CodeAnalysis.CSharp.Syntax.MemberBindingExpressionSyntax]) { continue }
        $name = $member.Name.Identifier.ValueText
        if ($name -notin @('DefineType', 'DefineNestedType', 'DefineEnum')) { continue }
        $invocation = if ($member.Parent -is [Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax] -and
            $member.Parent.Expression.Span.Equals($member.Span)) { $member.Parent } else { $null }
        $tracked = $false
        $nonInstantiable = $false
        $receiver = if ($member -is [Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax]) { $member.Expression.ToString() } else { '' }
        if ($receiver) {
            $symbol = [Microsoft.CodeAnalysis.CSharp.CSharpExtensions]::GetSymbolInfo($model, $member.Expression, [Threading.CancellationToken]::None).Symbol
            $tracked = $null -ne $symbol -and $symbol.Kind.ToString() -ceq 'NamedType' -and
                $symbol.ToString() -ceq 'SharpTS.Compilation.EmitTypeDefinitions' -and
                @($symbol.DeclaringSyntaxReferences | Where-Object { $_.SyntaxTree -eq $factoryTree }).Count -eq 1
        }
        if ($isFactory -and $invocation) {
            # Even the central factory must pass each raw definition immediately to Record.
            $argument = $invocation.Parent
            $recordCall = if ($argument -is [Microsoft.CodeAnalysis.CSharp.Syntax.ArgumentSyntax]) { $argument.Parent.Parent } else { $null }
            $recordMethod = if ($recordCall -is [Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax]) {
                [Microsoft.CodeAnalysis.CSharp.CSharpExtensions]::GetSymbolInfo($model, $recordCall, [Threading.CancellationToken]::None).Symbol
            } else { $null }
            $tracked = $recordMethod -and $recordMethod.Kind.ToString() -ceq 'Method' -and
                $recordMethod.IsStatic -and $recordMethod.Name -ceq 'Record' -and
                $recordMethod.ContainingType.ToString() -ceq 'SharpTS.Compilation.NativeOwnedTypeDeclarations' -and
                @($recordMethod.DeclaringSyntaxReferences | Where-Object { $_.SyntaxTree -eq $ledgerTree }).Count -eq 1
        }
        if (-not $tracked -and $invocation -and $name -ceq 'DefineType') {
            $arguments = $invocation.ArgumentList.Arguments
            $attribute = @($arguments | Where-Object { $_.NameColon -and $_.NameColon.Name.Identifier.ValueText -ceq 'attr' })
            $expression = if ($attribute.Count -eq 1) { $attribute[0].Expression } elseif ($arguments.Count -ge 2) { $arguments[1].Expression } else { $null }
            if ($expression) {
                $constant = $model.GetConstantValue($expression)
                $type = [Microsoft.CodeAnalysis.CSharp.CSharpExtensions]::GetTypeInfo($model, $expression, [Threading.CancellationToken]::None).Type
                if ($constant.HasValue -and $type -and $type.ToString() -ceq 'System.Reflection.TypeAttributes' -and
                    $type.ContainingAssembly.Name -ceq [Reflection.TypeAttributes].Assembly.GetName().Name) {
                    $nonInstantiable = ([int]$constant.Value -band [int]([Reflection.TypeAttributes]::Abstract -bor [Reflection.TypeAttributes]::Interface)) -ne 0
                }
            }
        }
        $safe = $tracked -or $nonInstantiable
        [pscustomobject]@{
            Path = $Path; Line = $member.GetLocation().GetLineSpan().StartLinePosition.Line + 1
            Method = $name; Receiver = $receiver; Tracked = $tracked; NonInstantiable = $nonInstantiable; Safe = $safe
            Start = if ($invocation) { $invocation.Span.Start } else { $member.Span.Start }
            End = if ($invocation) { $invocation.Span.End } else { $member.Span.End }
            ExpressionEnd = $member.Span.End
            OpenParenEnd = if ($invocation) { $invocation.ArgumentList.OpenParenToken.Span.End } else { -1 }
            ArgumentTokens = if ($invocation) { @($invocation.ArgumentList.Arguments | ForEach-Object { (@($_.Expression.DescendantTokens() | ForEach-Object { "$($_.RawKind):$($_.Text)" }) -join '|') }) } else { @() }
        }
    }
}

Export-ModuleMember -Function Get-NativeTypeDefinitionCalls
