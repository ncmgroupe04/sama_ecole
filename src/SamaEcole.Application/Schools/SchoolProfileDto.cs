namespace SamaEcole.Application.Schools;

/// <summary>
/// Identité de l'établissement (ticket configuration Directeur) : les champs de <c>School</c> que le
/// Directeur entretient — nom, adresse, téléphone, logo. Ils alimentent l'en-tête et le bas du reçu
/// d'inscription (JGK-E02) et les écrans. À distinguer de <c>SchoolSettingsDto</c> (barème, formats de
/// matricule, format de date…), qui porte les RÉGLAGES et non l'identité.
/// </summary>
public record SchoolProfileDto(
    string Name,
    string? Address,
    string? Phone,
    string? LogoUrl,

    // En-tête administratif du bulletin (IA / IEF / LYCEE DE) — voir School pour la sémantique.
    string? InspectionAcademie,
    string? InspectionEducationFormation,
    string? NomLycee,

    // Coordonnées et mentions légales de l'en-tête du reçu (NINEA / RCCM) — voir School.
    string? Email,
    string? Ninea,
    string? RegistreCommerce,

    // Annuaire public (B2C) — voir School.IsPubliclyListed. Ces champs ne sortent JAMAIS d'ici vers un
    // visiteur anonyme : l'annuaire a son propre contrat (PublicSchoolDto), servi depuis une vue
    // distincte. Ils n'apparaissent dans ce DTO que pour que le Directeur pilote sa propre fiche.
    bool IsPubliclyListed = false,
    string? City = null,
    string? Region = null,
    string? PublicDescription = null,

    // Identification réglementaire SIMEN (module Intégration étatique, JGK-M05) — voir School pour la
    // sémantique. Sans NationalSchoolCode, l'export Planète refuse de s'exécuter et la génération d'un
    // IEN provisoire échoue : ce sont les premiers champs à renseigner pour ouvrir le module.
    string? NationalSchoolCode = null,
    string? MinistryAuthorizationNumber = null,
    string? SchoolDistrictCode = null,
    decimal? GpsLatitude = null,
    decimal? GpsLongitude = null,

    // Chaîne d'affichage « lat, lon » calculée (culture invariante), null si l'une des deux manque —
    // pratique pour l'écran, jamais une donnée persistée. Voir School.GpsCoordinates.
    string? GpsCoordinates = null);
