using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using IndigoMovieManager.Thumbnail.Engines;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// 失敗時プレースホルダーと固定エラー画像の責務をまとめる。
    /// engine / preflight から直接使えるようにし、service 本体の知識を減らす。
    /// </summary>
    internal static class ThumbnailPlaceholderUtility
    {
        private static readonly string[] DrmErrorKeywords =
        [
            "prdy",
            "playready",
            "drm",
            "encrypted",
            "protected",
            "no decoder found for: none",
            "video stream is missing",
        ];

        private static readonly string[] UnsupportedErrorKeywords =
        [
            "decoder not found",
            "video stream not found",
            "unknown codec",
            "unsupported",
            "invalid data found",
            "failed to open input",
        ];

        private static readonly string[] CorruptionButNotUnsupportedKeywords =
        [
            "invalid nal unit size",
            "missing picture in access unit",
            "error splitting the input into nal units",
            "corrupt decoded frame",
            "error while decoding mb",
            "error submitting packet to decoder",
            "decoding error",
            "decode error rate",
            "terminating thread with return code",
        ];

        public static FailurePlaceholderKind ClassifyFailure(
            string codec,
            IReadOnlyList<string> engineErrorMessages
        )
        {
            StringBuilder merged = new();
            string normalizedCodec = NormalizeCodecForClassification(codec);
            if (!string.IsNullOrWhiteSpace(normalizedCodec))
            {
                merged.Append(normalizedCodec);
                merged.Append(' ');
            }

            if (engineErrorMessages != null)
            {
                for (int i = 0; i < engineErrorMessages.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(engineErrorMessages[i]))
                    {
                        continue;
                    }

                    merged.Append(engineErrorMessages[i]);
                    merged.Append(' ');
                }
            }

            string text = merged.ToString().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(text))
            {
                return FailurePlaceholderKind.None;
            }

            if (ContainsAnyKeyword(text, DrmErrorKeywords))
            {
                return FailurePlaceholderKind.DrmSuspected;
            }

            if (ContainsAnyKeyword(text, CorruptionButNotUnsupportedKeywords))
            {
                return FailurePlaceholderKind.None;
            }

            if (ContainsAnyKeyword(text, UnsupportedErrorKeywords))
            {
                return FailurePlaceholderKind.UnsupportedCodec;
            }

            return FailurePlaceholderKind.None;
        }

        // 全エンジン失敗時に、用途別の画像を作ってサムネイル欠損を防ぐ。
        public static bool TryCreateFailurePlaceholderThumbnail(
            ThumbnailJobContext context,
            FailurePlaceholderKind kind,
            out string detail
        )
        {
            detail = "";
            if (kind == FailurePlaceholderKind.None || context == null)
            {
                return false;
            }

            try
            {
                int columns = Math.Max(1, context.TabInfo?.Columns ?? 1);
                int rows = Math.Max(1, context.TabInfo?.Rows ?? 1);
                int width = Math.Max(1, context.TabInfo?.Width ?? 120);
                int height = Math.Max(1, context.TabInfo?.Height ?? 90);
                int count = columns * rows;

                List<Bitmap> frames = [];
                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        frames.Add(CreateFailurePlaceholderFrame(width, height, kind));
                    }

                    bool saved = ThumbnailImageUtility.SaveCombinedThumbnail(
                        context.SaveThumbFileName,
                        frames,
                        columns,
                        rows
                    );
                    if (!saved || !Path.Exists(context.SaveThumbFileName))
                    {
                        detail = "placeholder save failed";
                        return false;
                    }
                }
                finally
                {
                    for (int i = 0; i < frames.Count; i++)
                    {
                        frames[i]?.Dispose();
                    }
                }

                if (context.ThumbInfo?.SecBuffer != null && context.ThumbInfo.InfoBuffer != null)
                {
                    using FileStream dest = new(
                        context.SaveThumbFileName,
                        FileMode.Append,
                        FileAccess.Write
                    );
                    dest.Write(context.ThumbInfo.SecBuffer);
                    dest.Write(context.ThumbInfo.InfoBuffer);
                }

                detail = "placeholder saved";
                return true;
            }
            catch (Exception ex)
            {
                detail = ex.Message;
                return false;
            }
        }

        // 事前判定ヒット時にプレースホルダー作成が失敗しても、固定エラー画像を複製して必ずサムネイルを残す。
        public static bool TryCopyFixedErrorThumbnailForTab(
            int tabIndex,
            string saveThumbFileName,
            out string detail
        )
        {
            detail = "";
            if (string.IsNullOrWhiteSpace(saveThumbFileName))
            {
                detail = "save_path_empty";
                return false;
            }

            string imagesDir = Path.Combine(Directory.GetCurrentDirectory(), "Images");
            string primaryName = tabIndex switch
            {
                1 => "errorBig.jpg",
                2 => "errorGrid.jpg",
                3 => "errorList.jpg",
                4 => "errorBig.jpg",
                99 => "errorGrid.jpg",
                _ => "errorSmall.jpg",
            };
            string[] candidateNames =
            [
                primaryName,
                "errorSmall.jpg",
                "errorBig.jpg",
                "errorGrid.jpg",
                "errorList.jpg",
            ];

            try
            {
                string saveDir = Path.GetDirectoryName(saveThumbFileName) ?? "";
                if (!string.IsNullOrWhiteSpace(saveDir))
                {
                    Directory.CreateDirectory(saveDir);
                }

                for (int i = 0; i < candidateNames.Length; i++)
                {
                    string src = Path.Combine(imagesDir, candidateNames[i]);
                    if (!Path.Exists(src))
                    {
                        continue;
                    }

                    File.Copy(src, saveThumbFileName, true);
                    detail = $"fixed_image={candidateNames[i]}";
                    return true;
                }

                detail = "fixed_image_not_found";
                return false;
            }
            catch (Exception ex)
            {
                detail = $"fixed_copy_error:{ex.GetType().Name}";
                return false;
            }
        }

        private static bool ContainsAnyKeyword(string text, IReadOnlyList<string> keywords)
        {
            if (string.IsNullOrWhiteSpace(text) || keywords == null)
            {
                return false;
            }

            for (int i = 0; i < keywords.Count; i++)
            {
                string keyword = keywords[i];
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeCodecForClassification(string codec)
        {
            return string.Equals(codec?.Trim(), "unknown", StringComparison.OrdinalIgnoreCase)
                ? ""
                : codec ?? "";
        }

        // プレースホルダー1コマを描画する。画面で原因が分かることを優先する。
        private static Bitmap CreateFailurePlaceholderFrame(
            int width,
            int height,
            FailurePlaceholderKind kind
        )
        {
            Bitmap bitmap = new(width, height, PixelFormat.Format24bppRgb);
            using Graphics g = Graphics.FromImage(bitmap);

            Color background;
            Color stripe;
            string title;
            string subtitle;
            if (kind == FailurePlaceholderKind.DrmSuspected)
            {
                background = Color.FromArgb(90, 35, 35);
                stripe = Color.FromArgb(170, 65, 65);
                title = "DRM?";
                subtitle = "保護コンテンツの可能性";
            }
            else if (kind == FailurePlaceholderKind.FlashVideo)
            {
                background = Color.FromArgb(65, 55, 25);
                stripe = Color.FromArgb(200, 150, 45);
                title = "Flash";
                subtitle = "SWFシグネチャ検出";
            }
            else
            {
                background = Color.FromArgb(45, 45, 45);
                stripe = Color.FromArgb(85, 110, 130);
                title = "CODEC NG";
                subtitle = "非対応/破損の可能性";
            }

            g.Clear(background);
            using (Brush stripeBrush = new SolidBrush(stripe))
            {
                g.FillRectangle(stripeBrush, 0, 0, width, Math.Max(18, height / 4));
            }

            using (Pen borderPen = new(Color.FromArgb(220, 220, 220), 1))
            {
                g.DrawRectangle(borderPen, 0, 0, width - 1, height - 1);
            }

            float titleSize = Math.Max(8f, Math.Min(16f, width * 0.11f));
            float subtitleSize = Math.Max(6f, Math.Min(11f, width * 0.065f));
            using Font titleFont = new("Yu Gothic UI", titleSize, FontStyle.Bold, GraphicsUnit.Point);
            using Font subtitleFont = new(
                "Yu Gothic UI",
                subtitleSize,
                FontStyle.Regular,
                GraphicsUnit.Point
            );
            using Brush textBrush = new SolidBrush(Color.WhiteSmoke);
            using StringFormat centered = new()
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };

            Rectangle titleRect = new(0, Math.Max(18, height / 4), width, Math.Max(16, height / 3));
            Rectangle subtitleRect = new(
                0,
                titleRect.Bottom,
                width,
                Math.Max(14, height - titleRect.Bottom - 2)
            );
            g.DrawString(title, titleFont, textBrush, titleRect, centered);
            g.DrawString(subtitle, subtitleFont, textBrush, subtitleRect, centered);

            return bitmap;
        }
    }

    internal enum FailurePlaceholderKind
    {
        None,
        DrmSuspected,
        UnsupportedCodec,
        FlashVideo,
    }
}
