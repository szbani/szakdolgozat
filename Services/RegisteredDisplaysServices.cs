using Microsoft.EntityFrameworkCore;
using szakdolgozat.Models;
using szakdolgozat.Interface;

namespace szakdolgozat.Services;

public class RegisteredDisplaysServices : IRegisteredDisplaysServices
{
    private readonly AppDbContext _context;
    
    public RegisteredDisplaysServices(AppDbContext context)
    {
        _context = context;
    }
    
    public int RegisterDisplay(DisplayModel dto)
    {
        _context.Displays.Add(dto);
        return _context.SaveChanges();
    }
    
    public async Task<DisplayModel[]> GetRegisteredDisplaysAsync()
    {
        return await _context.Displays.ToArrayAsync();
    }
    
    public DisplayModel[] GetRegisteredDisplays()
    {
        return _context.Displays.ToArray();
    }
    
    public async Task<int> ModifyRegisteredDisplay(DisplayModel dto)
    {
        var display = await _context.Displays.FirstOrDefaultAsync(x => x.Id == dto.Id);
        if (display == null)
        {
            return 0;
        }
        display.DisplayName = dto.DisplayName;
        display.DisplayDescription = dto.DisplayDescription;
        if (dto.macAddress != null)
        {
            display.macAddress = dto.macAddress;
        }

        return await _context.SaveChangesAsync();
    }
    
    public Task RemoveRegisteredDisplay(Guid id)
    {
        var display = _context.Displays.FirstOrDefault(x => x.Id == id);
        if (display == null)
        {
            return Task.FromException(new Exception("No display found with the given ID."));
        }
        _context.Displays.Remove(display);
        return _context.SaveChangesAsync();
    }
    
}