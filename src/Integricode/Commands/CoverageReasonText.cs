using System.Text.RegularExpressions;
using Integricode.Agent;
using Integricode.Domain;

namespace Integricode.Commands;

/// <summary>
/// The published text of a coverage reason in the result's language. Reasons are written in English where they arise,
/// for the log and <c>result.json</c>; the summary shows them in Portuguese when the result asks for it. A reason with
/// no translation is shown as written.
/// </summary>
public static class CoverageReasonText
{
    /// <summary>The no-link reason without its how-to, which the summary prints right after it.</summary>
    private const string NoLinkEnglish = "no requirement linked";

    private static readonly Regex Refused = Pattern(@"requirements file (.+) refused: (.+)");

    private static readonly Dictionary<string, string> PortugueseRefusals = new(StringComparer.Ordinal)
    {
        [PathGuard.AbsoluteRefusal] = "caminhos absolutos não são permitidos",
        [PathGuard.ParentRefusal] = "segmentos de diretório pai não são permitidos",
        [PathGuard.OutsideRefusal] = "o caminho sai do checkout",
        [PathGuard.ExcludedRefusal] = "o caminho está excluído do contexto da revisão",
        [PathGuard.LinkRefusal] = "um link simbólico sai do checkout",
    };

    private static readonly (Regex Pattern, string Portuguese)[] Portuguese =
    [
        (Exact(ReviewWorkflow.NoLinkReason), "nenhum requisito vinculado"),
        (Exact(CommentCommand.ForkReason), "pull request de um fork; o Integricode revisa só pull requests cuja branch está neste repositório"),
        (Exact(ReviewCommand.ProviderConfigurationError), "erro de configuração do provedor do modelo: confira --model-provider, --model, --model-endpoint e INTEGRICODE_MODEL_API_KEY"),
        (Exact(ReviewWorkflow.SchemaFailureReason), "a resposta do modelo não seguiu o schema do resultado nem depois de um pedido de correção"),
        (Pattern(@"context budget reached \((\d+) tool calls\)"), "orçamento de contexto atingido ($1 chamadas de ferramenta)"),
        (Pattern(@"context budget reached \((\d+) characters\)"), "orçamento de contexto atingido ($1 caracteres)"),
        (Pattern(@"model provider error \((.+)\)"), "erro do provedor do modelo ($1)"),
        (Pattern(@"requirements file (.+) not found at the head SHA"), "arquivo de requisitos $1 não encontrado no head SHA"),
        (Pattern(@"(\S+) not found in (.+)"), "$1 não encontrado em $2"),
        (Pattern(@"Jira issue (\S+) unreadable: authentication failed \(HTTP 401\), check (\S+) and (\S+)"), "issue do Jira $1 ilegível: falha de autenticação (HTTP 401), confira $2 e $3"),
        (Pattern(@"Jira issue (\S+) unreadable: access denied \(HTTP 403\) for the Jira account"), "issue do Jira $1 ilegível: acesso negado (HTTP 403) para a conta do Jira"),
        (Pattern(@"Jira issue (\S+) unreadable: not found or not visible to the Jira account \(HTTP 404\)"), "issue do Jira $1 ilegível: não encontrada ou invisível para a conta do Jira (HTTP 404)"),
        (Pattern(@"Jira issue (\S+) unreadable: the request timed out"), "issue do Jira $1 ilegível: a requisição expirou"),
        (Pattern(@"Jira issue (\S+) unreadable: Jira unreachable \((\w+)\)"), "issue do Jira $1 ilegível: Jira inacessível ($2)"),
        (Pattern(@"Jira issue (\S+) unreadable: invalid response"), "issue do Jira $1 ilegível: resposta inválida"),
        (Pattern(@"Jira issue (\S+) unreadable: HTTP (\d+)"), "issue do Jira $1 ilegível: HTTP $2"),
        (Pattern(@"Jira issue (\S+) has no acceptance criteria in the description"), "issue do Jira $1 sem critérios de aceite na descrição"),
        (Pattern(@"Jira issue (\S+) has no acceptance criteria in (\S+)"), "issue do Jira $1 sem critérios de aceite no campo $2"),
        (Pattern(@"Jira source not configured for (\S+): missing (.+)"), "fonte do Jira não configurada para $1: falta $2"),
        (Pattern(@"Jira source not configured for (\S+)"), "fonte do Jira não configurada para $1"),
        (Pattern(@"no requirement source configured for (\S+)"), "nenhuma fonte de requisito configurada para $1"),
    ];

    /// <summary>The reason as the summary shows it in the given language.</summary>
    /// <param name="reason">A coverage reason as the review wrote it.</param>
    /// <param name="language">The result's language (<see cref="ReviewLanguage"/>).</param>
    /// <returns>The translated reason, or the reason as written when it has no translation.</returns>
    public static string Localize(string reason, string language)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (language != ReviewLanguage.PortugueseBrazil)
        {
            return reason == ReviewWorkflow.NoLinkReason ? NoLinkEnglish : reason;
        }

        // The cause of a refusal comes from PathGuard, so it is translated on its own.
        if (Refused.Match(reason) is { Success: true } refused)
        {
            var cause = refused.Groups[2].Value;
            return $"arquivo de requisitos {refused.Groups[1].Value} recusado: {PortugueseRefusals.GetValueOrDefault(cause, cause)}";
        }

        foreach (var (pattern, portuguese) in Portuguese)
        {
            if (pattern.IsMatch(reason))
            {
                return pattern.Replace(reason, portuguese);
            }
        }

        return reason;
    }

    private static Regex Exact(string text) => Pattern(Regex.Escape(text));

    private static Regex Pattern(string pattern) => new("^" + pattern + "$", RegexOptions.CultureInvariant);
}
