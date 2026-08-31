using System.IO;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using SamaEcole.Web.Filters;
using Xunit;

namespace SamaEcole.FunctionalTests.Platform;

/// <summary>
/// <see cref="EmptyFileResultGuardFilter"/> — filet global qui empêche qu'une réponse fichier de
/// 0 octet (générateur PDF/xlsx ayant produit du vide) parte en <c>200</c> muet. Le client afficherait
/// sinon « aperçu impossible, document vide (0 octet) » sur un cul-de-sac, sans trace serveur.
/// </summary>
public class EmptyFileResultGuardFilterTests
{
    private static EmptyFileResultGuardFilter Filter() =>
        new(NullLogger<EmptyFileResultGuardFilter>.Instance);

    private static ResultExecutingContext MakeContext(IActionResult result)
    {
        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-123" };
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/api/v1/finance/sessions/abc/closing-report";

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ResultExecutingContext(actionContext, new List<IFilterMetadata>(), result, controller: new object());
    }

    /// <summary>Exécute le filtre et signale si le pipeline (le « résultat suivant ») a bien été appelé.</summary>
    private static async Task<bool> RunAsync(EmptyFileResultGuardFilter filter, ResultExecutingContext ctx)
    {
        var nextCalled = false;
        await filter.OnResultExecutionAsync(ctx, () =>
        {
            nextCalled = true;
            return Task.FromResult(new ResultExecutedContext(ctx, ctx.Filters, ctx.Result, controller: new object()));
        });
        return nextCalled;
    }

    [Fact]
    public async Task Empty_FileContentResult_Becomes_A_Normalized_500()
    {
        var ctx = MakeContext(new FileContentResult(Array.Empty<byte>(), "application/pdf"));

        var nextCalled = await RunAsync(Filter(), ctx);

        var error = ctx.Result.Should().BeOfType<ObjectResult>().Subject;
        error.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);

        // Le corps est un type anonyme interne à SamaEcole.Web : on le relit via sa forme sérialisée
        // (mêmes clés que le format d'erreur normalisé — code / message / details / traceId).
        using var body = JsonSerializer.SerializeToDocument(error.Value);
        body.RootElement.GetProperty("code").GetString().Should().Be(EmptyFileResultGuardFilter.ErrorCode);
        body.RootElement.GetProperty("traceId").GetString().Should().Be("trace-123");
        body.RootElement.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();

        nextCalled.Should().BeTrue("le résultat de remplacement doit quand même être exécuté");
    }

    [Fact]
    public async Task Empty_Seekable_FileStreamResult_Becomes_A_Normalized_500()
    {
        var ctx = MakeContext(new FileStreamResult(new MemoryStream(Array.Empty<byte>()), "application/pdf"));

        await RunAsync(Filter(), ctx);

        ctx.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
    }

    [Fact]
    public async Task NonEmpty_FileContentResult_Passes_Through_Untouched()
    {
        var original = new FileContentResult(new byte[] { 0x25, 0x50, 0x44, 0x46 }, "application/pdf");
        var ctx = MakeContext(original);

        var nextCalled = await RunAsync(Filter(), ctx);

        ctx.Result.Should().BeSameAs(original);
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task A_Plain_ObjectResult_Is_Never_Touched()
    {
        var original = new OkObjectResult(new { hello = "world" });
        var ctx = MakeContext(original);

        await RunAsync(Filter(), ctx);

        ctx.Result.Should().BeSameAs(original);
    }
}
