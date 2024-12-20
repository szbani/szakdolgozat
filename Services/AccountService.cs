using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using szakdolgozat.Interface;

namespace szakdolgozat.Controllers;

public class AccountService : IAccountService
{
    private UserManager<IdentityUser> _userManager;

    public AccountService(UserManager<IdentityUser> userManager)
    {
        _userManager = userManager;
    }

    public int UpdateUser(string id, string username, string email, string password)
    {
        if (username == null || email == null || id == null)
        {
            return AccountErrors.NullError;
        }

        if (!AccountInformation.IsValidEmail(email))
        {
            return AccountErrors.InvalidEmail;
        }

        if (username.Length < 5 || username.Length > 32)
        {
            return AccountErrors.UsernameError;
        }

        var user = _userManager.FindByIdAsync(id).Result;
        if (user == null && password.Length == 0)
        {
            return AccountErrors.UserNotFoundError;
        }

        if (password.Length == 0)
        {
            user.UserName = username;
            user.Email = email;
            _userManager.UpdateAsync(user).Wait();
            return AccountErrors.Success;
        }

        if (password == null)
        {
            return AccountErrors.NullError;
        }

        if (password.Length >= 8 &&
            password.Length <= 100 && password.Any(char.IsDigit) &&
            password.Any(char.IsUpper) && password.Any(char.IsLower))
        {
            if (user == null)
            {
                return RegisterUser(username, email, password);
            }
            else
            {
                user.UserName = username;
                user.Email = email;
                _userManager.UpdateAsync(user).Wait();
                var token = _userManager.GeneratePasswordResetTokenAsync(user).Result;
                var result = _userManager.ResetPasswordAsync(user, token, password).Result;
                if (!result.Succeeded)
                {
                    return AccountErrors.PasswordError;
                }
                else
                {
                    return AccountErrors.Success;
                }
            }
        }
        else
        {
            return AccountErrors.PasswordError;
        }
    }

    public int RegisterUser(string username, string email, string password)
    {
        var user = new IdentityUser { UserName = username, Email = email };
        var result = _userManager.CreateAsync(user, password).Result;
        Console.WriteLine(result.Errors.ToString());
        return result.Succeeded ? AccountErrors.Success : AccountErrors.UnknownError;
    }

    public async Task<AccountInformation[]> GetUsersAsync()
    {
        var users = await _userManager.Users.ToListAsync();
        return users.Select(user => new AccountInformation
        {
            id = user.Id,
            UserName = user.UserName,
            Email = user.Email
        }).ToArray();
    }

    public int RemoveUser(string id)
    {
        var user = _userManager.FindByIdAsync(id).Result;
        if (user == null)
        {
            return 0;
        }

        return _userManager.DeleteAsync(user).Result.Succeeded ? 1 : 0;
    }
}

public class AccountErrors
{
    public const int PasswordError = 0;
    public const int Success = 1;
    public const int UsernameError = 2;
    public const int NullError = 3;
    public const int UserNotFoundError = 4;
    public const int InvalidEmail = 5;
    public const int UnknownError = 6;

    public static string GetErrorMessage(int errorCode)
    {
        return errorCode switch
        {
            0 => "Password error",
            2 => "Username error",
            3 => "Null error",
            4 => "User not found",
            5 => "Invalid Email",
            6 => "Unkonwn error",
            _ => "Success"
        };
    }
}

public class AccountInformation
{
    public string id { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }

    public static bool IsValidEmail(string email)
    {
        if (email.Length < 5 || email.Length > 100)
            return false;

        // Define a regular expression for invalid characters
        string invalidCharactersPattern = @"[ ;,!:?\#\$\%\^\&\*\(\)\[\]\{\}<>|\\\/\""'\`~=\+_-]";
    
        // Check if the email contains invalid characters
        if (Regex.IsMatch(email, invalidCharactersPattern))
            return false;

        // Check if the email contains "@" and "."
        if (!email.Contains("@") || !email.Contains("."))
            return false;

        return true;
    }
}