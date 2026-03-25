using System.Security.Cryptography;
using Jellyfin.Plugin.Podcasts.Database;
using Jellyfin.Plugin.Podcasts.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.Podcasts.Api.Management;

/// <summary>
/// Management API for podcast sync app passwords.
/// Authenticated via standard Jellyfin session token.
/// </summary>
[ApiController]
[Route("podcasts/management/app-passwords")]
[Authorize]
public class AppPasswordController : ControllerBase
{
    private readonly PodcastDbContext _db;

    public AppPasswordController(PodcastDbContext db)
    {
        _db = db;
    }

    private string UserId => User.FindFirst("sub")?.Value
        ?? User.FindFirst("nameid")?.Value
        ?? string.Empty;

    // GET /podcasts/management/app-passwords
    [HttpGet]
    public async Task<IActionResult> ListPasswords(CancellationToken ct)
    {
        var passwords = await _db.AppPasswords
            .Where(ap => ap.UserId == UserId)
            .OrderByDescending(ap => ap.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Ok(passwords.Select(ap => new
        {
            id = ap.Id,
            label = ap.Label,
            created_at = ap.CreatedAt.ToString("O"),
            last_used_at = ap.LastUsedAt?.ToString("O")
        }));
    }

    // POST /podcasts/management/app-passwords
    [HttpPost]
    public async Task<IActionResult> GeneratePassword([FromBody] GeneratePasswordRequest body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Label))
            return BadRequest("label is required");

        // Generate cryptographically random 32-character alphanumeric password
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var password = new char[32];
        for (int i = 0; i < 32; i++)
            password[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];

        var plaintext = new string(password);
        var hash = BCrypt.Net.BCrypt.HashPassword(plaintext, workFactor: 11);

        var ap = new AppPassword
        {
            UserId = UserId,
            Label = body.Label,
            PasswordHash = hash
        };

        _db.AppPasswords.Add(ap);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        // Return the plaintext password ONCE — it is not stored
        var host = $"{Request.Scheme}://{Request.Host}";
        return Ok(new
        {
            id = ap.Id,
            label = ap.Label,
            password = plaintext,  // shown once only
            created_at = ap.CreatedAt.ToString("O"),
            server_url = host,
            gpodder_url = $"{host}/podcasts/sync/gpodder",
            opa_url = $"{host}/podcasts/sync/opa"
        });
    }

    // DELETE /podcasts/management/app-passwords/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> RevokePassword(Guid id, CancellationToken ct)
    {
        var ap = await _db.AppPasswords
            .FirstOrDefaultAsync(ap => ap.Id == id && ap.UserId == UserId, ct)
            .ConfigureAwait(false);

        if (ap is null) return NotFound();

        _db.AppPasswords.Remove(ap);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }
}

public class GeneratePasswordRequest
{
    public string Label { get; set; } = string.Empty;
}
