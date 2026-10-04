using MediatR;

namespace SamaEcole.Application.Uniforms.Queries.GetStudentUniforms;

/// <summary>Les tenues d'un élève, inscription par inscription (inscriptions annulées exclues), avec badge de solde et propositions.</summary>
public record GetStudentUniformsQuery(Guid StudentId) : IRequest<StudentUniformsResult>;
