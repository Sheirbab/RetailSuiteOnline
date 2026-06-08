using Microsoft.EntityFrameworkCore;
using RetailSuite.Infrastructure.Exceptions;
using RetailSuite.Infrastructure.Modules.Locations.Entities;
using RetailSuite.Shared;

namespace RetailSuite.Infrastructure.Modules.Locations.Services;

public interface ILocationService
{
    Task<Location> CreateAsync(string code, string name, string? address, string? phone, string? notes, bool makeDefault);
    Task<Location> UpdateAsync(Guid id, string? name, string? address, string? phone, string? notes, bool? isActive);
    Task SetDefaultAsync(Guid id);
    Task<Location?> GetDefaultAsync();
}

public class LocationService : ILocationService
{
    private readonly RetailDbContext _db;
    private readonly ITenantContext _tenantContext;

    public LocationService(RetailDbContext db, ITenantContext tenantContext)
    {
        _db            = db;
        _tenantContext = tenantContext;
    }

    public async Task<Location> CreateAsync(
        string code, string name,
        string? address, string? phone, string? notes,
        bool makeDefault)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new UnauthorizedAccessException("Tenant context missing.");

        var normalisedCode = code.Trim().ToUpperInvariant();
        var exists = await _db.Locations.AnyAsync(l => l.Code == normalisedCode);
        if (exists)
            throw new BusinessRuleException($"A location with code '{normalisedCode}' already exists.");

        // If this is being made the default OR there's no default yet, take the slot.
        var existingDefault = await _db.Locations.FirstOrDefaultAsync(l => l.IsDefault);
        var shouldBeDefault = makeDefault || existingDefault == null;

        var loc = new Location(tenantId, code, name, shouldBeDefault);
        loc.UpdateContact(address, phone, notes);

        if (shouldBeDefault && existingDefault != null)
            existingDefault.SetDefault(false);

        _db.Locations.Add(loc);
        await _db.SaveChangesAsync();
        return loc;
    }

    public async Task<Location> UpdateAsync(
        Guid id,
        string? name, string? address, string? phone, string? notes,
        bool? isActive)
    {
        var loc = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id)
            ?? throw new NotFoundException("Location", id);

        if (!string.IsNullOrWhiteSpace(name)) loc.Rename(name);
        if (address != null || phone != null || notes != null)
            loc.UpdateContact(address ?? loc.Address, phone ?? loc.Phone, notes ?? loc.Notes);

        if (isActive.HasValue)
        {
            if (!isActive.Value && loc.IsDefault)
                throw new BusinessRuleException("Cannot deactivate the default location. Set another location as default first.");

            if (isActive.Value) loc.Activate();
            else                loc.Deactivate();
        }

        await _db.SaveChangesAsync();
        return loc;
    }

    public async Task SetDefaultAsync(Guid id)
    {
        var newDefault = await _db.Locations.FirstOrDefaultAsync(l => l.Id == id)
            ?? throw new NotFoundException("Location", id);

        if (!newDefault.IsActive)
            throw new BusinessRuleException("Cannot make an inactive location the default.");

        var currentDefault = await _db.Locations.FirstOrDefaultAsync(l => l.IsDefault && l.Id != id);
        currentDefault?.SetDefault(false);
        newDefault.SetDefault(true);

        await _db.SaveChangesAsync();
    }

    public Task<Location?> GetDefaultAsync()
        => _db.Locations.FirstOrDefaultAsync(l => l.IsDefault && l.IsActive);
}
