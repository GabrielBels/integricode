using System.Net;
using System.Text.Json.Nodes;
using Integricode.Requirements;
using Integricode.Requirements.Jira;
using Integricode.Tests.Fakes;

namespace Integricode.Tests.Requirements.Jira;

public sealed class JiraRequirementSourceTests : IDisposable
{
    private const string Field = "customfield_10042";

    private readonly FakeJira jira = new();

    public JiraRequirementSourceTests()
    {
        jira.Description = Adf.Doc(Adf.Paragraph("The cart shows a total for its items."));
        jira.CustomFields[Field] = Adf.Doc(Adf.BulletList("The total applies every discount in the cart.", "An empty cart totals zero."));
    }

    private static RequirementLink Link(string key = "SHOP-12") => new(RequirementKind.JiraIssue, key, RequirementLinkSource.Title);

    private Task<RequirementReadResult> ReadAsync(string? field = Field, TimeSpan? timeout = null) =>
        new JiraRequirementSource(jira.Client(timeout), field).ReadAsync(Link(), CancellationToken.None);

    [Fact]
    public async Task The_requirement_is_the_summary_description_acceptance_criteria_and_every_comment()
    {
        jira.AddComment(Adf.Paragraph("Discounts are percentages, not flat amounts."));
        jira.AddComment(Adf.Paragraph("Rounding happens once, on the total."));
        jira.AddComment(Adf.Paragraph("Free items still count as items."));

        var result = await ReadAsync();

        Assert.Null(result.UnreadableReason);
        Assert.Equal(
            """
            # SHOP-12: Cart totals apply discounts

            ## Description

            The cart shows a total for its items.

            ## Acceptance Criteria

            - The total applies every discount in the cart.
            - An empty cart totals zero.

            ## Comments

            ### Comment 1

            Discounts are percentages, not flat amounts.

            ### Comment 2

            Rounding happens once, on the total.

            ### Comment 3

            Free items still count as items.
            """.ReplaceLineEndings("\n"),
            result.Text);
        Assert.Equal(3, jira.Requests.Count);
    }

    [Fact]
    public async Task Without_a_configured_field_the_acceptance_criteria_come_from_the_description()
    {
        jira.Description = Adf.Doc(Adf.Paragraph("The cart shows a total."), Adf.BulletList("The total applies every discount in the cart."));

        var result = await ReadAsync(field: null);

        Assert.Null(result.UnreadableReason);
        Assert.Contains("## Description and Acceptance Criteria\n\nThe cart shows a total.\n\n- The total applies every discount in the cart.", result.Text);
        Assert.DoesNotContain("## Acceptance Criteria", result.Text);
        Assert.DoesNotContain("An empty cart totals zero.", result.Text);
        Assert.Equal("/rest/api/3/issue/SHOP-12?fields=summary,description", jira.Requests[0].Uri.PathAndQuery);
    }

    [Fact]
    public async Task A_plain_text_field_is_used_as_written()
    {
        jira.CustomFields[Field] = "An empty cart totals zero.";

        var result = await ReadAsync();

        Assert.Contains("## Acceptance Criteria\n\nAn empty cart totals zero.\n\n## Comments\n\n(none)", result.Text);
    }

    [Fact]
    public async Task An_empty_configured_field_keeps_the_text_and_reports_the_missing_acceptance_criteria()
    {
        jira.CustomFields.Remove(Field);

        var result = await ReadAsync();

        Assert.True(result.IsRead);
        Assert.Contains("## Acceptance Criteria\n\n(none)", result.Text);
        Assert.Equal("Jira issue SHOP-12 has no acceptance criteria in customfield_10042", result.UnreadableReason);
    }

    [Fact]
    public async Task An_empty_description_without_a_configured_field_reports_the_missing_acceptance_criteria()
    {
        jira.Description = null;

        var result = await ReadAsync(field: null);

        Assert.Contains("## Description and Acceptance Criteria\n\n(none)", result.Text);
        Assert.Equal("Jira issue SHOP-12 has no acceptance criteria in the description", result.UnreadableReason);
    }

    [Fact]
    public async Task Comment_authors_are_left_out()
    {
        jira.AddComment(Adf.Paragraph("Totals round once."));
        jira.AddComment(new JsonObject
        {
            ["type"] = "paragraph",
            ["content"] = new JsonArray(
                new JsonObject { ["type"] = "mention", ["attrs"] = new JsonObject { ["id"] = "synthetic-account-0001", ["text"] = "@" + FakeJira.AuthorName } },
                Adf.Text(" agreed.")),
        });

        var result = await ReadAsync();

        Assert.Contains("### Comment 2\n\n@user agreed.", result.Text);
        Assert.DoesNotContain(FakeJira.AuthorName, result.Text);
        Assert.DoesNotContain(FakeJira.AuthorEmail, result.Text);
        Assert.DoesNotContain("synthetic-account-0001", result.Text);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "Jira issue SHOP-12 unreadable: authentication failed (HTTP 401), check JIRA_EMAIL and JIRA_API_TOKEN")]
    [InlineData(HttpStatusCode.Forbidden, "Jira issue SHOP-12 unreadable: access denied (HTTP 403) for the Jira account")]
    [InlineData(HttpStatusCode.NotFound, "Jira issue SHOP-12 unreadable: not found or not visible to the Jira account (HTTP 404)")]
    [InlineData(HttpStatusCode.BadGateway, "Jira issue SHOP-12 unreadable: HTTP 502")]
    public async Task A_failed_issue_read_names_its_cause(HttpStatusCode status, string reason)
    {
        jira.IssueStatus = status;

        var result = await ReadAsync();

        Assert.False(result.IsRead);
        Assert.Equal(reason, result.UnreadableReason);
    }

    [Fact]
    public async Task A_failed_comments_read_makes_the_issue_unreadable()
    {
        jira.CommentsStatus = HttpStatusCode.Forbidden;

        var result = await ReadAsync();

        Assert.False(result.IsRead);
        Assert.Equal("Jira issue SHOP-12 unreadable: access denied (HTTP 403) for the Jira account", result.UnreadableReason);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"synthetic\"")]
    [InlineData("null")]
    [InlineData("{\"key\": \"SHOP-12\"}")]
    [InlineData("not json")]
    public async Task An_issue_response_that_is_not_an_issue_is_an_invalid_response(string body)
    {
        using var handler = new FakeHttpHandler().On(HttpMethod.Get, "/rest/api/3/issue/SHOP-12?fields=summary,description", _ => FakeHttpHandler.Json(body));

        var result = await new JiraRequirementSource(Client(handler), null).ReadAsync(Link(), CancellationToken.None);

        Assert.Equal("Jira issue SHOP-12 unreadable: invalid response", result.UnreadableReason);
    }

    [Fact]
    public async Task A_comment_page_without_a_total_is_an_invalid_response()
    {
        using var handler = new FakeHttpHandler()
            .OnJson(HttpMethod.Get, "/rest/api/3/issue/SHOP-12?fields=summary,description", """{"fields": {"summary": "Cart totals"}}""")
            .OnJson(HttpMethod.Get, "/rest/api/3/issue/SHOP-12/comment?startAt=0&maxResults=100&orderBy=created", """{"startAt": 0, "comments": [{"body": null}]}""");

        var result = await new JiraRequirementSource(Client(handler), null).ReadAsync(Link(), CancellationToken.None);

        Assert.Equal("Jira issue SHOP-12 unreadable: invalid response", result.UnreadableReason);
    }

    [Fact]
    public async Task An_unreachable_jira_names_only_the_exception_type()
    {
        using var handler = new FakeHttpHandler().On(HttpMethod.Get, "/rest/api/3/issue/SHOP-12?fields=summary,description", _ => throw new HttpRequestException("SYNTHETIC-CONNECTION-DETAIL"));

        var result = await new JiraRequirementSource(Client(handler), null).ReadAsync(Link(), CancellationToken.None);

        Assert.Equal("Jira issue SHOP-12 unreadable: Jira unreachable (HttpRequestException)", result.UnreadableReason);
    }

    [Fact]
    public async Task An_error_response_body_is_not_carried_into_the_reason()
    {
        using var handler = new FakeHttpHandler().On(HttpMethod.Get, "/rest/api/3/issue/SHOP-12?fields=summary,description", _ => FakeHttpHandler.Json("""{"errorMessages": ["SYNTHETIC-ERROR-BODY"]}""", HttpStatusCode.InternalServerError));

        var result = await new JiraRequirementSource(Client(handler), null).ReadAsync(Link(), CancellationToken.None);

        Assert.Equal("Jira issue SHOP-12 unreadable: HTTP 500", result.UnreadableReason);
    }

    private static JiraClient Client(FakeHttpHandler handler) =>
        new(new HttpClient(handler, disposeHandler: false), new Uri(FakeJira.BaseUrl), FakeJira.Email, FakeJira.ApiToken);

    [Fact]
    public async Task A_timeout_names_its_cause()
    {
        jira.Hang = true;

        var result = await ReadAsync(timeout: TimeSpan.FromMilliseconds(20));

        Assert.False(result.IsRead);
        Assert.Equal("Jira issue SHOP-12 unreadable: the request timed out", result.UnreadableReason);
    }

    [Fact]
    public async Task Cancelling_the_review_is_not_taken_for_a_timeout()
    {
        jira.Hang = true;
        using var cancellation = new CancellationTokenSource();
        var read = new JiraRequirementSource(jira.Client(), Field).ReadAsync(Link(), cancellation.Token);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }

    [Fact]
    public void No_jira_input_turns_the_source_off()
    {
        Assert.Null(JiraRequirementSource.Create(jira.HttpClient(), null, null, null, null));
        Assert.Null(JiraRequirementSource.Create(jira.HttpClient(), null, null, " ", ""));
    }

    [Theory]
    [InlineData(null, FakeJira.Email, FakeJira.ApiToken, null, "Jira source not configured for SHOP-12: missing --jira-base-url")]
    [InlineData(FakeJira.BaseUrl, null, FakeJira.ApiToken, null, "Jira source not configured for SHOP-12: missing JIRA_EMAIL")]
    [InlineData(FakeJira.BaseUrl, FakeJira.Email, " ", null, "Jira source not configured for SHOP-12: missing JIRA_API_TOKEN")]
    [InlineData(FakeJira.BaseUrl, null, null, null, "Jira source not configured for SHOP-12: missing JIRA_EMAIL, JIRA_API_TOKEN")]
    [InlineData(null, null, null, Field, "Jira source not configured for SHOP-12: missing --jira-base-url, JIRA_EMAIL, JIRA_API_TOKEN")]
    public async Task A_missing_jira_input_is_named_and_nothing_is_sent(string? baseUrl, string? email, string? apiToken, string? field, string reason)
    {
        var source = JiraRequirementSource.Create(jira.HttpClient(), baseUrl is null ? null : new Uri(baseUrl), field, email, apiToken);

        var result = await source!.ReadAsync(Link(), CancellationToken.None);

        Assert.Equal(RequirementKind.JiraIssue, source.Kind);
        Assert.False(result.IsRead);
        Assert.Equal(reason, result.UnreadableReason);
        Assert.Empty(jira.Requests);
    }

    [Fact]
    public async Task Every_jira_input_set_reads_the_issue()
    {
        var source = JiraRequirementSource.Create(jira.HttpClient(), new Uri(FakeJira.BaseUrl), Field, FakeJira.Email, FakeJira.ApiToken);

        var result = await source!.ReadAsync(Link(), CancellationToken.None);

        Assert.True(result.IsRead);
        Assert.All(jira.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
    }

    public void Dispose() => jira.Dispose();
}
