namespace WiSave.Portal.Auth.Models;

public record LoginRequest(string Email, string Password);

public record RegisterRequest(string Name, string Email, string Password, string PlanId);

public record UserResponse(string Id, string Name, string Email);

public record AuthResponse(UserResponse User);
