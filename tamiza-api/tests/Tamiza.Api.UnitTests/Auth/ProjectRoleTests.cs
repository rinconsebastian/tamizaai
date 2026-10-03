using Tamiza.Api.Data;

namespace Tamiza.Api.UnitTests.Auth;

public sealed class ProjectRoleTests
{
    [Fact]
    public void Roles_are_ordered_viewer_analyst_admin()
    {
        Assert.True(ProjectRole.Viewer < ProjectRole.Analyst);
        Assert.True(ProjectRole.Analyst < ProjectRole.Admin);
    }

    [Theory]
    [InlineData(ProjectRole.Admin, ProjectRole.Viewer, true)]
    [InlineData(ProjectRole.Admin, ProjectRole.Admin, true)]
    [InlineData(ProjectRole.Analyst, ProjectRole.Admin, false)]
    [InlineData(ProjectRole.Viewer, ProjectRole.Analyst, false)]
    public void A_role_satisfies_the_same_or_a_lower_minimum(ProjectRole role, ProjectRole minimum, bool allowed) =>
        Assert.Equal(allowed, role >= minimum);
}
