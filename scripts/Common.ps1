$utf8 = [Text.UTF8Encoding]::new($false)

function Get-MySqlExecutable([string]$MySqlHome, [string]$Name) {
    $path = Join-Path $MySqlHome ("bin\" + $Name)
    return (Resolve-Path -LiteralPath $path).ProviderPath
}

function Invoke-MySql([string]$Client, [string]$Options, [string]$Sql, [string]$SqlFile) {
    $ini = (Resolve-Path -LiteralPath $Options).ProviderPath
    $process = New-Object Diagnostics.Process
    $process.StartInfo.FileName = $Client
    $process.StartInfo.Arguments = "--defaults-file=`"$ini`" --batch --raw --skip-column-names"
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardInput = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.StandardOutputEncoding = $utf8
    $process.StartInfo.StandardErrorEncoding = $utf8
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        try {
            if ($SqlFile) {
                $snapshot = [IO.File]::OpenRead($SqlFile)
                try { $snapshot.CopyTo($process.StandardInput.BaseStream) }
                finally { $snapshot.Dispose() }
            } else {
                $bytes = $utf8.GetBytes($Sql)
                $process.StandardInput.BaseStream.Write($bytes,0,$bytes.Length)
            }
        } finally { $process.StandardInput.Close() }
        if (!$process.WaitForExit(60000)) {
            $process.Kill()
            throw 'Timed out waiting for MySQL.'
        }
        $out = $stdout.GetAwaiter().GetResult().Trim()
        $err = $stderr.GetAwaiter().GetResult().Trim()
        [pscustomobject]@{ ExitCode=$process.ExitCode; Out=$out; Error=$err }
    } finally { $process.Dispose() }
}

function Assert-ClientSuccess($Result) {
    if ($Result.ExitCode -ne 0) { throw $Result.Error }
}

function Resolve-BackupDirectory([string]$Path) {
    $item = Get-Item -LiteralPath $Path
    if (!$item.PSIsContainer) { throw "Expected a directory: $Path" }
    return $item.FullName
}
