using GH_Net.Service.Abstractions;

namespace GH_Net.Service.Implements;

public sealed class ZplTemplateRenderer : IZplTemplateRenderer
{
    public const string CodePlaceholder = "{{CODE}}";

    public void Validate(string template)
    {
        if (string.IsNullOrWhiteSpace(template)) throw new InvalidOperationException("ZPL template is required.");
        if (!template.Contains("^XA", StringComparison.Ordinal) || !template.Contains("^XZ", StringComparison.Ordinal))
            throw new InvalidOperationException("ZPL template must contain ^XA and ^XZ.");
        int first = template.IndexOf(CodePlaceholder, StringComparison.Ordinal);
        if (first < 0 || template.IndexOf(CodePlaceholder, first + CodePlaceholder.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("ZPL template must contain exactly one {{CODE}} placeholder.");
    }

    public string Render(string template, string code)
    {
        Validate(template);
        if (string.IsNullOrWhiteSpace(code)) throw new InvalidOperationException("Label code is required.");
        if (code.IndexOfAny(['^', '~', '\r', '\n', '\0']) >= 0)
            throw new InvalidOperationException("Label code contains unsupported ZPL control characters.");
        return template.Replace(CodePlaceholder, code, StringComparison.Ordinal);
    }
}
