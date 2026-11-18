
namespace SystemX.Server.Models;

public class VerificationCode
{
    public Guid Id { get; set; }

    public Guid? UserId { get; set; }

    public string Destination { get; set; } = string.Empty;

    public string CodeHash { get; set; } = string.Empty;

    public string Purpose { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public bool IsUsed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
