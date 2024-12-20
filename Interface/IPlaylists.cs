namespace szakdolgozat.Interface;

public class IPlaylists
{
    int Id { get; set; }
    string PlaylistName { get; set; }
    string PlaylistDescription { get; set; }
    string PlaylistType { get; set; }
    string[] PlaylistItems { get; set; }
}