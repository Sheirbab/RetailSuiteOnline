-- SQL Script to delete demo-store tenant and related data
-- Run this in SQL Server Management Studio or via sqlcmd

-- Step 1: Get the demo-store tenant ID
DECLARE @TenantId UNIQUEIDENTIFIER
SELECT @TenantId = Id FROM [dbo].[Tenants] WHERE [Subdomain] = 'demo-store'

IF @TenantId IS NOT NULL
BEGIN
    PRINT 'Deleting demo-store tenant data...'
    PRINT 'Tenant ID: ' + CAST(@TenantId AS NVARCHAR(36))

    -- Delete related data in order of dependencies
    DELETE FROM [dbo].[InventoryTransactions] WHERE [TenantId] = @TenantId
    PRINT 'Deleted InventoryTransactions'

    DELETE FROM [dbo].[InventoryItems] WHERE [TenantId] = @TenantId
    PRINT 'Deleted InventoryItems'

    DELETE FROM [dbo].[JournalEntryLines] WHERE [TenantId] = @TenantId
    PRINT 'Deleted JournalEntryLines'

    DELETE FROM [dbo].[JournalEntries] WHERE [TenantId] = @TenantId
    PRINT 'Deleted JournalEntries'

    DELETE FROM [dbo].[OrderItems] WHERE [TenantId] = @TenantId
    PRINT 'Deleted OrderItems'

    DELETE FROM [dbo].[Orders] WHERE [TenantId] = @TenantId
    PRINT 'Deleted Orders'

    DELETE FROM [dbo].[Payments] WHERE [TenantId] = @TenantId
    PRINT 'Deleted Payments'

    DELETE FROM [dbo].[VariantAttributeValues] WHERE [TenantId] = @TenantId
    PRINT 'Deleted VariantAttributeValues'

    DELETE FROM [dbo].[ProductAttributeValues] WHERE [TenantId] = @TenantId
    PRINT 'Deleted ProductAttributeValues'

    DELETE FROM [dbo].[ProductAttributes] WHERE [TenantId] = @TenantId
    PRINT 'Deleted ProductAttributes'

    DELETE FROM [dbo].[ProductVariants] WHERE [TenantId] = @TenantId
    PRINT 'Deleted ProductVariants'

    DELETE FROM [dbo].[ProductCategories] WHERE [TenantId] = @TenantId
    PRINT 'Deleted ProductCategories'

    DELETE FROM [dbo].[Products] WHERE [TenantId] = @TenantId
    PRINT 'Deleted Products'

    DELETE FROM [dbo].[Categories] WHERE [TenantId] = @TenantId
    PRINT 'Deleted Categories'

    DELETE FROM [dbo].[Accounts] WHERE [TenantId] = @TenantId
    PRINT 'Deleted Accounts'

    DELETE FROM [dbo].[Users] WHERE [TenantId] = @TenantId
    PRINT 'Deleted Users'

    -- Finally delete the tenant
    DELETE FROM [dbo].[Tenants] WHERE [Id] = @TenantId
    PRINT 'Deleted Tenant: demo-store'

    PRINT ''
    PRINT 'SUCCESS: demo-store tenant deleted completely'
    PRINT 'The API will automatically reseed on startup'
END
ELSE
BEGIN
    PRINT 'Warning: demo-store tenant not found'
END
