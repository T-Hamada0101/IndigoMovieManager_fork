using System.Globalization;
using System.Text;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// サムネイル系のCSV出力をまとめる。
    /// service 本体からCSV整形の知識を剥がす。
    /// </summary>
    internal static class ThumbnailCsvUtility
    {
        public static void WriteThumbnailCreateProcessLog(
            string logFileName,
            object syncRoot,
            string engineId,
            string movieFullPath,
            string codec,
            double? durationSec,
            long fileSizeBytes,
            string outputPath,
            bool isSuccess,
            string errorMessage
        )
        {
            try
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "IndigoMovieManager_fork",
                    "logs"
                );
                Directory.CreateDirectory(logDir);

                string logPath = Path.Combine(logDir, logFileName);
                bool needsHeader = !Path.Exists(logPath) || new FileInfo(logPath).Length == 0;
                string durationText =
                    durationSec.HasValue && durationSec.Value > 0
                        ? durationSec.Value.ToString("0.###", CultureInfo.InvariantCulture)
                        : "";
                string sizeText =
                    fileSizeBytes > 0 ? fileSizeBytes.ToString(CultureInfo.InvariantCulture) : "0";
                string movieFileName = Path.GetFileName(movieFullPath) ?? "";
                string line = string.Join(
                    ",",
                    EscapeCsvValue(
                        DateTime.Now.ToString(
                            "yyyy-MM-dd HH:mm:ss.fff",
                            CultureInfo.InvariantCulture
                        )
                    ),
                    EscapeCsvValue(engineId ?? ""),
                    EscapeCsvValue(movieFileName),
                    EscapeCsvValue(codec ?? ""),
                    EscapeCsvValue(durationText),
                    EscapeCsvValue(sizeText),
                    EscapeCsvValue(outputPath ?? ""),
                    EscapeCsvValue(isSuccess ? "success" : "failed"),
                    EscapeCsvValue(errorMessage ?? "")
                );

                lock (syncRoot)
                {
                    using StreamWriter writer = new(logPath, append: true, new UTF8Encoding(false));
                    if (needsHeader)
                    {
                        writer.WriteLine(
                            "datetime,engine,movie_file_name,codec,length_sec,size_bytes,output_path,status,error_message"
                        );
                    }

                    writer.WriteLine(line);
                }
            }
            catch
            {
                // ログ失敗で本体処理を止めない。
            }
        }

        public static string EscapeCsvValue(string value)
        {
            value ??= "";
            if (
                !value.Contains(',')
                && !value.Contains('"')
                && !value.Contains('\n')
                && !value.Contains('\r')
            )
            {
                return value;
            }

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
    }
}
