using System.IO;

namespace ChillWithYou.EnvSync.Core
{
    // Filesystem adapter kept at the application edge; callers own serialization.
    internal static class AtomicFileWriter
    {
        internal static void Commit(string tempPath, string targetPath, string backupPath)
        {
            try
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
            }
            catch
            {
                try { File.Delete(tempPath); } catch { }
                throw;
            }

            try { File.Delete(backupPath); } catch { }
        }
    }
}
