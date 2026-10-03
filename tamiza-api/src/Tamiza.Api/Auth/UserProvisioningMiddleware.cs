namespace Tamiza.Api.Auth;

internal sealed class UserProvisioningMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, UserProvisioner provisioner, CurrentUser currentUser, SuperAdminRule superAdmins)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var identity = TokenIdentity.FromPrincipal(context.User);
            if (identity is null)
            {
                // A valid token without a subject cannot be tied to a local user.
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var user = await provisioner.ResolveAsync(identity, context.RequestAborted);
            currentUser.Set(user, superAdmins.IsSuperAdmin(identity));
        }

        await next(context);
    }
}
