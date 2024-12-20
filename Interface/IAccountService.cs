using Microsoft.AspNetCore.Identity;
using szakdolgozat.Controllers;

namespace szakdolgozat.Interface;

public interface IAccountService
{
    int RegisterUser(string username, string email, string password);
    int UpdateUser(string id, string username, string email, string password);
    Task<AccountInformation[]> GetUsersAsync();
    int RemoveUser(string username);
}