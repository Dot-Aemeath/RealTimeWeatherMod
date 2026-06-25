using System.IO;

namespace ChillWithYou.EnvSync.Core
{
    internal static class AtomicFileWriter
    {
        internal static void Commit(string tempPath, string targetPath, string backupPath)
        {
            if (!File.Exists(targetPath))
            {
                File.Move(tempPath, targetPath);
                return;
            }

            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Replace(tempPath, targetPath, backupPath);
            try { File.Delete(backupPath); } catch { }
        }
    }
}
