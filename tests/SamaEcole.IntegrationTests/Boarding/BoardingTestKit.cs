using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>Tenant courant simulé pour les Handlers qui lisent <see cref="ITenantProvider"/>.</summary>
internal sealed class BoardingTenant(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}
