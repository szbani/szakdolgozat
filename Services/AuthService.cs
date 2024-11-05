using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace szakdolgozat.Controllers;

public class AuthService
{
    private readonly IAuthenticationService _authenticationService;
    
    public AuthService(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }
    
    public async Task<string> ValidateCookie(HttpContext context)
    {
        try
        {
            // AuthenticateAsync will validate the cookie and return a result
            var authResult = await _authenticationService.AuthenticateAsync(context, "Identity.Application");

            // Check if authentication was successful
            if (authResult.Succeeded && authResult.Principal?.Identity is ClaimsIdentity identity && identity.IsAuthenticated)
            {
                var username = authResult.Principal.FindFirst(ClaimTypes.Name)?.Value;
                Console.WriteLine("User authenticated.");
                if (username != null)
                {
                    return username;
                }
                return null;
            }
            else
            {
                Console.WriteLine("User not authenticated.");
                return null;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception: " + ex.Message);
            return null;
        }
    }
}