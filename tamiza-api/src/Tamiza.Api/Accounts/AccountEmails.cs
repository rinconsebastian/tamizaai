using Microsoft.EntityFrameworkCore;
using Tamiza.Api.Data;

namespace Tamiza.Api.Accounts;

public enum EmailOutcome
{
    Sent,
    RecentlySent,
    ExistingAccount,
    RateLimited,
    Failed,

    /// <summary>Password reset only: the member has no active account to email.</summary>
    NoAccount,
}

/// <summary>What happened to an account email; <see cref="RetryAfterSeconds"/> is set for the two outcomes that ask to wait.</summary>
public readonly record struct EmailResult(EmailOutcome Outcome, int RetryAfterSeconds = 0)
{
    /// <summary>The name the API and the UI use for the outcome.</summary>
    public string Wire => Outcome switch
    {
        EmailOutcome.Sent => "sent",
        EmailOutcome.RecentlySent => "recentlySent",
        EmailOutcome.ExistingAccount => "existingAccount",
        EmailOutcome.RateLimited => "rateLimited",
        EmailOutcome.NoAccount => "noAccount",
        _ => "failed",
    };
}

/// <summary>
/// The policy of account emails (design D6): which accounts may be created or emailed, the ownership record, the limits, and the
/// outcome to report. It knows no identity provider; <see cref="IAccountProvider"/> does the calls. Provider calls and the writes
/// that follow them do not use the request's cancellation token, so a closed tab cannot leave an account without its email.
/// </summary>
public sealed class AccountEmails(
    IAccountProviderSource source,
    AccountEmailLimits limits,
    TamizaDbContext db,
    TimeProvider time,
    ILogger<AccountEmails> logger)
{
    /// <summary>True when an identity provider that supports account emails is configured.</summary>
    public bool Enabled => source.Provider is not null;

    /// <summary>
    /// Creates the account for an invited email when there is none and emails it the setup link, or sends that link again to an
    /// account Tamiza created whose email is still unverified. Returns null when account emails are off.
    /// </summary>
    public async Task<EmailResult?> SendInvitationAsync(string normalizedEmail, Guid requesterId, string? language, CancellationToken cancellationToken)
    {
        if (source.Provider is not { } provider)
        {
            return null;
        }

        using var gate = await limits.LockAsync("invitation:" + normalizedEmail, cancellationToken);

        var before = limits.Peek(requesterId);
        if (!before.Allowed)
        {
            return new EmailResult(EmailOutcome.RateLimited, before.RetryAfterSeconds);
        }

        var found = await provider.FindByEmailAsync(normalizedEmail, CancellationToken.None);
        if (!found.Succeeded)
        {
            return Failed("find-by-email", found.Failure);
        }

        if (found.Value is { } account)
        {
            var owned = await db.InvitedAccounts.SingleOrDefaultAsync(a => a.AccountId == account.Id, CancellationToken.None);
            if (owned is null || account.EmailVerified || !account.Enabled)
            {
                return new EmailResult(EmailOutcome.ExistingAccount);
            }

            return await SendSetupAsync(provider, owned, requesterId, takeAllowance: true);
        }

        var allowance = limits.TryTake(requesterId);
        if (!allowance.Allowed)
        {
            return new EmailResult(EmailOutcome.RateLimited, allowance.RetryAfterSeconds);
        }

        var created = await provider.CreateAsync(normalizedEmail, language, CancellationToken.None);
        if (created.Failure == ProviderFailure.Conflict)
        {
            return new EmailResult(EmailOutcome.ExistingAccount);
        }

        var accountId = created.Succeeded ? created.Value : null;
        if (accountId is null)
        {
            // The reply can be lost after the provider made the account (a timeout, a reply without the account's address).
            // Holding this email's lock, having just seen no account for it, an enabled unverified one that shows up now is the
            // one that was asked for; without recording it, no later invitation could ever email it.
            if (created.Failure is not (ProviderFailure.Unreachable or ProviderFailure.Other or ProviderFailure.None))
            {
                return Failed("create", created.Failure);
            }

            var again = await provider.FindByEmailAsync(normalizedEmail, CancellationToken.None);
            if (again.Succeeded && again.Value is { EmailVerified: false, Enabled: true } adopted)
            {
                accountId = adopted.Id;
            }
            else
            {
                return Failed("create", created.Failure);
            }
        }

        var added = new InvitedAccount
        {
            AccountId = accountId,
            NormalizedEmail = normalizedEmail,
            CreatedBy = requesterId,
            CreatedAt = time.GetUtcNow(),
        };
        db.InvitedAccounts.Add(added);
        if (!await TrySaveAsync("record-account"))
        {
            db.Entry(added).State = EntityState.Detached;
            return new EmailResult(EmailOutcome.Failed);
        }

        return await SendSetupAsync(provider, added, requesterId, takeAllowance: false);
    }

    /// <summary>
    /// Emails a member's account the link that sets a new password. Returns null when account emails are off. The account is found
    /// from the token's <c>sub</c>, the same identity the member signs in with.
    /// </summary>
    public async Task<EmailResult?> SendPasswordResetAsync(string subject, Guid requesterId, CancellationToken cancellationToken)
    {
        if (source.Provider is not { } provider)
        {
            return null;
        }

        using var gate = await limits.LockAsync("reset:" + subject, cancellationToken);

        var before = limits.Peek(requesterId);
        if (!before.Allowed)
        {
            return new EmailResult(EmailOutcome.RateLimited, before.RetryAfterSeconds);
        }

        var found = await provider.FindBySubjectAsync(subject, CancellationToken.None);
        if (!found.Succeeded)
        {
            return Failed("find-by-subject", found.Failure);
        }

        if (found.Value is not { Enabled: true, HasEmail: true } account)
        {
            return new EmailResult(EmailOutcome.NoAccount);
        }

        var wait = limits.IntervalRemaining(account.Id);
        if (wait > 0)
        {
            return new EmailResult(EmailOutcome.RecentlySent, wait);
        }

        var allowance = limits.TryTake(requesterId);
        if (!allowance.Allowed)
        {
            return new EmailResult(EmailOutcome.RateLimited, allowance.RetryAfterSeconds);
        }

        var sent = await provider.SendPasswordResetEmailAsync(account.Id, CancellationToken.None);
        if (sent.Failure == ProviderFailure.AccountUnavailable)
        {
            return new EmailResult(EmailOutcome.NoAccount);
        }

        if (!sent.Succeeded)
        {
            return Failed("send-reset", sent.Failure);
        }

        limits.MarkSent(account.Id);
        return new EmailResult(EmailOutcome.Sent);
    }

    private async Task<EmailResult> SendSetupAsync(IAccountProvider provider, InvitedAccount account, Guid requesterId, bool takeAllowance)
    {
        var wait = limits.IntervalRemaining(account.AccountId);
        if (wait > 0)
        {
            return new EmailResult(EmailOutcome.RecentlySent, wait);
        }

        if (takeAllowance)
        {
            var allowance = limits.TryTake(requesterId);
            if (!allowance.Allowed)
            {
                return new EmailResult(EmailOutcome.RateLimited, allowance.RetryAfterSeconds);
            }
        }

        var sent = await provider.SendSetupEmailAsync(account.AccountId, CancellationToken.None);
        if (!sent.Succeeded)
        {
            return Failed("send-setup", sent.Failure);
        }

        // The email is on its way: start the interval before recording it, so a database failure cannot let a second one go at once.
        limits.MarkSent(account.AccountId);
        account.LastEmailAt = time.GetUtcNow();
        await TrySaveAsync("record-email-time");
        return new EmailResult(EmailOutcome.Sent);
    }

    /// <summary>Saves the pending changes; a database failure is logged (by type only) and answered as false, never thrown.</summary>
    private async Task<bool> TrySaveAsync(string operation)
    {
        try
        {
            await db.SaveChangesAsync(CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException or Npgsql.NpgsqlException)
        {
            logger.LogWarning("Account email step {Operation} could not be saved ({Reason}).", operation, exception.GetType().Name);
            return false;
        }
    }

    private EmailResult Failed(string operation, ProviderFailure failure)
    {
        logger.LogWarning("Account email step {Operation} failed ({Failure}).", operation, failure);
        return new EmailResult(EmailOutcome.Failed);
    }
}
