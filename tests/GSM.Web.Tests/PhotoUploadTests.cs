using GSM.UI.Shared.Uploads;
using Microsoft.AspNetCore.Components.Forms;

namespace GSM.Web.Tests;

/// <summary>
/// Fotos desde el iPhone (E3 de FEEDBACK-PRODUCCION): la foto se lee entera antes de enviarla. Si el navegador no
/// la puede convertir a JPEG, se manda tal cual, sin inventar el tipo. La conversión en sí la hace el navegador
/// (RequestImageFileAsync) y aquí no se puede ejercitar.
/// </summary>
public class PhotoUploadTests
{
    [Fact]
    public async Task Without_browser_conversion_the_file_is_buffered_whole_and_keeps_its_name_and_type()
    {
        var bytes = Enumerable.Range(0, 300_000).Select(i => (byte)(i % 251)).ToArray();

        var prepared = await PhotoUpload.PrepareAsync(new FakeFile("IMG_0042.png", "image/png", bytes));

        Assert.Equal("IMG_0042.png", prepared.FileName);
        Assert.Equal("image/png", prepared.ContentType);
        Assert.True(prepared.Data.CanSeek);
        Assert.Equal(0, prepared.Data.Position);
        var read = new MemoryStream();
        await prepared.Data.CopyToAsync(read);
        Assert.Equal(bytes, read.ToArray());
    }

    [Fact]
    public async Task A_file_without_type_is_not_passed_off_as_jpeg()
    {
        var prepared = await PhotoUpload.PrepareAsync(new FakeFile("IMG_0043.HEIC", "", [1, 2, 3]));

        Assert.Equal("application/octet-stream", prepared.ContentType);
    }

    [Fact]
    public async Task A_file_over_5_mb_fails_with_an_io_error_the_page_can_handle()
    {
        var file = new FakeFile("enorme.jpg", "image/jpeg", new byte[5 * 1024 * 1024 + 1]);

        await Assert.ThrowsAnyAsync<IOException>(() => PhotoUpload.PrepareAsync(file));
    }

    private sealed class FakeFile(string name, string contentType, byte[] content) : IBrowserFile
    {
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => content.Length;
        public string ContentType => contentType;

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
            => Size > maxAllowedSize
                ? throw new IOException($"Supported file size is {maxAllowedSize} bytes.")
                : new MemoryStream(content, writable: false);
    }
}
