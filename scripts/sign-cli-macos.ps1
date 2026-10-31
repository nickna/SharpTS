[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Executable,
    [Parameter(Mandatory)][ValidateSet('Managed', 'Native')][string]$Sku,
    [Parameter(Mandatory)][string]$ResultsDirectory
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $IsMacOS) { throw 'CLI signing and notarization require macOS.' }
$required = @('MACOS_DEVELOPER_ID_P12_BASE64', 'MACOS_DEVELOPER_ID_P12_PASSWORD', 'MACOS_DEVELOPER_ID_APPLICATION',
    'MACOS_NOTARY_KEY_BASE64', 'MACOS_NOTARY_KEY_ID', 'MACOS_NOTARY_ISSUER_ID')
foreach ($name in $required) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "Missing signing secret: $name" }
}
if ($env:MACOS_DEVELOPER_ID_APPLICATION -eq '-') { throw 'A Developer ID Application identity is required; ad-hoc signing is not accepted.' }
Import-Module (Join-Path $PSScriptRoot 'SmokeProcess.psm1') -Force
$binary = (Resolve-Path -LiteralPath $Executable).Path
$root = [IO.Path]::GetFullPath($ResultsDirectory)
New-Item -ItemType Directory -Path $root -Force | Out-Null
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('sharpts-signing-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$keychain = Join-Path $temporary 'signing.keychain-db'
$certificate = Join-Path $temporary 'developer-id.p12'
$notaryKey = Join-Path $temporary 'AuthKey.p8'
$password = [guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N')
function Invoke-Security([string[]]$Arguments) {
    # Never log arguments: import/keychain commands contain credential material.
    & /usr/bin/security @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Signing keychain operation failed.' }
}
try {
    [IO.File]::WriteAllBytes($certificate, [Convert]::FromBase64String($env:MACOS_DEVELOPER_ID_P12_BASE64))
    [IO.File]::WriteAllBytes($notaryKey, [Convert]::FromBase64String($env:MACOS_NOTARY_KEY_BASE64))
    & /bin/chmod 600 $certificate $notaryKey
    if ($LASTEXITCODE -ne 0) { throw 'Cannot restrict signing material permissions.' }
    Invoke-Security @('create-keychain', '-p', $password, $keychain)
    Invoke-Security @('unlock-keychain', '-p', $password, $keychain)
    Invoke-Security @('import', $certificate, '-k', $keychain, '-P', $env:MACOS_DEVELOPER_ID_P12_PASSWORD, '-T', '/usr/bin/codesign')
    Invoke-Security @('set-key-partition-list', '-S', 'apple-tool:,apple:,codesign:', '-s', '-k', $password, $keychain)
    $options = @{ WorkingDirectory = $root; LogDirectory = $root }
    $sign = @('--force', '--sign', $env:MACOS_DEVELOPER_ID_APPLICATION, '--keychain', $keychain, '--options', 'runtime', '--timestamp')
    if ($Sku -eq 'Managed') { $sign += @('--entitlements', (Join-Path $PSScriptRoot 'macos-cli-managed.entitlements.plist')) }
    Invoke-SmokeProcess -Name sign -FileName /usr/bin/codesign -Arguments @($sign + $binary) @options | Out-Null
    Invoke-SmokeProcess -Name verify-signature -FileName /usr/bin/codesign -Arguments @('--verify', '--deep', '--strict', '--verbose=4', $binary) @options | Out-Null
    $submission = Join-Path $temporary 'submission.zip'
    Invoke-SmokeProcess -Name notarization-zip -FileName /usr/bin/ditto -Arguments @('-c', '-k', '--keepParent', $binary, $submission) @options | Out-Null
    $response = Invoke-SmokeProcess -Name notarization -FileName /usr/bin/xcrun -Arguments @('notarytool', 'submit', $submission,
        '--key', $notaryKey, '--key-id', $env:MACOS_NOTARY_KEY_ID, '--issuer', $env:MACOS_NOTARY_ISSUER_ID, '--wait', '--output-format', 'json') -TimeoutMilliseconds 1200000 @options
    $result = $response | ConvertFrom-Json
    if ($result.status -cne 'Accepted') { throw "Apple notarization was not accepted: $($result.status)" }
    Invoke-SmokeProcess -Name gatekeeper -FileName /usr/sbin/spctl -Arguments @('--assess', '--type', 'execute', '--verbose=4', $binary) @options | Out-Null
    [ordered]@{ submissionId = $result.id; status = $result.status; executableSha256 = (Get-FileHash $binary -Algorithm SHA256).Hash.ToLowerInvariant() } |
        ConvertTo-Json | Set-Content (Join-Path $root 'notarization.json') -Encoding utf8NoBOM
}
finally {
    if (Test-Path -LiteralPath $keychain) { & /usr/bin/security delete-keychain $keychain | Out-Null }
    # Explicit files only; do not retain certificates, private keys, or keychain material.
    foreach ($path in @($certificate, $notaryKey)) { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force } }
}
