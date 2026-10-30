using Integricode.Requirements;

namespace Integricode.Tests.Requirements;

public class RequirementIdentifierTests
{
    [Fact]
    public void The_title_wins_over_the_branch_and_the_description()
    {
        var link = RequirementIdentifier.Identify("REQ-002: totals", "feat/req-001-cart", "Implements REQ-003.");

        Assert.Equal(new RequirementLink(RequirementKind.ReqEntry, "REQ-002", RequirementLinkSource.Title), link);
    }

    [Fact]
    public void The_branch_wins_over_the_description()
    {
        var link = RequirementIdentifier.Identify("Cart totals", "feature/SHOP-9-totals", "Implements REQ-003.");

        Assert.Equal(new RequirementLink(RequirementKind.JiraIssue, "SHOP-9", RequirementLinkSource.Branch), link);
    }

    [Fact]
    public void A_lowercase_req_id_in_the_branch_is_found_and_normalized()
    {
        var link = RequirementIdentifier.Identify("Cart totals", "feat/req-001-review-core", null);

        Assert.Equal(new RequirementLink(RequirementKind.ReqEntry, "REQ-001", RequirementLinkSource.Branch), link);
    }

    [Fact]
    public void The_description_is_read_last()
    {
        var link = RequirementIdentifier.Identify("Cart totals", "main-fix", "This implements REQ-004 as written.");

        Assert.Equal(new RequirementLink(RequirementKind.ReqEntry, "REQ-004", RequirementLinkSource.Description), link);
    }

    [Fact]
    public void A_jira_key_is_identified()
    {
        var link = RequirementIdentifier.Identify("PROJ-1234 Add the cart", null, null);

        Assert.Equal(new RequirementLink(RequirementKind.JiraIssue, "PROJ-1234", RequirementLinkSource.Title), link);
    }

    [Fact]
    public void The_first_match_in_a_field_wins()
    {
        var link = RequirementIdentifier.Identify("Fix PROJ-7 and REQ-001", null, null);

        Assert.Equal(new RequirementLink(RequirementKind.JiraIssue, "PROJ-7", RequirementLinkSource.Title), link);
    }

    [Theory]
    [InlineData("Refactor the cart", "feature/cleanup", "No requirement here.")]
    [InlineData("Refactor", "feature/abc-12", "lowercase keys are not jira keys")]
    [InlineData("", null, null)]
    public void No_link_returns_null(string title, string? branch, string? description)
    {
        Assert.Null(RequirementIdentifier.Identify(title, branch, description));
    }

    [Theory]
    [InlineData("XREQ-001 is not a link")]
    [InlineData("REQ-0012 is a five-digit id")]
    public void Ids_glued_to_other_characters_are_not_req_ids(string title)
    {
        var link = RequirementIdentifier.Identify(title, null, null);

        Assert.NotEqual(RequirementKind.ReqEntry, link?.Kind);
    }
}
