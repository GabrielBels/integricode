using Integricode.Agent;
using Integricode.Commands;
using Integricode.Domain;
using Integricode.Requirements;
using Integricode.Tests.Fakes;

namespace Integricode.Tests.Commands;

/// <summary>The reasons come from their producers wherever a test can build them, so rewording one breaks a test here.</summary>
public sealed class CoverageReasonTextTests : IDisposable
{
    private readonly TempRepository repository = new();

    public CoverageReasonTextTests()
    {
        repository.Write("docs/requirements.md", "# Requirements\n\n## [REQ-001] Cart Totals\n");
    }

    private static string Portuguese(string reason) => CoverageReasonText.Localize(reason, ReviewLanguage.PortugueseBrazil);

    private Task<RequirementReadResult> ReadReqEntry(string requirementsFile, string id) =>
        new ReqEntrySource(new PathGuard(repository.Root), requirementsFile)
            .ReadAsync(new RequirementLink(RequirementKind.ReqEntry, id, RequirementLinkSource.Title), CancellationToken.None);

    [Fact]
    public void The_fixed_reasons_are_translated()
    {
        Assert.Equal("nenhum requisito vinculado", Portuguese(ReviewWorkflow.NoLinkReason));
        Assert.Equal("pull request de um fork; o Integricode revisa só pull requests cuja branch está neste repositório", Portuguese(CommentCommand.ForkReason));
        Assert.StartsWith("erro de configuração do provedor do modelo: confira --model-provider", Portuguese(ReviewCommand.ProviderConfigurationError));
        Assert.Equal("a resposta do modelo não seguiu o schema do resultado nem depois de um pedido de correção", Portuguese(ReviewWorkflow.SchemaFailureReason));
    }

    [Fact]
    public void The_budget_reasons_are_translated()
    {
        var calls = new ContextBudget(maxToolCalls: 0);
        calls.TryStartCall();
        var characters = new ContextBudget(maxCharacters: 1);
        characters.TryConsume(5);

        Assert.Equal("orçamento de contexto atingido (0 chamadas de ferramenta)", Portuguese(calls.ReachedReason!));
        Assert.Equal("orçamento de contexto atingido (1 caracteres)", Portuguese(characters.ReachedReason!));
    }

    [Fact]
    public async Task The_req_entry_reasons_are_translated()
    {
        var missingFile = await ReadReqEntry("docs/missing.md", "REQ-001");
        var missingEntry = await ReadReqEntry("docs/requirements.md", "REQ-009");
        var refused = await ReadReqEntry("../outside.md", "REQ-001");

        Assert.Equal("arquivo de requisitos docs/missing.md não encontrado no head SHA", Portuguese(missingFile.UnreadableReason!));
        Assert.Equal("REQ-009 não encontrado em docs/requirements.md", Portuguese(missingEntry.UnreadableReason!));
        Assert.StartsWith("arquivo de requisitos ../outside.md recusado: ", Portuguese(refused.UnreadableReason!));
    }

    [Theory]
    [InlineData(RequirementKind.JiraIssue, "SHOP-12", "fonte do Jira não configurada para SHOP-12")]
    [InlineData(RequirementKind.ReqEntry, "REQ-001", "nenhuma fonte de requisito configurada para REQ-001")]
    public async Task A_link_without_a_source_is_translated(RequirementKind kind, string id, string expected)
    {
        var read = await new RequirementSources([]).ReadAsync(new RequirementLink(kind, id, RequirementLinkSource.Title), CancellationToken.None);

        Assert.Equal(expected, Portuguese(read.UnreadableReason!));
    }

    // The Jira causes are the texts JiraRequirementSource writes; JiraReviewCoverageTests checks that it writes them.
    [Theory]
    [InlineData("Jira issue SHOP-12 unreadable: authentication failed (HTTP 401), check JIRA_EMAIL and JIRA_API_TOKEN", "issue do Jira SHOP-12 ilegível: falha de autenticação (HTTP 401), confira JIRA_EMAIL e JIRA_API_TOKEN")]
    [InlineData("Jira issue SHOP-12 unreadable: access denied (HTTP 403) for the Jira account", "issue do Jira SHOP-12 ilegível: acesso negado (HTTP 403) para a conta do Jira")]
    [InlineData("Jira issue SHOP-12 unreadable: not found or not visible to the Jira account (HTTP 404)", "issue do Jira SHOP-12 ilegível: não encontrada ou invisível para a conta do Jira (HTTP 404)")]
    [InlineData("Jira issue SHOP-12 unreadable: HTTP 500", "issue do Jira SHOP-12 ilegível: HTTP 500")]
    [InlineData("Jira issue SHOP-12 unreadable: the request timed out", "issue do Jira SHOP-12 ilegível: a requisição expirou")]
    [InlineData("Jira issue SHOP-12 unreadable: Jira unreachable (HttpRequestException)", "issue do Jira SHOP-12 ilegível: Jira inacessível (HttpRequestException)")]
    [InlineData("Jira issue SHOP-12 unreadable: invalid response", "issue do Jira SHOP-12 ilegível: resposta inválida")]
    [InlineData("Jira issue SHOP-12 has no acceptance criteria in the description", "issue do Jira SHOP-12 sem critérios de aceite na descrição")]
    [InlineData("Jira issue SHOP-12 has no acceptance criteria in customfield_10042", "issue do Jira SHOP-12 sem critérios de aceite no campo customfield_10042")]
    [InlineData("Jira source not configured for SHOP-12: missing JIRA_EMAIL, JIRA_API_TOKEN", "fonte do Jira não configurada para SHOP-12: falta JIRA_EMAIL, JIRA_API_TOKEN")]
    [InlineData("model provider error (HTTP 429)", "erro do provedor do modelo (HTTP 429)")]
    [InlineData("model provider error (InvalidOperationException)", "erro do provedor do modelo (InvalidOperationException)")]
    public void The_jira_and_model_reasons_are_translated(string reason, string expected)
    {
        Assert.Equal(expected, Portuguese(reason));
    }

    [Fact]
    public void An_unknown_reason_is_shown_as_written_and_english_keeps_reasons_as_written()
    {
        Assert.Equal("something new", Portuguese("something new"));
        Assert.Equal("context budget reached (40 tool calls)", CoverageReasonText.Localize("context budget reached (40 tool calls)", ReviewLanguage.English));
        Assert.Equal("no requirement linked", CoverageReasonText.Localize(ReviewWorkflow.NoLinkReason, ReviewLanguage.English));
    }

    public void Dispose() => repository.Dispose();
}
