using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data;

public class AnichronDbContext(DbContextOptions<AnichronDbContext> options) : DbContext(options), IUnitOfWork
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserStorageConfig> StorageConfigs => Set<UserStorageConfig>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<Metadata> Metadata => Set<Metadata>();
    public DbSet<ProxyFile> ProxyFiles => Set<ProxyFile>();
    public DbSet<Burst> Bursts => Set<Burst>();
    public DbSet<AssetInteraction> Interactions => Set<AssetInteraction>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Invite> Invites => Set<Invite>();

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        await using var tx = await Database.BeginTransactionAsync(ct);
        var result = await action();
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken ct = default)
    {
        await using var tx = await Database.BeginTransactionAsync(ct);
        await action();
        await tx.CommitAsync(ct);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AssetInteraction>(entity =>
        {
            entity.ToTable(t => t.HasComment(
                    "Per-user state for one asset. Never shared between users: two people looking at "
                    + "the same file each get their own row. Drives flashback weighting — hidden "
                    + "excludes, starred always includes and sorts first, liked raises DisplayWeight."
                ));
            entity.HasIndex(i => new { i.UserId, i.AssetId }).IsUnique();
            entity.HasQueryFilter(i => !i.Asset.IsSoftDeleted);
        });

        modelBuilder.Entity<Burst>(entity =>
        {
            entity.ToTable(t => t.HasComment(
                    "Groups a rapid-fire sequence so a flashback shows one memory rather than nine "
                    + "near-identical frames. The table and schema exist; burst DETECTION is not yet "
                    + "implemented in the Worker, so in a running system this table is empty."
                ));
            entity.Property(b => b.PrimaryAssetId).HasComment(
                "The frame chosen to represent the burst. Restrict on delete: losing the cover "
                + "would leave a group that cannot be rendered.");

            // Primary Asset reference (1:1 style lookup)
            entity.HasOne(b => b.PrimaryAsset)
                .WithMany()
                .HasForeignKey(b => b.PrimaryAssetId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasQueryFilter(b => !b.PrimaryAsset.IsSoftDeleted);
        });

        modelBuilder.Entity<MediaAsset>(entity =>
        {
            entity.ToTable(t => t.HasComment(
                    "One row per media file, scoped to a storage config. Written ONLY by the Worker — "
                    + "the API never writes media data."
                ));
            entity.Property(m => m.ContentHash).HasComment(
                "XXHash64 of the file contents. Dedup is deliberately CONFIG-SCOPED: the same "
                + "bytes under two storage configs are two assets, which is what makes multi-user "
                + "work. A global unique index on this column alone would break that and must "
                + "never be added.");
            entity.Property(m => m.FilePath).HasComment(
                "Path relative to the storage config root. Not to be confused with "
                + "ProxyFiles.ProxyPath, which is relative to the proxy root.");
            entity.Property(m => m.Month).HasComment(
                "Stored apart from DateCaptured so \"On This Day\" is an index lookup on "
                + "(Month, Day) rather than an EXTRACT() scan over every row.");
            entity.Property(m => m.Day).HasComment(
                "See Month — the pair backs IX_MediaAsset_Flashback.");
            entity.Property(m => m.PairedAssetId).HasComment(
                "Live Photo pairing: the HEIC parent points at its MOV child. SetNull on delete "
                + "so losing one half leaves the other a usable standalone asset.");
            entity.Property(m => m.IsSoftDeleted).HasComment(
                "Set by reconciliation when a file is gone from the NAS. Soft rather than hard so "
                + "the user's interactions survive a temporarily unmounted share.");

            // Composite Index for "On This Day" (Optimized for Flashbacks)
            entity.HasIndex(m => new { m.Month, m.Day }).HasDatabaseName("IX_MediaAsset_Flashback");

            // Index for Move Tracking (scoped to storage config for efficient idempotency lookups).
            // ⚠️ Deliberately NOT unique today, and that is a known defect (#159): nothing enforces
            // the "one active asset per (config, hash)" invariant IdempotencyCheckMiddleware
            // assumes, so concurrent consumers can create silent duplicates. The fix is a
            // config-scoped PARTIAL unique index filtered to active rows — not a plain unique
            // index, which would collide with soft-deleted history.
            entity.HasIndex(m => new { m.StorageConfigId, m.ContentHash });

            // Unique Constraint: One file path per storage config. Unlike the hash index above
            // this one IS enforced, so re-ingesting the same path cannot produce a second row
            // even while #159 is open.
            entity.HasIndex(m => new { m.StorageConfigId, m.FilePath }).IsUnique();

            // Self-Reference (Paired Asset)
            entity.HasOne(m => m.PairedAsset)
                  .WithMany()
                  .HasForeignKey(m => m.PairedAssetId)
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

            // Partial index covering only active (non-deleted) assets
            entity.HasIndex(m => m.IsSoftDeleted)
                  .HasFilter(@"""IsSoftDeleted"" = false")
                  .HasDatabaseName("IX_MediaAssets_Active");

            // Global Query Filter: Hide soft-deleted files by default
            entity.HasQueryFilter(m => !m.IsSoftDeleted);

            // Enum Conversion
            entity.Property(m => m.MediaType).HasConversion<string>();
        });

        modelBuilder.Entity<Metadata>(entity =>
        {
            entity.ToTable(t => t.HasComment(
                    "EXIF data, 1:1 with MediaAsset and keyed by it — there is no separate Id. "
                    + "Populated by ExifExtractionMiddleware; every field is best-effort, because a "
                    + "file may carry no EXIF at all."
                ));
            entity.Property(m => m.OrientationDegrees).HasComment(
                "Rotation needed to display the image upright, already normalised from the raw "
                + "EXIF orientation flag so consumers never interpret the flag themselves.");
            entity.HasKey(m => m.AssetId);

            // 1:1 with MediaAsset
            entity.HasOne(m => m.Asset)
                  .WithOne(a => a.Metadata)
                  .HasForeignKey<Metadata>(m => m.AssetId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(e => !e.Asset.IsSoftDeleted);
        });

        modelBuilder.Entity<ProxyFile>(entity =>
        {
            entity.ToTable(t => t.HasComment(
                    "One row per generated artefact on the local SSD. An asset has several. Written "
                    + "only by ProxyStagingWriter, at the moment the file reaches its final name."
                ));
            entity.Property(p => p.ProxyPath).HasComment(
                "Path relative to the proxy root, sharded by CONTENT rather than by ingestion "
                + "attempt so a retry overwrites its own debris instead of stranding it. "
                + "IProxyDirectoryStrategy is the single place that rule lives.");
            entity.Property(p => p.ProxyType).HasComment(
                "Thumbnail, WebVideo, FullPreview or BlurHash, persisted as text. Note BlurHash "
                + "is an ordinary proxy ROW whose content is a file — not a string column on the "
                + "asset.");

            entity.HasIndex(p => p.AssetId);
            entity.Property(p => p.ProxyType).HasConversion<string>();
            entity.HasQueryFilter(e => !e.Asset.IsSoftDeleted);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable(t => t.HasComment(
                    "Issued and revoked by the API; expired rows are swept by the Worker's "
                    + "TokenCleanupService. One of the few tables both services touch."
                ));
            entity.Property(r => r.TokenHash).HasComment(
                "Hash, never the token itself — a database disclosure must not hand out usable "
                + "sessions.");

            entity.HasIndex(r => r.TokenHash).IsUnique();
            entity.HasIndex(r => new { r.UserId, r.ExpiresAt });

            entity.HasOne(r => r.User)
                  .WithMany(u => u.RefreshTokens)
                  .HasForeignKey(r => r.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Invite>(entity =>
        {
            entity.ToTable(t => t.HasComment(
                    "Admin-issued registration tokens. Registration requires one; the first user "
                    + "(bootstrap admin) is exempt. Redemption is a read-then-write race, which is "
                    + "why this is the only table carrying a concurrency token."
                ));
            entity.Property(i => i.TokenHash).HasComment(
                "Hash, never the token itself. 44 chars — base64 of a 32-byte value.");

            entity.HasIndex(i => i.TokenHash).IsUnique();
            entity.Property(i => i.TokenHash).HasMaxLength(44);

            // xmin is a Postgres system column incremented on every UPDATE.
            // Npgsql recognises the "xmin" column name and excludes it from migrations.
            entity.Property<uint>("xmin")
                  .HasColumnName("xmin")
                  .ValueGeneratedOnAddOrUpdate()
                  .IsConcurrencyToken();

            entity.HasOne(i => i.CreatedBy)
                  .WithMany()
                  .HasForeignKey(i => i.CreatedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(i => i.UsedBy)
                  .WithMany()
                  .HasForeignKey(i => i.UsedByUserId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable(t => t.HasComment(
                    "Owns storage configs and interactions. All identity writes happen in the API; "
                    + "the Worker only reads."
                ));
            entity.Property(u => u.PasswordHash).HasComment(
                "Argon2id. The parameters live with the hasher, not here, so rotating them does "
                + "not require a migration.");
            entity.Property(u => u.MustChangePassword).HasComment(
                "Set by an admin reset. MustChangePasswordMiddleware then 403s every route except "
                + "the handful needed to change it.");

            entity.HasIndex(u => u.Username).IsUnique().HasDatabaseName(UserIndexNames.UsernameUnique);
            entity.HasIndex(u => u.Email).IsUnique().HasDatabaseName(UserIndexNames.EmailUnique);

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
            entity.ToTable(t => t.HasComment(
                    "A NAS root assigned to one user. This is the boundary that scopes assets to a "
                    + "user and lets several Workers each monitor a separate config."
                ));
            entity.Property(s => s.RootPath).HasComment(
                "GLOBALLY unique, not per user: a path cannot be assigned to two configs across "
                + "all users. Two users indexing the same directory would duplicate every asset "
                + "and let one user's config removal cascade away the other's rows.");

            entity.HasIndex(s => s.UserId);
            entity.HasIndex(s => s.RootPath).IsUnique();

            // 1:N with MediaAsset
            entity.HasMany(s => s.Assets)
                  .WithOne(a => a.StorageConfig)
                  .HasForeignKey(a => a.StorageConfigId);
        });
    }
}
