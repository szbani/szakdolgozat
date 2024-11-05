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
    
    public DisplayModel[] GetRegisteredDisplays()
    {
        return _context.displays.ToArray();
    }
    
    public int ModifyRegisteredDisplay(DisplayModel dto)
    {
        var display = _context.displays.FirstOrDefault(x => x.Id == dto.Id);
        if (display == null)
        {
            return 0;
        }
        display = dto;
        return _context.SaveChanges();
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