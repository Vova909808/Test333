
namespace SystemX.Server.Models;

public class Device
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string DeviceName { get; set; } = string.Empty;

    public string DeviceUniqueId { get; set; } = string.Empty;

    public string? OperatingSystem { get; set; }

    public string? OperatingSystemVersion { get; set; }

    public string? ProcessorName { get; set; }

    public string? GraphicsCardName { get; set; }

    public string? MotherboardName { get; set; }

    public decimal? RamTotalGb { get; set; }

    public bool IsOnline { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public User User { get; set; } = null!;

    public ICollection<RefreshToken> RefreshTokens { get; set; }
        = new List<RefreshToken>();
}
