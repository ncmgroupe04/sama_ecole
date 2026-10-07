namespace SamaEcole.Domain.Entities;

/// <summary>Personne habilitée à récupérer un pensionnaire. Stockée en JSON dans <see cref="BoardingEnrollment"/>.</summary>
public class AllowedExitPerson
{
    public string Name { get; set; } = string.Empty;

    public string Relationship { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;
}
