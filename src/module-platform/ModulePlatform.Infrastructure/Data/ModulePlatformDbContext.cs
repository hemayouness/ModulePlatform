using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using ModulePlatform.Core.Domain;

namespace ModulePlatform.Infrastructure.Data;

public class ModulePlatformDbContext(DbContextOptions<ModulePlatformDbContext> options)
    : DbContext(options)
{
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<ModuleVersion> ModuleVersions => Set<ModuleVersion>();
    public DbSet<ModuleRecord> ModuleRecords => Set<ModuleRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Module>(e =>
        {
            e.ToTable("Modules");
            e.HasKey(x => x.Id);

            // One registry name per platform. This is the invariant that makes
            // `/modules/{name}/...` unambiguous.
            e.HasIndex(x => x.Name).IsUnique();

            e.Property(x => x.Name).HasMaxLength(64).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(128).IsRequired();
            e.Property(x => x.Description).HasMaxLength(512);

            // Filtering discovery by enabled-ness is the hottest query.
            e.HasIndex(x => x.IsEnabled);

            e.HasOne(x => x.ActiveVersion)
                .WithMany()
                .HasForeignKey(x => x.ActiveVersionId)
                // Restrict, not Cascade: deleting the row a module points at
                // must fail loudly rather than silently dark the module.
                .OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Versions)
                .WithOne(x => x.Module)
                .HasForeignKey(x => x.ModuleId)
                // Removing a module removes its version rows; artifacts are
                // cleaned up by the manager before the row is deleted.
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<ModuleVersion>(e =>
        {
            e.ToTable("ModuleVersions");
            e.HasKey(x => x.Id);

            // A given version of a module can only ever be installed once.
            // This is what makes versioned URLs safe to cache forever: the
            // bytes behind /modules/requests/1.0.0/ can never change.
            e.HasIndex(x => new { x.ModuleId, x.Version }).IsUnique();

            // Ordering versions correctly needs numeric components, so the
            // sort key is indexed rather than the SemVer string.
            e.HasIndex(x => new { x.ModuleId, x.SortMajor, x.SortMinor, x.SortPatch });

            // Cheap integrity/dedupe lookups ("have we seen these exact bytes?").
            e.HasIndex(x => x.ContentHash);

            e.Property(x => x.Version).HasMaxLength(64).IsRequired();
            e.Property(x => x.EntryPath).HasMaxLength(256).IsRequired();
            e.Property(x => x.RoutesExposedModule).HasMaxLength(128).IsRequired();
            e.Property(x => x.StoragePrefix).HasMaxLength(256).IsRequired();
            e.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(128);
            e.Property(x => x.Description).HasMaxLength(512);
            e.Property(x => x.ContractsVersion).HasMaxLength(64);
            e.Property(x => x.InstalledBy).HasMaxLength(128);

            // Small JSON blob (nav hints + required permissions). Kept as a
            // document because the Shell round-trips it verbatim and the
            // platform never queries inside it.
            // The type name is provider-specific, so only pin it on SQL Server;
            // the tests run the same model on SQLite.
            if (Database.IsSqlServer())
            {
                e.Property(x => x.MetadataJson).HasColumnType("nvarchar(max)");
            }

            // At most one active version per module, enforced by the database
            // rather than only by application code.
            e.HasIndex(x => x.ModuleId)
                .IsUnique()
                .HasFilter("[IsActive] = 1")
                .HasDatabaseName("UX_ModuleVersions_OneActivePerModule");
        });

        b.Entity<ModuleRecord>(e =>
        {
            e.ToTable("ModuleRecords");
            e.HasKey(x => x.Id);

            e.Property(x => x.ModuleName).HasMaxLength(64).IsRequired();
            e.Property(x => x.Collection).HasMaxLength(64).IsRequired();
            e.Property(x => x.ExternalId).HasMaxLength(128).IsRequired();
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.CreatedBy).HasMaxLength(128);
            e.Property(x => x.UpdatedBy).HasMaxLength(128);

            // The module owns ID generation, so one external ID must be unique
            // within its own (module, collection) bucket — but two different
            // modules (or two collections in the same module) may reuse the
            // same external ID without colliding.
            e.HasIndex(x => new { x.ModuleName, x.Collection, x.ExternalId })
                .IsUnique()
                .HasDatabaseName("UX_ModuleRecords_Module_Collection_ExternalId");

            // Listing a module's collection ("give me every request") is the
            // hot path; the unique index above already covers it as a prefix,
            // but a dedicated index keeps the query plan simple regardless of
            // how the unique index is implemented by the provider.
            e.HasIndex(x => new { x.ModuleName, x.Collection })
                .HasDatabaseName("IX_ModuleRecords_Module_Collection");

            // Server-side filtering ("only drafts", "only my records") needs
            // these indexed, scoped within a module's own collection.
            e.HasIndex(x => new { x.ModuleName, x.Collection, x.Status })
                .HasDatabaseName("IX_ModuleRecords_Module_Collection_Status");
            e.HasIndex(x => new { x.ModuleName, x.Collection, x.CreatedBy })
                .HasDatabaseName("IX_ModuleRecords_Module_Collection_CreatedBy");

            // Opaque JSON payload — the platform never queries inside it, only
            // the owning module's own frontend interprets it.
            if (Database.IsSqlServer())
            {
                e.Property(x => x.DataJson).HasColumnType("nvarchar(max)");
            }
            else
            {
                // SQLite — which the test suite runs the same model on — refuses
                // to ORDER BY a DateTimeOffset at all. Listing a collection is
                // ordered by CreatedAt by default, so without this the ordering
                // behaviour simply could not be tested against a real relational
                // provider. Storing UTC ticks keeps the sort order identical to
                // SQL Server's, and every value written here is already UTC.
                var utcTicks = new ValueConverter<DateTimeOffset, long>(
                    v => v.UtcTicks,
                    v => new DateTimeOffset(v, TimeSpan.Zero));

                e.Property(x => x.CreatedAt).HasConversion(utcTicks);
                e.Property(x => x.UpdatedAt).HasConversion(utcTicks);
            }
        });
    }
}
