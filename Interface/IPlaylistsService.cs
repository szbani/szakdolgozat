using szakdolgozat.DBContext.Models;

namespace szakdolgozat.Interface;

public interface IPlaylistsService
{
    PlaylistsModel GetPlaylist(int id);
    PlaylistsModel[] GetPlaylists();
    int AddPlaylist(PlaylistsModel dto);
    int ModifyPlaylist(PlaylistsModel dto);
    int RemovePlaylist(int id);
}