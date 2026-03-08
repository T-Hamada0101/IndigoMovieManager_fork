using System.Globalization;
using System.Runtime.InteropServices;

namespace IndigoMovieManager.Thumbnail
{
    /// <summary>
    /// Shell 経由の最終フォールバック情報取得をまとめる。
    /// COM 解放を含む不安定な処理を service 本体から切り離す。
    /// </summary>
    internal static class ThumbnailShellMetadataUtility
    {
        public static double? TryGetDurationSecFromShell(string fileName)
        {
            object shellObj = null;
            object folderObj = null;
            object itemObj = null;
            try
            {
                var shellAppType = Type.GetTypeFromProgID("Shell.Application");
                if (shellAppType == null)
                {
                    return null;
                }

                shellObj = Activator.CreateInstance(shellAppType);
                if (shellObj == null)
                {
                    return null;
                }

                string path = Path.GetDirectoryName(fileName) ?? "";
                string name = Path.GetFileName(fileName);
                folderObj = shellAppType.InvokeMember(
                    "NameSpace",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    shellObj,
                    [path]
                );
                if (folderObj == null)
                {
                    return null;
                }

                itemObj = folderObj
                    .GetType()
                    .InvokeMember(
                        "ParseName",
                        System.Reflection.BindingFlags.InvokeMethod,
                        null,
                        folderObj,
                        [name]
                    );
                if (itemObj == null)
                {
                    return null;
                }

                string timeString = folderObj
                    .GetType()
                    .InvokeMember(
                        "GetDetailsOf",
                        System.Reflection.BindingFlags.InvokeMethod,
                        null,
                        folderObj,
                        [itemObj, 27]
                    )
                    ?.ToString();

                if (TimeSpan.TryParse(timeString, out TimeSpan ts) && ts.TotalSeconds > 0)
                {
                    return Math.Truncate(ts.TotalSeconds);
                }

                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                ReleaseComObject(itemObj);
                ReleaseComObject(folderObj);
                ReleaseComObject(shellObj);
            }
        }

        private static void ReleaseComObject(object comObj)
        {
            if (comObj == null)
            {
                return;
            }

            try
            {
                if (Marshal.IsComObject(comObj))
                {
                    Marshal.FinalReleaseComObject(comObj);
                }
            }
            catch
            {
                // COM解放失敗は握り潰す。
            }
        }
    }
}
