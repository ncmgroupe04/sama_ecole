using System.IO;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;
using SamaEcole.Web.Filters;
using Xunit;

namespace SamaEcole.FunctionalTests.Platform;

/// <summary>
/// <see cref="PdfPreviewDispositionFilter"/> — quand le fetch de l'aperçu envoie
/// <c>X-Pdf-Preview: 1</c>, la réponse PDF part en <c>text/plain</c> inline (sans nom de fichier)
/// pour qu'Internet Download Manager &amp; consorts cessent de happer le fetch et de le laisser vide
/// (204). Un téléchargement normal (sans l'en-tête) n'est pas touché.
/// </summary>
public class PdfPreviewDispositionFilterTests
{
    private static ResultExecutingContext MakeContext(IActionResult result, bool withPreviewHeader)
    {
        var httpContext = new DefaultHttpContext();
        if (withPreviewHeader)
        {
            httpContext.Request.Headers[PdfPreviewDispositionFilter.PreviewHeader] = "1";
        }

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ResultExecutingContext(actionContext, new List<IFilterMetadata>(), result, controller: new object());
    }

    private static async Task RunAsync(ResultExecutingContext ctx)
    {
        var filter = new PdfPreviewDispositionFilter();
        await filter.OnResultExecutionAsync(ctx, () =>
            Task.FromResult(new ResultExecutedContext(ctx, ctx.Filters, ctx.Result, controller: new object())));
    }

    [Fact]
    public async Task With_Header_A_Pdf_FileContentResult_Is_Served_As_Text_Plain_Inline()
    {
        var ctx = MakeContext(new FileContentResult(new byte[] { 0x25, 0x50, 0x44, 0x46 }, "application/pdf"), withPreviewHeader: true);

        await RunAsync(ctx);

        var rewritten = ctx.Result.Should().BeOfType<FileContentResult>().Subject;
        rewritten.ContentType.Should().Be("text/plain");
        rewritten.FileContents.Should().Equal(0x25, 0x50, 0x44, 0x46);
        rewritten.FileDownloadName.Should().BeNullOrEmpty("aucun nom de fichier ne doit trahir un PDF");
        ctx.HttpContext.Response.Headers[HeaderNames.ContentDisposition].ToString().Should().Be("inline");
    }

    [Fact]
    public async Task Without_Header_A_Pdf_Response_Is_Left_Untouched()
    {
        var original = new FileContentResult(new byte[] { 0x25, 0x50, 0x44, 0x46 }, "application/pdf")
        {
            FileDownloadName = "Bulletin.pdf"
        };
        var ctx = MakeContext(original, withPreviewHeader: false);

        await RunAsync(ctx);

        ctx.Result.Should().BeSameAs(original);
        original.ContentType.Should().Be("application/pdf");
    }

    [Fact]
    public async Task With_Header_A_Non_Pdf_File_Response_Is_Left_Untouched()
    {
        var original = new FileContentResult(new byte[] { 1, 2, 3 },
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var ctx = MakeContext(original, withPreviewHeader: true);

        await RunAsync(ctx);

        ctx.Result.Should().BeSameAs(original);
    }

    [Fact]
    public async Task With_Header_A_Plain_ObjectResult_Is_Left_Untouched()
    {
        var original = new OkObjectResult(new { hello = "world" });
        var ctx = MakeContext(original, withPreviewHeader: true);

        await RunAsync(ctx);

        ctx.Result.Should().BeSameAs(original);
    }

    [Fact]
    public async Task With_Header_A_Pdf_FileStreamResult_Is_Served_As_Text_Plain()
    {
        var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 });
        var ctx = MakeContext(new FileStreamResult(stream, "application/pdf"), withPreviewHeader: true);

        await RunAsync(ctx);

        ctx.Result.Should().BeOfType<FileStreamResult>()
            .Which.ContentType.Should().Be("text/plain");
        ctx.HttpContext.Response.Headers[HeaderNames.ContentDisposition].ToString().Should().Be("inline");
    }
}
