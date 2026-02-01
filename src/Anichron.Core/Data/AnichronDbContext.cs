using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data;

public class AnichronDbContext(DbContextOptions<AnichronDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserStorageConfig> StorageConfigs => Set<UserStorageConfig>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<Metadata> Metadata => Set<Metadata>();
    public DbSet<ProxyFile> ProxyFiles => Set<ProxyFile>();
    public DbSet<Burst> Bursts => Set<Burst>();
    public DbSet<AssetInteraction> Interactions => Set<AssetInteraction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AssetInteraction>(entity =>
        {
            entity.HasIndex(i => new { i.UserId, i.AssetId }).IsUnique();
        });

        modelBuilder.Entity<Burst>(entity =>
        {
            // Primary Asset reference (1:1 style lookup)
            entity.HasOne(b => b.PrimaryAsset)
                  .WithMany()
                  .HasForeignKey(b => b.PrimaryAssetId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MediaAsset>(entity =>
        {
            // Composite Index for "On This Day" (Optimized for Flashbacks)
            entity.HasIndex(m => new { m.Month, m.Day }).HasDatabaseName("IX_MediaAsset_Flashback");

            // Index for Move Tracking
            entity.HasIndex(m => m.ContentHash);

            // Unique Constraint: One file path per storage config
            entity.HasIndex(m => new { m.StorageConfigId, m.FilePath }).IsUnique();

            // Self-Reference (Live Photo)
            entity.HasOne(m => m.LivePhotoPair)
                  .WithMany()
                  .HasForeignKey(m => m.LivePhotoPairId)
                  .OnDelete(DeleteBehavior.SetNull);

            // N:1 with Burst
            entity.HasOne(m => m.Burst)
                  .WithMany(b => b.Assets)
                  .HasForeignKey(m => m.BurstId)
                  .OnDelete(DeleteBehavior.SetNull); // Keeping the photo if the burst is dissolved

            // 1:N with ProxyFiles
            entity.HasMany(m => m.ProxyFiles)
                  .WithOne(p => p.Asset)
                  .HasForeignKey(p => p.AssetId)
                  .OnDelete(DeleteBehavior.Cascade);

            // 1:N with Interactions
            entity.HasMany(m => m.Interactions)
                  .WithOne(i => i.Asset)
                  .HasForeignKey(i => i.AssetId);

            // Global Query Filter: Hide soft-deleted files by default
            entity.HasQueryFilter(m => !m.IsSoftDeleted);

            // Enum Conversion
            entity.Property(m => m.MediaType).HasConversion<string>();
        });

        modelBuilder.Entity<Metadata>(entity =>
        {
            entity.HasKey(m => m.AssetId);

            // 1:1 with MediaAsset
            entity.HasOne(m => m.Asset)
                  .WithOne(a => a.Metadata)
                  .HasForeignKey<Metadata>(m => m.AssetId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ProxyFile>(entity =>
        {
            entity.HasIndex(p => p.AssetId);
            entity.Property(p => p.ProxyType).HasConversion<string>();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Username).IsUnique();
            entity.HasIndex(u => u.Email).IsUnique();

            // 1:N with StorageConfig
            entity.HasMany(u => u.StorageConfigs)
                  .WithOne(s => s.User)
                  .HasForeignKey(s => s.UserId);

            // 1:N with Interactions
            entity.HasMany(u => u.Interactions)
                  .WithOne(i => i.User)
                  .HasForeignKey(i => i.UserId);
        });

        modelBuilder.Entity<UserStorageConfig>(entity =>
        {
            entity.HasIndex(s => s.UserId);

            // 1:N with MediaAsset
            entity.HasMany(s => s.Assets)
                  .WithOne(a => a.StorageConfig)
                  .HasForeignKey(a => a.StorageConfigId);
        });
    }
}