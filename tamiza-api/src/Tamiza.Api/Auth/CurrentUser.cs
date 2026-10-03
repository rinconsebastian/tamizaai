namespace Tamiza.Api.Auth;

/// <summary>The local user behind the current request.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid Id { get; }

    string Name { get; }

    string? Email { get; }

    bool IsSuperAdmin { get; }
}

internal sealed class CurrentUser : ICurrentUser
{
    private UserSnapshot? _user;

    public bool IsAuthenticated => _user is not null;

    public Guid Id => Require().Id;

    public string Name => Require().Name;

    public string? Email => Require().Email;

    public bool IsSuperAdmin { get; private set; }

    public void Set(UserSnapshot user, bool isSuperAdmin)
    {
        _user = user;
        IsSuperAdmin = isSuperAdmin;
    }

    private UserSnapshot Require() =>
        _user ?? throw new InvalidOperationException("No authenticated user is associated with this request.");
}
