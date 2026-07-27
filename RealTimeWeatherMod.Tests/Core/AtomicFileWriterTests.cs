using System;
using System.IO;
using ChillWithYou.EnvSync.Core;
using Xunit;

namespace RealTimeWeatherMod.Tests.Core
{
    public class AtomicFileWriterTests
    {
        [Fact]
        public void Commit_CreatesTarget_WhenTargetDoesNotExist()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string targetPath = Path.Combine(directory, "state");
                string tempPath = targetPath + ".tmp";
                string backupPath = targetPath + ".bak";
                File.WriteAllText(tempPath, "first");

                AtomicFileWriter.Commit(tempPath, targetPath, backupPath);

                Assert.Equal("first", File.ReadAllText(targetPath));
                Assert.False(File.Exists(tempPath));
                Assert.False(File.Exists(backupPath));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void Commit_ReplacesTarget_WhenTargetAlreadyExists()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string targetPath = Path.Combine(directory, "state");
                string tempPath = targetPath + ".tmp";
                string backupPath = targetPath + ".bak";
                File.WriteAllText(targetPath, "old");
                File.WriteAllText(tempPath, "new");

                AtomicFileWriter.Commit(tempPath, targetPath, backupPath);

                Assert.Equal("new", File.ReadAllText(targetPath));
                Assert.False(File.Exists(tempPath));
                Assert.False(File.Exists(backupPath));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [Fact]
        public void Commit_CleansUpTemp_WhenReplaceFails()
        {
            string directory = CreateTemporaryDirectory();
            try
            {
                string targetPath = Path.Combine(directory, "state");
                string tempPath = targetPath + ".tmp";
                string backupPath = targetPath + ".bak";
                File.WriteAllText(targetPath, "old");
                File.WriteAllText(tempPath, "new");

                using (File.Open(targetPath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    Assert.ThrowsAny<IOException>(
                        () => AtomicFileWriter.Commit(tempPath, targetPath, backupPath));
                }

                Assert.False(File.Exists(tempPath));
                Assert.Equal("old", File.ReadAllText(targetPath));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        private static string CreateTemporaryDirectory()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "RealTimeWeatherMod.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
