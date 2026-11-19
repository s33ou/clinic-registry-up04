param(
    [Parameter(Mandatory)][string]$MySqlHome,
    [Parameter(Mandatory)][string]$Options,
    [Parameter(Mandatory)][string]$Manifest
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
$file = (Resolve-Path -LiteralPath $Manifest).ProviderPath
$info = Get-Content -Raw -Encoding UTF8 -LiteralPath $file | ConvertFrom-Json
if ($info.database -ne 'clinic06_up04' -or $info.snapshot -notmatch '^clinic06-[0-9]{8}T[0-9]{9}Z-[a-f0-9]{8}\.sql$') {
    throw 'Unexpected snapshot manifest.'
}
$source = Join-Path (Split-Path $file -Parent) $info.snapshot
if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $info.sha256) {
    throw 'SHA-256 mismatch. Restore stopped.'
}
$client = Get-MySqlExecutable $MySqlHome 'mysql.exe'
$exists = Invoke-MySql $client $Options "SELECT COUNT(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME='clinic06_up04';"
Assert-ClientSuccess $exists
if ($exists.Out -ne '0') { throw 'Restore needs an empty instance without the clinic06_up04 schema.' }
$restored = Invoke-MySql -Client $client -Options $Options -SqlFile $source
Assert-ClientSuccess $restored
[pscustomobject]@{ Snapshot=$info.snapshot; SHA256=$info.sha256 }
