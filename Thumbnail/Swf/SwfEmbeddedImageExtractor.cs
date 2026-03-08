using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;

namespace IndigoMovieManager.Thumbnail.Swf
{
    /// <summary>
    /// SWF内の埋め込み画像タグを走査し、代表画像を直接取り出す。
    /// 描画を避けられるケースでは、まずここで回収する。
    /// </summary>
    internal sealed class SwfEmbeddedImageExtractor
    {
        private const int SwfHeaderLength = 8;
        private const int EndTag = 0;
        private const int JpegTablesTag = 8;
        private const int DefineBitsTag = 6;
        private const int DefineBitsJpeg2Tag = 21;
        private const int DefineBitsJpeg3Tag = 35;
        private const int DefineBitsJpeg4Tag = 90;

        public SwfEmbeddedImageExtractionResult TryExtractRepresentativeImage(
            string swfInputPath,
            string outputPath,
            SwfThumbnailCaptureOptions options
        )
        {
            options ??= SwfThumbnailCaptureOptions.CreateDefault(320, 240);

            if (string.IsNullOrWhiteSpace(swfInputPath) || !Path.Exists(swfInputPath))
            {
                return SwfEmbeddedImageExtractionResult.CreateFailed("swf input file is missing");
            }

            if (!TryLoadNormalizedSwfBytes(swfInputPath, out byte[] swfBytes, out string loadDetail))
            {
                return SwfEmbeddedImageExtractionResult.CreateFailed(loadDetail);
            }

            if (!TryResolveTagStreamOffset(swfBytes, out int tagOffset, out string offsetDetail))
            {
                return SwfEmbeddedImageExtractionResult.CreateFailed(offsetDetail);
            }

            byte[] jpegTables = null;
            Bitmap bestBitmap = null;
            string bestSourceTag = "";

            try
            {
                int offset = tagOffset;
                while (offset + 2 <= swfBytes.Length)
                {
                    ushort recordHeader = ReadUInt16LittleEndian(swfBytes, offset);
                    offset += 2;

                    int tagType = recordHeader >> 6;
                    int tagLength = recordHeader & 0x3F;
                    if (tagLength == 0x3F)
                    {
                        if (offset + 4 > swfBytes.Length)
                        {
                            return SwfEmbeddedImageExtractionResult.CreateFailed(
                                "swf tag extended length is truncated"
                            );
                        }

                        tagLength = ReadInt32LittleEndian(swfBytes, offset);
                        offset += 4;
                    }

                    if (tagLength < 0 || offset + tagLength > swfBytes.Length)
                    {
                        return SwfEmbeddedImageExtractionResult.CreateFailed(
                            $"swf tag payload is truncated: tag={tagType}"
                        );
                    }

                    if (tagType == EndTag)
                    {
                        break;
                    }

                    ReadOnlySpan<byte> payload = new ReadOnlySpan<byte>(swfBytes, offset, tagLength);
                    offset += tagLength;

                    if (tagType == JpegTablesTag)
                    {
                        // 古い DefineBits が参照するJPEGテーブルは後段で合成する。
                        jpegTables = payload.ToArray();
                        continue;
                    }

                    if (
                        !TryDecodeBitmapFromTag(
                            tagType,
                            payload,
                            jpegTables,
                            out Bitmap candidateBitmap,
                            out string sourceTag
                        )
                    )
                    {
                        continue;
                    }

                    if (
                        SwfThumbnailFrameAnalyzer.IsMostlyFlatBrightFrame(
                            candidateBitmap,
                            options
                        )
                    )
                    {
                        candidateBitmap.Dispose();
                        continue;
                    }

                    if (!TryPromoteBestBitmap(ref bestBitmap, candidateBitmap))
                    {
                        candidateBitmap.Dispose();
                        continue;
                    }

                    bestSourceTag = sourceTag;
                }

                if (bestBitmap == null)
                {
                    return SwfEmbeddedImageExtractionResult.CreateFailed(
                        "no supported embedded bitmap tag found"
                    );
                }

                string outputDir = Path.GetDirectoryName(outputPath) ?? "";
                if (!string.IsNullOrWhiteSpace(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                using Bitmap normalized = NormalizeBitmap(bestBitmap, options);
                normalized.Save(outputPath, ImageFormat.Jpeg);
                return SwfEmbeddedImageExtractionResult.CreateSucceeded(
                    outputPath,
                    bestSourceTag,
                    normalized.Width,
                    normalized.Height
                );
            }
            catch (Exception ex)
            {
                return SwfEmbeddedImageExtractionResult.CreateFailed(
                    $"embedded image extraction failed: {ex.Message}"
                );
            }
            finally
            {
                bestBitmap?.Dispose();
            }
        }

        private static bool TryLoadNormalizedSwfBytes(
            string swfInputPath,
            out byte[] swfBytes,
            out string detail
        )
        {
            swfBytes = null;
            detail = "";

            try
            {
                byte[] fileBytes = File.ReadAllBytes(swfInputPath);
                if (fileBytes.Length < SwfHeaderLength)
                {
                    detail = "swf header is too short";
                    return false;
                }

                if (fileBytes[0] == 0x46 && fileBytes[1] == 0x57 && fileBytes[2] == 0x53)
                {
                    swfBytes = fileBytes;
                    detail = "signature=FWS";
                    return true;
                }

                if (fileBytes[0] == 0x43 && fileBytes[1] == 0x57 && fileBytes[2] == 0x53)
                {
                    using MemoryStream input = new(fileBytes, SwfHeaderLength, fileBytes.Length - SwfHeaderLength);
                    using ZLibStream zlib = new(input, CompressionMode.Decompress);
                    using MemoryStream body = new();
                    zlib.CopyTo(body);

                    swfBytes = new byte[SwfHeaderLength + body.Length];
                    Buffer.BlockCopy(fileBytes, 0, swfBytes, 0, SwfHeaderLength);
                    swfBytes[0] = 0x46;
                    Buffer.BlockCopy(body.GetBuffer(), 0, swfBytes, SwfHeaderLength, (int)body.Length);
                    detail = "signature=CWS";
                    return true;
                }

                if (fileBytes[0] == 0x5A && fileBytes[1] == 0x57 && fileBytes[2] == 0x53)
                {
                    detail = "signature=ZWS is not supported for direct extraction";
                    return false;
                }

                detail = "swf signature is unknown";
                return false;
            }
            catch (Exception ex)
            {
                detail = $"swf load failed: {ex.Message}";
                return false;
            }
        }

        private static bool TryResolveTagStreamOffset(
            byte[] swfBytes,
            out int tagOffset,
            out string detail
        )
        {
            tagOffset = 0;
            detail = "";

            if (swfBytes == null || swfBytes.Length < SwfHeaderLength + 6)
            {
                detail = "swf body is too short";
                return false;
            }

            int rectStart = SwfHeaderLength;
            int nBits = swfBytes[rectStart] >> 3;
            int rectBitLength = 5 + (nBits * 4);
            int rectByteLength = (rectBitLength + 7) / 8;
            tagOffset = rectStart + rectByteLength + 4;

            if (tagOffset > swfBytes.Length)
            {
                detail = "swf tag stream offset is outside of file";
                return false;
            }

            return true;
        }

        private static bool TryDecodeBitmapFromTag(
            int tagType,
            ReadOnlySpan<byte> payload,
            byte[] jpegTables,
            out Bitmap bitmap,
            out string sourceTag
        )
        {
            bitmap = null;
            sourceTag = "";

            byte[] imageBytes = tagType switch
            {
                DefineBitsTag => ExtractDefineBitsJpegBytes(payload, jpegTables),
                DefineBitsJpeg2Tag => ExtractDefineBitsJpeg2Bytes(payload),
                DefineBitsJpeg3Tag => ExtractDefineBitsJpeg3Bytes(payload),
                DefineBitsJpeg4Tag => ExtractDefineBitsJpeg4Bytes(payload),
                _ => null,
            };

            if (imageBytes == null || imageBytes.Length < 4)
            {
                return false;
            }

            if (!TryLoadBitmap(imageBytes, out bitmap))
            {
                return false;
            }

            sourceTag = $"tag={tagType}";
            return true;
        }

        private static byte[] ExtractDefineBitsJpegBytes(
            ReadOnlySpan<byte> payload,
            byte[] jpegTables
        )
        {
            if (payload.Length <= 2)
            {
                return null;
            }

            byte[] imageBytes = TrimJpegPrefix(payload.Slice(2).ToArray());
            if (TryLooksLikeStandaloneJpeg(imageBytes))
            {
                return imageBytes;
            }

            if (jpegTables == null || jpegTables.Length < 4)
            {
                return imageBytes;
            }

            byte[] normalizedTables = TrimJpegTerminalMarker(jpegTables);
            byte[] normalizedImage = TrimJpegPrefix(imageBytes);
            if (normalizedImage.Length >= 2 && normalizedImage[0] == 0xFF && normalizedImage[1] == 0xD8)
            {
                normalizedImage = normalizedImage[2..];
            }

            byte[] merged = new byte[normalizedTables.Length + normalizedImage.Length];
            Buffer.BlockCopy(normalizedTables, 0, merged, 0, normalizedTables.Length);
            Buffer.BlockCopy(normalizedImage, 0, merged, normalizedTables.Length, normalizedImage.Length);
            return merged;
        }

        private static byte[] ExtractDefineBitsJpeg2Bytes(ReadOnlySpan<byte> payload)
        {
            if (payload.Length <= 2)
            {
                return null;
            }

            return TrimJpegPrefix(payload.Slice(2).ToArray());
        }

        private static byte[] ExtractDefineBitsJpeg3Bytes(ReadOnlySpan<byte> payload)
        {
            if (payload.Length <= 6)
            {
                return null;
            }

            int alphaDataOffset = ReadInt32LittleEndian(payload, 2);
            int imageStart = 6;
            if (alphaDataOffset < 0 || imageStart + alphaDataOffset > payload.Length)
            {
                return null;
            }

            return TrimJpegPrefix(payload.Slice(imageStart, alphaDataOffset).ToArray());
        }

        private static byte[] ExtractDefineBitsJpeg4Bytes(ReadOnlySpan<byte> payload)
        {
            if (payload.Length <= 8)
            {
                return null;
            }

            int alphaDataOffset = ReadInt32LittleEndian(payload, 2);
            int imageStart = 8;
            if (alphaDataOffset < 0 || imageStart + alphaDataOffset > payload.Length)
            {
                return null;
            }

            return TrimJpegPrefix(payload.Slice(imageStart, alphaDataOffset).ToArray());
        }

        private static bool TryLoadBitmap(byte[] imageBytes, out Bitmap bitmap)
        {
            bitmap = null;
            try
            {
                using MemoryStream ms = new(imageBytes, writable: false);
                using Bitmap loaded = new(ms);
                bitmap = new Bitmap(loaded);
                return true;
            }
            catch
            {
                bitmap = null;
                return false;
            }
        }

        private static bool TryPromoteBestBitmap(ref Bitmap currentBest, Bitmap candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            if (currentBest == null)
            {
                currentBest = candidate;
                return true;
            }

            long currentArea = (long)currentBest.Width * currentBest.Height;
            long candidateArea = (long)candidate.Width * candidate.Height;
            if (candidateArea <= currentArea)
            {
                return false;
            }

            currentBest.Dispose();
            currentBest = candidate;
            return true;
        }

        private static Bitmap NormalizeBitmap(Bitmap source, SwfThumbnailCaptureOptions options)
        {
            int targetWidth = Math.Max(1, options?.Width ?? source.Width);
            int targetHeight = Math.Max(1, options?.Height ?? source.Height);

            if (source.Width == targetWidth && source.Height == targetHeight)
            {
                return new Bitmap(source);
            }

            Rectangle cropRect = ResolveCenterCrop(source.Width, source.Height, targetWidth, targetHeight);
            using Bitmap cropped = new(cropRect.Width, cropRect.Height, PixelFormat.Format24bppRgb);
            using (Graphics cropGraphics = Graphics.FromImage(cropped))
            {
                cropGraphics.DrawImage(
                    source,
                    new Rectangle(0, 0, cropRect.Width, cropRect.Height),
                    cropRect,
                    GraphicsUnit.Pixel
                );
            }

            Bitmap resized = new(targetWidth, targetHeight, PixelFormat.Format24bppRgb);
            using Graphics resizeGraphics = Graphics.FromImage(resized);
            resizeGraphics.InterpolationMode =
                System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            resizeGraphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            resizeGraphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            resizeGraphics.DrawImage(
                cropped,
                new Rectangle(0, 0, targetWidth, targetHeight),
                new Rectangle(0, 0, cropped.Width, cropped.Height),
                GraphicsUnit.Pixel
            );
            return resized;
        }

        private static Rectangle ResolveCenterCrop(
            int sourceWidth,
            int sourceHeight,
            int targetWidth,
            int targetHeight
        )
        {
            if (sourceWidth <= 0 || sourceHeight <= 0 || targetWidth <= 0 || targetHeight <= 0)
            {
                return new Rectangle(0, 0, Math.Max(1, sourceWidth), Math.Max(1, sourceHeight));
            }

            double sourceAspect = (double)sourceWidth / sourceHeight;
            double targetAspect = (double)targetWidth / targetHeight;

            if (Math.Abs(sourceAspect - targetAspect) < 0.0001d)
            {
                return new Rectangle(0, 0, sourceWidth, sourceHeight);
            }

            if (sourceAspect > targetAspect)
            {
                int cropWidth = Math.Max(1, (int)Math.Round(sourceHeight * targetAspect));
                int x = Math.Max(0, (sourceWidth - cropWidth) / 2);
                return new Rectangle(x, 0, Math.Min(cropWidth, sourceWidth), sourceHeight);
            }

            int cropHeight = Math.Max(1, (int)Math.Round(sourceWidth / targetAspect));
            int y = Math.Max(0, (sourceHeight - cropHeight) / 2);
            return new Rectangle(0, y, sourceWidth, Math.Min(cropHeight, sourceHeight));
        }

        private static bool TryLooksLikeStandaloneJpeg(byte[] imageBytes)
        {
            return imageBytes != null
                && imageBytes.Length >= 4
                && imageBytes[0] == 0xFF
                && imageBytes[1] == 0xD8;
        }

        private static byte[] TrimJpegPrefix(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length < 4)
            {
                return imageBytes;
            }

            if (
                imageBytes[0] == 0xFF
                && imageBytes[1] == 0xD9
                && imageBytes[2] == 0xFF
                && imageBytes[3] == 0xD8
            )
            {
                return imageBytes[2..];
            }

            return imageBytes;
        }

        private static byte[] TrimJpegTerminalMarker(byte[] jpegTables)
        {
            if (
                jpegTables != null
                && jpegTables.Length >= 2
                && jpegTables[^2] == 0xFF
                && jpegTables[^1] == 0xD9
            )
            {
                byte[] trimmed = new byte[jpegTables.Length - 2];
                Buffer.BlockCopy(jpegTables, 0, trimmed, 0, trimmed.Length);
                return trimmed;
            }

            return jpegTables ?? [];
        }

        private static ushort ReadUInt16LittleEndian(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | (buffer[offset + 1] << 8));
        }

        private static int ReadInt32LittleEndian(byte[] buffer, int offset)
        {
            return buffer[offset]
                | (buffer[offset + 1] << 8)
                | (buffer[offset + 2] << 16)
                | (buffer[offset + 3] << 24);
        }

        private static int ReadInt32LittleEndian(ReadOnlySpan<byte> buffer, int offset)
        {
            return buffer[offset]
                | (buffer[offset + 1] << 8)
                | (buffer[offset + 2] << 16)
                | (buffer[offset + 3] << 24);
        }
    }

    internal sealed class SwfEmbeddedImageExtractionResult
    {
        private SwfEmbeddedImageExtractionResult(
            bool isSuccess,
            string outputPath,
            string detail,
            int width,
            int height
        )
        {
            IsSuccess = isSuccess;
            OutputPath = outputPath ?? "";
            Detail = detail ?? "";
            Width = width;
            Height = height;
        }

        public bool IsSuccess { get; }

        public string OutputPath { get; }

        public string Detail { get; }

        public int Width { get; }

        public int Height { get; }

        public static SwfEmbeddedImageExtractionResult CreateSucceeded(
            string outputPath,
            string detail,
            int width,
            int height
        )
        {
            return new SwfEmbeddedImageExtractionResult(true, outputPath, detail, width, height);
        }

        public static SwfEmbeddedImageExtractionResult CreateFailed(string detail)
        {
            return new SwfEmbeddedImageExtractionResult(false, "", detail, 0, 0);
        }
    }
}
