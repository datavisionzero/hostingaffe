using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Hostingaffe.Domain.Identities;

namespace Hostingaffe.Infrastructure.Persistence.Configurations;

/// <inheritdoc cref="IdentityConfiguration"/>
public sealed class TokenConfiguration : IEntityTypeConfiguration<Token>
{
    public void Configure(EntityTypeBuilder<Token> builder)
    {
        builder.ToTable("token", table =>
            table.HasCheckConstraint("ck_token_kind", "kind in ('user', 'agent')"));

        builder.HasKey(t => t.Id).HasName("pk_token");
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.IdentityId).HasColumnName("identity_id").IsRequired();

        builder.HasOne<Identity>()
            .WithMany()
            .HasForeignKey(t => t.IdentityId)
            .HasConstraintName("fk_token_identity")
            .OnDelete(DeleteBehavior.NoAction);

        // Copied from the identity so that the server tells a user token from an
        // agent token from the row it already holds, without a join (ADR 0015).
        builder.Property(t => t.Kind)
            .HasColumnName("kind")
            .HasConversion(new SnakeCaseEnumConverter<IdentityKind>())
            .IsRequired();

        builder.Property(t => t.Prefix).HasColumnName("prefix").IsRequired();

        // The lookup authentication runs on: the presented secret is hashed and
        // found here, and the unique index is the index.
        builder.Property(t => t.SecretHash).HasColumnName("secret_hash").IsRequired();
        builder.HasIndex(t => t.SecretHash).IsUnique().HasDatabaseName("token_secret_hash");

        // An agent has exactly one token; a user as many as they create.
        builder.HasIndex(t => t.IdentityId)
            .IsUnique()
            .HasFilter("kind = 'agent'")
            .HasDatabaseName("token_agent");

        builder.Property(t => t.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(t => t.RevokedAt).HasColumnName("revoked_at");

        builder.Ignore(t => t.Revoked);
    }
}
