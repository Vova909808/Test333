
using Microsoft.EntityFrameworkCore;
using SystemX.Server.Models;

namespace SystemX.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<VerificationCode> VerificationCodes =>
        Set<VerificationCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<VerificationCode>(entity =>
        {
            entity.ToTable("verification_codes", "public");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Id)
                .HasColumnName("id");

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

            entity.Property(x => x.CreatedAt)
                .HasColumnName("created_at");
        });
    }
}
