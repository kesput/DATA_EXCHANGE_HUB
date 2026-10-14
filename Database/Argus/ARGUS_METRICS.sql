IF OBJECT_ID('dbo.process_metrics', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.process_metrics
    (
        process_metric_id bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_process_metrics PRIMARY KEY CLUSTERED,
        [timestamp] datetime2 NOT NULL,
        server_name nvarchar(128) NOT NULL,
        process_name nvarchar(256) NULL,
        pid int NULL,
        cpu_percent decimal(18, 2) NULL,
        memory_mb decimal(18, 2) NULL,
        command_line nvarchar(max) NULL,
        start_time datetime2 NULL,
        runtime_seconds bigint NULL
    );
END;

IF OBJECT_ID('dbo.server_metrics', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.server_metrics
    (
        server_metric_id bigint IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_server_metrics PRIMARY KEY CLUSTERED,
        [timestamp] datetime2 NOT NULL,
        server_name nvarchar(128) NOT NULL,
        cpu_percent decimal(18, 2) NULL,
        memory_used_percent decimal(18, 2) NULL,
        memory_used_gb decimal(18, 2) NULL,
        memory_available_gb decimal(18, 2) NULL,
        disk_name nvarchar(64) NULL,
        disk_used_percent decimal(18, 2) NULL,
        disk_free_gb decimal(18, 2) NULL,
        disk_read_mb_sec decimal(18, 2) NULL,
        disk_write_mb_sec decimal(18, 2) NULL,
        network_in_mbps decimal(18, 2) NULL,
        network_out_mbps decimal(18, 2) NULL,
        uptime_minutes bigint NULL,
        top_cpu_processes_json nvarchar(max) NULL,
        top_memory_processes_json nvarchar(max) NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_server_metrics_server_timestamp' AND object_id = OBJECT_ID('dbo.server_metrics'))
    CREATE INDEX IX_server_metrics_server_timestamp ON dbo.server_metrics (server_name, [timestamp]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_server_metrics_timestamp' AND object_id = OBJECT_ID('dbo.server_metrics'))
    CREATE INDEX IX_server_metrics_timestamp ON dbo.server_metrics ([timestamp]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_server_metrics_snapshot_disk' AND object_id = OBJECT_ID('dbo.server_metrics'))
    CREATE UNIQUE INDEX UQ_server_metrics_snapshot_disk ON dbo.server_metrics (server_name, [timestamp], disk_name);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_process_metrics_server_process_timestamp' AND object_id = OBJECT_ID('dbo.process_metrics'))
    CREATE INDEX IX_process_metrics_server_process_timestamp ON dbo.process_metrics (server_name, process_name, [timestamp]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_process_metrics_timestamp_cpu' AND object_id = OBJECT_ID('dbo.process_metrics'))
    CREATE INDEX IX_process_metrics_timestamp_cpu ON dbo.process_metrics ([timestamp], cpu_percent DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_process_metrics_timestamp_memory' AND object_id = OBJECT_ID('dbo.process_metrics'))
    CREATE INDEX IX_process_metrics_timestamp_memory ON dbo.process_metrics ([timestamp], memory_mb DESC);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_process_metrics_snapshot_pid' AND object_id = OBJECT_ID('dbo.process_metrics'))
    CREATE UNIQUE INDEX UQ_process_metrics_snapshot_pid ON dbo.process_metrics (server_name, [timestamp], pid);
