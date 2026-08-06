using System;
using System.Collections.Generic;
using Bulbul;

namespace ChillWithYou.EnvSync.Core
{
    public partial class SceneryAutomationSystem
    {
        private static bool _ownershipDirty = false;

        private void RestoreManagedOwnership()
        {
            if (ChillEnvPlugin.Cfg_AutoManagedEnvironments == null)
            {
                return;
            }

            _ownership.ClearManaged();

            int skippedUserOwned = 0;
            foreach (string name in AutoManagedStateCodec.Parse(
                ChillEnvPlugin.Cfg_AutoManagedEnvironments.Value))
            {
                EnvironmentType environment;
                if (!Enum.TryParse(name, out environment) ||
                    !_rules.Exists(rule => rule.EnvType == environment))
                {
                    continue;
                }

                if (!_ownership.TryMarkManaged(environment) &&
                    _ownership.IsUserOwned(environment))
                {
                    skippedUserOwned++;
                }
            }

            if (skippedUserOwned > 0)
            {
                ChillEnvPlugin.Log?.LogInfo(
                    $"[自动托管] {skippedUserOwned} 项已被用户接管，未恢复托管");
            }

            PersistManagedOwnership();
            if (_ownership.ManagedCount > 0)
            {
                ChillEnvPlugin.Log?.LogInfo(
                    $"[自动托管] 已恢复 {_ownership.ManagedCount} 项跨会话托管状态");
            }
        }

        private static void MarkAutoManaged(EnvironmentType environment)
        {
            if (_ownership.TryMarkManaged(environment))
            {
                _ownershipDirty = true;
            }
        }

        private static void ReleaseAutoManaged(EnvironmentType environment)
        {
            if (_ownership.ReleaseManaged(environment))
            {
                _ownershipDirty = true;
            }
        }

        private static void PersistManagedOwnership()
        {
            if (ChillEnvPlugin.Cfg_AutoManagedEnvironments == null)
            {
                return;
            }

            var names = new List<string>();
            foreach (EnvironmentType environment in _ownership.ManagedSnapshot())
            {
                names.Add(environment.ToString());
            }

            ChillEnvPlugin.Cfg_AutoManagedEnvironments.Value =
                AutoManagedStateCodec.Serialize(names);
        }

        private static void FlushOwnershipIfDirty()
        {
            if (!_ownershipDirty)
            {
                return;
            }

            PersistManagedOwnership();
            _ownershipDirty = false;
        }
    }
}
