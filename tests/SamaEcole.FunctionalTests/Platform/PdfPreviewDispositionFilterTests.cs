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
/// <see cref="PdfPreviewDispositionFilter"/> — quand le fetch de la modale d'aperçu envoie
/// <c>X-Pdf-Preview: 1</c>, la réponse PDF part en <c>application/octet-stream</c> inline pour qu'un
/// gestionnaire de téléchargement (IDM &amp; co.) cesse d'intercepter le fetch. Un téléchargement
/// normal (sans l'en-tête) n'est pas touché.
/// </summary>
public class PdfPreviewDispositionFilterTests
{
    private static ResultExecutingContext MakeContext(IActionResult result, bool withPreviewHeader)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/api/v1/enrollments/abc/certificate/pdf";
        if (withPreviewHeader)
        {
            httpContext.Request.Headers[PdfPreviewDispositionFilter.PreviewHeader] = "1";
        }

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        return new ResultExecutingContext(actionContext, new List<IFilterMetadata>(), result, controller: new object());
    }

    private static Task Run(ResultExecutingContext ctx) =>
        new PdfPreviewDispositionFilter().OnResultExecutionAsync(ctx, () =>
            Task.FromResult(new ResultExecutedContext(ctx, ctx.Filters, ctx.Result, controller: new object())));

    [Fact]
    public async Task With_Preview_Header_A_Pdf_FileContentResult_Is_Served_As_Opaque_Inline()
    {
        var ctx = MakeContext(new FileContentResult(new byte[] { 0x25, 0x50, 0x44, 0x46 }, "application/pdf")
        {
            FileDownloadName = "Certificat.pdf"
        }, withPreviewHeader: true);

        await Run(ctx);

        var file = ctx.Result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("application/octet-stream");
        file.FileDownloadName.Should().BeNullOrEmpty("aucun nom de fichier à « attraper »");
        ctx.HttpContext.Response.Headers[HeaderNames.ContentDisposition].ToString().Should().Be("inline");
        file.FileContents.Should().Equal(0x25, 0x50, 0x44, 0x46);
    }

    [Fact]
    public async Task With_Preview_Header_A_Pdf_FileStreamResult_Is_Served_As_Opaque()
    {
        var ctx = MakeContext(new FileStreamResult(new MemoryStream(new byte[] { 1, 2, 3 }), "application/pdf"),
            withPreviewHeader: true);

        await Run(ctx);

        ctx.Result.Should().BeOfType<FileStreamResult>()
            .Which.ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task Without_The_Header_A_Pdf_Download_Is_Left_Untouched()
    {
        var original = new FileContentResult(new byte[] { 0x25, 0x50, 0x44, 0x46 }, "application/pdf")
        {
            FileDownloadName = "Certificat.pdf"
        };
        var ctx = MakeContext(original, withPreviewHeader: false);

        await Run(ctx);

        ctx.Result.Should().BeSameAs(original);
        ((FileContentResult)ctx.Result).ContentType.Should().Be("application/pdf");
        ctx.HttpContext.Response.Headers.ContainsKey(HeaderNames.ContentDisposition).Should().BeFalse();
    }

    [Fact]
    public async Task With_The_Header_A_NonPdf_File_Is_Left_Untouched()
    {
        // Un export xlsx passant par le même chemin ne doit pas être maquillé en octet-stream.
        var original = new FileContentResult(new byte[] { 1, 2, 3 },
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var ctx = MakeContext(original, withPreviewHeader: true);

        await Run(ctx);

        ctx.Result.Should().BeSameAs(original);
    }

    [Fact]
    public async Task With_The_Header_A_Plain_ObjectResult_Is_Left_Untouched()
    {
        var original = new OkObjectResult(new { ok = true });
        var ctx = MakeContext(original, withPreviewHeader: true);

        await Run(ctx);

        ctx.Result.Should().BeSameAs(original);
    }
}
