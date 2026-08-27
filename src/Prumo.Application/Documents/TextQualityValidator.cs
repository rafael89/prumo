using Prumo.Application.Abstractions;
using System.Text;

namespace Prumo.Application.Documents;

public sealed class TextQualityValidator : ITextQualityValidator
{
    private readonly TextQualityOptions options;

    public TextQualityValidator(TextQualityOptions options)
    {
        this.options = options;
    }

    public TextQualityResult Validate(string text)
    {
        var normalized = text?.Trim() ?? string.Empty;
        var characterCount = normalized.Length;
        if (characterCount == 0) return new(false, "Texto vazio.", 0, 0);

        var printableCount = normalized.Count(character => !char.IsControl(character) || character is '\n' or '\r' or '\t');
        var ratio = (decimal)printableCount / characterCount;
        if (characterCount < options.MinimumCharacters) return new(false, "Texto insuficiente para análise.", characterCount, ratio);
        if (ratio < options.MinimumPrintableRatio) return new(false, "Texto possui caracteres inválidos em excesso.", characterCount, ratio);

        return new(true, null, characterCount, ratio);
    }
}

public sealed class TextQualityOptions
{
    public int MinimumCharacters { get; set; } = 100;
    public decimal MinimumPrintableRatio { get; set; } = 0.7m;
}
