using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>Voir <see cref="ExamDossierConfiguration"/> pour le filtre tenant et la policy RLS.</summary>
public class ExamResultConfiguration : IEntityTypeConfiguration<ExamResult>
{
    public void Configure(EntityTypeBuilder<ExamResult> builder)
    {
        builder.ToTable("exam_results");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.SchoolId).IsRequired();

        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(r => r.Mention).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.AverageScore).HasPrecision(5, 2);

        // Au plus un résultat par dossier (Volume 3 DDS §5.10).
        builder.HasIndex(r => r.ExamDossierId).IsUnique();

        builder.HasOne(r => r.ExamDossier)
            .WithOne(d => d.Result)
            .HasForeignKey<ExamResult>(r => r.ExamDossierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(r => r.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
