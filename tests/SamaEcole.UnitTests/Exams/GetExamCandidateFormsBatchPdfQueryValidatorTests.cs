using FluentAssertions;
using SamaEcole.Application.Exams.Queries.GetExamCandidateFormsBatchPdf;
using Xunit;

namespace SamaEcole.UnitTests.Exams;

public class GetExamCandidateFormsBatchPdfQueryValidatorTests
{
    private readonly GetExamCandidateFormsBatchPdfQueryValidator _validator = new();

    [Fact]
    public void ExamSessionId_Alone_Should_Pass()
    {
        _validator.Validate(new GetExamCandidateFormsBatchPdfQuery { ExamSessionId = Guid.NewGuid() })
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void ClassroomId_Alone_Should_Pass()
    {
        _validator.Validate(new GetExamCandidateFormsBatchPdfQuery { ClassroomId = Guid.NewGuid() })
            .IsValid.Should().BeTrue();
    }

    [Fact]
    public void No_Filter_At_All_Should_Fail()
    {
        // Sans filtre, le lot engloberait toute l'école : le lot doit être borné explicitement.
        _validator.Validate(new GetExamCandidateFormsBatchPdfQuery()).IsValid.Should().BeFalse();
    }
}
