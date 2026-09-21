using System;
using System.Collections.Generic;
using UnityEngine;
using Bulbul;

namespace ChillWithYou.EnvSync.Core
{
  public partial class SceneryAutomationSystem : MonoBehaviour
  {
    private static readonly SceneryAutomationOwnershipState<EnvironmentType> _ownership =
      new SceneryAutomationOwnershipState<EnvironmentType>();
    private static bool IsSystemOperation = false;
    internal static float InitializationTime;
    private static bool IsUserInteractionTrackingReady { get; set; }

    // --- 核心修复：点击冷却 + 延迟验证 ---
    private Dictionary<EnvironmentType, float> _lastClickTime = new Dictionary<EnvironmentType, float>();
    private Dictionary<EnvironmentType, PendingAction> _pendingActions = new Dictionary<EnvironmentType, PendingAction>();

    private const float ClickCooldown = 2.0f;      // 点击冷却
    private const float VerifyDelay = 0.5f;        // 点击后延迟验证的时间
    private const float UserInteractionQuietPeriod = 5.0f;
    private const float StartupAmbientSoundSyncInterval = 0.25f;
    private const float StartupAmbientSoundSyncTimeout = 10f;
    private static float _lastEnvironmentControllerSetupTime;
    private static SceneryAutomationSystem _instance;
    private bool _startupAmbientSoundSyncRunning;
    private bool _startupAmbientSoundSyncDone;
    private bool _needsImmediateAutomationCheck = true;
    private float _nextImmediateAutomationCheckTime;

    private class PendingAction
    {
      public bool TargetState;      // true=开启, false=关闭
      public float VerifyTime;      // 验证时间点
      public string RuleName;       // 规则名（用于日志）
    }

    private class SceneryRule
    {
      public EnvironmentType EnvType;

      // Both take the pass's snapshot rather than reading the clock themselves:
      // every rule in one pass must agree on what "now" is.
      public Func<EnvironmentInputSnapshot, bool> ShouldBeActive;
      public Func<EnvironmentInputSnapshot, bool> TryTrigger;
      public string Name;
      public SceneryRuleCategory Category = SceneryRuleCategory.SeasonalScenery;
    }

    private enum SceneryRuleCategory
    {
      SeasonalScenery,
      AmbientSound
    }

    private List<SceneryRule> _rules = new List<SceneryRule>();
    private float _checkTimer = 0f;
    private const float CheckInterval = 5f;

    // Mod内部枚举
    private const EnvironmentType Env_Fireworks = EnvironmentType.Fireworks;
    private const EnvironmentType Env_Cooking = EnvironmentType.CookSimmer;
    private const EnvironmentType Env_AC = EnvironmentType.RoomNoise;
    private const EnvironmentType Env_Sakura = EnvironmentType.Sakura;
    private const EnvironmentType Env_Cicada = EnvironmentType.Chicada;
    private const EnvironmentType Env_DeepSea = EnvironmentType.DeepSea;
    private const EnvironmentType Env_Space = EnvironmentType.Space;
    private const EnvironmentType Env_Locomotive = EnvironmentType.Locomotive;
    private const EnvironmentType Env_Balloon = EnvironmentType.Balloon;
    private const EnvironmentType Env_Books = EnvironmentType.Books;
    private const EnvironmentType Env_BlueButterfly = EnvironmentType.BlueButterfly;
    private const EnvironmentType Env_WindBell = EnvironmentType.WindBell;
    private const EnvironmentType Env_HotSpring = EnvironmentType.HotSpring;
    private const EnvironmentType Env_Whale = EnvironmentType.Whale;

    // 概率触发相关状态
    private static System.Random _random = new System.Random();
    private static readonly SceneryDailyTriggerState _dailyTriggers =
      new SceneryDailyTriggerState();
    
    // 鲸鱼专属标志：标记当前鲸鱼是否为系统抽中（而非用户手动开启）
    internal static bool IsWhaleSystemTriggered => _dailyTriggers.WhaleOverrideActive;

    private void Start()
    {
      _instance = this;
      InitializationTime = Time.realtimeSinceStartup;
      _lastEnvironmentControllerSetupTime = InitializationTime;
      IsUserInteractionTrackingReady = false;
      ResetTriggerStateDefaults();
      InitializeRules();
      RestoreManagedOwnership();
      RestoreTriggerState();
      LoadUserInteractedMods();
      StartCoroutine(EarlyAmbientSoundSync());
    }

    private static void ResetTriggerStateDefaults()
    {
      _dailyTriggers.Reset(DateTime.MinValue);
    }

    internal static void NotifyEnvironmentControllerSetup()
    {
      _lastEnvironmentControllerSetupTime = Time.realtimeSinceStartup;
      IsUserInteractionTrackingReady = false;
    }

    private void UpdateUserInteractionTrackingReadiness()
    {
      if (IsUserInteractionTrackingReady ||
          GameEnvironmentRuntime.Shared.RegisteredControllerCount == 0) return;

      float readyBaseline = Math.Max(InitializationTime, _lastEnvironmentControllerSetupTime);
      if (Time.realtimeSinceStartup - readyBaseline < UserInteractionQuietPeriod) return;

      IsUserInteractionTrackingReady = true;
      ChillEnvPlugin.Log?.LogDebug("[用户交互] 环境加载已稳定，开始追踪用户操作");
    }

    private void InitializeRules()
    {
      // 1. 烟花 (WindowView) - 农历除夕和春节期间（正月初一到初五）
      _rules.Add(new SceneryRule
      {
        Name = "Fireworks",
        EnvType = Env_Fireworks,
        ShouldBeActive = SceneryConditions.IsFireworksWindow
      });

      // 2. 做饭 (AmbientSound)
      _rules.Add(new SceneryRule
      {
        Name = "CookingAudio",
        EnvType = Env_Cooking,
        Category = SceneryRuleCategory.AmbientSound,
        ShouldBeActive = SceneryConditions.IsCookingWindow
      });

      // 3. 空调 (AmbientSound)
      _rules.Add(new SceneryRule
      {
        Name = "AC_Audio",
        EnvType = Env_AC,
        Category = SceneryRuleCategory.AmbientSound,
        ShouldBeActive = SceneryConditions.IsAirConditionerWindow
      });

      // 4. 樱花 (WindowView)
      _rules.Add(new SceneryRule
      {
        Name = "Sakura",
        EnvType = Env_Sakura,
        ShouldBeActive = SceneryConditions.IsSakuraWindow
      });

      // 5. 蝉鸣 (AmbientSound)
      _rules.Add(new SceneryRule
      {
        Name = "Cicadas",
        EnvType = Env_Cicada,
        Category = SceneryRuleCategory.AmbientSound,
        ShouldBeActive = SceneryConditions.IsCicadaWindow
      });

      // 6. 宇宙 (WindowView) - 极低概率触发 且 晴朗夜晚
      _rules.Add(new SceneryRule
      {
        Name = "Space",
        EnvType = Env_Space,
        ShouldBeActive = inputs =>
          SceneryConditions.IsSpaceWindow(inputs) &&
          _dailyTriggers.IsTriggered(SceneryDailyTrigger.Space),
        TryTrigger = inputs =>
          SceneryConditions.IsSpaceWindow(inputs) &&
          TryRollDailyTrigger(SceneryDailyTrigger.Space, 0.01)
      });

      // 7. 火车 (WindowView) - 12.24-12.25 仅限夜晚
      _rules.Add(new SceneryRule
      {
        Name = "Locomotive",
        EnvType = Env_Locomotive,
        ShouldBeActive = SceneryConditions.IsLocomotiveWindow
      });

      // 8. 热气球 (WindowView) - 6.1 儿童节仅限白天
      _rules.Add(new SceneryRule
      {
        Name = "Balloon",
        EnvType = Env_Balloon,
        ShouldBeActive = SceneryConditions.IsBalloonWindow
      });

      // 9. 魔法书 (WindowView) - 4.23 读书日 或 9.1 开学
      _rules.Add(new SceneryRule
      {
        Name = "Books",
        EnvType = Env_Books,
        ShouldBeActive = SceneryConditions.IsBooksWindow
      });

      // 10. 蓝蝶 (WindowView) - 5-6月夜晚（发光蝴蝶），20分钟一段，一天只触发一次
      _rules.Add(new SceneryRule
      {
        Name = "BlueButterfly",
        EnvType = Env_BlueButterfly,
        ShouldBeActive = inputs =>
        {
          if (!SceneryConditions.IsBlueButterflyWindow(inputs)) return false;
          if (!_ownership.IsManaged(Env_BlueButterfly)) return false;
          return _dailyTriggers.IsActiveWithin(
            SceneryDailyTrigger.BlueButterfly,
            inputs.Now,
            SceneryDailyTriggerState.BlueButterflyDuration);
        },
        TryTrigger = inputs =>
        {
          if (!SceneryConditions.IsBlueButterflyWindow(inputs)) return false;

          var segmentRandom = new System.Random(
            SceneryConditions.BlueButterflySegmentSeed(inputs.Now));
          bool stateChanged;
          bool triggered = _dailyTriggers.TryRollRecurring(
            SceneryDailyTrigger.BlueButterfly,
            inputs.Now,
            0.15,
            segmentRandom.NextDouble(),
            out stateChanged);
          if (stateChanged)
          {
            PersistTriggerState();
          }
          return triggered;
        }
      });

      // 11. 风铃 (WindowView) - 7-8月，启动后5%概率，每日计算一次
      _rules.Add(new SceneryRule
      {
        Name = "WindBell",
        EnvType = Env_WindBell,
        ShouldBeActive = inputs =>
          SceneryConditions.IsWindBellWindow(inputs) &&
          _dailyTriggers.IsTriggered(SceneryDailyTrigger.WindBell),
        TryTrigger = inputs =>
          SceneryConditions.IsWindBellWindow(inputs) &&
          TryRollDailyTrigger(SceneryDailyTrigger.WindBell, 0.05)
      });

      // 12. 温泉 (WindowView) - 11月-2月，启动后5%概率（下雪时大幅提升），每日计算一次
      _rules.Add(new SceneryRule
      {
        Name = "HotSpring",
        EnvType = Env_HotSpring,
        ShouldBeActive = inputs =>
          SceneryConditions.IsHotSpringWindow(inputs) &&
          _dailyTriggers.IsTriggered(SceneryDailyTrigger.HotSpring),
        TryTrigger = inputs =>
          SceneryConditions.IsHotSpringWindow(inputs) &&
          TryRollDailyTrigger(
            SceneryDailyTrigger.HotSpring,
            SceneryConditions.HotSpringProbability(inputs))
      });

      // 13. 鲸鱼 (WindowView) - 启动后0.05%概率，每日计算一次，触发时强制好天气
      _rules.Add(new SceneryRule
      {
        Name = "Whale",
        EnvType = Env_Whale,
        ShouldBeActive = _ => _dailyTriggers.IsTriggered(SceneryDailyTrigger.Whale),
        TryTrigger = _ => TryRollDailyTrigger(SceneryDailyTrigger.Whale, 0.0005)
      });
    }

    private bool TryRollDailyTrigger(
      SceneryDailyTrigger trigger,
      double probability)
    {
      bool stateChanged;
      bool triggeredNow;
      bool triggered = _dailyTriggers.TryRoll(
        trigger,
        probability,
        _random.NextDouble(),
        out stateChanged,
        out triggeredNow);
      if (stateChanged)
      {
        PersistTriggerState();
      }

      if (trigger == SceneryDailyTrigger.Whale && triggeredNow)
      {
        ChillEnvPlugin.Log?.LogWarning(
          "[鲸鱼彩蛋] 系统抽中鲸鱼，保持当前时段...");
      }

      return triggered;
    }

    private void Update()
    {
      if (!ChillEnvPlugin.Initialized) return;

      UpdateUserInteractionTrackingReadiness();

      // 处理延迟验证
      ProcessPendingActions();
      FlushOwnershipIfDirty();

      if (!HasAnyAutomationEnabled())
      {
        if (_ownership.ManagedCount > 0 && !ChillEnvPlugin.IsInCutscene())
        {
          CleanupDisabledAutoMods(ModEnvironmentContext.Capture());
        }
        return;
      }

      if (TryRunImmediateAutomationCheck())
      {
        return;
      }

      _checkTimer += Time.deltaTime;
      if (_checkTimer >= CheckInterval)
      {
        _checkTimer = 0f;
        RunAutomationLogic();
      }
    }

    private bool TryRunImmediateAutomationCheck()
    {
      if (!_needsImmediateAutomationCheck) return false;
      if (Time.time < _nextImmediateAutomationCheckTime) return false;

      _nextImmediateAutomationCheckTime = Time.time + StartupAmbientSoundSyncInterval;
      if (!AreAmbientSoundControllersReady()) return false;
      if (ChillEnvPlugin.IsInCutscene()) return false;

      _needsImmediateAutomationCheck = false;
      _checkTimer = 0f;
      RunAutomationLogic();
      return true;
    }

    private bool AreAmbientSoundControllersReady()
    {
      foreach (var rule in _rules)
      {
        if (rule.Category != SceneryRuleCategory.AmbientSound) continue;
        if (!GameEnvironmentRuntime.Shared.HasController(rule.EnvType)) return false;
      }

      return true;
    }

    private System.Collections.IEnumerator EarlyAmbientSoundSync()
    {
      if (_startupAmbientSoundSyncRunning || _startupAmbientSoundSyncDone)
      {
        yield break;
      }

      _startupAmbientSoundSyncRunning = true;
      float timeout = StartupAmbientSoundSyncTimeout;

      try
      {
        while (!_startupAmbientSoundSyncDone && timeout > 0f)
        {
          try
          {
            if (TryApplyStartupAmbientSoundState(out _))
            {
              break;
            }
          }
          catch (Exception ex)
          {
            ChillEnvPlugin.Log?.LogWarning(
              $"[启动同步] 环境音预同步异常，回退到常规定时器: {ex.Message}");
            break;
          }

          yield return new WaitForSeconds(StartupAmbientSoundSyncInterval);
          timeout -= StartupAmbientSoundSyncInterval;
        }

        if (!_startupAmbientSoundSyncDone)
        {
          ChillEnvPlugin.Log?.LogDebug("[启动同步] 环境音数据服务未就绪，交给常规定时器兜底");
        }
      }
      finally
      {
        _startupAmbientSoundSyncRunning = false;
      }
    }

    internal static bool TryApplyStartupAmbientSoundState(out int changedCount)
    {
      changedCount = 0;

      var instance = _instance;
      if (instance == null || !GameEnvironmentRuntime.Shared.TryPrepare())
      {
        return false;
      }

      if (instance._startupAmbientSoundSyncDone)
      {
        return true;
      }

      changedCount = instance.ApplyStartupAmbientSoundState();
      instance._startupAmbientSoundSyncDone = true;
      instance._needsImmediateAutomationCheck = true;
      FlushOwnershipIfDirty();

      if (changedCount > 0)
      {
        ChillEnvPlugin.Log?.LogInfo($"[启动同步] 已预写 {changedCount} 项环境音状态");
      }

      return true;
    }

    internal static void RequestImmediateAutomationCheck()
    {
      if (_instance == null)
      {
        return;
      }

      _instance._needsImmediateAutomationCheck = true;
      _instance._nextImmediateAutomationCheckTime = 0f;
      _instance._checkTimer = 0f;
    }

    private int ApplyStartupAmbientSoundState()
    {
      int changedCount = 0;
      EnvironmentInputSnapshot inputs = ModEnvironmentContext.Capture();

      foreach (var rule in _rules)
      {
        if (rule.Category != SceneryRuleCategory.AmbientSound) continue;
        if (_ownership.IsUserOwned(rule.EnvType)) continue;

        try
        {
          bool conditionSatisfied = rule.ShouldBeActive(inputs);
          bool shouldBeActive = IsRuleEnabled(rule) && conditionSatisfied;
          bool wasManaged = _ownership.IsManaged(rule.EnvType);
          if (!GameEnvironmentRuntime.Shared.TryIsAmbientSoundActive(
              rule.EnvType,
              out var isCurrentlyActive)) continue;

          bool isOrphanActive = isCurrentlyActive && !conditionSatisfied;
          if (!wasManaged && !shouldBeActive && !isOrphanActive) continue;

          if (!GameEnvironmentRuntime.Shared.TrySetAmbientSoundState(
              rule.EnvType,
              shouldBeActive,
              out var changed))
          {
            continue;
          }

          if (changed) changedCount++;

          if (shouldBeActive)
          {
            MarkAutoManaged(rule.EnvType);
          }
          else
          {
            ReleaseAutoManaged(rule.EnvType);
          }
        }
        catch (Exception ex)
        {
          ChillEnvPlugin.Log?.LogWarning(
            $"[启动同步] 预写环境音状态失败: {rule.Name}: {ex.Message}");
        }
      }

      return changedCount;
    }

    /// <summary>
    /// 处理延迟验证：检查点击后的状态是否符合预期
    /// </summary>
    private void ProcessPendingActions()
    {
      List<EnvironmentType> completed = new List<EnvironmentType>();

      foreach (var kvp in _pendingActions)
      {
        if (Time.time >= kvp.Value.VerifyTime)
        {
          var env = kvp.Key;
          var action = kvp.Value;
          bool currentState = IsEnvActive(env);

          if (currentState == action.TargetState)
          {
            // 状态符合预期
            if (action.TargetState)
            {
              MarkAutoManaged(env);
              ChillEnvPlugin.Log?.LogInfo($"[自动托管] ✓ 已开启: {action.RuleName}");
            }
            else
            {
              ReleaseAutoManaged(env);
              ChillEnvPlugin.Log?.LogInfo($"[自动托管] ✓ 已关闭: {action.RuleName}");
            }
          }
          else
          {
            // 状态不符合预期（可能点击失败或用户已手动操作）
            ChillEnvPlugin.Log?.LogInfo(
              $"[自动托管] ✗ 状态验证失败: {action.RuleName} (期望={action.TargetState}, 实际={currentState})"
            );
          }

          completed.Add(env);
        }
      }

      foreach (var env in completed)
      {
        _pendingActions.Remove(env);
      }
    }

    private void RunAutomationLogic()
    {
      // 剧情保护：剧情中跳过所有彩蛋自动切换
      if (ChillEnvPlugin.IsInCutscene()) return;

      // Playback owns only weather/time presentation. Its accelerated calendar must
      // not reroll or persist the user's daily events or retime ambient automation.
      if (WeatherDebugControl.Playback != null) return;

      // One reading of time and weather for the whole pass. Rules must not
      // disagree about "now" just because the clock moved between them.
      EnvironmentInputSnapshot inputs = ModEnvironmentContext.Capture();

      CheckDailyReset(inputs);
      CleanupDisabledAutoMods(inputs);

      if (IsEnvActive(Env_DeepSea))
      {
        CleanupAllAutoMods();
        return;
      }

      // Step 1: 检查已托管的环境，关闭不满足条件的
      List<EnvironmentType> toCheck = _ownership.ManagedSnapshot();
      foreach (var envType in toCheck)
      {
        // 用户手动操作过的不管
        if (_ownership.IsUserOwned(envType))
        {
          ReleaseAutoManaged(envType);
          continue;
        }

        // 如果正在等待验证，跳过
        if (_pendingActions.ContainsKey(envType)) continue;

        var rule = _rules.Find(r => r.EnvType == envType);
        if (rule == null)
        {
          ReleaseAutoManaged(envType);
          continue;
        }

        if (!IsRuleEnabled(rule) || !rule.ShouldBeActive(inputs))
        {
          DisableMod(rule.Name, envType);
        }
      }

      // Step 2: 检查未托管的环境，开启满足条件的
      foreach (var rule in _rules)
      {
        if (!IsRuleEnabled(rule)) continue;

        // 跳过：用户操作过的、已托管的、等待验证的
        if (_ownership.IsUserOwned(rule.EnvType)) continue;
        if (_ownership.IsManaged(rule.EnvType)) continue;
        if (_pendingActions.ContainsKey(rule.EnvType)) continue;

        // 已经开着了就不管
        if (IsEnvActive(rule.EnvType)) continue;

        if (rule.ShouldBeActive(inputs) ||
            (rule.TryTrigger != null && rule.TryTrigger(inputs)))
        {
          EnableMod(rule.Name, rule.EnvType);
        }
      }

      // Step 3: 孤儿环境检测 — 关闭活跃但不应播放的未托管环境
      // 处理启动竞态：环境控制器未就绪时 DisableMod 会释放托管，
      // 游戏加载存档后环境变为活跃但已脱离自动托管，此步骤兜底关闭。
      foreach (var rule in _rules)
      {
        if (_ownership.IsUserOwned(rule.EnvType)) continue;
        if (_ownership.IsManaged(rule.EnvType)) continue;
        if (_pendingActions.ContainsKey(rule.EnvType)) continue;

        if (!IsEnvActive(rule.EnvType)) continue;
        if (rule.ShouldBeActive(inputs)) continue;

        if (!IsRuleEnabled(rule))
        {
          DisableMod(rule.Name, rule.EnvType);
          continue;
        }

        ChillEnvPlugin.Log?.LogWarning($"[孤儿检测] {rule.Name} 不应播放但处于激活状态，强制关闭");
        DisableMod(rule.Name, rule.EnvType);
      }
    }

    private bool HasAnyAutomationEnabled()
    {
      return ChillEnvPlugin.Cfg_EnableEasterEggs.Value || ChillEnvPlugin.Cfg_EnableAmbientSounds.Value;
    }

    private bool IsRuleEnabled(SceneryRule rule)
    {
      if (rule.Category == SceneryRuleCategory.AmbientSound)
        return ChillEnvPlugin.Cfg_EnableAmbientSounds.Value;

      return ChillEnvPlugin.Cfg_EnableEasterEggs.Value;
    }

    private void CleanupDisabledAutoMods(EnvironmentInputSnapshot inputs)
    {
      List<EnvironmentType> toCheck = _ownership.ManagedSnapshot();
      foreach (var envType in toCheck)
      {
        if (_pendingActions.ContainsKey(envType)) continue;

        var rule = _rules.Find(r => r.EnvType == envType);
        if (rule == null)
        {
          ReleaseAutoManaged(envType);
          continue;
        }

        if (SceneryAutomationCleanupPolicy.ShouldDisableManagedMod(
          IsRuleEnabled(rule),
          rule.ShouldBeActive(inputs)))
        {
          DisableMod(rule.Name, envType);
        }
      }
    }

    private void EnableMod(string ruleName, EnvironmentType env)
    {
      IsSystemOperation = true;
      try
      {
        // 1. 冷却检查
        if (_lastClickTime.TryGetValue(env, out float lastTime))
        {
          if (Time.time - lastTime < ClickCooldown)
          {
            return; // 冷却中
          }
        }

        // 2. 再次确认当前状态
        if (IsEnvActive(env))
        {
          // 可能是玩家在本轮检查后手动开启，不主动接管。
          ChillEnvPlugin.Log?.LogInfo($"[自动托管] 已由外部开启，跳过托管: {ruleName}");
          return;
        }

        // 3. 执行点击
        if (GameEnvironmentRuntime.Shared.HasController(env))
        {
          var rule = _rules.Find(r => r.EnvType == env);
          if (rule != null &&
              rule.Category == SceneryRuleCategory.AmbientSound &&
              GameEnvironmentRuntime.Shared.TrySetAmbientSoundState(
                env,
                true,
                out _))
          {
            _lastClickTime[env] = Time.time;
            _pendingActions[env] = new PendingAction
            {
              TargetState = true,
              VerifyTime = Time.time + VerifyDelay,
              RuleName = ruleName
            };
            return;
          }

          ChillEnvPlugin.Log?.LogInfo($"[自动托管] → 点击开启: {ruleName}");
          GameEnvironmentRuntime.Shared.TrySimulateMainIconClick(env);
          _lastClickTime[env] = Time.time;

          // 4. 登记延迟验证任务
          _pendingActions[env] = new PendingAction
          {
            TargetState = true,
            VerifyTime = Time.time + VerifyDelay,
            RuleName = ruleName
          };
        }
      }
      finally
      {
        IsSystemOperation = false;
      }
    }

    private void DisableMod(string ruleName, EnvironmentType env)
    {
      IsSystemOperation = true;
      try
      {
        // 1. 冷却检查
        if (_lastClickTime.TryGetValue(env, out float lastTime))
        {
          if (Time.time - lastTime < ClickCooldown)
          {
            return;
          }
        }

        if (!GameEnvironmentRuntime.Shared.HasController(env))
        {
          ChillEnvPlugin.Log?.LogWarning($"[自动托管] 控制器未就绪，强制释放: {ruleName}");
          ReleaseAutoManaged(env);
          return;
        }

        // 3. 再次确认当前状态
        if (!IsEnvActive(env))
        {
          ReleaseAutoManaged(env);
          ChillEnvPlugin.Log?.LogInfo($"[自动托管] ↻ 已是关闭状态: {ruleName}");
          return;
        }

        // 4. 执行点击
        {
          var rule = _rules.Find(r => r.EnvType == env);
          if (rule != null &&
              rule.Category == SceneryRuleCategory.AmbientSound &&
              GameEnvironmentRuntime.Shared.TrySetAmbientSoundState(
                env,
                false,
                out _))
          {
            _lastClickTime[env] = Time.time;
            _pendingActions[env] = new PendingAction
            {
              TargetState = false,
              VerifyTime = Time.time + VerifyDelay,
              RuleName = ruleName
            };
            return;
          }

          ChillEnvPlugin.Log?.LogInfo($"[自动托管] → 点击关闭: {ruleName}");
          GameEnvironmentRuntime.Shared.TrySimulateMainIconClick(env);
          _lastClickTime[env] = Time.time;

          // 5. 登记延迟验证任务
          _pendingActions[env] = new PendingAction
          {
            TargetState = false,
            VerifyTime = Time.time + VerifyDelay,
            RuleName = ruleName
          };
        }
      }
      finally
      {
        IsSystemOperation = false;
      }
    }

    private void CleanupAllAutoMods()
    {
      List<EnvironmentType> toClean = _ownership.ManagedSnapshot();
      foreach (var env in toClean)
      {
        var rule = _rules.Find(r => r.EnvType == env);
        if (rule != null)
        {
          DisableMod(rule.Name, env);
        }
      }
    }

    /// <summary>
    /// 每日重置概率触发标志
    /// </summary>
    private void CheckDailyReset(EnvironmentInputSnapshot inputs)
    {
      if (_dailyTriggers.EnsureDate(inputs.Now.Date))
      {
        PersistTriggerState();
        ChillEnvPlugin.Log?.LogDebug("[每日重置] 概率触发标志已重置");
      }
    }

    /// <summary>
    /// 状态检测：读取 VolumeSlider 或 WindowView 的真实值
    /// </summary>
    private bool IsEnvActive(EnvironmentType env)
    {
      return GameEnvironmentRuntime.Shared.TryIsActive(env, out var isActive) && isActive;
    }

  }
}
