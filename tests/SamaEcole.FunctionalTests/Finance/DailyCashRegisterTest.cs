using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Persistence;
using SamaEcole.Domain.Entities;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetDailyCashRegisterPdf;
using Xunit;
using SamaEcole.FunctionalTests.Common;
using MediatR;
using FluentAssertions;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Net;

namespace SamaEcole.FunctionalTests.Finance
{
    public class DailyCashRegisterTest(AuthApiFactory factory) : IClassFixture<AuthApiFactory>, IAsyncLifetime
    {
        private readonly HttpClient _client = factory.CreateClient();

        public Task InitializeAsync() => factory.ResetTestUsersAsync();
        public Task DisposeAsync() => Task.CompletedTask;

        private record Tokens(string AccessToken, string RefreshToken, int ExpiresIn);

        private async Task<Tokens> LoginAsync(string email, string password)
        {
            var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return (await response.Content.ReadFromJsonAsync<Tokens>())!;
        }

        [Fact]
        public async Task TestGetDailyCashRegister()
        {
            var tokens = await LoginAsync(AuthApiFactory.DirecteurEmail, AuthApiFactory.DirecteurPassword);
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

            var response = await _client.GetAsync("/api/v1/finance/daily-cash-register/pdf");
            
            var content = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue("Erreur: " + content);
        }
    }
}
