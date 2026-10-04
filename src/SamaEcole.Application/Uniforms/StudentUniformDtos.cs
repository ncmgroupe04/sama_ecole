namespace SamaEcole.Application.Uniforms;

/// <summary>Une ligne d'une demande de tenues : un modèle, une taille éventuelle, un nombre d'exemplaires.</summary>
public record StudentUniformRequestLine(Guid UniformItemId, Guid? UniformVariantId = null, int Quantity = 1);

/// <summary>
/// Une dotation telle que l'écran la lit. <c>BillingStatus</c> est la facturation ATTENDUE pour une ligne encore à
/// remettre (calculée depuis les lignes de frais de l'inscription), et la facturation FIGÉE pour une ligne remise.
/// <c>StockAvailable</c> est le disponible vivant du lot de la taille choisie, ou <c>null</c> sans taille.
/// </summary>
public record StudentUniformDto(
    Guid Id,
    Guid EnrollmentId,
    Guid SchoolYearId,
    Guid UniformItemId,
    string ItemName,
    Guid? UniformVariantId,
    string? SizeLabel,
    int Quantity,
    string Status,
    string BillingStatus,
    DateOnly? DeliveredOn,
    int? StockAvailable,
    uint RowVersion);

/// <summary>Un modèle à PROPOSER pour une inscription : payé (ligne de frais), adapté à l'élève, pas encore demandé.</summary>
public record UniformProposalDto(Guid UniformItemId, string ItemName, string Gender, int QuantityIncluded);

/// <summary>
/// Les tenues d'une inscription. <c>HasUnpaidBalance</c> est un badge d'INFORMATION au niveau de l'inscription : il
/// n'autorise ni n'interdit la remise (arbitrage 2). Le détail par catégorie de frais n'est pas fiable, la ventilation
/// des versements étant facultative.
/// </summary>
public record StudentUniformEnrollmentDto(
    Guid EnrollmentId,
    Guid SchoolYearId,
    string SchoolYearLabel,
    bool IsActiveYear,
    string ClassroomName,
    decimal TotalDue,
    decimal AmountPaid,
    decimal BalanceDue,
    bool HasUnpaidBalance,
    IReadOnlyList<StudentUniformDto> Lines,
    IReadOnlyList<UniformProposalDto> Proposals);

public record StudentUniformsResult(
    Guid StudentId,
    string StudentName,
    IReadOnlyList<StudentUniformEnrollmentDto> Enrollments);
