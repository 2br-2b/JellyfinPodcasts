#pragma warning disable CA2007
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Template.Data;
using Jellyfin.Plugin.Template.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Jellyfin.Plugin.Template.Services;

/// <inheritdoc />
public class AppPasswordStore : IAppPasswordStore
{
    private static readonly PasswordHasher<AppPassword> PasswordHasher = new();

    private readonly IDbContextFactory<PodcastsDbContext> _dbContextFactory;

    /// <summary>Initializes a new instance of the <see cref="AppPasswordStore"/> class.</summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public AppPasswordStore(IDbContextFactory<PodcastsDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    /// <inheritdoc />
    public async Task<(string Token, AppPassword Password)> CreateAsync(
        Guid userId, string label, CancellationToken ct = default)
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        var token = Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

        var password = new AppPassword
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Label = label,
            CreatedAt = DateTime.UtcNow,
        };
        password.TokenHash = PasswordHasher.HashPassword(password, token);

        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        ctx.AppPasswords.Add(password);
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);

        return (token, password);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AppPassword>> GetForUserAsync(
        Guid userId, CancellationToken ct = default)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        return await ctx.AppPasswords
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid userId, Guid passwordId, CancellationToken ct = default)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var password = await ctx.AppPasswords
            .FirstOrDefaultAsync(p => p.Id == passwordId && p.UserId == userId, ct)
            .ConfigureAwait(false);

        if (password is null)
        {
            return false;
        }

        ctx.AppPasswords.Remove(password);
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task<AppPassword?> ValidateTokenAsync(string token, CancellationToken ct = default)
    {
        await using var ctx = await _dbContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var passwords = await ctx.AppPasswords.ToListAsync(ct).ConfigureAwait(false);
        var password = passwords.Find(p =>
            PasswordHasher.VerifyHashedPassword(p, p.TokenHash, token) != PasswordVerificationResult.Failed);

        if (password is null)
        {
            return null;
        }

        password.LastUsedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(ct).ConfigureAwait(false);
        return password;
    }
}
#pragma warning restore CA2007
