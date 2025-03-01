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
        if (username == null)
        {
            return AccountErrors.UsernameError;
        }

        if (email == null)
        {
            return AccountErrors.EmailError;
        }

        if (id == null)
        {
            return AccountErrors.UnknownError;
        }

        if (!AccountInformation.IsValidEmail(email))
        {
            return AccountErrors.InvalidEmail;
        }

        if (username.Length < 5)
        {
            return AccountErrors.UserNameTooShort;
        }
        if (username.Length > 32)
        {
            return AccountErrors.UserNameTooLong;
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
            return AccountErrors.PasswordError;
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
                    return AccountErrors.UnknownError;
                }
                else
                {
                    return AccountErrors.Success;
                }
            }
        }
        else
        {
            if (password.Length < 8)
            {
                return AccountErrors.PasswordTooShort;
            }
            else if (password.Length > 100)
            {
                return AccountErrors.PasswordTooLong;
            }
            else if (!password.Any(char.IsDigit))
            {
                return AccountErrors.PasswordNoDigit;
            }
            else if (!password.Any(char.IsUpper))
            {
                return AccountErrors.PasswordNoUpper;
            }
            else if (!password.Any(char.IsLower))
            {
                return AccountErrors.PasswordNoLower;
            }
            else
            {
                return AccountErrors.UnknownError;   
            }
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
    public const int NullError = 0;
    public const int Success = 1;
    public const int UnknownError = 2;
    public const int UserFound = 3;

    public const int UsernameError = 10;
    public const int UserNameTooShort = 11;
    public const int UserNameTooLong = 12;
    public const int UserNotFoundError = 13;
    public const int EmailError = 20;
    public const int InvalidEmail = 21;
    
    public const int PasswordError = 30;
    public const int PasswordTooShort = 31;
    public const int PasswordTooLong = 32;
    public const int PasswordNoDigit = 33;
    public const int PasswordNoUpper = 34;
    public const int PasswordNoLower = 35;
    

    public static string GetErrorMessage(int errorCode)
    {
        return errorCode switch
        {
            NullError => "Null error",
            Success => "Success",
            UnknownError => "Unknown error",
            UserFound => "User found",
            UsernameError => "Username Not Found",
            UserNameTooShort => "Username too short",
            UserNameTooLong => "Username too long",
            UserNotFoundError => "User not found",
            EmailError => "Email Not Found",
            InvalidEmail => "Invalid email",
            PasswordError => "Password Not Found",
            PasswordTooShort => "Password too short",
            PasswordTooLong => "Password too long",
            PasswordNoDigit => "Password no digit",
            PasswordNoUpper => "Password no upper",
            PasswordNoLower => "Password no lower",
            _ => "Unknown error"
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