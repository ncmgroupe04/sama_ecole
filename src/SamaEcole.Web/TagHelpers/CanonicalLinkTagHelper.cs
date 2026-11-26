using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace SamaEcole.Web.TagHelpers;

/// <summary>Force la canonique des pages publiques vers le domaine HTTPS sans www.</summary>
[HtmlTargetElement("link", Attributes = "rel=canonical")]
public sealed class CanonicalLinkTagHelper : TagHelper
{
    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = default!;

    public override int Order => int.MaxValue;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var path = ViewContext.HttpContext.Request.Path.Value ?? "/";
        output.Attributes.SetAttribute("href", HtmlEncoder.Default.Encode($"https://unikol.net{path}"));
    }
}
