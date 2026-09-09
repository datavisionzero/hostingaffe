using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.Email).HasColumnName("email");
        builder.Property(u => u.NormalizedEmail).HasColumnName("normalized_email");
        builder.Property(u => u.State).HasColumnName("user_state").HasConversion(new SnakeCaseEnumConverter<UserState>());
        builder.Property(u => u.PasswordHash).HasColumnName("password_hash");
        builder.Property(u => u.BootstrapExchangedAt).HasColumnName("bootstrap_exchanged_at");
        builder.HasIndex(u => u.NormalizedEmail).IsUnique().HasDatabaseName("identity_email").HasFilter("kind = 'user'");
    }
}

public sealed class OneTimeSecretConfiguration : IEntityTypeConfiguration<OneTimeSecret>
{
    public void Configure(EntityTypeBuilder<OneTimeSecret> builder)
    {
        builder.ToTable("one_time_secret", t => {
            t.HasCheckConstraint("ck_one_time_secret_purpose", "purpose in ('invitation', 'password_recovery', 'email_change')");
            t.HasCheckConstraint("ck_one_time_secret_pending_email", "(purpose = 'email_change') = (pending_email is not null)");
        });
        builder.HasKey(x => x.Id).HasName("pk_one_time_secret");
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Purpose).HasColumnName("purpose").HasConversion(new SnakeCaseEnumConverter<OneTimeSecretPurpose>());
        builder.Property(x => x.SecretHash).HasColumnName("secret_hash"); builder.HasIndex(x => x.SecretHash).IsUnique().HasDatabaseName("one_time_secret_hash");
        builder.Property(x => x.PendingEmail).HasColumnName("pending_email"); builder.Property(x => x.PendingNormalizedEmail).HasColumnName("pending_normalized_email");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at"); builder.Property(x => x.ExpiresAt).HasColumnName("expires_at"); builder.Property(x => x.UsedAt).HasColumnName("used_at");
        builder.HasIndex(x => new { x.UserId, x.Purpose }).IsUnique().HasDatabaseName("one_live_secret_per_purpose").HasFilter("used_at is null");
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).HasConstraintName("fk_one_time_secret_user").OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class BrowserSessionConfiguration : IEntityTypeConfiguration<BrowserSession>
{
    public void Configure(EntityTypeBuilder<BrowserSession> builder)
    {
        builder.ToTable("browser_session"); builder.HasKey(x => x.Id).HasName("pk_browser_session");
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.SecretHash).HasColumnName("secret_hash"); builder.HasIndex(x => x.SecretHash).IsUnique().HasDatabaseName("browser_session_hash");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at"); builder.Property(x => x.LastUsedAt).HasColumnName("last_used_at"); builder.Property(x => x.ExpiresAt).HasColumnName("expires_at"); builder.Property(x => x.RevokedAt).HasColumnName("revoked_at");
        builder.HasIndex(x => new { x.UserId, x.CreatedAt }).IsDescending(false, true).HasDatabaseName("browser_session_user");
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).HasConstraintName("fk_browser_session_user").OnDelete(DeleteBehavior.NoAction);
    }
}

/// <summary>
/// The device logins of ADR 0005. Short-lived rows: one is worth nothing ten
/// minutes after it was made, and the purge takes them the way it takes a spent
/// one-time secret.
/// </summary>
public sealed class DeviceLoginConfiguration : IEntityTypeConfiguration<DeviceLogin>
{
    public void Configure(EntityTypeBuilder<DeviceLogin> builder)
    {
        builder.ToTable("device_login");
        builder.HasKey(x => x.Id).HasName("pk_device_login");
        builder.Property(x => x.Id).HasColumnName("id");

        // The credential is found by its hash, and the row keeps no code.
        builder.Property(x => x.DeviceCodeHash).HasColumnName("device_code_hash");
        builder.HasIndex(x => x.DeviceCodeHash).IsUnique().HasDatabaseName("device_login_code_hash");

        // What a person types, stored without its dash. Unique among the ones
        // still waiting: two live logins under one code would be a person
        // approving whichever the lookup happened to find.
        builder.Property(x => x.UserCode).HasColumnName("user_code").HasMaxLength(UserCode.Length);
        builder.HasIndex(x => x.UserCode).IsUnique().HasDatabaseName("device_login_user_code")
            .HasFilter("approved_at is null and denied_at is null");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        builder.Property(x => x.ApprovedAt).HasColumnName("approved_at");
        builder.Property(x => x.ApprovedByUserId).HasColumnName("approved_by_user_id");
        builder.Property(x => x.DeniedAt).HasColumnName("denied_at");
        builder.Property(x => x.RedeemedAt).HasColumnName("redeemed_at");
        builder.Property(x => x.IssuedTokenId).HasColumnName("issued_token_id");

        builder.HasOne<User>().WithMany().HasForeignKey(x => x.ApprovedByUserId)
            .HasConstraintName("fk_device_login_user").OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Token>().WithMany().HasForeignKey(x => x.IssuedTokenId)
            .HasConstraintName("fk_device_login_token").OnDelete(DeleteBehavior.NoAction);
    }
}
