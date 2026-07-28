using MediatR;

namespace SamaEcole.Application.Schools.Commands.UpdateCurrentSchool;

/// <summary>
/// PUT /schools/current — le Directeur corrige l'identité de son établissement. Aucun <c>schoolId</c>
/// dans la charge utile : l'établissement visé est TOUJOURS celui du JWT (AGENTS.md règle #10).
/// </summary>
public record UpdateCurrentSchoolCommand(
    string Name,
    string? Address,
    string? Phone,
    string? LogoUrl,

    // Bloc « Informations Académiques & Administration » du formulaire Établissement : alimente
    // l'en-tête du bulletin (IA / IEF / LYCEE DE). Facultatifs — une ligne vide s'imprime vide.
    string? InspectionAcademie,
    string? InspectionEducationFormation,
    string? NomLycee,

    // Bloc « Coordonnées & mentions légales » : e-mail de contact et identifiants d'entreprise
    // imprimés dans l'en-tête du reçu. Facultatifs — une mention absente ne s'imprime pas.
    string? Email = null,
    string? Ninea = null,
    string? RegistreCommerce = null) : IRequest<SchoolProfileDto>;
