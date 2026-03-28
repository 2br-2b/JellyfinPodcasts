using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Template.Models;

namespace Jellyfin.Plugin.Template.Services;

/// <summary>Manages per-user app passwords for external podcast clients.</summary>
public interface IAppPasswordStore
{
    /// <summary>Creates a new app password, returning the plaintext token shown only once.</summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="label">The user-supplied label for the password.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The plaintext token plus the stored password record.</returns>
    Task<(string Token, AppPassword Password)> CreateAsync(Guid userId, string label, CancellationToken ct = default);

    /// <summary>Lists all app passwords for a user.</summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The app passwords for the user.</returns>
    Task<IReadOnlyList<AppPassword>> GetForUserAsync(Guid userId, CancellationToken ct = default);

    /// <summary>Deletes an app password.</summary>
    /// <param name="userId">The Jellyfin user ID.</param>
    /// <param name="passwordId">The app password identifier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns><c>true</c> when the password was deleted; otherwise <c>false</c>.</returns>
    Task<bool> DeleteAsync(Guid userId, Guid passwordId, CancellationToken ct = default);

    /// <summary>Validates a raw token string and updates the last-used timestamp.</summary>
    /// <param name="token">The raw app password token.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The matching password record when valid; otherwise <c>null</c>.</returns>
    Task<AppPassword?> ValidateTokenAsync(string token, CancellationToken ct = default);
}
