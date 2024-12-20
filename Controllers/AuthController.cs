using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace szakdolgozat.Controllers;

[EnableCors("AllowAll")]
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly UserManager<IdentityUser> _userManager;
    
    public AuthController(SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }
    
    [HttpPost("login")]
    [Route("login")]
    public async Task<IActionResult> Login([FromBody] LoginModel model)
    {
        var user = await _userManager.FindByNameAsync(model.Username);
        if (user == null)
        {
            return BadRequest("Invalid username or password");
        }
        
        var result = await _signInManager.PasswordSignInAsync(user, model.Password, false, false);
        if (result.Succeeded)
        {
            Console.WriteLine("OK"); 
            return Ok();
        }
        
        return BadRequest("Invalid username or password");
    }
    
    [HttpPost("LogoutAccount")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return Ok();
    }
    
    [HttpDelete("DeleteAccount")]
    public async Task<IActionResult> DeleteAccount([FromBody] LoginModel model)
    {
        var user = await _userManager.FindByNameAsync(model.Username);
        if (user == null)
        {
            return BadRequest("Invalid username or password");
        }
        
        var result = await _userManager.DeleteAsync(user);
        if (result.Succeeded)
        {
            return Ok();
        }
        
        return BadRequest(result.Errors);
    }
    
    [HttpPost("ModifyAccount")]
    public async Task<IActionResult> ModifyAccount([FromBody] ModifyAccountModel model)
    {
        var user = await _userManager.FindByNameAsync(model.Username);
        if (user == null)
        {
            return BadRequest("Invalid username or password");
        }
        
        var result = await _userManager.ChangePasswordAsync(user, model.OldPassword, model.NewPassword);
        if (result.Succeeded)
        {
            return Ok();
        }
        
        return BadRequest(result.Errors);
    }
    
    [HttpPost("RegisterAccount")]
    public async Task<IActionResult> Register([FromBody] RegisterModel model)
    {
        var user = new IdentityUser
        {
            UserName = model.Username,
            Email = model.Email,
        };
        if (model.Password != model.Password2)
        {
            return BadRequest("Passwords do not match");
        }
        
        var result = await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            Console.WriteLine("Account created");
            return Ok();
        }
        
        return BadRequest(result.Errors);
    }
    
    public class RegisterModel
    {
        public string Username { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
        public string Password2 { get; set; }
    }
    
    public class ModifyAccountModel
    {
        public string Username { get; set; }
        public string OldPassword { get; set; }
        public string NewPassword { get; set; }
    }

    public class LoginModel
    {
        public string Username { get; set; }
        public string Password { get; set; }
    }
}