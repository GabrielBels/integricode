using Integricode.Domain;

namespace Integricode.Tests.Domain;

public class ReviewLanguageTests
{
    [Theory]
    [InlineData("Corrige o limite da busca para que exatamente 100 números sejam aceitos.", ReviewLanguage.PortugueseBrazil)]
    [InlineData("Fixes the search limit so that exactly 100 numbers are accepted.", ReviewLanguage.English)]
    [InlineData("PR de teste: não fazer merge.\n\n🤖 Generated with [Claude Code](https://claude.example.test)", ReviewLanguage.PortugueseBrazil)]
    [InlineData("<!-- Describe the change and link the issue this pull request is for. -->\nAjusta a verificação do limite de números.", ReviewLanguage.PortugueseBrazil)]
    [InlineData("Ajusta `if the value is not in the list` na busca.", ReviewLanguage.PortugueseBrazil)]
    [InlineData("Fixes it; see https://github.com/octo/shop/issues/1 and https://docs.example.com/a", ReviewLanguage.English)]
    [InlineData("Fixes it, ping dev@team.com.br", ReviewLanguage.English)]
    [InlineData("fix: trata quando o campo for vazio", ReviewLanguage.PortugueseBrazil)]
    [InlineData("Ajusta is_valid e has_items na busca", ReviewLanguage.PortugueseBrazil)]
    public void The_description_decides_the_language(string description, string expected)
    {
        Assert.Equal(expected, ReviewLanguage.Detect([description]));
    }

    [Fact]
    public void The_description_and_the_commits_are_counted_together()
    {
        string[] texts =
        [
            "Fixes the cart total.",
            "fix(cart): apply the discount to the total",
            "test(cart): cover the discount with a test",
        ];

        Assert.Equal(ReviewLanguage.English, ReviewLanguage.Detect(texts));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("REQ-150")]
    [InlineData("```csharp\nif (numeros.Count > Maximo) return the;\n```")]
    [InlineData("the de")]
    public void Texts_that_do_not_tell_give_no_language(string? text)
    {
        Assert.Null(ReviewLanguage.Detect([text]));
    }
}
