param(
    [ValidateSet('Prepare','PrepareEmpty','InstallAccess','Start','Stop','ShowTables')]
    [string]$Action = 'Start',
    [string]$State = (Join-Path $env:LOCALAPPDATA 'Clinic06-UP04-Case6'),
    [int]$Port = 33476,
    [string]$App,
    [string]$MySqlHome
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$utf8 = New-Object Text.UTF8Encoding($false)
$ProgressPreference = 'SilentlyContinue'

function Locate-MySql {
    $download = Join-Path $State 'mysql-8.0.46-winx64'
    $savedHome = Join-Path $State 'mysql-home.txt'
    $saved = if (Test-Path -LiteralPath $savedHome) { (Get-Content -LiteralPath $savedHome -Raw).Trim() }
    $previousHint = Join-Path $env:LOCALAPPDATA 'Clinic06-UP04-Case2\mysql-home.txt'
    $previousHome = if (Test-Path -LiteralPath $previousHint) { (Get-Content -LiteralPath $previousHint -Raw).Trim() }
    $locations = @($MySqlHome, $env:CLINIC06_MYSQL_HOME, $saved, $download, $previousHome, "$env:ProgramFiles\MySQL\MySQL Server 8.0",
                  "$env:ProgramFiles\MySQL\MySQL Server 8.4")
    foreach ($location in $locations) {
        if ($location -and (Test-Path -LiteralPath (Join-Path $location 'bin\mysqld.exe')) -and
            (Test-Path -LiteralPath (Join-Path $location 'bin\mysql.exe'))) {
            return $location
        }
    }
    if ($Action -in @('Stop','ShowTables')) {
        throw 'Файлы MySQL не найдены. Сначала запустите приложение.'
    }
    Write-Host 'Скачиваю MySQL Community Server с cdn.mysql.com для первого запуска.'
    $zip = Join-Path $State 'mysql-server.zip'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://cdn.mysql.com/Downloads/MySQL-8.0/mysql-8.0.46-winx64.zip' -OutFile $zip
    Expand-Archive -LiteralPath $zip -DestinationPath $State -Force
    if (!(Test-Path -LiteralPath (Join-Path $download 'bin\mysqld.exe'))) { throw 'Не удалось распаковать MySQL.' }
    Remove-Item -LiteralPath $zip
    return $download
}

function Run-Client([string]$optionFile, [string]$sql) {
    $process = New-Object Diagnostics.Process
    $process.StartInfo.FileName = $client
    $process.StartInfo.Arguments = ('--defaults-file="{0}" --batch --raw --skip-column-names --connect-timeout=3' -f $optionFile)
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
        $bytes = $utf8.GetBytes($sql + [Environment]::NewLine)
        $process.StandardInput.BaseStream.Write($bytes, 0, $bytes.Length)
        $process.StandardInput.Close()
        if (!$process.WaitForExit(20000)) {
            $process.Kill()
            throw 'Клиент MySQL не ответил вовремя.'
        }
        if ($process.ExitCode -ne 0) { throw $stderr.GetAwaiter().GetResult().Trim() }
        return $stdout.GetAwaiter().GetResult().Trim()
    }
    finally { $process.Dispose() }
}

function Write-ClientOptions([string]$file, [string]$username, [string]$secret) {
    $contents = @"
[client]
host=127.0.0.1
port=$Port
protocol=tcp
user=$username
password=$secret
default-character-set=utf8mb4
ssl-mode=REQUIRED
"@
    [IO.File]::WriteAllText($file, $contents, $utf8)
}

function Query-OwnServer {
    try {
        $dir = Run-Client $rootSettings 'SELECT @@datadir;'
        if (!$dir) { return $false }
        $actual = [IO.Path]::GetFullPath($dir.TrimEnd('/','\'))
        if ($actual -ne $dataDir) { throw "Сервер на порту $Port использует другой каталог данных." }
        return $true
    } catch {
        if ($_.Exception.Message -like '*другой каталог данных*') { throw }
        return $false
    }
}

if ($Action -in @('Stop','ShowTables') -and !(Test-Path -LiteralPath $State)) {
    throw 'Сначала запустите приложение.'
}
New-Item -ItemType Directory -Path $State -Force | Out-Null
$State = (Resolve-Path -LiteralPath $State).ProviderPath
$dataDir = Join-Path $State 'data'
$serverOptions = Join-Path $State 'my.ini'
$rootSettings = Join-Path $State 'admin-client.ini'
$appSettings = Join-Path $State 'app-client.ini'
$credentials = Join-Path $State 'credentials.json'
$connection = Join-Path $State 'connection.json'
$stateHash = [BitConverter]::ToString(([Security.Cryptography.SHA256]::Create().ComputeHash(
    $utf8.GetBytes($State.ToLowerInvariant())))).Replace('-','')
$gate = New-Object Threading.Mutex($false, "Local\Clinic06UP04_$stateHash")
$locked = $false
try {
    try { $locked = $gate.WaitOne(150000) } catch [Threading.AbandonedMutexException] { $locked = $true }
    if (!$locked) { throw 'MySQL уже запускается в другом окне.' }
    $mysqlBase = Locate-MySql
    [IO.File]::WriteAllText((Join-Path $State 'mysql-home.txt'), $mysqlBase, $utf8)
    $server = Join-Path $mysqlBase 'bin\mysqld.exe'
    $client = Join-Path $mysqlBase 'bin\mysql.exe'
    if (Test-Path -LiteralPath $credentials) {
        $auth = Get-Content -Raw -LiteralPath $credentials | ConvertFrom-Json
        $Port = [int]$auth.port
    } else {
        if ($Action -in @('Stop','ShowTables')) { throw 'Сначала подготовьте базу.' }
        while (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) { $Port += 1 }
        $auth = [ordered]@{
            port=$Port
            admin=[Guid]::NewGuid().ToString('N')
            app=[Guid]::NewGuid().ToString('N')
        }
        [IO.File]::WriteAllText($credentials, ($auth | ConvertTo-Json), $utf8)
    }
    Write-ClientOptions $rootSettings 'root' $auth.admin
    Write-ClientOptions $appSettings 'clinic06_app' $auth.app
    if ($Action -eq 'Stop') {
        if (Query-OwnServer) {
            [void](Run-Client $rootSettings 'SHUTDOWN;')
            Write-Host 'Учебная база остановлена.'
        }
        return
    }
    if ($Action -eq 'ShowTables') {
        if (!(Query-OwnServer)) { throw 'Сначала запустите учебную базу.' }
        & $client "--defaults-file=$appSettings" '--table' '--database=clinic06_up04' '--execute=SELECT VERSION() AS MySQL; SHOW TABLES; SELECT COUNT(*) AS patients FROM patients; SELECT COUNT(*) AS doctors FROM doctors; SELECT COUNT(*) AS slots FROM slots; SELECT COUNT(*) AS appointments FROM appointments; SELECT COUNT(*) AS history FROM appointment_history; SELECT id,login,is_active,LENGTH(password_hash) AS hash_bytes,LENGTH(password_salt) AS salt_bytes FROM users;'
        return
    }

    $basePath = $mysqlBase.Replace('\','/')
    $myData = $dataDir.Replace('\','/')
    $logPath = (Join-Path $State 'mysql.log').Replace('\','/')
    $options = @"
[mysqld]
basedir="$basePath"
datadir="$myData"
port=$Port
bind-address=127.0.0.1
mysqlx=OFF
character-set-server=utf8mb4
log-error="$logPath"
"@
    if (!(Query-OwnServer)) {
        if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) {
            throw "Порт $Port занят. Проверьте инструкцию."
        }
        [IO.File]::WriteAllText($serverOptions, $options, $utf8)
        $serverArg = '--defaults-file="{0}"' -f $serverOptions
        $bootstrap = Join-Path $State 'bootstrap.sql'
        if (!(Test-Path -LiteralPath (Join-Path $dataDir 'mysql.ibd'))) {
            Write-Host 'Подготавливаю локальный каталог MySQL.'
            $initial = Start-Process -FilePath $server -ArgumentList @($serverArg,'--initialize-insecure') -WindowStyle Hidden -PassThru -Wait
            if ($initial.ExitCode -ne 0) { throw "Не удалось инициализировать MySQL. Журнал $logPath" }
            $alter = "ALTER USER 'root'@'localhost' IDENTIFIED BY '$($auth.admin)';"
            [IO.File]::WriteAllText($bootstrap, ($alter + [Environment]::NewLine), $utf8)
        }
        $serverArgs = @($serverArg)
        if (Test-Path -LiteralPath $bootstrap) { $serverArgs += '--init-file="{0}"' -f $bootstrap }
        $worker = Start-Process -FilePath $server -ArgumentList $serverArgs -WorkingDirectory $mysqlBase -WindowStyle Hidden -PassThru
        $ready = $false
        for ($attempt = 0; $attempt -lt 75; $attempt++) {
            if (Query-OwnServer) { $ready = $true; break }
            if ($worker.HasExited) { break }
            Start-Sleep -Milliseconds 500
        }
        if (!$ready) { throw "MySQL не запустилась. Журнал $logPath" }
        if (Test-Path -LiteralPath $bootstrap) { Remove-Item -LiteralPath $bootstrap }
    }
    if ($Action -eq 'PrepareEmpty') { return }
    $schemaReady = Join-Path $State 'schema-v1.ready'
    if ($Action -ne 'InstallAccess' -and !(Test-Path -LiteralPath $schemaReady)) {
        Write-Host 'Создаю таблицы и учебные записи.'
        [void](Run-Client $rootSettings ([IO.File]::ReadAllText((Join-Path $root 'database\01_schema.sql'), $utf8)))
        [void](Run-Client $rootSettings ([IO.File]::ReadAllText((Join-Path $root 'database\02_demo.sql'), $utf8)))
    }
    if ($Action -eq 'InstallAccess' -or !(Test-Path -LiteralPath $schemaReady)) {
        $grants = @"
CREATE USER IF NOT EXISTS 'clinic06_app'@'127.0.0.1' IDENTIFIED BY '$($auth.app)' REQUIRE SSL;
GRANT SELECT ON clinic06_up04.roles TO 'clinic06_app'@'127.0.0.1';
GRANT SELECT ON clinic06_up04.specialties TO 'clinic06_app'@'127.0.0.1';
GRANT SELECT,INSERT,UPDATE ON clinic06_up04.users TO 'clinic06_app'@'127.0.0.1';
GRANT SELECT,INSERT,UPDATE,DELETE ON clinic06_up04.patients TO 'clinic06_app'@'127.0.0.1';
GRANT SELECT,INSERT,UPDATE,DELETE ON clinic06_up04.doctors TO 'clinic06_app'@'127.0.0.1';
GRANT SELECT,INSERT,UPDATE,DELETE ON clinic06_up04.slots TO 'clinic06_app'@'127.0.0.1';
GRANT SELECT,INSERT,UPDATE ON clinic06_up04.appointments TO 'clinic06_app'@'127.0.0.1';
GRANT SELECT,INSERT ON clinic06_up04.appointment_history TO 'clinic06_app'@'127.0.0.1';
"@
        [void](Run-Client $rootSettings $grants)
        [void](Run-Client $rootSettings ([IO.File]::ReadAllText((Join-Path $root 'database\03_access.sql'), $utf8)))
        [void](Run-Client $appSettings 'SELECT COUNT(*) FROM clinic06_up04.users;')
        New-Item -ItemType File -Path $schemaReady -Force | Out-Null
    }
    $connectionData = [ordered]@{
        server='127.0.0.1'; port=$Port; database='clinic06_up04'; user='clinic06_app'; password=$auth.app
    }
    [IO.File]::WriteAllText($connection, ($connectionData | ConvertTo-Json), $utf8)
    Write-Host "База регистратуры доступна на 127.0.0.1:$Port"
}
finally {
    if ($locked) { $gate.ReleaseMutex() }
    $gate.Dispose()
}

if ($Action -eq 'Start') {
    if (!$App) { $App = Join-Path $root 'bin\Clinic06.exe' }
    if (!(Test-Path -LiteralPath $App)) { throw "Файл приложения не найден. Проверьте $App" }
    Start-Process -FilePath $App -ArgumentList ('"{0}"' -f $connection) -WorkingDirectory (Split-Path -Parent $App)
}
