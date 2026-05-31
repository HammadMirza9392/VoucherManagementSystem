-- Add user tracking fields to Banks table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Banks]') AND name = 'CreatedBy')
BEGIN
    ALTER TABLE [Banks] ADD [CreatedBy] NVARCHAR(100) NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Banks]') AND name = 'UpdatedDate')
BEGIN
    ALTER TABLE [Banks] ADD [UpdatedDate] DATETIME2 NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Banks]') AND name = 'UpdatedBy')
BEGIN
    ALTER TABLE [Banks] ADD [UpdatedBy] NVARCHAR(100) NULL;
END

-- Add user tracking fields to Customers table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Customers]') AND name = 'CreatedBy')
BEGIN
    ALTER TABLE [Customers] ADD [CreatedBy] NVARCHAR(100) NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Customers]') AND name = 'UpdatedDate')
BEGIN
    ALTER TABLE [Customers] ADD [UpdatedDate] DATETIME2 NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Customers]') AND name = 'UpdatedBy')
BEGIN
    ALTER TABLE [Customers] ADD [UpdatedBy] NVARCHAR(100) NULL;
END

-- Add user tracking fields to Items table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Items]') AND name = 'CreatedBy')
BEGIN
    ALTER TABLE [Items] ADD [CreatedBy] NVARCHAR(100) NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Items]') AND name = 'UpdatedDate')
BEGIN
    ALTER TABLE [Items] ADD [UpdatedDate] DATETIME2 NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Items]') AND name = 'UpdatedBy')
BEGIN
    ALTER TABLE [Items] ADD [UpdatedBy] NVARCHAR(100) NULL;
END

-- Add user tracking fields to Projects table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Projects]') AND name = 'CreatedBy')
BEGIN
    ALTER TABLE [Projects] ADD [CreatedBy] NVARCHAR(100) NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Projects]') AND name = 'UpdatedDate')
BEGIN
    ALTER TABLE [Projects] ADD [UpdatedDate] DATETIME2 NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Projects]') AND name = 'UpdatedBy')
BEGIN
    ALTER TABLE [Projects] ADD [UpdatedBy] NVARCHAR(100) NULL;
END

-- Add user tracking fields to ExpenseHeads table
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[ExpenseHeads]') AND name = 'CreatedBy')
BEGIN
    ALTER TABLE [ExpenseHeads] ADD [CreatedBy] NVARCHAR(100) NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[ExpenseHeads]') AND name = 'UpdatedDate')
BEGIN
    ALTER TABLE [ExpenseHeads] ADD [UpdatedDate] DATETIME2 NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[ExpenseHeads]') AND name = 'UpdatedBy')
BEGIN
    ALTER TABLE [ExpenseHeads] ADD [UpdatedBy] NVARCHAR(100) NULL;
END

-- Add UpdatedDate and UpdatedBy to Vouchers table (CreatedBy already exists)
IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Vouchers]') AND name = 'UpdatedDate')
BEGIN
    ALTER TABLE [Vouchers] ADD [UpdatedDate] DATETIME2 NULL;
END

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Vouchers]') AND name = 'UpdatedBy')
BEGIN
    ALTER TABLE [Vouchers] ADD [UpdatedBy] NVARCHAR(100) NULL;
END

PRINT 'User tracking fields added successfully to all tables!';

ALTER TABLE PageLocks ADD LockMode nvarchar(20) NOT NULL DEFAULT 'JustView';
GO
INSERT INTO MasterPasswords (PasswordType, Password)
VALUES ('MasterLock', 'admin123');

INSERT INTO Users
(
    Username,
    Password,
    FullName,
    Email,
    Phone,
    Role,
    IsActive,
    CreatedDate,
    LastLoginDate,
    CreatedBy
)
VALUES
(
    'admin',
    'admin123',
    'System Administrator',
    'admin@system.com',
    NULL,
    'Admin',
    1,
    '2025-12-18 21:33:39.5019905',
    NULL,
    'system'
);

--Steps to Complete the Implementation
--Run the SQL Script:
--Open SQL Server Management Studio (SSMS) or your preferred SQL client
--Connect to your database
--Open the file AddUserTrackingFieldsScript.sql (located in your project root)
--Execute the script
--This script will safely add the new columns to your database tables without affecting your existing data. It includes checks to ensure columns aren't added if they already exist.
--Update the Entity Framework Migration History (to keep EF in sync): After running the SQL script, you need to tell Entity Framework that a migration was applied. Run this command:
--dotnet ef migrations add AddUserTrackingFields --output-dir Migrations
--dotnet ef migrations script --idempotent
--Then manually add a record to your __EFMigrationsHistory table to mark this migration as applied.