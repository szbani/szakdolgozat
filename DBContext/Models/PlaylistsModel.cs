using szakdolgozat.Interface;

namespace szakdolgozat.DBContext.Models;

public class PlaylistsModel : IPlaylists
{
    public int Id { get; set; }
    public string PlaylistName { get; set; }
    public string PlaylistDescription { get; set; }
    public string PlaylistType { get; set; }
    public string[] PlaylistItems { get; set; }
}