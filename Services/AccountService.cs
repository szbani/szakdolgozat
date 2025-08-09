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

    public Task UpdateUserAsync(string id, string username, string email, string password)
    {
        if (username == null)
        {
            return Task.FromException(AccountErrors.UsernameError);
        }

        if (email == null)
        {
            return Task.FromException(AccountErrors.EmailError);
        }

        if (id == null)
        {
            return Task.FromException(AccountErrors.NullError);
        }

        if (!AccountInformation.IsValidEmail(email))
        {
            return Task.FromException(AccountErrors.InvalidEmail);
        }

        if (username.Length < 5)
        {
            return Task.FromException(AccountErrors.UserNameTooShort);
        }
        if (username.Length > 32)
        {
            return Task.FromException(AccountErrors.UserNameTooLong);
        }

        var user = _userManager.FindByIdAsync(id).Result;
        if (user == null && password.Length == 0)
        {
            return Task.FromException(AccountErrors.UserNotFoundError);
        }

        if (password.Length == 0 || password == "******")
        {
            user.UserName = username;
            user.Email = email;
            _userManager.UpdateAsync(user).Wait();
            return Task.CompletedTask;
        }

        if (password == null)
        {
            return Task.FromException(AccountErrors.PasswordError);
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
                    return Task.FromException(AccountErrors.UnknownError);
                }
                else
                {
                    return Task.CompletedTask;
                }
            }
        }
        else
        {
            if (password.Length < 8)
            {
                return Task.FromException(AccountErrors.PasswordTooShort);
            }
            else if (password.Length > 100)
            {
                return Task.FromException(AccountErrors.PasswordTooLong);
            }
            else if (!password.Any(char.IsDigit))
            {
                return Task.FromException(AccountErrors.PasswordNoDigit);
            }
            else if (!password.Any(char.IsUpper))
            {
                return Task.FromException(AccountErrors.PasswordNoUpper);
            }
            else if (!password.Any(char.IsLower))
            {
                return Task.FromException(AccountErrors.PasswordNoLower);
            }
            else
            {
                return Task.FromException(AccountErrors.UnknownError);   
            }
        }
    }

    public Task RegisterUser(string username, string email, string password)
    {
        var user = new IdentityUser { UserName = username, Email = email };
        var result = _userManager.CreateAsync(user, password).Result;
        Console.WriteLine(result.Errors.ToString());
        if (result.Succeeded)
        {
            return Task.CompletedTask;
        }
        else
        {
            return Task.FromException(AccountErrors.UnknownError);
        }
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

    public Task DeleteUserAsync(string id)
    {
        var user = _userManager.FindByIdAsync(id).Result;
        if (user == null)
        {
            return Task.FromException(AccountErrors.NullError);
        }
        
        return _userManager.DeleteAsync(user);
    }
}

public class AccountErrors
{
    // General Exceptions
    public static readonly Exception NullError = new ArgumentNullException("A required value was null.");
    public static readonly Exception UnknownError = new ApplicationException("An unknown error occurred.");

    // Username Exceptions
    public static readonly Exception UsernameError = new ArgumentException("Username not found or invalid.");
    public static readonly Exception UserNameTooShort = new ArgumentException("Username is too short.");
    public static readonly Exception UserNameTooLong = new ArgumentException("Username is too long.");
    public static readonly Exception UserNotFoundError = new KeyNotFoundException("User not found.");

    // Email Exceptions
    public static readonly Exception EmailError = new ArgumentException("Email not found or invalid.");
    public static readonly Exception InvalidEmail = new FormatException("Invalid email format.");

    // Password Exceptions
    public static readonly Exception PasswordError = new ArgumentException("Password not found or invalid.");
    public static readonly Exception PasswordTooShort = new ArgumentException("Password is too short.");
    public static readonly Exception PasswordTooLong = new ArgumentException("Password is too long.");
    public static readonly Exception PasswordNoDigit = new ArgumentException("Password requires at least one digit.");
    public static readonly Exception PasswordNoUpper = new ArgumentException("Password requires at least one uppercase letter.");
    public static readonly Exception PasswordNoLower = new ArgumentException("Password requires at least one lowercase letter.");
    
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