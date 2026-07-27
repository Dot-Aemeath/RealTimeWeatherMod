using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace ChillWithYou.EnvSync.Core
{
    public partial class SceneryAutomationSystem
    {
        private static readonly object _triggerStateLock = new object();
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

                    ApplyTriggerEntry(key, val);
                }

                if (fileDate.Date == DateTime.Today)
                {
                    _lastDailyCheck = DateTime.Today;
                    ChillEnvPlugin.Log?.LogInfo("[触发状态] 已恢复当日概率彩蛋触发状态");
                }
            }
            catch (Exception ex)
            {
                ChillEnvPlugin.Log?.LogWarning($"[触发状态] 读取失败: {ex.Message}");
            }
        }

        private static void ApplyTriggerEntry(string name, string val)
        {
            SceneryAutomationTriggerStateCodec.Entry entry;
            if (!SceneryAutomationTriggerStateCodec.TryParseEntry(val, out entry))
                return;

            switch (name)
            {
                case "BlueButterfly":
                    if (entry.Triggered)
                    {
                        _blueButterflyTriggeredToday = true;
                        if (entry.StartTime != DateTime.MinValue)
                            _blueButterflyStartTime = entry.StartTime;
                        else
                        {
                            ChillEnvPlugin.Log?.LogWarning(
                                "[触发状态] 蓝蝶起始时间不可用，已使用当前时间兜底");
                            _blueButterflyStartTime = DateTime.Now;
                        }
                    }
                    break;
                case "WindBell":
                    _windBellTriggeredToday = entry.Triggered;
                    _windBellRollDoneToday = entry.RollDone || entry.Triggered;
                    break;
                case "HotSpring":
                    _hotSpringTriggeredToday = entry.Triggered;
                    _hotSpringRollDoneToday = entry.RollDone || entry.Triggered;
                    break;
                case "Space":
                    _spaceTriggeredToday = entry.Triggered;
                    _spaceRollDoneToday = entry.RollDone || entry.Triggered;
                    break;
                case "Whale":
                    _whaleTriggeredToday = entry.Triggered;
                    _whaleRollDoneToday = entry.RollDone || entry.Triggered;
                    if (entry.Triggered)
                    {
                        IsWhaleSystemTriggered = _autoEnabledMods.Contains(Env_Whale);
                    }
                    break;
            }
        }

        internal static void PersistTriggerState()
        {
            try
            {
                var lines = new List<string>();
                lines.Add($"date={DateTime.Today:yyyy-MM-dd}");

                lock (_triggerStateLock)
                {
                    if (_blueButterflyTriggeredToday)
                        AddTriggerLine(lines, "BlueButterfly", true, false, _blueButterflyStartTime);
                    AddTriggerLine(lines, "WindBell", _windBellTriggeredToday, _windBellRollDoneToday);
                    AddTriggerLine(lines, "HotSpring", _hotSpringTriggeredToday, _hotSpringRollDoneToday);
                    AddTriggerLine(lines, "Space", _spaceTriggeredToday, _spaceRollDoneToday);
                    AddTriggerLine(lines, "Whale", _whaleTriggeredToday, _whaleRollDoneToday);

                    var path = GetTriggerStatePath();
                    var tempPath = path + ".tmp";
                    var backupPath = path + ".bak";
                    File.WriteAllLines(tempPath, lines);
                    AtomicFileWriter.Commit(tempPath, path, backupPath);
                }
            }
            catch (Exception ex)
            {
                ChillEnvPlugin.Log?.LogWarning($"[触发状态] 写入失败: {ex.Message}");
            }
        }

        private static void AddTriggerLine(
            List<string> lines,
            string name,
            bool triggered,
            bool rollDone)
        {
            AddTriggerLine(lines, name, triggered, rollDone, DateTime.MinValue);
        }

        private static void AddTriggerLine(
            List<string> lines,
            string name,
            bool triggered,
            bool rollDone,
            DateTime startTime)
        {
            string value = SceneryAutomationTriggerStateCodec.FormatEntry(
                triggered,
                rollDone,
                startTime);
            if (value != null)
                lines.Add(name + "=" + value);
        }
    }
}
