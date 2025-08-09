using Microsoft.AspNetCore.Identity;
using szakdolgozat.Controllers;

namespace szakdolgozat.Interface;

public interface IAccountService
{
    Task RegisterUser(string username, string email, string password);
    Task UpdateUserAsync(string id, string username, string email, string password);
    Task<AccountInformation[]> GetUsersAsync();
    Task DeleteUserAsync(string username);
}