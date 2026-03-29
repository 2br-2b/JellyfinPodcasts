namespace Jellyfin.Plugin.Template.Models;

/// <summary>Known app password kinds for podcast clients.</summary>
public static class AppPasswordKinds
{
    /// <summary>App password intended for Open Podcast API clients.</summary>
    public const string OpenPodcastApi = "openpodcastapi";

    /// <summary>App password intended for gPodder-compatible clients.</summary>
    public const string GPodder = "gpodder";
}
