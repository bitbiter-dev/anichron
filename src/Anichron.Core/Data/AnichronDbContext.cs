using Anichron.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Anichron.Core.Data;

public class AnichronDbContext(DbContextOptions<AnichronDbContext> options) : DbContext(options), IUnitOfWork
{
    private const string identityNormalizedComment =
        "Always stored trimmed and lowercased: an EF value converter applies "
        + "IdentityNormalizer.Normalize on every write and to every equality comparison against "
        + "this column, so the case-sensitive unique index cannot hold two spellings of one name.";

    public DbSet<User> Users => Set<User>();
    public DbSet<UserStorageConfig> StorageConfigs => Set<UserStorageConfig>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<Metadata> Metadata => Set<Metadata>();
    public DbSet<ProxyFile> ProxyFiles => Set<ProxyFile>();
    public DbSet<Burst> Bursts => Set<Burst>();
    public DbSet<AssetInteraction> Interactions => Set<AssetInteraction>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    // Stryker disable all
    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
    {
        await using var tx = await Database.BeginTransactionAsync(ct);
        var result = await action();
        await tx.CommitAsync(ct);
        return result;
    }

    // Stryker disable all
    public async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken ct = default)
    {
        await using var tx = await Database.BeginTransactionAsync(ct);
        await action();
        await tx.CommitAsync(ct);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Stryker disable once Statement
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

            // One active asset per (config, hash)
            entity.HasIndex(m => new { m.StorageConfigId, m.ContentHash })
                  .IsUnique()
                  .HasFilter(@"""IsSoftDeleted"" = false")
                  .HasDatabaseName(MediaAssetIndexNames.ActiveContentHashUnique);

            // Unique Constraint: One file path per storage config
            entity.HasIndex(m => new { m.StorageConfigId, m.FilePath })
                  .IsUnique()
                  .HasDatabaseName(MediaAssetIndexNames.FilePathUnique);

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
            // Stryker disable once Statement
            entity.HasMany(m => m.ProxyFiles)
                  .WithOne(p => p.Asset)
                  .HasForeignKey(p => p.AssetId)
                  .OnDelete(DeleteBehavior.Cascade);

            // 1:N with Interactions
            // Stryker disable once Statement
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
            entity.Property(m => m.Orientation)
                  .HasDefaultValue(ExifOrientation.Upright)
                  .HasComment(
                      "Raw EXIF orientation tag, 1-8 (1 = upright, also used when absent or invalid). "
                      + "Stored raw because 5 and 7 combine a rotation with a mirror and cannot be "
                      + "expressed as degrees; a reader converts, and for 5-8 swaps Width/Height. "
                      + "Proxies keep this tag on unrotated pixels, so do not rotate them twice.");
            entity.Property(m => m.Blurhash).HasComment(
                "BlurHash placeholder string (4x3 components), computed by the Worker from the "
                + "upright image. Kept here rather than as a proxy file so a response needs no disk "
                + "read for it. Null for videos, and for images ingested before the column existed, "
                + "which are not backfilled.");
            entity.HasKey(m => m.AssetId);

            // 1:1 with MediaAsset
            // Stryker disable once Statement
            entity.HasOne(m => m.Asset)
                  .WithOne(a => a.Metadata)
                  .HasForeignKey<Metadata>(m => m.AssetId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasQueryFilter(e => !e.Asset.IsSoftDeleted);
        });

        modelBuilder.Entity<ProxyFile>(entity =>
        {
            entity.ToTable(t => t.HasComment(
                    "One row per generated artefact on the local SSD. An asset has several, but at "
                    + "most one per ProxyType, so of two attempts racing to add the same proxy only one "
                    + "row lands. Written only by ProxyStagingWriter, at the moment the file reaches "
                    + "its final name."
                ));
            entity.Property(p => p.ProxyPath).HasComment(
                "Path relative to the proxy root, sharded by CONTENT rather than by ingestion "
                + "attempt so a retry overwrites its own debris instead of stranding it. "
                + "IProxyDirectoryStrategy is the single place that rule lives.");
            entity.Property(p => p.ProxyType).HasComment(
                "Thumbnail, WebVideo or FullPreview, persisted as text. The BlurHash placeholder "
                + "is not a proxy: it is the Metadata.Blurhash column.");

            entity.HasIndex(p => new { p.AssetId, p.ProxyType })
                  .IsUnique()
                  .HasDatabaseName(ProxyFileIndexNames.OnePerAssetAndType);
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

            // Stryker disable once Statement
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

            entity.Property(u => u.Username)
                  .HasConversion(v => IdentityNormalizer.Normalize(v), v => v)
                  .HasComment(identityNormalizedComment);
            entity.Property(u => u.Email)
                  .HasConversion(v => IdentityNormalizer.Normalize(v), v => v)
                  .HasComment(identityNormalizedComment);

            entity.HasIndex(u => u.Username).IsUnique().HasDatabaseName(UserIndexNames.UsernameUnique);
            entity.HasIndex(u => u.Email).IsUnique().HasDatabaseName(UserIndexNames.EmailUnique);

            // 1:N with StorageConfig
            // Stryker disable once Statement
            entity.HasMany(u => u.StorageConfigs)
                  .WithOne(s => s.User)
                  .HasForeignKey(s => s.UserId);

            // 1:N with Interactions
            // Stryker disable once Statement
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

            // Stryker disable once Statement
            entity.HasIndex(s => s.UserId);
            entity.HasIndex(s => s.RootPath).IsUnique().HasDatabaseName(StorageConfigIndexNames.RootPathUnique);

            // 1:N with MediaAsset
            // Stryker disable once Statement
            entity.HasMany(s => s.Assets)
                  .WithOne(a => a.StorageConfig)
                  .HasForeignKey(a => a.StorageConfigId);
        });

        modelBuilder.Entity<WorkItem>(entity =>
        {
            entity.ToTable(t => t.HasComment(
                    "Durable work for the Worker: periodic maintenance today, admin requests later. "
                    + "Only IWorkQueue inserts rows; every later state change belongs to the Worker's "
                    + "executor. Delivery is at least once, so handlers start from scratch and converge."
                ));
            entity.Property(w => w.Kind).HasConversion<string>().HasComment(
                "WorkKind, persisted as text. Picks the payload type and the handler through the "
                + "registry, never through a switch.");
            entity.Property(w => w.Payload).HasColumnType("jsonb").HasComment(
                "The kind's IWorkPayload, serialised with System.Text.Json and NodaTime settings.");
            entity.Property(w => w.DedupKey).HasComment(
                "Opt-in coalescing. While an item with this key is Queued, enqueueing the same key "
                + "returns that item instead of adding a second; the partial unique index enforces it.");
            entity.Property(w => w.RequestedBy).HasComment(
                "The admin who asked for the work; null when a Worker's scheduler enqueued it.");
            entity.Property(w => w.Status).HasConversion<string>().HasComment(
                "Queued, Running, Succeeded, Failed or Cancelled, persisted as text.");
            entity.Property(w => w.Attempt).HasComment(
                "Incremented by every claim. Every write a Worker makes while running an item is "
                + "fenced by (Id, Attempt), so an attempt that lost its lease changes nothing.");
            entity.Property(w => w.Stalls).HasComment(
                "Lease expiries, counted by lease recovery. A graceful shutdown hands the item back "
                + "without counting one; the item fails as stalled once the limit is reached.");
            entity.Property(w => w.ClaimedBy).HasComment(
                "The claiming Worker for display: its WORKER__USER or 'all', then its host name.");
            entity.Property(w => w.LeaseUntil).HasComment(
                "Extended by every heartbeat. A Running item past it belongs to a dead or paused "
                + "Worker and goes back to the queue.");
            entity.Property(w => w.UpdatedAt).HasComment(
                "Moves on every insert, claim, heartbeat and transition, so a reader can ask what "
                + "changed since a point in time.");
            entity.Property(w => w.ProgressNote).HasComment(
                "The latest progress snapshot (done, optional total, note), overwritten by each "
                + "heartbeat; no history is kept.");
            entity.Property(w => w.Result).HasColumnType("jsonb").HasComment(
                "The handler's results summary, written when the item succeeds.");

            entity.HasIndex(w => w.RequestedAt)
                  .HasFilter(@"""Status"" = 'Queued'")
                  .HasDatabaseName("ix_workitems_claim");
            entity.HasIndex(w => w.DedupKey)
                  .IsUnique()
                  .HasFilter(@"""Status"" = 'Queued' AND ""DedupKey"" IS NOT NULL")
                  .HasDatabaseName(WorkItemIndexNames.QueuedDedupKeyUnique);
            entity.HasIndex(w => w.LeaseUntil)
                  .HasFilter(@"""Status"" = 'Running'")
                  .HasDatabaseName("ix_workitems_lease");
            entity.HasIndex(w => w.UpdatedAt);

            entity.HasOne<User>()
                  .WithMany()
                  .HasForeignKey(w => w.RequestedBy)
                  .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
