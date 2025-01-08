using szakdolgozat.DBContext;
using szakdolgozat.DBContext.Models;
using szakdolgozat.Interface;

namespace szakdolgozat.Controllers;

public class PlaylistsService : IPlaylistsService
{
    private AppDbContext _context;
    
    public PlaylistsService(AppDbContext context)
    {
        _context = context;
    }
    
    public int AddPlaylist(PlaylistsModel dto)
    {
        _context.playlists.Add(dto);
        return _context.SaveChanges();
    }
    
    public PlaylistsModel GetPlaylist(int id)
    {
        return _context.playlists.FirstOrDefault(x => x.Id == id);
    }
    
    public PlaylistsModel[] GetPlaylists()
    {
        return _context.playlists.ToArray();
    }
    
    public int ModifyPlaylist(PlaylistsModel dto)
    {
        var playlist = _context.playlists.FirstOrDefault(x => x.Id == dto.Id);
        if (playlist == null)
        {
            return 0;
        }
        playlist.PlaylistName = dto.PlaylistName;
        playlist.PlaylistDescription = dto.PlaylistDescription;
        return _context.SaveChanges();
    }
    
    public int RemovePlaylist(int id)
    {
        var playlist = _context.playlists.FirstOrDefault(x => x.Id == id);
        if (playlist == null)
        {
            return 0;
        }
        _context.playlists.Remove(playlist);
        return _context.SaveChanges();
    }
    
}