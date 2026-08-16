using Api.Data.Models;

using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<BankHoliday> BankHolidays => Set<BankHoliday>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>()
            .HasIndex(p => p.Username)
            .IsUnique();

        modelBuilder.Entity<Person>()
            .HasIndex(p => p.Color)
            .IsUnique();

        modelBuilder.Entity<Absence>()
            .HasIndex(a => new { a.PersonId, a.StartDate, a.EndDate });

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

        // Every lookup is "this person's own", newest first, so that's the one index worth
        // having; the read query below sorts by Id for the same tie-breaking reason
        // AuditLog does.
        modelBuilder.Entity<Notification>()
            .HasIndex(n => new { n.PersonId, n.Id });

        modelBuilder.Entity<Notification>()
            .Property(n => n.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        base.OnModelCreating(modelBuilder);
    }
}
