using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SamaEcole.Persistence.Configurations;

/// <summary>
/// Voir <see cref="ExamDossierConfiguration"/> pour le filtre tenant et la policy RLS.
///
/// L'unicité (SchoolId, SchoolYearId, ExamType, Series) est posée en SQL brut dans la migration
/// (avec COALESCE sur Series) plutôt qu'ici : HasIndex ne sait pas exprimer une expression de colonne,
/// et NULL n'est jamais égal à NULL dans un index UNIQUE standard — deux sessions CFEE de la même
/// année (Series toujours nul) ne seraient donc jamais détectées comme doublon sans ce contournement.
/// </summary>
public class ExamSessionConfiguration : IEntityTypeConfiguration<ExamSession>
{
    public void Configure(EntityTypeBuilder<ExamSession> builder)
    {
        builder.ToTable("exam_sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.SchoolId).IsRequired();

        builder.Property<uint>("xmin").IsRowVersion();

        builder.Property(s => s.ExamType)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(s => s.Series).HasMaxLength(20);
        builder.Property(s => s.CenterName).HasMaxLength(150);

        builder.Property(s => s.Status)
            .HasConversion<string>()
            .HasMaxLength(25)
            .IsRequired()
            .HasDefaultValue(ExamSessionStatus.EnPreparation);

        builder.Property(s => s.NextCandidateSeq).IsRequired().HasDefaultValue(0);

        builder.HasIndex(s => new { s.SchoolId, s.SchoolYearId });

        builder.HasOne(s => s.SchoolYear)
            .WithMany()
            .HasForeignKey(s => s.SchoolYearId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<School>()
            .WithMany()
            .HasForeignKey(s => s.SchoolId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
