﻿using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Réglage « cycles gérés » (<c>school_settings."ManagedCycles"</c>) : les cycles que l'établissement
    /// gère, texte séparé par des virgules comme <c>WorkingDays</c>. Réversible : Down supprime la colonne et
    /// son CHECK. Aucune table ni policy RLS touchée (<c>school_settings</c> est déjà sous RLS).
    ///
    /// REPRISE — on PRÉSERVE le comportement actuel : toutes les écoles reçoivent TOUS les cycles (valeur par
    /// défaut de la colonne), SAUF celles déjà passées par le profil Élémentaire, que trois écrans restreignent
    /// aujourd'hui à Maternelle + Primaire (classrooms.js, enrollments.js, exams.js). Leur donner tous les cycles
    /// ferait réapparaître Collège et Lycée chez elles.
    /// </summary>
    public partial class AddManagedCyclesToSchoolSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ManagedCycles",
                table: "school_settings",
                type: "character varying(60)",
                maxLength: 60,
                nullable: false,
                defaultValue: "Maternelle,Primaire,College,Lycee");

            // Exécutée par le rôle propriétaire (hors RLS) : elle voit toutes les écoles.
            migrationBuilder.Sql("""
                UPDATE school_settings
                   SET "ManagedCycles" = 'Maternelle,Primaire'
                 WHERE "ProfileEtablissement" = 'ElementairePrimaire';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_school_settings_managed_cycles_not_empty",
                table: "school_settings",
                sql: "\"ManagedCycles\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_school_settings_managed_cycles_not_empty",
                table: "school_settings");

            migrationBuilder.DropColumn(
                name: "ManagedCycles",
                table: "school_settings");
        }
    }
}
