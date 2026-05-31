-- Fix migration history by marking existing migrations as applied
-- This resolves the issue where tables exist but migrations aren't recorded

-- First, ensure __EFMigrationsHistory exists
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[__EFMigrationsHistory]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END

-- Check and insert missing migration records
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20251122204920_init')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251122204920_init', N'8.0.0');
END

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20251216165057_pagelockadded')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251216165057_pagelockadded', N'8.0.0');
END

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20251216170000_AddMasterPasswordTable')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251216170000_AddMasterPasswordTable', N'8.0.0');
END

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20251216180000_AddUsersTableFinal')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251216180000_AddUsersTableFinal', N'8.0.0');
END

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20251216204243_AddUserTrackingFields')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251216204243_AddUserTrackingFields', N'8.0.0');
END

-- Now apply only the ThemeSettings migration
-- Check if ThemeSettings table exists
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[ThemeSettings]') AND type in (N'U'))
BEGIN
    -- Create ThemeSettings table
    CREATE TABLE [ThemeSettings] (
        [Id] int NOT NULL IDENTITY,
        [ThemeMode] nvarchar(20) NOT NULL,
        [PrimaryColor] nvarchar(7) NOT NULL,
        [SecondaryColor] nvarchar(7) NOT NULL,
        [SuccessColor] nvarchar(7) NOT NULL,
        [DangerColor] nvarchar(7) NOT NULL,
        [WarningColor] nvarchar(7) NOT NULL,
        [InfoColor] nvarchar(7) NOT NULL,
        [BackgroundColor] nvarchar(7) NOT NULL,
        [TextColor] nvarchar(7) NOT NULL,
        [CardBackgroundColor] nvarchar(7) NOT NULL,
        [NavbarBackgroundColor] nvarchar(7) NOT NULL,
        [SidebarBackgroundColor] nvarchar(7) NOT NULL,
        [FooterBackgroundColor] nvarchar(7) NOT NULL,
        [LastUpdated] datetime2 NOT NULL,
        [UpdatedBy] nvarchar(100) NULL,
        [IsActive] bit NOT NULL,
        CONSTRAINT [PK_ThemeSettings] PRIMARY KEY ([Id])
    );

    -- Insert default Light theme
    INSERT INTO [ThemeSettings] (
        [ThemeMode], [PrimaryColor], [SecondaryColor], [SuccessColor],
        [DangerColor], [WarningColor], [InfoColor], [BackgroundColor],
        [TextColor], [CardBackgroundColor], [NavbarBackgroundColor],
        [SidebarBackgroundColor], [FooterBackgroundColor],
        [LastUpdated], [IsActive]
    )
    VALUES (
        N'Light', N'#0d6efd', N'#6c757d', N'#198754',
        N'#dc3545', N'#ffc107', N'#0dcaf0', N'#ffffff',
        N'#212529', N'#ffffff', N'#ffffff',
        N'#ffffff', N'#f8f9fa',
        GETDATE(), 1
    );
END

-- Check if LockMode column exists in PageLocks
IF NOT EXISTS (
    SELECT * FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[PageLocks]')
    AND name = 'LockMode'
)
BEGIN
    ALTER TABLE [PageLocks]
    ADD [LockMode] nvarchar(20) NOT NULL DEFAULT 'Locked';
END

-- Mark the AddThemeSettings migration as applied
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20251218174557_AddThemeSettings')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20251218174557_AddThemeSettings', N'8.0.0');
END

PRINT 'Migration history updated successfully!';
PRINT 'ThemeSettings table created (if it did not exist).';
PRINT 'You can now run your application!';
