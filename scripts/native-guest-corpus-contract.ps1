Set-StrictMode -Version Latest

function Get-NativeCorpusExpression($Node) {
    if ($Node -is [Management.Automation.Language.StatementBlockAst] -and $Node.Statements.Count -eq 1) {
        return Get-NativeCorpusExpression $Node.Statements[0]
    }
    if ($Node -is [Management.Automation.Language.PipelineAst] -and $Node.PipelineElements.Count -eq 1) {
        return Get-NativeCorpusExpression $Node.PipelineElements[0]
    }
    if ($Node -is [Management.Automation.Language.CommandExpressionAst]) { return $Node.Expression }
    if ($Node -is [Management.Automation.Language.ExpressionAst]) { return $Node }
    throw 'Native corpus data must be a literal expression.'
}

function Get-NativeCorpusStrings($Expression, [string]$Label, [switch]$AllowEmpty) {
    try { $values = @($Expression.SafeGetValue()) }
    catch { throw "$Label must contain literal case names." }
    if ($values.Count -eq 0 -and -not $AllowEmpty) { throw "$Label must not be empty." }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($value in $values) {
        if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) { throw "$Label must contain nonempty literal strings." }
        if (-not $seen.Add($value)) { throw "$Label repeats case '$value'." }
    }
    return ,([string[]]$values)
}

function Assert-NativeCorpusSet([string[]]$Expected, [string[]]$Actual, [string]$Label) {
    $expectedSet = [Collections.Generic.HashSet[string]]::new($Expected, [StringComparer]::Ordinal)
    $actualSet = [Collections.Generic.HashSet[string]]::new($Actual, [StringComparer]::Ordinal)
    $missing = @($Expected | Where-Object { -not $actualSet.Contains($_) })
    $extra = @($Actual | Where-Object { -not $expectedSet.Contains($_) })
    if ($missing.Count -or $extra.Count) {
        throw "$Label coverage differs: missing [$($missing -join ', ')]; extra [$($extra -join ', ')]."
    }
}

function Get-NativeWorkflowRuns([string]$Text) {
    $lines = $Text -split '\r?\n'
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -notmatch '^(?<indent> +)(?:- +)?run:\s*(?<value>.*)$') { continue }
        $indent = $Matches.indent.Length
        $value = $Matches.value.Trim()
        if ($value -notmatch '^[>|][-+]?$') { $value; continue }
        $folded = $value.StartsWith('>', [StringComparison]::Ordinal)
        $body = [Collections.Generic.List[string]]::new()
        while ($index + 1 -lt $lines.Count) {
            $next = $lines[$index + 1]
            if (-not [string]::IsNullOrWhiteSpace($next) -and ([regex]::Match($next, '^ *').Length -le $indent)) { break }
            $index++; $body.Add($next)
        }
        if ($body.Count) {
            $nonempty = @($body | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            if ($nonempty.Count) {
                $bodyIndent = ($nonempty | ForEach-Object { [regex]::Match($_, '^ *').Length } | Measure-Object -Minimum).Minimum
                $deindented = @($body | ForEach-Object { if ($_.Length -ge $bodyIndent) { $_.Substring($bodyIndent) } else { '' } })
                $deindented -join $(if ($folded) { ' ' } else { "`n" })
            }
        }
    }
}

function Test-NativeGuestCorpusContract([string]$HarnessText, [string]$WorkflowText, [string]$CorpusRoot) {
    $tokens = $null; $parseErrors = $null
    $harness = [Management.Automation.Language.Parser]::ParseInput($HarnessText, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count) { throw "Native harness must parse: $($parseErrors[0].Message)" }
    $parameters = @($harness.ParamBlock.Parameters | Where-Object { $_.Name.VariablePath.UserPath -ceq 'Cases' })
    if ($parameters.Count -ne 1 -or $null -eq $parameters[0].DefaultValue) { throw 'Native harness must declare one literal Cases default.' }
    $defaults = Get-NativeCorpusStrings $parameters[0].DefaultValue 'Native default Cases'
    $assignments = @($harness.EndBlock.Statements | Where-Object {
        $_ -is [Management.Automation.Language.AssignmentStatementAst] -and
        $_.Left -is [Management.Automation.Language.VariableExpressionAst] -and $_.Left.VariablePath.UserPath -ceq 'cohort'
    })
    if ($assignments.Count -ne 1) { throw 'Native harness must define one literal cohort.' }
    $cohort = Get-NativeCorpusExpression $assignments[0].Right
    if ($cohort -isnot [Management.Automation.Language.HashtableAst]) { throw 'Native cohort must be a literal hashtable.' }
    $names = [Collections.Generic.List[string]]::new()
    $sources = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $root = [IO.Path]::GetFullPath($CorpusRoot)
    foreach ($entry in $cohort.KeyValuePairs) {
        $name = $entry.Item1.SafeGetValue()
        if ($name -isnot [string] -or [string]::IsNullOrWhiteSpace($name) -or $sources.ContainsKey($name)) { throw 'Native cohort case names must be unique literal strings.' }
        $definition = Get-NativeCorpusExpression $entry.Item2
        if ($definition -isnot [Management.Automation.Language.HashtableAst]) { throw "Native case '$name' must have a literal definition." }
        $sourceEntries = @($definition.KeyValuePairs | Where-Object { $_.Item1.SafeGetValue() -ceq 'source' })
        if ($sourceEntries.Count -ne 1) { throw "Native case '$name' must declare one literal source." }
        $source = (Get-NativeCorpusExpression $sourceEntries[0].Item2).SafeGetValue()
        if ($source -isnot [string] -or [string]::IsNullOrWhiteSpace($source) -or [IO.Path]::IsPathRooted($source)) { throw "Native case '$name' source must be a relative literal path." }
        $path = [IO.Path]::GetFullPath($source, $root)
        $relative = [IO.Path]::GetRelativePath($root, $path)
        if ($relative -eq '..' -or $relative.StartsWith('../', [StringComparison]::Ordinal) -or $relative.StartsWith('..\', [StringComparison]::Ordinal) -or
            -not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Native case '$name' source is missing from the corpus: $source" }
        $names.Add($name); $sources.Add($name, $source)
    }
    Assert-NativeCorpusSet $defaults $names.ToArray() 'Native literal cohort'
    $commands = [Collections.Generic.List[object]]::new()
    foreach ($run in @(Get-NativeWorkflowRuns $WorkflowText)) {
        $runAst = [Management.Automation.Language.Parser]::ParseInput($run, [ref]$tokens, [ref]$parseErrors)
        foreach ($statement in $runAst.EndBlock.Statements) {
            if ($statement -isnot [Management.Automation.Language.PipelineAst]) { continue }
            foreach ($command in $statement.PipelineElements) {
                if ($command -isnot [Management.Automation.Language.CommandAst]) { continue }
                $commandName = $command.GetCommandName()
                if ($null -eq $commandName -or $commandName.Replace('\', '/') -cnotmatch '^(?:\./)?scripts/test-native-guest\.ps1$') { continue }
                if ($parseErrors.Count) { throw "Native workflow command must parse: $($parseErrors[0].Message)" }
                $elements = $command.CommandElements
                $caseParameters = @(for ($at = 0; $at -lt $elements.Count; $at++) {
                    if ($elements[$at] -is [Management.Automation.Language.CommandParameterAst] -and $elements[$at].ParameterName -ieq 'Cases') { $at }
                })
                if ($caseParameters.Count -ne 1) { throw 'Native workflow must pass one explicit literal -Cases list.' }
                $parameter = $elements[$caseParameters[0]]
                $argument = $parameter.Argument
                if ($null -eq $argument -and $caseParameters[0] + 1 -lt $elements.Count) { $argument = $elements[$caseParameters[0] + 1] }
                if ($argument -isnot [Management.Automation.Language.ExpressionAst]) { throw 'Native workflow -Cases must be a literal expression.' }
                $selected = Get-NativeCorpusStrings $argument 'Native workflow Cases' -AllowEmpty
                Assert-NativeCorpusSet $defaults $selected 'Native workflow Cases'
                $commands.Add([pscustomobject]@{ Text = $command.Extent.Text; Cases = $selected;
                    CasesStart = $argument.Extent.StartOffset - $command.Extent.StartOffset; CasesLength = $argument.Extent.EndOffset - $argument.Extent.StartOffset })
            }
        }
    }
    if ($commands.Count -eq 0) { throw 'Native workflow has no literal native harness invocation.' }
    return [pscustomobject]@{ Cases = $defaults; Sources = $sources; Commands = $commands.ToArray() }
}
