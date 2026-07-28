using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SamaEcole.FunctionalTests.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace SamaEcole.FunctionalTests.Students;

/// <summary>
/// Feature B — PUT /api/v1/students/{id}/photo contre un vrai PostgreSQL. Route inédite : couvre le
/// dépôt, le retrait, le verrouillage optimiste (règle #5) et la garde de rôle, sans lesquels une
/// régression silencieuse effacerait des photos ou laisserait la Finance modifier une fiche élève.
/// </summary>
public class StudentPhotoEndpointsTests : IClassFixture<AuthApiFactory>, IAsyncLifetime
{
    private readonly AuthApiFactory _factory;
    private readonly HttpClient _client;

    public StudentPhotoEndpointsTests(AuthApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    }

    public Task InitializeAsync() => _factory.ResetTestUsersAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private record Tokens(string AccessToken, int ExpiresIn);
    private record ClassroomDto(Guid Id, string Name, string Level, int Capacity, int StudentCount);
    private record StudentCreated(Guid Id, string Matricule);
    private record StudentIdentityDto(Guid Id, string? PhotoUrl, string? PhotoDisplayUrl, uint RowVersion);
    private record StudentDetailDto(StudentIdentityDto Identity);
    private record SetPhotoResult(Guid StudentId, uint RowVersion, string? PhotoDisplayUrl);

    /// <summary>1×1 pixel JPEG minimal — suffit à exercer le décodage base64, pas besoin d'une vraie photo.</summary>
    private const string TinyJpegBase64 =
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAj/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAAX/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCdABmX/9k=";

    private async Task<string> TokenAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Tokens>())!.AccessToken;
    }

    private Task<string> DirecteurTokenAsync() =>
        TokenAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);

    private Task<string> FinanceTokenAsync() =>
        TokenAsync(AuthApiFactory.FinanceEmail, AuthApiFactory.FinancePassword);

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return await _client.SendAsync(request);
    }

    private async Task<ClassroomDto> CreateClassroomAsync(string token, string name)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/classrooms", token,
            new { name, level = "Primaire", capacity = 40 });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ClassroomDto>())!;
    }

    private async Task<StudentCreated> CreateStudentAsync(string token, Guid classroomId)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/v1/students", token, new
        {
            fullName = "Photo Test",
            birthDate = "2015-03-12",
            birthPlace = "Dakar",
            gender = "F",
            classroomId
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<StudentCreated>())!;
    }

    private async Task<StudentIdentityDto> FetchStudentAsync(string token, Guid id)
    {
        var response = await SendAsync(HttpMethod.Get, $"/api/v1/students/{id}", token);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = (await response.Content.ReadFromJsonAsync<StudentDetailDto>())!;
        return detail.Identity;
    }

    [Fact]
    public async Task A_Directeur_Can_Upload_A_Photo_And_It_Becomes_The_Display_Url()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CI Photo Upload");
        var created = await CreateStudentAsync(directeur, classroom.Id);
        var student = await FetchStudentAsync(directeur, created.Id);

        student.PhotoDisplayUrl.Should().BeNull("aucune photo n'a encore été déposée");

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/students/{created.Id}/photo", directeur,
            new { photoData = TinyJpegBase64, rowVersion = student.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = (await response.Content.ReadFromJsonAsync<SetPhotoResult>())!;
        result.PhotoDisplayUrl.Should().StartWith("data:image/jpeg;base64,");

        var refreshed = await FetchStudentAsync(directeur, created.Id);
        refreshed.PhotoDisplayUrl.Should().Be(result.PhotoDisplayUrl);
    }

    [Fact]
    public async Task Removing_The_Uploaded_Photo_Falls_Back_To_The_External_Url()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CI Photo Fallback");
        var created = await CreateStudentAsync(directeur, classroom.Id);
        var student = await FetchStudentAsync(directeur, created.Id);

        // Pose une URL externe ET une photo téléversée : la photo doit l'emporter à l'affichage tant
        // qu'elle existe (voir PhotoDisplay.ToDisplayUrl).
        var updateResponse = await SendAsync(HttpMethod.Put, $"/api/v1/students/{created.Id}", directeur, new
        {
            fullName = "Photo Test",
            birthDate = "2015-03-12",
            birthPlace = "Dakar",
            gender = "F",
            classroomId = classroom.Id,
            photoUrl = "https://exemple.sn/photo.jpg",
            guardianName = (string?)null,
            guardianPhone = (string?)null,
            rowVersion = student.RowVersion
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterUrl = await FetchStudentAsync(directeur, created.Id);
        afterUrl.PhotoUrl.Should().Be("https://exemple.sn/photo.jpg");
        afterUrl.PhotoDisplayUrl.Should().Be("https://exemple.sn/photo.jpg", "aucune photo téléversée pour l'instant");

        var uploadResponse = await SendAsync(HttpMethod.Put, $"/api/v1/students/{created.Id}/photo", directeur,
            new { photoData = TinyJpegBase64, rowVersion = afterUrl.RowVersion });
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterUpload = await FetchStudentAsync(directeur, created.Id);
        afterUpload.PhotoUrl.Should().Be("https://exemple.sn/photo.jpg", "l'URL externe reste stockée, même masquée à l'affichage");
        afterUpload.PhotoDisplayUrl.Should().StartWith("data:image/jpeg;base64,", "la photo téléversée l'emporte sur l'URL externe");

        // Retrait (photoData null) : l'affichage retombe sur l'URL externe, jamais perdue.
        var removeResponse = await SendAsync(HttpMethod.Put, $"/api/v1/students/{created.Id}/photo", directeur,
            new { photoData = (string?)null, rowVersion = afterUpload.RowVersion });
        removeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterRemove = await FetchStudentAsync(directeur, created.Id);
        afterRemove.PhotoDisplayUrl.Should().Be("https://exemple.sn/photo.jpg");
    }

    [Fact]
    public async Task A_Finance_Must_Not_Upload_A_Student_Photo()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CI Photo Protégée");
        var created = await CreateStudentAsync(directeur, classroom.Id);
        var student = await FetchStudentAsync(directeur, created.Id);

        var finance = await FinanceTokenAsync();
        var response = await SendAsync(HttpMethod.Put, $"/api/v1/students/{created.Id}/photo", finance,
            new { photoData = TinyJpegBase64, rowVersion = student.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Uploading_A_Photo_With_A_Stale_RowVersion_Should_Return_409()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CI Photo Concurrente");
        var created = await CreateStudentAsync(directeur, classroom.Id);
        var staleVersion = (await FetchStudentAsync(directeur, created.Id)).RowVersion;

        var firstUpload = await SendAsync(HttpMethod.Put, $"/api/v1/students/{created.Id}/photo", directeur,
            new { photoData = TinyJpegBase64, rowVersion = staleVersion });
        firstUpload.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondUpload = await SendAsync(HttpMethod.Put, $"/api/v1/students/{created.Id}/photo", directeur,
            new { photoData = TinyJpegBase64, rowVersion = staleVersion });

        secondUpload.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Uploading_An_Invalid_Base64_Photo_Should_Return_422()
    {
        var directeur = await DirecteurTokenAsync();
        var classroom = await CreateClassroomAsync(directeur, "CI Photo Invalide");
        var created = await CreateStudentAsync(directeur, classroom.Id);
        var student = await FetchStudentAsync(directeur, created.Id);

        var response = await SendAsync(HttpMethod.Put, $"/api/v1/students/{created.Id}/photo", directeur,
            new { photoData = "pas du base64 valide !!!", rowVersion = student.RowVersion });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
