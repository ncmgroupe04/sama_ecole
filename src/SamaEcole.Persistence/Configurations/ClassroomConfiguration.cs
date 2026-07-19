using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Le Global Query Filter (SchoolId + IsDeleted) est appliqué automatiquement par
/// ApplicationDbContext.OnModelCreating à toute entité ITenantEntity — ne pas en ajouter un ici.
/// La policy RLS PostgreSQL équivalente est posée par la migration AddClassrooms, qui inscrit aussi
/// `classrooms` dans TenantTables (AGENTS.md règle #2 : les DEUX protections, jamais une seule).
/// </summary>
public class ClassroomConfiguration : IEntityTypeConfiguration<Classroom>
{
    public void Configure(EntityTypeBuilder<Classroom> builder)
    {
        builder.ToTable("classrooms");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.SchoolId).IsRequired();

        // Verrou optimiste xmin (AGENTS.md règle #5) : UpdateClassroomCommand/DeleteClassroomCommand
        // en dépendent pour refuser en 409 une écriture sur une classe modifiée entre-temps, comme
        // Grade et Enrollment. Propriété fantôme, aucune migration requise (convention Npgsql).
        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(c => c.Name).IsRequired().HasMaxLength(50);
        builder.Property(c => c.Level).IsRequired().HasMaxLength(50);

        // Deux classes ne peuvent pas porter le même nom dans la même école — mais « CM2 A » peut
        // évidemment exister dans deux écoles différentes. Le soft delete fait partie de la clé :
        // sans lui, on ne pourrait jamais recréer une classe portant le nom d'une classe archivée.
        builder.HasIndex(c => new { c.SchoolId, c.Name, c.IsDeleted }).IsUnique();
        builder.HasIndex(c => c.SchoolId);

        // Restrict : on ne supprime jamais physiquement une école (AGENTS.md règle #6).
        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(c => c.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
