using System.Buffers.Binary;
using System.IO;
using System.Xml.Linq;
using Xunit;

namespace WinClonerX.App.Tests;

public sealed class IconAssetTests
{
    [Fact]
    public void ApplicationIconIsPresentAndContainsExpectedSizes()
    {
        var root = FindRepoRoot();
        var iconPath = Path.Combine(root, "assets", "icon", "winclonerx.ico");
        Assert.True(File.Exists(iconPath), $"The ICO file was not found at '{iconPath}'.");

        var data = File.ReadAllBytes(iconPath);
        Assert.True(data.Length >= 6, "The ICO file is shorter than its directory header.");
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0, 2)));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2, 2)));

        var entryCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4, 2));
        Assert.True(entryCount >= 4, "The ICO file must have enough image entries.");
        Assert.True(data.Length >= 6 + (entryCount * 16), "The ICO directory is truncated.");

        var sizes = new HashSet<int>();
        for (var index = 0; index < entryCount; index++)
        {
            var entryOffset = 6 + (index * 16);
            var width = data[entryOffset] == 0 ? 256 : data[entryOffset];
            var height = data[entryOffset + 1] == 0 ? 256 : data[entryOffset + 1];
            var byteCount = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entryOffset + 8, 4));
            var imageOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(entryOffset + 12, 4));

            Assert.Equal(width, height);
            Assert.True(byteCount > 0 && (ulong)imageOffset + byteCount <= (ulong)data.Length, "An ICO image payload is out of bounds.");
            sizes.Add(width);
        }

        Assert.Contains(16, sizes);
        Assert.Contains(32, sizes);
        Assert.Contains(48, sizes);
        Assert.Contains(256, sizes);

        var projectPath = Path.Combine(root, "src", "WinClonerX.App", "WinClonerX.App.csproj");
        var project = XDocument.Load(projectPath);
        Assert.Contains(project.Descendants("ApplicationIcon"), element =>
            element.Value.Contains("winclonerx.ico", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WinClonerX.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"WinClonerX.slnx was not found above '{AppContext.BaseDirectory}'.");
    }
}
