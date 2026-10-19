using Tamiza.Api.Accounts;

namespace Tamiza.Api.IntegrationTests.Infrastructure;

/// <summary>An identity provider that answers from memory and records what it was asked, so the policy can be tested alone.</summary>
public sealed class FakeAccountProvider : IAccountProvider, IAccountProviderSource
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ProviderAccount> _byEmail = [];
    private readonly List<string> _calls = [];
    private int _created;

    public IAccountProvider? Provider => this;

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToList();
            }
        }
    }

    public string? LastLanguage { get; private set; }

    public ProviderFailure? FindFailure { get; set; }

    public ProviderFailure? CreateFailure { get; set; }

    public ProviderFailure? SendFailure { get; set; }

    /// <summary>When set, a create makes the account but answers as if the reply was lost (a timeout).</summary>
    public bool LoseCreateReply { get; set; }

    /// <summary>Adds an account that already exists at the provider, found by email and by its id.</summary>
    public ProviderAccount AddAccount(string email, bool verified = false, bool enabled = true, bool hasEmail = true, string? id = null)
    {
        var account = new ProviderAccount(id ?? $"acc-{Guid.NewGuid():N}", verified, enabled, hasEmail);
        lock (_gate)
        {
            _byEmail[email] = account;
        }

        return account;
    }

    public void Remove(string email)
    {
        lock (_gate)
        {
            _byEmail.Remove(email);
        }
    }

    public void SetAccount(string email, ProviderAccount account)
    {
        lock (_gate)
        {
            _byEmail[email] = account;
        }
    }

    public int Count(string call) => Calls.Count(c => c == call);

    public Task<ProviderResult<ProviderAccount?>> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        Record("find-by-email");
        if (FindFailure is { } failure)
        {
            return Task.FromResult(new ProviderResult<ProviderAccount?>(null, failure));
        }

        lock (_gate)
        {
            return Task.FromResult(new ProviderResult<ProviderAccount?>(_byEmail.GetValueOrDefault(normalizedEmail)));
        }
    }

    public Task<ProviderResult<ProviderAccount?>> FindBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        Record("find-by-subject");
        if (FindFailure is { } failure)
        {
            return Task.FromResult(new ProviderResult<ProviderAccount?>(null, failure));
        }

        lock (_gate)
        {
            return Task.FromResult(new ProviderResult<ProviderAccount?>(_byEmail.Values.FirstOrDefault(a => a.Id == subject)));
        }
    }

    public async Task<ProviderResult<string>> CreateAsync(string email, string? language, CancellationToken cancellationToken)
    {
        Record("create");
        LastLanguage = language;
        await Task.Yield();
        if (CreateFailure is { } failure)
        {
            return new ProviderResult<string>(null, failure);
        }

        lock (_gate)
        {
            if (_byEmail.ContainsKey(email))
            {
                return new ProviderResult<string>(null, ProviderFailure.Conflict);
            }

            var id = $"new-{++_created}";
            _byEmail[email] = new ProviderAccount(id, false, true, true);
            return LoseCreateReply ? new ProviderResult<string>(null, ProviderFailure.Unreachable) : new ProviderResult<string>(id);
        }
    }

    public Task<ProviderResult> SendSetupEmailAsync(string accountId, CancellationToken cancellationToken)
    {
        Record("send-setup:" + accountId);
        return Task.FromResult(SendFailure is { } failure ? new ProviderResult(failure) : ProviderResult.Ok);
    }

    public Task<ProviderResult> SendPasswordResetEmailAsync(string accountId, CancellationToken cancellationToken)
    {
        Record("send-reset:" + accountId);
        return Task.FromResult(SendFailure is { } failure ? new ProviderResult(failure) : ProviderResult.Ok);
    }

    private void Record(string call)
    {
        lock (_gate)
        {
            _calls.Add(call);
        }
    }
}
