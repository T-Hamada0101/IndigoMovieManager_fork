using System.Drawing;
using System.Drawing.Imaging;
using System.IO.Compression;
using IndigoMovieManager.Thumbnail.Swf;

namespace IndigoMovieManager_fork.Tests;

[TestFixture]
[NonParallelizable]
public class SwfThumbnailGenerationServiceTests
{
    [Test]
    public async Task TryCaptureRepresentativeFrameAsync_FWS埋め込みJPEGがあればffmpegへ縮退しない()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string swfPath = CreateSwfWithEmbeddedJpeg(tempRoot, "FWS");
            string outputPath = Path.Combine(tempRoot, "thumb.jpg");
            var service = new ObservingSwfThumbnailGenerationService();

            SwfThumbnailCandidate result = await service.TryCaptureRepresentativeFrameAsync(
                swfPath,
                outputPath,
                SwfThumbnailCaptureOptions.CreateDefault(96, 72)
            );

            Assert.That(result.IsFrameAccepted, Is.True);
            Assert.That(result.CaptureKind, Is.EqualTo("extract"));
            Assert.That(service.FallbackCallCount, Is.EqualTo(0));
            Assert.That(Path.Exists(outputPath), Is.True);

            using Bitmap bitmap = new(outputPath);
            Assert.That(bitmap.Width, Is.EqualTo(96));
            Assert.That(bitmap.Height, Is.EqualTo(72));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void TryExtractRepresentativeImage_CWS埋め込みJPEG2を直接取り出せる()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string swfPath = CreateSwfWithEmbeddedJpeg(tempRoot, "CWS");
            string outputPath = Path.Combine(tempRoot, "extract.jpg");
            var extractor = new SwfEmbeddedImageExtractor();

            SwfEmbeddedImageExtractionResult result = extractor.TryExtractRepresentativeImage(
                swfPath,
                outputPath,
                SwfThumbnailCaptureOptions.CreateDefault(80, 60)
            );

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Detail, Does.Contain("tag=21"));
            Assert.That(Path.Exists(outputPath), Is.True);

            using Bitmap bitmap = new(outputPath);
            Assert.That(bitmap.Width, Is.EqualTo(80));
            Assert.That(bitmap.Height, Is.EqualTo(60));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public void TryExtractRepresentativeImage_ZWSは未対応署名として判定する()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string swfPath = CreateSignatureOnlySwf(tempRoot, "ZWS");
            string outputPath = Path.Combine(tempRoot, "extract.jpg");
            var extractor = new SwfEmbeddedImageExtractor();

            SwfEmbeddedImageExtractionResult result = extractor.TryExtractRepresentativeImage(
                swfPath,
                outputPath,
                SwfThumbnailCaptureOptions.CreateDefault(80, 60)
            );

            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Detail, Does.Contain("signature=ZWS"));
            Assert.That(Path.Exists(outputPath), Is.False);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Test]
    public async Task TryCaptureRepresentativeFrameAsync_ZWSは抽出を諦めてffmpeg縮退へ進む()
    {
        string tempRoot = CreateTempRoot();
        try
        {
            string swfPath = CreateSignatureOnlySwf(tempRoot, "ZWS");
            string outputPath = Path.Combine(tempRoot, "thumb.jpg");
            var service = new ObservingSwfThumbnailGenerationService();

            SwfThumbnailCandidate result = await service.TryCaptureRepresentativeFrameAsync(
                swfPath,
                outputPath,
                SwfThumbnailCaptureOptions.CreateDefault(96, 72)
            );

            Assert.That(result.IsFrameAccepted, Is.False);
            Assert.That(result.CaptureKind, Is.EqualTo("ffmpeg"));
            Assert.That(service.FallbackCallCount, Is.EqualTo(1));
            Assert.That(result.FailureReason, Is.EqualTo("ffmpeg fallback should not run"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string CreateTempRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "IndigoMovieManager_fork_swf_tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateSwfWithEmbeddedJpeg(string tempRoot, string signature)
    {
        string path = Path.Combine(tempRoot, $"sample-{signature.ToLowerInvariant()}.swf");
        byte[] jpegBytes = CreateJpegBytes(40, 30, Color.Coral);
        byte[] body = BuildSwfBodyWithDefineBitsJpeg2(jpegBytes);
        byte[] fileBytes = BuildSwfFile(signature, body);
        File.WriteAllBytes(path, fileBytes);
        return path;
    }

    private static string CreateSignatureOnlySwf(string tempRoot, string signature)
    {
        string path = Path.Combine(tempRoot, $"sample-{signature.ToLowerInvariant()}.swf");
        byte[] fileBytes = BuildSignatureOnlySwfFile(signature);
        File.WriteAllBytes(path, fileBytes);
        return path;
    }

    private static byte[] CreateJpegBytes(int width, int height, Color fillColor)
    {
        using Bitmap bitmap = new(width, height);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(fillColor);
        using MemoryStream ms = new();
        bitmap.Save(ms, ImageFormat.Jpeg);
        return ms.ToArray();
    }

    private static byte[] BuildSwfBodyWithDefineBitsJpeg2(byte[] jpegBytes)
    {
        using MemoryStream body = new();

        // RECT(0,0,0,0) + FrameRate + FrameCount を最小構成で入れる。
        body.WriteByte(0x08);
        body.WriteByte(0x00);
        body.WriteByte(0x0C);
        body.WriteByte(0x00);
        body.WriteByte(0x01);
        body.WriteByte(0x00);

        using MemoryStream payload = new();
        WriteUInt16(payload, 1);
        payload.Write(jpegBytes, 0, jpegBytes.Length);
        WriteTag(body, 21, payload.ToArray());
        WriteTag(body, 0, []);
        return body.ToArray();
    }

    private static byte[] BuildSwfFile(string signature, byte[] body)
    {
        byte[] header = new byte[8];
        byte[] signatureBytes = signature.ToUpperInvariant() switch
        {
            "FWS" => [0x46, 0x57, 0x53],
            "CWS" => [0x43, 0x57, 0x53],
            _ => throw new ArgumentOutOfRangeException(nameof(signature)),
        };

        Buffer.BlockCopy(signatureBytes, 0, header, 0, 3);
        header[3] = 9;

        int uncompressedFileLength = header.Length + body.Length;
        header[4] = (byte)(uncompressedFileLength & 0xFF);
        header[5] = (byte)((uncompressedFileLength >> 8) & 0xFF);
        header[6] = (byte)((uncompressedFileLength >> 16) & 0xFF);
        header[7] = (byte)((uncompressedFileLength >> 24) & 0xFF);

        if (string.Equals(signature, "FWS", StringComparison.OrdinalIgnoreCase))
        {
            byte[] fileBytes = new byte[header.Length + body.Length];
            Buffer.BlockCopy(header, 0, fileBytes, 0, header.Length);
            Buffer.BlockCopy(body, 0, fileBytes, header.Length, body.Length);
            return fileBytes;
        }

        using MemoryStream compressedBody = new();
        using (ZLibStream zlib = new(compressedBody, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(body, 0, body.Length);
        }

        byte[] compressed = compressedBody.ToArray();
        byte[] result = new byte[header.Length + compressed.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(compressed, 0, result, header.Length, compressed.Length);
        return result;
    }

    private static byte[] BuildSignatureOnlySwfFile(string signature)
    {
        byte[] header = new byte[8];
        byte[] signatureBytes = signature.ToUpperInvariant() switch
        {
            "ZWS" => [0x5A, 0x57, 0x53],
            "FWS" => [0x46, 0x57, 0x53],
            "CWS" => [0x43, 0x57, 0x53],
            _ => throw new ArgumentOutOfRangeException(nameof(signature)),
        };

        Buffer.BlockCopy(signatureBytes, 0, header, 0, 3);
        header[3] = 9;
        header[4] = (byte)(header.Length & 0xFF);
        header[5] = (byte)((header.Length >> 8) & 0xFF);
        header[6] = (byte)((header.Length >> 16) & 0xFF);
        header[7] = (byte)((header.Length >> 24) & 0xFF);
        return header;
    }

    private static void WriteTag(Stream stream, int tagType, byte[]? payload)
    {
        int length = payload?.Length ?? 0;
        if (length >= 0x3F)
        {
            WriteUInt16(stream, (ushort)((tagType << 6) | 0x3F));
            WriteInt32(stream, length);
        }
        else
        {
            WriteUInt16(stream, (ushort)((tagType << 6) | length));
        }

        if (payload is { Length: > 0 })
        {
            stream.Write(payload, 0, payload.Length);
        }
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
    }

    private static void WriteInt32(Stream stream, int value)
    {
        stream.WriteByte((byte)(value & 0xFF));
        stream.WriteByte((byte)((value >> 8) & 0xFF));
        stream.WriteByte((byte)((value >> 16) & 0xFF));
        stream.WriteByte((byte)((value >> 24) & 0xFF));
    }

    private sealed class ObservingSwfThumbnailGenerationService : SwfThumbnailGenerationService
    {
        public int FallbackCallCount { get; private set; }

        protected override Task<SwfThumbnailCandidate> TryCaptureWithFfmpegCandidatesAsync(
            string swfInputPath,
            string outputPath,
            SwfThumbnailCaptureOptions options,
            CancellationToken cts
        )
        {
            FallbackCallCount++;
            return Task.FromResult(
                SwfThumbnailCandidate.CreateRejected(
                    0d,
                    outputPath,
                    "ffmpeg fallback should not run",
                    "",
                    false,
                    false,
                    "ffmpeg"
                )
            );
        }
    }
}
