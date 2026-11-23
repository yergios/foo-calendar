using Api.Data.Models;

using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<BankHoliday> BankHolidays => Set<BankHoliday>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>()
            .HasIndex(p => p.Username)
            .IsUnique();

        modelBuilder.Entity<Absence>()
            .HasIndex(a => new { a.PersonId, a.StartDate, a.EndDate });

        // Erasure, spelled out rather than inherited. This is what EF already infers from a
        // required foreign key, so declaring it changes no schema; the point is that
        // deleting a person is the app's answer to a GDPR Article 17 request
        // (docs/data-protection.md), which makes "what goes with them" a decision someone
        // has to have made on purpose, not a default that could shift under a future EF
        // version or a nullable-ing of the key.
        modelBuilder.Entity<Absence>()
            .HasOne(a => a.Person)
            .WithMany(p => p.Absences)
            .HasForeignKey(a => a.PersonId)
            .OnDelete(DeleteBehavior.Cascade);

        // AuditEntry deliberately has no relationship to Person at all: it names its actor
        // and target as free text captured at the time, so it survives the deletion above
        // and stays readable afterwards, which is the whole point of an audit log. What
        // bounds it instead is Retention:AuditEntryDays.

        modelBuilder.Entity<Person>()
            .Property(p => p.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        modelBuilder.Entity<Absence>()
            .Property(a => a.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        modelBuilder.Entity<BankHoliday>()
            .HasIndex(b => new { b.Region, b.Date })
            .IsUnique(); // Upsert target for the Nager bank-holiday sync.

        modelBuilder.Entity<BankHoliday>()
            .Property(b => b.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // The app always sets these, so they are non-nullable in C#, but their columns have
        // allowed NULL since they were created. Left that way on purpose (docs/specs/0003):
        // tightening a column is a schema change of its own, SQLite does it by rebuilding
        // the table, and a deployment may hold rows from before the app enforced the rule.
        modelBuilder.Entity<Person>().Property(p => p.Username).IsRequired(false);
        modelBuilder.Entity<Person>().Property(p => p.CreatedBy).IsRequired(false);
        modelBuilder.Entity<Absence>().Property(a => a.CreatedBy).IsRequired(false);
        modelBuilder.Entity<BankHoliday>().Property(b => b.Region).IsRequired(false);
        modelBuilder.Entity<BankHoliday>().Property(b => b.CreatedBy).IsRequired(false);
        modelBuilder.Entity<AuditEntry>().Property(e => e.Actor).IsRequired(false);
        modelBuilder.Entity<AuditEntry>().Property(e => e.Action).IsRequired(false);

        base.OnModelCreating(modelBuilder);
    }
}
