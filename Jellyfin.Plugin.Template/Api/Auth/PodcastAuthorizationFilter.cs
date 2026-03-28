using System;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Template.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Template.Api.Auth;

/// <summary>
/// Authorization filter for the OpenPodcastAPI endpoints.
/// </summary>
/// <remarks>
/// This filter delegates authentication to existing libraries and frameworks:
/// Jellyfin's auth context for server tokens, Jellyfin's user manager for password auth,
/// ASP.NET authentication middleware for OAuth/bearer auth, and the app password store for
/// podcast-client specific tokens.
/// </remarks>
public class PodcastAuthorizationFilter : IAsyncAuthorizationFilter
{
    /// <summary>Key used to store the authenticated user ID in HttpContext.Items.</summary>
    public const string UserIdKey = "PodcastUserId";

    private readonly IAuthorizationContext _authContext;
    private readonly IAppPasswordStore _appPasswordStore;
    private readonly IUserManager _userManager;
    private readonly ILogger<PodcastAuthorizationFilter> _logger;

    /// <summary>Initializes a new instance of the <see cref="PodcastAuthorizationFilter"/> class.</summary>
    /// <param name="authContext">The Jellyfin authorization context.</param>
    /// <param name="appPasswordStore">The podcast client app password store.</param>
    /// <param name="userManager">The Jellyfin user manager.</param>
    /// <param name="logger">The logger instance.</param>
    public PodcastAuthorizationFilter(
        IAuthorizationContext authContext,
        IAppPasswordStore appPasswordStore,
        IUserManager userManager,
        ILogger<PodcastAuthorizationFilter> logger)
    {
        _authContext = authContext;
        _appPasswordStore = appPasswordStore;
        _userManager = userManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;
        var cancellationToken = context.HttpContext.RequestAborted;

        var oauthUserId = await TryAuthenticateWithAspNetAsync(context, cancellationToken).ConfigureAwait(false);
        if (oauthUserId.HasValue)
        {
            context.HttpContext.Items[UserIdKey] = oauthUserId.Value;
            return;
        }

        try
        {
            var info = await _authContext.GetAuthorizationInfo(request).ConfigureAwait(false);
            if (info?.UserId is Guid jellyfinUserId && jellyfinUserId != Guid.Empty)
            {
                context.HttpContext.Items[UserIdKey] = jellyfinUserId;
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Jellyfin token auth failed, trying other authentication methods.");
        }

        if (AuthenticationHeaderValue.TryParse(request.Headers.Authorization.ToString(), out var authHeader)
            && string.Equals(authHeader.Scheme, "Basic", StringComparison.OrdinalIgnoreCase))
        {
            var userId = await ValidateBasicAuthAsync(
                    authHeader.Parameter,
                    context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                    cancellationToken)
                .ConfigureAwait(false);
            if (userId.HasValue)
            {
                context.HttpContext.Items[UserIdKey] = userId.Value;
                return;
            }
        }

        if (request.Headers.TryGetValue("api_key", out var apiKeyValues))
        {
            var apiKey = apiKeyValues.ToString();
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                var password = await _appPasswordStore.ValidateTokenAsync(apiKey, cancellationToken)
                    .ConfigureAwait(false);
                if (password is not null)
                {
                    context.HttpContext.Items[UserIdKey] = password.UserId;
                    return;
                }
            }
        }

        _logger.LogWarning("Podcast API request rejected: no valid credentials.");
        context.Result = new UnauthorizedObjectResult(new { code = 401, message = "User not authorized" });
    }

    private async Task<Guid?> TryAuthenticateWithAspNetAsync(
        AuthorizationFilterContext context,
        CancellationToken cancellationToken)
    {
        var result = await context.HttpContext.AuthenticateAsync().ConfigureAwait(false);
        var principal = result.Principal;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var nameIdentifier = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        if (Guid.TryParse(nameIdentifier, out var userId) && _userManager.GetUserById(userId) is not null)
        {
            return userId;
        }

        var username = principal.Identity?.Name ?? principal.FindFirstValue(ClaimTypes.Name);
        if (!string.IsNullOrWhiteSpace(username))
        {
            return _userManager.GetUserByName(username)?.Id;
        }

        _logger.LogDebug(
            "Authenticated ASP.NET principal did not contain a resolvable Jellyfin user identity.");
        _ = cancellationToken;
        return null;
    }

    private async Task<Guid?> ValidateBasicAuthAsync(
        string? encodedCredentials,
        string remoteEndPoint,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(encodedCredentials))
            {
                return null;
            }

            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encodedCredentials));
            var colonIndex = decoded.IndexOf(':', StringComparison.Ordinal);
            if (colonIndex <= 0)
            {
                return null;
            }

            var username = decoded[..colonIndex];
            var secret = decoded[(colonIndex + 1)..];
            var user = _userManager.GetUserByName(username);
            if (user is null)
            {
                return null;
            }

            var authenticatedUser = await _userManager
                .AuthenticateUser(username, secret, remoteEndPoint, false)
                .ConfigureAwait(false);
            if (authenticatedUser is not null)
            {
                return authenticatedUser.Id;
            }

            var appPassword = await _appPasswordStore.ValidateTokenAsync(secret, cancellationToken)
                .ConfigureAwait(false);
            if (appPassword?.UserId == user.Id)
            {
                return user.Id;
            }
        }
        catch (FormatException ex)
        {
            _logger.LogDebug(ex, "Basic auth header was not valid base64.");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Basic auth validation failed.");
        }

        return null;
    }
}
