namespace Prumo.Application.Abstractions;

public interface ITextQualityValidator
{
    TextQualityResult Validate(string text);
}

public sealed record TextQualityResult(bool IsValid, string? Error, int CharacterCount, decimal PrintableCharacterRatio);
