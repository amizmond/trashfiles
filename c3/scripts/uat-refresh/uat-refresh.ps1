<#
.SYNOPSIS
    Refreshes the UAT Estimation database from the newest PROD backup in a drop folder.

.DESCRIPTION
    Runs unattended on the UAT server from Task Scheduler. One run:
      1. Takes the newest backup file in SourceFolder that is no longer being written to.
      2. Stops when that file was restored already (state.json), unless -Force is given.
      3. Copies it to WorkFolder\backups with robocopy and verifies it with RESTORE VERIFYONLY.
      4. Restores it as <DatabaseName>_restore next to the live database; files go to DataFolder and LogFolder.
      5. Refuses to go on when the backup carries EF migrations this UAT build does not know.
      6. Runs uat-post-restore.sql inside the restored copy: owner, recovery model, logins, UAT settings, UAT admins.
      7. Stops the IIS app pool, renames live -> <DatabaseName>_previous and the copy -> live, starts the app pool.
      8. Keeps _previous for a rollback until the next run, or drops it right away with -DropPrevious.
    A file named freeze.txt in WorkFolder pauses refreshes. -CheckOnly reports what would happen and changes nothing.
    Exit code 0 means refreshed or nothing to do, 1 means failed. A failure never leaves the live database broken.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File D:\SqlRefresh\uat-refresh.ps1 -SourceFolder \\UAT1\SqlTransfer -AppLogin "CORP\svc-estimation-uat"
#>
[CmdletBinding()]
param(
    [string]$SourceFolder = '\\UAT-SERVER\SqlTransfer',
    [string]$FilePattern = '*.bak',
    [string]$WorkFolder = 'D:\SqlRefresh',
    [string]$SqlInstance = '.',
    [string]$DatabaseName = 'db_estimation',
    [string]$DataFolder = '',
    [string]$LogFolder = '',
    [string]$AppPoolName = 'Estimation',
    [string]$AppLogin = '',
    [string]$PostRestoreScript = '',
    [string]$WebhookUrl = '',
    [int]$MinFileAgeMinutes = 15,
    [int]$KeepLocalCopies = 2,
    [switch]$DropPrevious,
    [switch]$Force,
    [switch]$CheckOnly
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if ($PostRestoreScript -eq '') { $PostRestoreScript = Join-Path $PSScriptRoot 'uat-post-restore.sql' }

$restoreDb = $DatabaseName + '_restore'
$previousDb = $DatabaseName + '_previous'
$backupsFolder = Join-Path $WorkFolder 'backups'
$logsFolder = Join-Path $WorkFolder 'logs'
$stateFile = Join-Path $WorkFolder 'state.json'
$freezeFile = Join-Path $WorkFolder 'freeze.txt'
$logFile = Join-Path $logsFolder ('uat-refresh-{0:yyyyMM}.log' -f (Get-Date))
$script:exitCode = 1

foreach ($folder in @($WorkFolder, $backupsFolder, $logsFolder)) {
    if (-not (Test-Path -LiteralPath $folder)) { New-Item -ItemType Directory -Path $folder | Out-Null }
}

function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $line = '{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}' -f (Get-Date), $Level, $Message
    switch ($Level) {
        'ERROR' { Write-Host $line -ForegroundColor Red }
        'WARN' { Write-Host $line -ForegroundColor Yellow }
        default { Write-Host $line }
    }
    Add-Content -LiteralPath $logFile -Value $line -Encoding UTF8
}

function Get-SqlName([string]$Name) { return '[' + $Name.Replace(']', ']]') + ']' }

function Get-SqlString([string]$Value) { return "N'" + $Value.Replace("'", "''") + "'" }

function Invoke-Sql {
    param([string]$Sql, [string]$Database = 'master', [hashtable]$Parameters = @{}, [int]$TimeoutSeconds = 600, [switch]$Query)
    $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
    $builder['Data Source'] = $SqlInstance
    $builder['Initial Catalog'] = $Database
    $builder['Integrated Security'] = $true
    $builder['Application Name'] = 'UAT refresh'
    $builder['Connect Timeout'] = 30
    $connection = New-Object System.Data.SqlClient.SqlConnection $builder.ConnectionString
    $connection.add_InfoMessage([System.Data.SqlClient.SqlInfoMessageEventHandler]{
        param($sender, $e)
        foreach ($info in $e.Errors) { Write-Log ('SQL: ' + $info.Message) }
    })
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        $command.CommandTimeout = $TimeoutSeconds
        foreach ($key in $Parameters.Keys) { [void]$command.Parameters.AddWithValue('@' + $key, $Parameters[$key]) }
        if ($Query) {
            $table = New-Object System.Data.DataTable
            $reader = $command.ExecuteReader()
            try { $table.Load($reader) } finally { $reader.Close() }
            return ,$table
        }
        [void]$command.ExecuteNonQuery()
    }
    finally {
        $connection.Dispose()
    }
}

function Test-Database([string]$Name) {
    $table = Invoke-Sql -Query -Sql 'SELECT CASE WHEN DB_ID(@name) IS NULL THEN 0 ELSE 1 END AS Present' -Parameters @{ name = $Name }
    return ([int]$table.Rows[0].Present -eq 1)
}

function Remove-Database([string]$Name) {
    if (-not (Test-Database $Name)) { return }
    Write-Log "Dropping database $Name"
    $quoted = Get-SqlName $Name
    Invoke-Sql -Sql "ALTER DATABASE $quoted SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE $quoted;"
}

function Rename-Database([string]$From, [string]$To) {
    Write-Log "Renaming database $From -> $To"
    $source = Get-SqlName $From
    $target = Get-SqlName $To
    Invoke-Sql -Sql "ALTER DATABASE $source SET SINGLE_USER WITH ROLLBACK IMMEDIATE; ALTER DATABASE $source MODIFY NAME = $target; ALTER DATABASE $target SET MULTI_USER;"
}

function Set-AppPool([string]$State) {
    if ($AppPoolName -eq '') { return }
    Import-Module WebAdministration
    if ((Get-WebAppPoolState -Name $AppPoolName).Value -eq $State) { return }
    Write-Log "App pool $AppPoolName -> $State"
    if ($State -eq 'Stopped') { Stop-WebAppPool -Name $AppPoolName } else { Start-WebAppPool -Name $AppPoolName }
    $deadline = (Get-Date).AddSeconds(90)
    while ((Get-WebAppPoolState -Name $AppPoolName).Value -ne $State) {
        if ((Get-Date) -gt $deadline) { throw "App pool $AppPoolName did not reach state $State within 90 seconds" }
        Start-Sleep -Seconds 2
    }
}

function Test-FileClosed([string]$Path) {
    try {
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None)
        $stream.Close()
        return $true
    }
    catch {
        Write-Log "Cannot open $Path exclusively: $($_.Exception.Message)" 'WARN'
        return $false
    }
}

function Assert-FreeSpace([hashtable]$BytesPerFolder) {
    $perRoot = @{}
    foreach ($folder in $BytesPerFolder.Keys) {
        $root = [System.IO.Path]::GetPathRoot([System.IO.Path]::GetFullPath($folder))
        if ($root -like '\\*') { continue }
        if (-not $perRoot.ContainsKey($root)) { $perRoot[$root] = [int64]0 }
        $perRoot[$root] += [int64]$BytesPerFolder[$folder]
    }
    foreach ($root in $perRoot.Keys) {
        $free = (New-Object System.IO.DriveInfo $root).AvailableFreeSpace
        Write-Log ('{0} has {1:N1} GB free, {2:N1} GB needed' -f $root, ($free / 1GB), ($perRoot[$root] / 1GB))
        if ($free -lt $perRoot[$root]) { throw "Not enough free space on $root" }
    }
}

function Read-State {
    $state = @{ LastRestoredFile = ''; LastRestoredBackupFinish = ''; LastRunUtc = ''; LastResult = ''; LastMessage = '' }
    if (Test-Path -LiteralPath $stateFile) {
        $json = Get-Content -LiteralPath $stateFile -Raw
        if ($json -and $json.Trim() -ne '') {
            $saved = $json | ConvertFrom-Json
            foreach ($property in $saved.PSObject.Properties) { $state[$property.Name] = [string]$property.Value }
        }
    }
    return $state
}

function Save-State([hashtable]$State) {
    $State | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding UTF8
}

function Send-Notice([string]$Text) {
    if ($WebhookUrl -eq '') { return }
    try {
        [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
        $body = [System.Text.Encoding]::UTF8.GetBytes((@{ text = $Text } | ConvertTo-Json -Compress))
        Invoke-RestMethod -Method Post -Uri $WebhookUrl -ContentType 'application/json; charset=utf-8' -Body $body | Out-Null
    }
    catch {
        Write-Log "Webhook call failed: $($_.Exception.Message)" 'WARN'
    }
}

function Invoke-Refresh {
    $mutex = $null
    $acquired = $false
    $state = $null
    $appPoolStopped = $false
    $swapped = $false

    try {
        Write-Log "=== UAT refresh started: $SourceFolder -> $SqlInstance $DatabaseName ==="
        $mutex = New-Object System.Threading.Mutex($false, 'Global\EstimationUatRefresh')
        try { $acquired = $mutex.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) {
            Write-Log 'Another refresh is still running; nothing to do'
            $script:exitCode = 0
            return
        }

        $state = Read-State
        if ($state.LastRestoredFile -ne '') {
            Write-Log "Last restored: $($state.LastRestoredFile), PROD backup finished $($state.LastRestoredBackupFinish)"
        }
        if ((Test-Path -LiteralPath $freezeFile) -and -not $Force) {
            Write-Log "Refreshes are frozen because $freezeFile exists; nothing to do"
            $script:exitCode = 0
            return
        }

        if (-not (Test-Path -LiteralPath $SourceFolder)) { throw "Source folder $SourceFolder is not reachable" }
        $candidates = @(Get-ChildItem -LiteralPath $SourceFolder -Filter $FilePattern -File | Sort-Object LastWriteTimeUtc -Descending)
        if ($candidates.Count -eq 0) {
            Write-Log "No $FilePattern files in $SourceFolder; nothing to do"
            $script:exitCode = 0
            return
        }
        $newest = $candidates[0]
        Write-Log ('Newest backup: {0}, {1:N2} GB, written {2:yyyy-MM-dd HH:mm:ss} UTC' -f $newest.Name, ($newest.Length / 1GB), $newest.LastWriteTimeUtc)
        if ($newest.Name -eq $state.LastRestoredFile -and -not $Force) {
            Write-Log 'It was restored already; nothing to do'
            $script:exitCode = 0
            return
        }
        if ($newest.LastWriteTimeUtc -gt (Get-Date).ToUniversalTime().AddMinutes(-$MinFileAgeMinutes)) {
            Write-Log "It was written less than $MinFileAgeMinutes minutes ago; trying again next run"
            $script:exitCode = 0
            return
        }
        if (-not (Test-FileClosed $newest.FullName)) {
            Write-Log 'It is still open on the source side; trying again next run'
            $script:exitCode = 0
            return
        }
        if ($CheckOnly) {
            Write-Log "Check only: $($newest.Name) would be restored onto $DatabaseName"
            $script:exitCode = 0
            return
        }

        $instance = (Invoke-Sql -Query -Sql "SELECT ISNULL(CAST(SERVERPROPERTY('ProductMajorVersion') AS int), 0) AS Major, CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(500)) AS DataPath, CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(500)) AS LogPath").Rows[0]
        if ($DataFolder -eq '') { $DataFolder = [string]$instance.DataPath }
        if ($LogFolder -eq '') { $LogFolder = [string]$instance.LogPath }
        if ($DataFolder -eq '' -or $LogFolder -eq '') { throw 'The instance has no default data or log folder; pass -DataFolder and -LogFolder' }
        if ($AppPoolName -ne '') {
            Import-Module WebAdministration
            Write-Log "App pool $AppPoolName is $((Get-WebAppPoolState -Name $AppPoolName).Value)"
        }
        if (-not (Test-Path -LiteralPath $PostRestoreScript)) { throw "Post-restore script $PostRestoreScript not found" }

        $localPath = Join-Path $backupsFolder $newest.Name
        $needed = @{}
        $needed[$backupsFolder] = $newest.Length
        Assert-FreeSpace $needed
        Write-Log "Copying to $localPath"
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $sourceArg = $SourceFolder.TrimEnd('\')
        if ($sourceArg.EndsWith(':')) { $sourceArg += '\' }
        $robocopyOutput = & robocopy.exe $sourceArg $backupsFolder $newest.Name /Z /J /R:3 /W:30 /NP /NJH /NJS /NDL /NFL
        $robocopyExit = $LASTEXITCODE
        foreach ($line in @($robocopyOutput)) { if ($line -and $line.Trim() -ne '') { Write-Log ('robocopy: ' + $line.Trim()) } }
        if ($robocopyExit -ge 8) { throw "robocopy failed with exit code $robocopyExit" }
        $newest.Refresh()
        $localFile = Get-Item -LiteralPath $localPath
        if ($localFile.Length -ne $newest.Length) { throw "The copy is $($localFile.Length) bytes but the source is $($newest.Length) bytes; the source file is still changing" }
        Write-Log ('Copied in {0:N0} s' -f $stopwatch.Elapsed.TotalSeconds)

        $header = (Invoke-Sql -Query -Sql 'RESTORE HEADERONLY FROM DISK = @path' -Parameters @{ path = $localPath }).Rows[0]
        if ([int]$header.BackupType -ne 1) { throw "$($newest.Name) is not a full database backup (BackupType $($header.BackupType))" }
        $backupFinish = [datetime]$header.BackupFinishDate
        Write-Log ('Full backup of {0}, finished {1:yyyy-MM-dd HH:mm:ss}, SQL Server version {2}, copy-only {3}, checksums {4}' -f $header.DatabaseName, $backupFinish, $header.SoftwareVersionMajor, $header.IsCopyOnly, $header.HasBackupChecksums)
        if ($state.LastRestoredBackupFinish -ne '' -and -not $Force) {
            $lastFinish = [datetime]::Parse($state.LastRestoredBackupFinish, [System.Globalization.CultureInfo]::InvariantCulture)
            if ($backupFinish -le $lastFinish) {
                Write-Log 'This backup is not newer than the last restored one; nothing to do'
                $script:exitCode = 0
                return
            }
        }
        if ([int]$instance.Major -gt 0 -and [int]$header.SoftwareVersionMajor -gt [int]$instance.Major) {
            throw "The backup comes from SQL Server version $($header.SoftwareVersionMajor) but this instance is version $($instance.Major); a backup cannot be restored to an older version"
        }

        $verifySql = 'RESTORE VERIFYONLY FROM DISK = @path'
        if ([bool]$header.HasBackupChecksums) { $verifySql += ' WITH CHECKSUM' }
        Invoke-Sql -Sql $verifySql -Parameters @{ path = $localPath } -TimeoutSeconds 0
        Write-Log 'Backup file verified'

        $stamp = '{0:yyyyMMdd_HHmmss}' -f (Get-Date)
        $moves = @()
        $needed = @{}
        foreach ($file in (Invoke-Sql -Query -Sql 'RESTORE FILELISTONLY FROM DISK = @path' -Parameters @{ path = $localPath }).Rows) {
            $folder = $DataFolder
            if ([string]$file.Type -eq 'L') { $folder = $LogFolder }
            $extension = [System.IO.Path]::GetExtension([string]$file.PhysicalName)
            $safeName = ([string]$file.LogicalName) -replace '[^\w\-.]', '_'
            $target = Join-Path $folder ('{0}_{1}_{2}{3}' -f $DatabaseName, $stamp, $safeName, $extension)
            $moves += 'MOVE ' + (Get-SqlString ([string]$file.LogicalName)) + ' TO ' + (Get-SqlString $target)
            if (-not $needed.ContainsKey($folder)) { $needed[$folder] = [int64]0 }
            $needed[$folder] += [int64]$file.Size
            Write-Log "  $($file.LogicalName) -> $target"
        }
        Assert-FreeSpace $needed

        Remove-Database $restoreDb
        Remove-Database $previousDb
        Write-Log "Restoring $($newest.Name) as $restoreDb"
        $stopwatch.Restart()
        Invoke-Sql -Sql ('RESTORE DATABASE ' + (Get-SqlName $restoreDb) + ' FROM DISK = @path WITH ' + ($moves -join ', ') + ', RECOVERY, STATS = 10') -Parameters @{ path = $localPath } -TimeoutSeconds 0
        Write-Log ('Restored in {0:N0} s' -f $stopwatch.Elapsed.TotalSeconds)

        if (Test-Database $DatabaseName) {
            $restoreHistory = (Get-SqlName $restoreDb) + '.dbo.__EFMigrationsHistory'
            $liveHistory = (Get-SqlName $DatabaseName) + '.dbo.__EFMigrationsHistory'
            $unknown = @((Invoke-Sql -Query -Sql "SELECT MigrationId FROM $restoreHistory EXCEPT SELECT MigrationId FROM $liveHistory").Rows | ForEach-Object { [string]$_.MigrationId })
            if ($unknown.Count -gt 0) { throw "The backup contains EF migrations this UAT build does not know ($($unknown -join ', ')); deploy a newer build to UAT first" }
            $pending = @((Invoke-Sql -Query -Sql "SELECT MigrationId FROM $liveHistory EXCEPT SELECT MigrationId FROM $restoreHistory").Rows).Count
            if ($pending -gt 0) { Write-Log "The UAT application will apply $pending pending migration(s) when it starts" }
        }

        Write-Log "Running $PostRestoreScript inside $restoreDb"
        $sqlText = (Get-Content -LiteralPath $PostRestoreScript -Raw).Replace('$(CurrentDatabase)', $DatabaseName.Replace("'", "''")).Replace('$(AppLogin)', $AppLogin.Replace("'", "''"))
        foreach ($batch in ($sqlText -split '(?im)^\s*GO\s*$')) {
            if ($batch.Trim() -ne '') { Invoke-Sql -Database $restoreDb -Sql $batch }
        }

        Set-AppPool 'Stopped'
        $appPoolStopped = $true
        if (Test-Database $DatabaseName) { Rename-Database $DatabaseName $previousDb }
        Rename-Database $restoreDb $DatabaseName
        $swapped = $true
        Set-AppPool 'Started'
        $appPoolStopped = $false
        if ($DropPrevious) { Remove-Database $previousDb }

        Get-ChildItem -LiteralPath $backupsFolder -Filter $FilePattern -File | Sort-Object LastWriteTimeUtc -Descending | Select-Object -Skip $KeepLocalCopies | ForEach-Object {
            Write-Log "Deleting old local copy $($_.Name)"
            Remove-Item -LiteralPath $_.FullName
        }

        $state.LastRestoredFile = $newest.Name
        $state.LastRestoredBackupFinish = $backupFinish.ToString('o')
        $state.LastRunUtc = (Get-Date).ToUniversalTime().ToString('o')
        $state.LastResult = 'Success'
        $state.LastMessage = "$DatabaseName restored from $($newest.Name)"
        Save-State $state
        Send-Notice ('UAT refresh done on {0}: {1}, PROD backup finished {2:yyyy-MM-dd HH:mm}' -f $env:COMPUTERNAME, $state.LastMessage, $backupFinish)
        $script:exitCode = 0
    }
    catch {
        $message = $_.Exception.Message
        Write-Log "FAILED: $message" 'ERROR'
        Write-Log $_.ScriptStackTrace 'ERROR'
        if (-not $swapped) {
            try {
                if (-not (Test-Database $DatabaseName) -and (Test-Database $previousDb)) { Rename-Database $previousDb $DatabaseName }
                Remove-Database $restoreDb
            }
            catch {
                Write-Log "Clean-up after the failure failed too: $($_.Exception.Message)" 'ERROR'
            }
        }
        if ($state -ne $null) {
            $state.LastRunUtc = (Get-Date).ToUniversalTime().ToString('o')
            $state.LastResult = 'Failed'
            $state.LastMessage = $message
            Save-State $state
        }
        Send-Notice ('UAT refresh FAILED on {0}: {1}' -f $env:COMPUTERNAME, $message)
        $script:exitCode = 1
    }
    finally {
        if ($appPoolStopped) {
            try { Set-AppPool 'Started' } catch { Write-Log "Could not start app pool ${AppPoolName}: $($_.Exception.Message)" 'ERROR' }
        }
        if ($acquired) { $mutex.ReleaseMutex() }
        if ($mutex -ne $null) { $mutex.Dispose() }
        Write-Log "=== UAT refresh finished, exit code $($script:exitCode) ==="
    }
}

Invoke-Refresh
exit $script:exitCode
