using Estimation.Core.JiraIntegration.Client;
using Estimation.Core.Resources.Models;
using Xunit;

namespace Estimation.Core.Tests.JiraIntegration;

public class JiraPersonMatcherTests
{
    private static HumanResource Person(int id, string fullName, string? accountName = null, string? email = null) =>
        new() { Id = id, EmployeeName = $"Person {id}", FullName = fullName, AccountName = accountName, Email = email };

    private static JiraIssueResponse Assigned(string? userName = null, string? email = null, string? displayName = null) =>
        new() { Key = "PROJ-1", AssigneeUserName = userName, AssigneeEmail = email, AssigneeDisplayName = displayName };

    [Theory]
    [InlineData("jsmith")]
    [InlineData("JSmith")]
    [InlineData("CORP\\jsmith")]
    public void The_account_name_links_the_jira_username(string accountName)
    {
        var smith = Person(1, "John Smith", accountName);

        var found = JiraPersonMatcher.FindAssignee(Assigned("jsmith", displayName: "Smith J."), [Person(2, "Ann Lee", "alee"), smith]);

        Assert.Same(smith, found);
    }

    [Fact]
    public void The_email_links_when_the_account_name_does_not()
    {
        var smith = Person(1, "John Smith", email: "John.Smith@Example.test");

        var found = JiraPersonMatcher.FindAssignee(Assigned("G01234567", "john.smith@example.test", "Smith J."), [smith]);

        Assert.Same(smith, found);
    }

    [Theory]
    [InlineData("Jan Novák", "Jan Novak")]
    [InlineData("Jan Novak", "NOVAK, Jan")]
    [InlineData("Jan  Novak", " jan novak ")]
    public void The_full_name_links_when_account_name_and_email_do_not(string fullName, string displayName)
    {
        var novak = Person(1, fullName);

        var found = JiraPersonMatcher.FindAssignee(Assigned("jnovak", displayName: displayName), [Person(2, "Eva Horak"), novak]);

        Assert.Same(novak, found);
    }

    [Fact]
    public void A_full_name_shared_by_two_people_links_nobody()
    {
        var found = JiraPersonMatcher.FindAssignee(
            Assigned("jsmith", displayName: "John Smith"),
            [Person(1, "John Smith"), Person(2, "John Smith")]);

        Assert.Null(found);
    }

    [Fact]
    public void An_account_name_match_wins_over_a_namesake()
    {
        var namesake = Person(1, "John Smith");
        var smith = Person(2, "John A. Smith", "jsmith");

        var found = JiraPersonMatcher.FindAssignee(Assigned("jsmith", displayName: "John Smith"), [namesake, smith]);

        Assert.Same(smith, found);
    }

    [Fact]
    public void An_account_name_used_by_two_people_falls_back_to_the_email()
    {
        var smith = Person(2, "John Smith", "jsmith", "john.smith@example.test");

        var found = JiraPersonMatcher.FindAssignee(
            Assigned("jsmith", "john.smith@example.test"),
            [Person(1, "Jane Smith", "jsmith", "jane.smith@example.test"), smith]);

        Assert.Same(smith, found);
    }

    [Fact]
    public void Nobody_is_linked_when_no_field_matches()
    {
        var found = JiraPersonMatcher.FindAssignee(
            Assigned("jsmith", "john.smith@example.test", "John Smith"),
            [Person(1, "Ann Lee", "alee", "ann.lee@example.test")]);

        Assert.Null(found);
    }

    [Fact]
    public void An_unassigned_issue_links_nobody()
    {
        Assert.Null(JiraPersonMatcher.FindAssignee(Assigned(), [Person(1, "John Smith")]));
    }
}
