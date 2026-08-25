using MediatR;
using System;

namespace SamaEcole.Application.Teachers.Commands.UnassignTeacher;

public record UnassignTeacherCommand(Guid TeacherId, Guid AssignmentId) : IRequest;
