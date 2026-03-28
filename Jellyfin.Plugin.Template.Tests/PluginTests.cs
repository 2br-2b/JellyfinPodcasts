using System;
using Jellyfin.Plugin.Template.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.Template.Tests;

public class PluginTests
{
    private readonly Mock<IApplicationPaths> _appPaths = new();
    private readonly Mock<IXmlSerializer> _xmlSerializer = new();

    private Plugin CreatePlugin()
    {
        _appPaths.Setup(p => p.PluginsPath).Returns("/tmp/plugins");
        _appPaths.Setup(p => p.PluginConfigurationsPath).Returns("/tmp/plugins/config");
        _xmlSerializer.Setup(s => s.DeserializeFromFile(typeof(PluginConfiguration), It.IsAny<string>()))
            .Returns(new PluginConfiguration());

        return new Plugin(_appPaths.Object, _xmlSerializer.Object);
    }

    [Fact]
    public void Plugin_Name_IsCorrect()
    {
        var plugin = CreatePlugin();
        Assert.Equal("Jellyfin Podcasts", plugin.Name);
    }

    [Fact]
    public void Plugin_Id_IsCorrect()
    {
        var plugin = CreatePlugin();
        Assert.Equal(Guid.Parse("22395da6-06bf-4fb0-aa05-2f44881361f8"), plugin.Id);
    }

    [Fact]
    public void Plugin_Instance_IsSet_AfterConstruction()
    {
        var plugin = CreatePlugin();
        Assert.NotNull(Plugin.Instance);
        Assert.Same(plugin, Plugin.Instance);
    }

    [Fact]
    public void Plugin_GetPages_ReturnsOnePage()
    {
        var plugin = CreatePlugin();
        var pages = plugin.GetPages();
        Assert.Single(pages);
    }

    [Fact]
    public void Plugin_GetPages_PageName_MatchesPluginName()
    {
        var plugin = CreatePlugin();
        var page = Assert.Single(plugin.GetPages());
        Assert.Equal(plugin.Name, page.Name);
    }

    [Fact]
    public void PluginConfiguration_Defaults_AreCorrect()
    {
        var config = new PluginConfiguration();
        Assert.True(config.TrueFalseSetting);
        Assert.Equal(2, config.AnInteger);
        Assert.Equal("string", config.AString);
        Assert.Equal(SomeOptions.AnotherOption, config.Options);
    }
}
