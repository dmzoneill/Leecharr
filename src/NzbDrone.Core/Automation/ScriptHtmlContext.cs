#nullable enable
#pragma warning disable SA1300
using System.Collections.Generic;
using System.Linq;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace NzbDrone.Core.Automation;

public class ScriptHtmlContext
{
    private static readonly HtmlParser Parser = new();

    public HtmlDocumentWrapper parse(string html)
    {
        var document = Parser.ParseDocument(html ?? string.Empty);
        return new HtmlDocumentWrapper(document);
    }

    public class HtmlDocumentWrapper
    {
        private readonly IHtmlDocument _document;

        public HtmlDocumentWrapper(IHtmlDocument document)
        {
            _document = document;
        }

        public HtmlElementWrapper? select(string selector)
        {
            var el = _document.QuerySelector(selector);
            return el != null ? new HtmlElementWrapper(el) : null;
        }

        public List<HtmlElementWrapper> selectAll(string selector)
        {
            var elements = _document.QuerySelectorAll(selector);
            return elements.Select(e => new HtmlElementWrapper(e)).ToList();
        }
    }

    public class HtmlElementWrapper
    {
        private readonly AngleSharp.Dom.IElement _element;

        public HtmlElementWrapper(AngleSharp.Dom.IElement element)
        {
            _element = element;
        }

        public string text => _element.TextContent?.Trim() ?? string.Empty;

        public string innerHtml => _element.InnerHtml ?? string.Empty;

        public string outerHtml => _element.OuterHtml ?? string.Empty;

        public string? attr(string name) => _element.GetAttribute(name);

        public string? value => _element.GetAttribute("value");

        public HtmlElementWrapper? select(string selector)
        {
            var el = _element.QuerySelector(selector);
            return el != null ? new HtmlElementWrapper(el) : null;
        }

        public List<HtmlElementWrapper> selectAll(string selector)
        {
            var elements = _element.QuerySelectorAll(selector);
            return elements.Select(e => new HtmlElementWrapper(e)).ToList();
        }
    }
}
