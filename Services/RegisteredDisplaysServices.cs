using Microsoft.EntityFrameworkCore;
using szakdolgozat.DBContext;
using szakdolgozat.DBContext.Models;
using szakdolgozat.Interface;

namespace szakdolgozat.Controllers;

public class RegisteredDisplaysServices : IRegisteredDisplaysServices
{
    private AppDbContext _context;
    
    public RegisteredDisplaysServices(AppDbContext context)
    {
        _context = context;
    }
    
    public int RegisterDisplay(DisplayModel dto)
    {
        _context.displays.Add(dto);
        return _context.SaveChanges();
    }
    
    public async Task<DisplayModel[]> GetRegisteredDisplaysAsync()
    {
        return await _context.displays.ToArrayAsync();
    }
    
    public DisplayModel[] GetRegisteredDisplays()
    {
        return _context.displays.ToArray();
    }
    
    public async Task<int> ModifyRegisteredDisplay(DisplayModel dto)
    {
        var display = await _context.displays.FirstOrDefaultAsync(x => x.Id == dto.Id);
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

    
    public int RemoveRegisteredDisplay(int id)
    {
        var display = _context.displays.FirstOrDefault(x => x.Id == id);
        if (display == null)
        {
            return 0;
        }
        _context.displays.Remove(display);
        return _context.SaveChanges();
    }
    
}