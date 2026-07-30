using System.Text;
using EmailMcp.Server.Tools;
using FluentAssertions;

namespace EmailMcp.Server.Tests;

public sealed class AttachmentLoaderTests : IDisposable
{
    private readonly string tempDir;

    public AttachmentLoaderTests()
    {
        this.tempDir = Path.Combine(Path.GetTempPath(), "emailmcp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this.tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.tempDir))
        {
            Directory.Delete(this.tempDir, recursive: true);
        }
    }

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(this.tempDir, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void Load_NullOrEmpty_ReturnsNoAttachments()
    {
        AttachmentLoader.Load(null).Attachments.Should().BeEmpty();
        AttachmentLoader.Load("  ").Attachments.Should().BeEmpty();
    }

    [Fact]
    public void Load_ReadsFileContentAndFilename()
    {
        var path = this.WriteFile("note.txt", Encoding.UTF8.GetBytes("hello"));

        var result = AttachmentLoader.Load(path);

        result.Error.Should().BeNull();
        result.Attachments.Should().HaveCount(1);
        result.Attachments[0].Filename.Should().Be("note.txt");
        result.Attachments[0].Content.Should().Equal(Encoding.UTF8.GetBytes("hello"));
    }

    [Fact]
    public void Load_MultiplePathsSeparatedByCommas()
    {
        var a = this.WriteFile("a.txt", [1]);
        var b = this.WriteFile("b.txt", [2]);

        var result = AttachmentLoader.Load($"{a}, {b}");

        result.Error.Should().BeNull();
        result.Attachments.Select(x => x.Filename).Should().Equal("a.txt", "b.txt");
    }

    [Theory]
    [InlineData("doc.pdf", "application/pdf")]
    [InlineData("pic.png", "image/png")]
    [InlineData("photo.JPG", "image/jpeg")]
    [InlineData("app.apk", "application/vnd.android.package-archive")]
    [InlineData("mystery.zzz", "application/octet-stream")]
    public void Load_InfersMimeTypeFromExtension(string filename, string expected)
    {
        var path = this.WriteFile(filename, [1, 2, 3]);

        var result = AttachmentLoader.Load(path);

        result.Attachments[0].MimeType.Should().Be(expected);
    }

    [Fact]
    public void Load_MissingFile_ReturnsErrorNamingThePath()
    {
        var missing = Path.Combine(this.tempDir, "nope.txt");

        var result = AttachmentLoader.Load(missing);

        result.Attachments.Should().BeEmpty();
        result.Error.Should().NotBeNull();
        result.Error.Should().Contain("nope.txt");
    }

    [Fact]
    public void Load_TotalOverTwentyFiveMegabytes_ReturnsErrorMentioningTheLimit()
    {
        // Gmail rejects messages over 25 MB. Catch it here with a clear message
        // rather than letting the API fail opaquely after uploading everything.
        var big = this.WriteFile("big.bin", new byte[26 * 1024 * 1024]);

        var result = AttachmentLoader.Load(big);

        result.Attachments.Should().BeEmpty();
        result.Error.Should().NotBeNull();
        result.Error.Should().Contain("25");
    }

    [Fact]
    public void Load_SizeLimitAppliesToTheTotalNotEachFile()
    {
        var a = this.WriteFile("a.bin", new byte[14 * 1024 * 1024]);
        var b = this.WriteFile("b.bin", new byte[14 * 1024 * 1024]);

        var result = AttachmentLoader.Load($"{a},{b}");

        result.Error.Should().NotBeNull("28 MB across two files still exceeds the limit");
    }
}
