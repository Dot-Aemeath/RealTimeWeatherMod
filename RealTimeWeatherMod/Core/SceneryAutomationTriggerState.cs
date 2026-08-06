using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace ChillWithYou.EnvSync.Core
{
    public partial class SceneryAutomationSystem
    {
        private static string _triggerStatePath;

        private static string GetTriggerStatePath()
        {
            if (_triggerStatePath == null)
            {
                _triggerStatePath = Path.Combine(Paths.ConfigPath, "chillwithyou.envsync.triggerstate");
            }
            return _triggerStatePath;
        }

        private static void RestoreTriggerState()
        {
            try
            {
                var path = GetTriggerStatePath();
                if (!File.Exists(path)) return;

                var lines = File.ReadAllLines(path);
                DateTime fileDate = DateTime.MinValue;

                foreach (string line in lines)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0) continue;

                    int eq = trimmed.IndexOf('=');
                    if (eq < 0) continue;

                    string key = trimmed.Substring(0, eq).Trim();
                    string val = trimmed.Substring(eq + 1).Trim();

                    if (key == "date")
                    {
                        DateTime parsedDate;
                        if (SceneryAutomationTriggerStateCodec.TryParseDate(val, out parsedDate))
                            fileDate = parsedDate;
                        continue;
                    }

                    if (fileDate.Date != DateTime.Today)
                        continue;

                    SceneryDailyTrigger trigger;
                    if (Enum.TryParse(key, out trigger) && Enum.IsDefined(typeof(SceneryDailyTrigger), trigger))
                    {
                        ApplyTriggerEntry(trigger, val);
                    }
                }

                if (fileDate.Date == DateTime.Today)
                {
                    _dailyTriggers.RestoreDate(DateTime.Today);
                    ChillEnvPlugin.Log?.LogInfo("[触发状态] 已恢复当日概率彩蛋触发状态");
                }
            }
            catch (Exception ex)
            {
                ChillEnvPlugin.Log?.LogWarning($"[触发状态] 读取失败: {ex.Message}");
            }
        }

        private static void ApplyTriggerEntry(SceneryDailyTrigger trigger, string val)
        {
            SceneryAutomationTriggerStateCodec.Entry entry;
            if (!SceneryAutomationTriggerStateCodec.TryParseEntry(val, out entry))
                return;

            DateTime startTime = entry.StartTime;
            if (SceneryDailyTriggerState.HasDuration(trigger) &&
                entry.Triggered &&
                startTime == DateTime.MinValue)
            {
                // A timed egg without a timestamp would never expire. Start its clock
                // now so it runs its normal duration instead of lasting all day.
                ChillEnvPlugin.Log?.LogWarning(
                    $"[触发状态] {trigger} 起始时间不可用，已使用当前时间兜底");
                startTime = DateTime.Now;
            }

            _dailyTriggers.Restore(trigger, entry.Triggered, entry.RollDone, startTime);

            if (trigger == SceneryDailyTrigger.Whale)
            {
                _dailyTriggers.RestoreWhaleOverride(
                    entry.Triggered && _ownership.IsManaged(Env_Whale));
            }
        }

        private static void PersistTriggerState()
        {
            try
            {
                var lines = new List<string>();
                // The state normally carries its own date (set by CheckDailyReset from
                // that pass's snapshot). Only a persist before the first automation
                // pass needs to fall back to a fresh reading.
                DateTime stateDate = _dailyTriggers.Date == DateTime.MinValue
                    ? ModEnvironmentContext.Capture().Now.Date
                    : _dailyTriggers.Date;
                lines.Add($"date={stateDate:yyyy-MM-dd}");

                foreach (SceneryDailyTrigger trigger in SceneryDailyTriggerState.All)
                {
                    AddTriggerLine(lines, trigger, _dailyTriggers.Snapshot(trigger));
                }

                var path = GetTriggerStatePath();
                var tempPath = path + ".tmp";
                var backupPath = path + ".bak";
                File.WriteAllLines(tempPath, lines);
                AtomicFileWriter.Commit(tempPath, path, backupPath);
            }
            catch (Exception ex)
            {
                ChillEnvPlugin.Log?.LogWarning($"[触发状态] 写入失败: {ex.Message}");
            }
        }

        private static void AddTriggerLine(
            List<string> lines,
            SceneryDailyTrigger trigger,
            SceneryTriggerSnapshot snapshot)
        {
            string value = SceneryAutomationTriggerStateCodec.FormatEntry(
                snapshot.Triggered,
                snapshot.RollDone,
                snapshot.StartTime);
            if (value != null)
                lines.Add(trigger + "=" + value);
        }
    }
}
