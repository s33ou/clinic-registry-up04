param(
    [Parameter(Mandatory)][string]$MySqlHome,
    [Parameter(Mandatory)][string]$Options,
    [Parameter(Mandatory)][string]$LocalDir,
    [Parameter(Mandatory)][string]$OffsiteDir,
    [switch]$Lab
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Common.ps1')
$dump = Get-MySqlExecutable $MySqlHome 'mysqldump.exe'
$ini = (Resolve-Path -LiteralPath $Options).ProviderPath
$local = Resolve-BackupDirectory $LocalDir
$remote = Resolve-BackupDirectory $OffsiteDir
if ($local -eq $remote) { throw 'Use two different backup directories.' }
if (!$Lab -and [IO.Path]::GetPathRoot($local) -eq [IO.Path]::GetPathRoot($remote)) {
    throw 'Use different volumes or an authorized offsite share. Use -Lab for a local rehearsal.'
}

$time = [DateTime]::UtcNow
$id = 'clinic06-' + $time.ToString('yyyyMMddTHHmmssfffZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$name = $id + '.sql'
$localPart = Join-Path $local ($name + '.part')
$localFile = Join-Path $local $name
$remotePart = Join-Path $remote ($name + '.part')
$remoteFile = Join-Path $remote $name
$manifest = $id + '.json'
try {
    & $dump "--defaults-file=$ini" '--single-transaction' '--quick' '--no-tablespaces' '--set-gtid-purged=OFF' "--result-file=$localPart" '--databases' 'clinic06_up04'
    if ($LASTEXITCODE -ne 0) { throw 'mysqldump returned an error.' }
    if ((Get-Item -LiteralPath $localPart).Length -eq 0) { throw 'The snapshot is empty.' }
    $hash = (Get-FileHash -LiteralPath $localPart -Algorithm SHA256).Hash
    Copy-Item -LiteralPath $localPart -Destination $remotePart
    if ((Get-FileHash -LiteralPath $remotePart -Algorithm SHA256).Hash -ne $hash) { throw 'SHA-256 mismatch.' }
    Move-Item -LiteralPath $localPart -Destination $localFile
    Move-Item -LiteralPath $remotePart -Destination $remoteFile
    $info = [ordered]@{
        database='clinic06_up04'; snapshot=$name; sha256=$hash; utc=$time.ToString('o')
        bytes=(Get-Item -LiteralPath $localFile).Length; lab=[bool]$Lab
    }
    $json = $info | ConvertTo-Json
    foreach ($directory in @($local,$remote)) {
        $pending = Join-Path $directory ($manifest + '.part')
        [IO.File]::WriteAllText($pending,$json,$utf8)
        Move-Item -LiteralPath $pending -Destination (Join-Path $directory $manifest)
    }
    [pscustomobject]@{ Name=$name; Local=$localFile; Offsite=$remoteFile; Manifest=$manifest; SHA256=$hash; Bytes=$info.bytes }
} finally {
    foreach ($path in @($localPart,$remotePart,(Join-Path $local ($manifest + '.part')),(Join-Path $remote ($manifest + '.part')))) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
}
