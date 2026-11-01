using System.Net;
using System.Text.Json;
using Integricode.Requirements.Jira;
using Integricode.Tests.Fakes;

namespace Integricode.Tests.Requirements.Jira;

public sealed class JiraClientTests : IDisposable
{
    private readonly FakeJira jira = new();

    [Fact]
    public async Task Reads_the_summary_and_description_with_basic_authentication()
    {
        var issue = await jira.Client().GetIssueAsync("SHOP-12", null, CancellationToken.None);

        Assert.Equal("Cart totals apply discounts", issue.Summary);
        Assert.Equal("doc", issue.Description!.Value.GetProperty("type").GetString());
        Assert.Null(issue.AcceptanceCriteria);
        var request = Assert.Single(jira.Requests);
        Assert.Equal("/rest/api/3/issue/SHOP-12?fields=summary,description", request.Uri.PathAndQuery);
        Assert.Equal(FakeJira.Authorization, request.Authorization);
    }

    [Fact]
    public async Task Reads_the_configured_acceptance_criteria_field()
    {
        jira.CustomFields["customfield_10042"] = Adf.Doc(Adf.BulletList("Discounts apply."));

        var issue = await jira.Client().GetIssueAsync("SHOP-12", "customfield_10042", CancellationToken.None);

        Assert.Equal(JsonValueKind.Object, issue.AcceptanceCriteria!.Value.ValueKind);
        Assert.Equal("/rest/api/3/issue/SHOP-12?fields=summary,description,customfield_10042", Assert.Single(jira.Requests).Uri.PathAndQuery);
    }

    [Fact]
    public async Task An_empty_or_absent_field_is_null()
    {
        jira.Description = null;

        var issue = await jira.Client().GetIssueAsync("SHOP-12", "customfield_10042", CancellationToken.None);

        Assert.Null(issue.Description);
        Assert.Null(issue.AcceptanceCriteria);
    }

    [Fact]
    public async Task Reads_every_comment_across_pages_oldest_first()
    {
        for (var i = 1; i <= 5; i++)
        {
            jira.AddComment(Adf.Paragraph($"Clarification {i}."));
        }

        var bodies = await jira.Client().GetCommentBodiesAsync("SHOP-12", CancellationToken.None);

        Assert.Equal(
            ["Clarification 1.", "Clarification 2.", "Clarification 3.", "Clarification 4.", "Clarification 5."],
            bodies.Select(AdfText.ToText));
        Assert.Equal(
            [
                "/rest/api/3/issue/SHOP-12/comment?startAt=0&maxResults=100&orderBy=created",
                "/rest/api/3/issue/SHOP-12/comment?startAt=2&maxResults=100&orderBy=created",
                "/rest/api/3/issue/SHOP-12/comment?startAt=4&maxResults=100&orderBy=created",
            ],
            jira.Requests.Select(r => r.Uri.PathAndQuery));
    }

    [Fact]
    public async Task No_comments_take_one_request()
    {
        var bodies = await jira.Client().GetCommentBodiesAsync("SHOP-12", CancellationToken.None);

        Assert.Empty(bodies);
        Assert.Single(jira.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task A_failed_call_throws_with_its_status(HttpStatusCode status)
    {
        jira.IssueStatus = status;
        jira.CommentsStatus = status;

        var issue = await Assert.ThrowsAsync<JiraApiException>(() => jira.Client().GetIssueAsync("SHOP-12", null, CancellationToken.None));
        var comments = await Assert.ThrowsAsync<JiraApiException>(() => jira.Client().GetCommentBodiesAsync("SHOP-12", CancellationToken.None));

        Assert.Equal(status, issue.StatusCode);
        Assert.Equal(status, comments.StatusCode);
    }

    [Fact]
    public async Task Every_request_is_a_get()
    {
        jira.CustomFields["customfield_10042"] = Adf.Doc(Adf.Paragraph("Discounts apply."));
        for (var i = 1; i <= 3; i++)
        {
            jira.AddComment(Adf.Paragraph($"Clarification {i}."));
        }

        var client = jira.Client();
        await client.GetIssueAsync("SHOP-12", "customfield_10042", CancellationToken.None);
        await client.GetCommentBodiesAsync("SHOP-12", CancellationToken.None);
        jira.IssueStatus = HttpStatusCode.Forbidden;
        await Assert.ThrowsAsync<JiraApiException>(() => client.GetIssueAsync("SHOP-12", null, CancellationToken.None));

        Assert.Equal(4, jira.Requests.Count);
        Assert.All(jira.Requests, request => Assert.Equal(HttpMethod.Get, request.Method));
        Assert.All(jira.Requests, request => Assert.Null(request.Body));
    }

    public void Dispose() => jira.Dispose();
}
