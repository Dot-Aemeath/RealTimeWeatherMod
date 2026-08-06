using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Bulbul;

namespace ChillWithYou.EnvSync.Core
{
    public partial class SceneryAutomationSystem
    {
        private static string _userDataPath;

        private static string GetUserDataPath()
        {
            if (_userDataPath == null)
            {
                _userDataPath = Path.Combine(Paths.ConfigPath, "chillwithyou.envsync.userdata");
            }
            return _userDataPath;
        }

        private static bool LoadUserInteractedMods()
        {
            try
            {
                var path = GetUserDataPath();
                if (!File.Exists(path)) return true;

                var content = File.ReadAllText(path);
                var parsed = AutoManagedStateCodec.Parse(content);
                bool releasedManaged = false;
                foreach (string name in parsed)
                {
                    if (Enum.TryParse(name, out EnvironmentType env))
                    {
                        OwnershipTransition transition =
                            _ownership.RecordUserInteraction(env);
                        releasedManaged |= transition.ManagedOwnershipReleased;
                        if (env == EnvironmentType.Whale)
                        {
                            _dailyTriggers.ClearWhaleOverride();
                        }
                    }
                }

                if (releasedManaged)
                {
                    _ownershipDirty = true;
                    FlushOwnershipIfDirty();
                }

                int count = _ownership.UserOwnedCount;
                if (count > 0)
                {
                    ChillEnvPlugin.Log?.LogInfo(
                        $"[用户数据] 已恢复 {count} 项用户操作记录");
                }
                return true;
            }
            catch (Exception ex)
            {
                ChillEnvPlugin.Log?.LogWarning($"[用户数据] 读取失败: {ex}");
                return false;
            }
        }

        private static bool PersistUserInteractedMods()
        {
            try
            {
                var names = new List<string>();
                foreach (EnvironmentType env in _ownership.UserOwnedSnapshot())
                {
                    names.Add(env.ToString());
                }

                var path = GetUserDataPath();
                var tempPath = path + ".tmp";
                var backupPath = path + ".bak";
                File.WriteAllText(tempPath, AutoManagedStateCodec.Serialize(names));
                AtomicFileWriter.Commit(tempPath, path, backupPath);
                return true;
            }
            catch (Exception ex)
            {
                ChillEnvPlugin.Log?.LogWarning($"[用户数据] 写入失败: {ex}");
                return false;
            }
        }

        /// <summary>
        /// The only user-event seam exposed to Harmony. User ownership, managed
        /// ownership, persistence and whale override state change as one operation.
        /// </summary>
        internal static void RecordUserInteraction(EnvironmentType environment)
        {
            if (IsSystemOperation || !IsUserInteractionTrackingReady)
            {
                return;
            }

            OwnershipTransition transition = _ownership.RecordUserInteraction(environment);
            if (transition.UserOwnershipAdded)
            {
                PersistUserInteractedMods();
                ChillEnvPlugin.Log?.LogInfo(
                    $"[用户交互] 用户接管了 {environment}，停止自动托管。");
            }

            if (transition.ManagedOwnershipReleased)
            {
                _ownershipDirty = true;
                FlushOwnershipIfDirty();
                ChillEnvPlugin.Log?.LogDebug(
                    $"[用户交互] 已从托管列表移除 {environment}");
            }

            if (environment == EnvironmentType.Whale &&
                _dailyTriggers.ClearWhaleOverride())
            {
                PersistTriggerState();
                ChillEnvPlugin.Log?.LogInfo(
                    "[鲸鱼彩蛋] 用户手动关闭了系统抽中的鲸鱼，标志已清除");
            }
        }
    }
}
