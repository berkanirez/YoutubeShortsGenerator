using Microsoft.EntityFrameworkCore;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure;

public sealed class ShortsGeneratorDbContext : DbContext
{
    public ShortsGeneratorDbContext(DbContextOptions<ShortsGeneratorDbContext> options)
        : base(options)
    {
    }

    public DbSet<VideoJob> VideoJobs => Set<VideoJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VideoJob>(entity =>
        {
            entity.HasKey(job => job.Id);
            entity.Property(job => job.Topic).IsRequired().HasMaxLength(300);
            entity.Property(job => job.SourceScript);
            entity.Property(job => job.GeneratedScript);
            entity.Property(job => job.Status).HasConversion<string>().IsRequired().HasMaxLength(50);

            // SQLite has no native DateTimeOffset type and can't order by it directly;
            // store as UTC ticks so ORDER BY / comparisons work correctly.
            var dateTimeOffsetConverter = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, long>(
                v => v.UtcTicks,
                v => new DateTimeOffset(v, TimeSpan.Zero));

            entity.Property(job => job.CreatedAt).HasConversion(dateTimeOffsetConverter).IsRequired();
            entity.Property(job => job.UpdatedAt).HasConversion(dateTimeOffsetConverter).IsRequired();
            entity.Property(job => job.ErrorMessage);
            entity.Property(job => job.OutputDirectory);
            entity.Property(job => job.FinalVideoPath);
        });
    }
}
