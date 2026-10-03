namespace Tamiza.Api.Data;

/// <summary>Role in a project. Ordered: each role includes the permissions of the ones below it.</summary>
public enum ProjectRole
{
    Viewer = 1,
    Analyst = 2,
    Admin = 3,
}
