-- Runs inside the freshly restored copy before it becomes the live UAT database.
-- uat-refresh.ps1 replaces $(CurrentDatabase) with the name of the database serving UAT right now
-- (it does not exist on the very first refresh) and $(AppLogin) with the UAT application's SQL Server login.

-- Owner and recovery model
DECLARE @sql nvarchar(max);
SET @sql = N'ALTER AUTHORIZATION ON DATABASE::' + QUOTENAME(DB_NAME()) + N' TO [sa];';
EXEC (@sql);
SET @sql = N'ALTER DATABASE ' + QUOTENAME(DB_NAME()) + N' SET RECOVERY SIMPLE;';
EXEC (@sql);
GO

-- Database users that PROD mapped to logins by SID point nowhere here; map them again by name
DECLARE @name sysname, @sql nvarchar(max);
DECLARE orphans CURSOR LOCAL FAST_FORWARD FOR
    SELECT dp.name
    FROM sys.database_principals dp
    WHERE dp.type IN ('S', 'U')
      AND dp.authentication_type IN (1, 3)
      AND dp.principal_id > 4
      AND NOT EXISTS (SELECT 1 FROM sys.server_principals sp WHERE sp.sid = dp.sid)
      AND EXISTS (SELECT 1 FROM sys.server_principals sp WHERE sp.name = dp.name);
OPEN orphans;
FETCH NEXT FROM orphans INTO @name;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'ALTER USER ' + QUOTENAME(@name) + N' WITH LOGIN = ' + QUOTENAME(@name) + N';';
    EXEC (@sql);
    FETCH NEXT FROM orphans INTO @name;
END
CLOSE orphans;
DEALLOCATE orphans;
GO

-- The UAT application's login gets a db_owner user (EF migrations need DDL rights)
DECLARE @login sysname = N'$(AppLogin)', @sql nvarchar(max);
IF @login <> N''
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @login)
        RAISERROR (N'Login %s does not exist on this SQL Server instance', 16, 1, @login);
    ELSE
    BEGIN
        IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @login)
        BEGIN
            SET @sql = N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login) + N';';
            EXEC (@sql);
        END
        SET @sql = N'ALTER ROLE db_owner ADD MEMBER ' + QUOTENAME(@login) + N';';
        EXEC (@sql);
    END
END
GO

-- PROD's own switches must not run on UAT: keep the values UAT had before this refresh,
-- or leave everything switched off on the very first refresh
UPDATE dbo.BackupSettings SET Enabled = 0, NextBackupAt = NULL;
UPDATE dbo.JiraSyncSettings SET Enabled = 0, NextRunAt = NULL;
DELETE FROM dbo.BackupHistory;
GO

DECLARE @current sysname = N'$(CurrentDatabase)', @sql nvarchar(max);
IF DB_ID(@current) IS NOT NULL
BEGIN
    SET @sql = N'
        UPDATE n SET
            n.Enabled = c.Enabled, n.BackupFolderPath = c.BackupFolderPath, n.ScheduleType = c.ScheduleType,
            n.IntervalHours = c.IntervalHours, n.DailyTime = c.DailyTime, n.WeeklyDay = c.WeeklyDay,
            n.RetentionCount = c.RetentionCount, n.RetentionDays = c.RetentionDays,
            n.RetentionMaxTotalGb = c.RetentionMaxTotalGb, n.VerifyAfterBackup = c.VerifyAfterBackup,
            n.LastBackupAt = c.LastBackupAt
        FROM dbo.BackupSettings n CROSS JOIN ' + QUOTENAME(@current) + N'.dbo.BackupSettings c;

        UPDATE n SET
            n.Enabled = c.Enabled, n.CycleCooldownMinutes = c.CycleCooldownMinutes,
            n.ServiceAccountUserName = c.ServiceAccountUserName, n.JiraTimeZoneId = c.JiraTimeZoneId,
            n.LabelsSyncEnabled = c.LabelsSyncEnabled
        FROM dbo.JiraSyncSettings n CROSS JOIN ' + QUOTENAME(@current) + N'.dbo.JiraSyncSettings c;';
    EXEC (@sql);
END
GO

-- Whoever administered UAT before the refresh still does afterwards
DECLARE @current sysname = N'$(CurrentDatabase)', @sql nvarchar(max);
IF DB_ID(@current) IS NOT NULL
BEGIN
    SET @sql = N'
        UPDATE n SET n.IsAdmin = 1, n.IsApproved = 1
        FROM dbo.AppUsers n
        JOIN ' + QUOTENAME(@current) + N'.dbo.AppUsers c ON c.WindowsUserName = n.WindowsUserName
        WHERE c.IsAdmin = 1;

        INSERT INTO dbo.AppUsers (WindowsUserName, DisplayName, SamAccountName, EmployeeId, EmailAddress,
                                  IsAdmin, IsApproved, IsAccessRequested, ApprovedAt, ApprovedBy, CreatedAt)
        SELECT c.WindowsUserName, c.DisplayName, c.SamAccountName, c.EmployeeId, c.EmailAddress,
               1, 1, 0, c.ApprovedAt, c.ApprovedBy, c.CreatedAt
        FROM ' + QUOTENAME(@current) + N'.dbo.AppUsers c
        WHERE c.IsAdmin = 1
          AND NOT EXISTS (SELECT 1 FROM dbo.AppUsers n WHERE n.WindowsUserName = c.WindowsUserName);';
    EXEC (@sql);
END
GO

-- Anything else UAT needs after a refresh (test accounts, switches, data masking) goes below this line.
