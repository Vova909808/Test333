
using Microsoft.EntityFrameworkCore;
using SystemX.Server.Models;

namespace SystemX.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<VerificationCode> VerificationCodes =>
        Set<VerificationCode>();

    public DbSet<RefreshToken> RefreshTokens =>
        Set<RefreshToken>();

    public DbSet<Device> Devices => Set<Device>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // =========================================
        // USERS
        // =========================================

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users", "public");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id")
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(x => x.Username)
                .HasColumnName("username")
                .HasMaxLength(50)
                .IsRequired();

            entity.HasIndex(x => x.Username)
                .IsUnique();

            entity.Property(x => x.Email)
                .HasColumnName("email")
                .HasMaxLength(255);

            entity.HasIndex(x => x.Email)
                .IsUnique();

            entity.Property(x => x.PhoneNumber)
                .HasColumnName("phone_number")
                .HasMaxLength(30);

            entity.HasIndex(x => x.PhoneNumber)
                .IsUnique();

            entity.Property(x => x.PasswordHash)
                .HasColumnName("password_hash");

            entity.Property(x => x.FirstName)
                .HasColumnName("first_name")
                .HasMaxLength(100);

            entity.Property(x => x.LastName)
                .HasColumnName("last_name")
                .HasMaxLength(100);

            entity.Property(x => x.BirthDate)
                .HasColumnName("birth_date")
                .HasColumnType("date");

            entity.Property(x => x.Gender)
                .HasColumnName("gender")
                .HasMaxLength(30);

            entity.Property(x => x.Country)
                .HasColumnName("country")
                .HasMaxLength(100);

            entity.Property(x => x.City)
                .HasColumnName("city")
                .HasMaxLength(100);

            entity.Property(x => x.AvatarData)
                .HasColumnName("avatar_data");

            entity.Property(x => x.AvatarContentType)
                .HasColumnName("avatar_content_type")
                .HasMaxLength(50);

            entity.Property(x => x.AvatarUpdatedAt)
                .HasColumnName("avatar_updated_at");

            entity.Property(x => x.IsEmailVerified)
                .HasColumnName("is_email_verified")
                .HasDefaultValue(false);

            entity.Property(x => x.IsPhoneVerified)
                .HasColumnName("is_phone_verified")
                .HasDefaultValue(false);

            entity.Property(x => x.IsActive)
                .HasColumnName("is_active")
                .HasDefaultValue(true);

            entity.Property(x => x.IsBlocked)
                .HasColumnName("is_blocked")
                .HasDefaultValue(false);

            entity.Property(x => x.LastLoginAt)
                .HasColumnName("last_login_at");

            entity.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()");

            entity.Property(x => x.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("NOW()");
        });


        // =========================================
        // ROLES
        // =========================================

        modelBuilder.Entity<Role>(entity =>
        {
            entity.ToTable("roles", "public");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id")
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(x => x.Name)
                .HasColumnName("name")
                .HasMaxLength(50)
                .IsRequired();

            entity.HasIndex(x => x.Name)
                .IsUnique();

            entity.Property(x => x.Description)
                .HasColumnName("description")
                .HasMaxLength(255);

            entity.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()");
        });


        // =========================================
        // USER_ROLES
        // =========================================

        modelBuilder.Entity<UserRole>(entity =>
        {
            entity.ToTable("user_roles", "public");

            entity.HasKey(x => new
            {
                x.UserId,
                x.RoleId
            });

            entity.Property(x => x.UserId)
                .HasColumnName("user_id");

            entity.Property(x => x.RoleId)
                .HasColumnName("role_id");

            entity.Property(x => x.AssignedAt)
                .HasColumnName("assigned_at")
                .HasDefaultValueSql("NOW()");

            entity.HasOne(x => x.User)
                .WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Role)
                .WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });


        // =========================================
        // VERIFICATION_CODES
        // =========================================

        modelBuilder.Entity<VerificationCode>(entity =>
        {
            entity.ToTable("verification_codes", "public");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id")
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(x => x.UserId)
                .HasColumnName("user_id");

            entity.Property(x => x.Destination)
                .HasColumnName("destination")
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(x => x.CodeHash)
                .HasColumnName("code_hash")
                .IsRequired();

            entity.Property(x => x.Purpose)
                .HasColumnName("purpose")
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.ExpiresAt)
                .HasColumnName("expires_at");

            entity.Property(x => x.IsUsed)
                .HasColumnName("is_used")
                .HasDefaultValue(false);

            // Количество неправильных попыток ввода SMS-кода
            entity.Property(x => x.Attempts)
                .HasColumnName("attempts")
                .HasDefaultValue(0)
                .IsRequired();

            entity.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()");

            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new
            {
                x.Destination,
                x.Purpose,
                x.CreatedAt
            });
        });


        // =========================================
        // DEVICES
        // =========================================

        modelBuilder.Entity<Device>(entity =>
        {
            entity.ToTable("devices", "public");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id")
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(x => x.UserId)
                .HasColumnName("user_id");

            entity.Property(x => x.DeviceName)
                .HasColumnName("device_name")
                .HasMaxLength(150)
                .IsRequired();

            entity.Property(x => x.DeviceUniqueId)
                .HasColumnName("device_unique_id")
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(x => x.OperatingSystem)
                .HasColumnName("operating_system")
                .HasMaxLength(150);

            entity.Property(x => x.OperatingSystemVersion)
                .HasColumnName("operating_system_version")
                .HasMaxLength(100);

            entity.Property(x => x.ProcessorName)
                .HasColumnName("processor_name")
                .HasMaxLength(255);

            entity.Property(x => x.GraphicsCardName)
                .HasColumnName("graphics_card_name")
                .HasMaxLength(255);

            entity.Property(x => x.MotherboardName)
                .HasColumnName("motherboard_name")
                .HasMaxLength(255);

            entity.Property(x => x.RamTotalGb)
                .HasColumnName("ram_total_gb")
                .HasPrecision(10, 2);

            entity.Property(x => x.IsOnline)
                .HasColumnName("is_online")
                .HasDefaultValue(false);

            entity.Property(x => x.LastSeenAt)
                .HasColumnName("last_seen_at");

            entity.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()");

            entity.Property(x => x.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("NOW()");

            entity.HasIndex(x => new
            {
                x.UserId,
                x.DeviceUniqueId
            })
            .IsUnique();

            entity.HasOne(x => x.User)
                .WithMany(x => x.Devices)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });


        // =========================================
        // REFRESH_TOKENS
        // =========================================

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens", "public");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id")
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(x => x.UserId)
                .HasColumnName("user_id");

            entity.Property(x => x.DeviceId)
                .HasColumnName("device_id");

            entity.Property(x => x.TokenHash)
                .HasColumnName("token_hash")
                .IsRequired();

            entity.HasIndex(x => x.TokenHash)
                .IsUnique();

            entity.Property(x => x.ExpiresAt)
                .HasColumnName("expires_at");

            entity.Property(x => x.RevokedAt)
                .HasColumnName("revoked_at");

            entity.Property(x => x.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("NOW()");

            entity.HasIndex(x => x.UserId);

            entity.HasIndex(x => x.DeviceId);

            entity.HasOne(x => x.User)
                .WithMany(x => x.RefreshTokens)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Device)
                .WithMany(x => x.RefreshTokens)
                .HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
