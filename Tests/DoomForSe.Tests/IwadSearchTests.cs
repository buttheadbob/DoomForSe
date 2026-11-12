using ManagedDoom;
using Xunit;

namespace DoomForSe.Tests;

/// <summary>The player's own IWAD is found whatever the case of its file name, as on case-sensitive file systems.</summary>
public sealed class IwadSearchTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "DoomForSe.Tests." + Guid.NewGuid().ToString("N"));

    public IwadSearchTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string Add(string name)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, new byte[] { 0x49, 0x57, 0x41, 0x44 }); // "IWAD"
        return path;
    }

    [Theory]
    [InlineData("doom2.wad")]
    [InlineData("DOOM2.WAD")]
    [InlineData("Doom2.Wad")]
    [InlineData("freedoom1.wad")]
    public void FindsTheIwadIgnoringCase(string name)
    {
        var path = Add(name);
        Assert.Equal(path, ConfigUtilities.FindIwad(folder));
    }

    [Fact]
    public void KeepsThePriorityOrderWhateverTheCase()
    {
        Add("freedoom1.wad");
        var doom2 = Add("doom2.wad");
        Add("DOOM.WAD");
        Assert.Equal(doom2, ConfigUtilities.FindIwad(folder));
    }

    [Fact]
    public void IgnoresOtherFiles()
    {
        Add("doomsav7-freedoom1-1b760c4.dsg");
        Add("managed-doom.cfg");
        Add("mymod.wad");
        Assert.Null(ConfigUtilities.FindIwad(folder));
    }

    [Fact]
    public void MissingFolderGivesNull() =>
        Assert.Null(ConfigUtilities.FindIwad(Path.Combine(folder, "missing")));
}
