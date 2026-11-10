using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Models;

namespace SchoolAdmission.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AdmissionClass> Classes => Set<AdmissionClass>();
    public DbSet<Hold> Holds => Set<Hold>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdmissionClass>(entity =>
        {
            entity.ToTable("classes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(64);
            entity.Property(x => x.Name).HasColumnName("name").IsRequired().HasMaxLength(200);
            entity.Property(x => x.TotalSeats).HasColumnName("total_seats").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        });

        modelBuilder.Entity<Hold>(entity =>
        {
            entity.ToTable("holds");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasMaxLength(64);
            entity.Property(x => x.ClassId).HasColumnName("class_id").IsRequired().HasMaxLength(64);
            entity.Property(x => x.StudentId).HasColumnName("student_id").IsRequired().HasMaxLength(64);
            entity.Property(x => x.Status).HasColumnName("status").IsRequired().HasMaxLength(20);
            entity.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
            entity.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
            entity.HasIndex(x => new { x.StudentId, x.ClassId });
            entity.HasIndex(x => new { x.ClassId, x.Status, x.ExpiresAt });
            entity.HasOne(x => x.Class)
                .WithMany(x => x.Holds)
                .HasForeignKey(x => x.ClassId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
