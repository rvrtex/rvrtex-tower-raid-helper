using System.Collections;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(Tower_Helper.TowerHelperMod), "Matt's Tower/Raid Helper", "0.3.0", "Matthew")]
[assembly: MelonGame("Crate Entertainment", "Farthest Frontier")]

namespace Tower_Helper
{
    public class TowerHelperMod : MelonMod
    {
        private MelonPreferences_Category config;
        private MelonPreferences_Entry<KeyCode> enableTowersKey;
        private MelonPreferences_Entry<KeyCode> disableTowersKey;
        private MelonPreferences_Entry<KeyCode> raidWindowKey;
        private MelonPreferences_Entry<KeyCode> endlessRaidKey;
        private MelonPreferences_Entry<float> raidIntervalSecondsEntry;

        private bool showRaidWindow = false;
        private Rect raidWindowRect = new Rect(20, 220, 380, 480);
        private Vector2 raidWindowScroll = Vector2.zero;

        private int selectedDifficultyIndex = 2;
        private int selectedRaidIndex = 4;
        private int selectedGroupIndex = 0;
        private string groupAmountText = "20";
        private string endlessIntervalText = "30";

        private bool endlessRaidMode = false;
        private float nextRaidTime = 0f;
        private int endlessDifficultyIndex = 1; // 0 is RaiderDifficulty_None
        private int endlessRaidIndex = 0;

        public override void OnInitializeMelon()
        {
            config = MelonPreferences.CreateCategory("MatthewTowerHelper");

            enableTowersKey = config.CreateEntry(
                "EnableTowersKey",
                KeyCode.F11,
                "Key used to enable all guard towers");

            disableTowersKey = config.CreateEntry(
                "DisableTowersKey",
                KeyCode.F10,
                "Key used to disable all guard towers");

            raidWindowKey = config.CreateEntry(
                "RaidWindowKey",
                KeyCode.Home,
                "Key used to show/hide the raid spawner window");

            endlessRaidKey = config.CreateEntry(
                "EndlessRaidToggleKey",
                KeyCode.F8,
                "Key used to toggle endless escalating raids");

            raidIntervalSecondsEntry = config.CreateEntry(
                "RaidIntervalSeconds",
                30f,
                "Seconds between endless raid spawns");

            endlessIntervalText = Mathf.Clamp(raidIntervalSecondsEntry.Value, 10f, 3600f).ToString("0");

            config.SaveToFile();

            MelonLogger.Msg("Matthew Tower Helper loaded.");
            MelonLogger.Msg($"Enable towers key: {enableTowersKey.Value}");
            MelonLogger.Msg($"Disable towers key: {disableTowersKey.Value}");
            MelonLogger.Msg($"Raid window key: {raidWindowKey.Value}");
            MelonLogger.Msg($"Endless raid key: {endlessRaidKey.Value}");
            MelonLogger.Msg($"Endless raid interval: {raidIntervalSecondsEntry.Value} seconds");
        }

        public override void OnUpdate()
        {
            if (Input.GetKeyDown(enableTowersKey.Value))
            {
                SetTowersEnabled(true);
            }

            if (Input.GetKeyDown(disableTowersKey.Value))
            {
                SetTowersEnabled(false);
            }

            if (Input.GetKeyDown(raidWindowKey.Value))
            {
                showRaidWindow = !showRaidWindow;
            }

            if (Input.GetKeyDown(endlessRaidKey.Value))
            {
                ToggleEndlessRaidMode();
            }

            ProcessEndlessRaidMode();
        }

        public override void OnGUI()
        {
            if (showRaidWindow)
            {
                GUI.depth = 0;

                raidWindowRect = GUI.Window(
                    998877,
                    raidWindowRect,
                    DrawRaidWindow,
                    "Matthew Tower Helper"
                );

                Input.ResetInputAxes();
            }
        }

        private GameManager GetGameManager()
        {
            var gameManagerObject = GameObject.Find("GameManager");
            return gameManagerObject == null ? null : gameManagerObject.GetComponent<GameManager>();
        }

        private CombatManager GetCombatManager()
        {
            var gameManager = GetGameManager();
            return gameManager == null ? null : gameManager.combatManager;
        }

        private void SetTowersEnabled(bool enabled)
        {
            var towers = Object.FindObjectsOfType<GuardTower>();

            int changed = 0;
            int skipped = 0;

            foreach (var tower in towers)
            {
                try
                {
                    if (!ShouldToggleTower(tower))
                    {
                        skipped++;
                        continue;
                    }

                    tower.SetWorkEnabled(enabled, true);
                    changed++;
                }
                catch (System.Exception ex)
                {
                    skipped++;
                    MelonLogger.Error($"Failed to toggle tower at {tower.transform.position}: {ex.Message}");
                }
            }

            MelonLogger.Msg($"Tower toggle complete. Changed: {changed}, Skipped: {skipped}");
        }

        private bool ShouldToggleTower(GuardTower tower)
        {
            if (tower == null)
            {
                return false;
            }

            var gameObject = tower.gameObject;

            if (gameObject == null || !gameObject.activeInHierarchy)
            {
                return false;
            }

            if (gameObject.name != "GuardTower")
            {
                return false;
            }

            return gameObject.GetComponent<BuildingWidgetController>() != null &&
                   gameObject.GetComponent<WorkerComponent>() != null &&
                   gameObject.GetComponent<DefensiveBuildingWidgetBlackboard>() != null;
        }

        private void KillAllRaiders()
        {
            try
            {
                var combatManager = GetCombatManager();

                if (combatManager == null)
                {
                    MelonLogger.Error("CombatManager not found.");
                    return;
                }

                var field = combatManager.GetType().GetField(
                    "raiders",
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Instance);

                if (field == null)
                {
                    MelonLogger.Error("Could not find raiders field.");
                    return;
                }

                var raidersEnumerable = field.GetValue(combatManager) as IEnumerable;

                if (raidersEnumerable == null)
                {
                    MelonLogger.Error("Raiders collection is null.");
                    return;
                }

                var raidersToKill = new List<object>();

                foreach (var raider in raidersEnumerable)
                {
                    if (raider != null)
                    {
                        raidersToKill.Add(raider);
                    }
                }

                int removed = 0;

                foreach (var raider in raidersToKill)
                {
                    if (TryCallSimpleDeathMethod(raider))
                    {
                        removed++;
                        continue;
                    }

                    var component = raider as Component;

                    if (component != null && component.gameObject != null)
                    {
                        GameObject.Destroy(component.gameObject);
                        removed++;
                    }
                }

                MelonLogger.Msg($"KillAllRaiders complete. Raiders removed or killed: {removed}");
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error(ex.ToString());
            }
        }

        private bool TryCallSimpleDeathMethod(object raider)
        {
            if (raider == null)
            {
                return false;
            }

            var methods = raider.GetType().GetMethods(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

            foreach (var method in methods)
            {
                string name = method.Name.ToLowerInvariant();

                // Avoid Unity lifecycle methods like OnDestroy. Only call obvious no-argument gameplay death methods.
                bool looksLikeDeathMethod =
                    name == "die" ||
                    name == "kill" ||
                    name == "killself" ||
                    name == "ondeath";

                if (!looksLikeDeathMethod)
                {
                    continue;
                }

                var parameters = method.GetParameters();

                if (parameters.Length != 0)
                {
                    continue;
                }

                try
                {
                    method.Invoke(raider, null);
                    MelonLogger.Msg($"Killed raider using {method.Name}");
                    return true;
                }
                catch (System.Exception ex)
                {
                    MelonLogger.Warning($"Failed invoking {method.Name}: {ex.Message}");
                }
            }

            return false;
        }

        private void ToggleEndlessRaidMode()
        {
            endlessRaidMode = !endlessRaidMode;

            if (endlessRaidMode)
            {
                endlessDifficultyIndex = Mathf.Max(1, endlessDifficultyIndex);
                endlessRaidIndex = Mathf.Max(0, endlessRaidIndex);
                nextRaidTime = Time.time + 5f;
                MelonLogger.Msg("Endless raid mode ENABLED. First raid in 5 seconds.");
            }
            else
            {
                MelonLogger.Msg("Endless raid mode DISABLED.");
            }
        }

        private void ProcessEndlessRaidMode()
        {
            if (!endlessRaidMode || Time.time < nextRaidTime)
            {
                return;
            }

            SpawnConfiguredRaid(endlessDifficultyIndex, endlessRaidIndex);
            AdvanceEndlessRaidSelection();

            float interval = Mathf.Clamp(raidIntervalSecondsEntry.Value, 10f, 3600f);
            nextRaidTime = Time.time + interval;
        }

        private void AdvanceEndlessRaidSelection()
        {
            endlessRaidIndex++;

            int raidCount = GetRaidCount(endlessDifficultyIndex);

            if (raidCount <= 0 || endlessRaidIndex >= raidCount)
            {
                endlessRaidIndex = 0;
                endlessDifficultyIndex++;
            }

            var combatManager = GetCombatManager();

            if (combatManager == null || combatManager.raiderDifficulties == null || combatManager.raiderDifficulties.Count == 0)
            {
                endlessDifficultyIndex = 1;
                return;
            }

            int maxDifficultyIndex = combatManager.raiderDifficulties.Count - 1;

            if (endlessDifficultyIndex > maxDifficultyIndex)
            {
                endlessDifficultyIndex = maxDifficultyIndex;
            }

            endlessDifficultyIndex = Mathf.Max(1, endlessDifficultyIndex);
        }

        private void SpawnConfiguredRaid(int difficultyIndex, int raidIndex)
        {
            try
            {
                var combatManager = GetCombatManager();

                if (combatManager == null)
                {
                    MelonLogger.Error("CombatManager not found.");
                    return;
                }

                var difficulties = combatManager.raiderDifficulties;

                if (difficulties == null || difficulties.Count == 0)
                {
                    MelonLogger.Error("No raid difficulties found.");
                    return;
                }

                difficultyIndex = Mathf.Clamp(difficultyIndex, 0, difficulties.Count - 1);

                var raids = difficulties[difficultyIndex].raidGroups;

                if (raids == null || raids.Count == 0)
                {
                    MelonLogger.Error("No raid groups found for selected difficulty.");
                    return;
                }

                raidIndex = Mathf.Clamp(raidIndex, 0, raids.Count - 1);

                var raidData = raids[raidIndex].raidIncursionSetupData;

                MelonLogger.Msg($"Spawning raid: {difficulties[difficultyIndex].name} / {raidData.name}");
                combatManager.DebugSpawnRaid(raidData);
            }
            catch (System.Exception ex)
            {
                MelonLogger.Error(ex.ToString());
            }
        }

        private int GetRaidCount(int difficultyIndex)
        {
            try
            {
                var combatManager = GetCombatManager();

                if (combatManager == null || combatManager.raiderDifficulties == null || combatManager.raiderDifficulties.Count == 0)
                {
                    return 0;
                }

                difficultyIndex = Mathf.Clamp(difficultyIndex, 0, combatManager.raiderDifficulties.Count - 1);
                var raids = combatManager.raiderDifficulties[difficultyIndex].raidGroups;

                return raids == null ? 0 : raids.Count;
            }
            catch
            {
                return 0;
            }
        }

        private void DrawRaidWindow(int windowId)
        {
            try
            {
                GUIStyle boldLabel = new GUIStyle(GUI.skin.label);
                boldLabel.fontStyle = FontStyle.Bold;

                var combatManager = GetCombatManager();

                if (combatManager == null)
                {
                    GUILayout.Label("CombatManager not found.");
                    GUI.DragWindow();
                    return;
                }

                var difficulties = combatManager.raiderDifficulties;

                if (difficulties == null || difficulties.Count == 0)
                {
                    GUILayout.Label("No raid difficulties found.");
                    GUI.DragWindow();
                    return;
                }

                GUILayout.Label("HOME toggles this window.");

                // =========================
                // TOWERS
                // =========================

                GUILayout.Space(6);

                GUILayout.BeginHorizontal();

                if (GUILayout.Button("Enable Towers"))
                {
                    SetTowersEnabled(true);
                }

                if (GUILayout.Button("Disable Towers"))
                {
                    SetTowersEnabled(false);
                }

                GUILayout.EndHorizontal();

                // =========================
                // DIFFICULTY
                // =========================

                GUILayout.Space(10);

                GUILayout.BeginHorizontal();

                GUILayout.Label("Difficulty:", boldLabel, GUILayout.Width(85));

                if (GUILayout.Button("<", GUILayout.Width(32)))
                {
                    selectedDifficultyIndex = Mathf.Max(0, selectedDifficultyIndex - 1);
                    selectedRaidIndex = 0;
                    selectedGroupIndex = 0;
                }

                selectedDifficultyIndex = Mathf.Clamp(selectedDifficultyIndex, 0, difficulties.Count - 1);

                GUILayout.Label(
                    CleanName(difficulties[selectedDifficultyIndex].name),
                    GUILayout.Width(180));

                if (GUILayout.Button(">", GUILayout.Width(32)))
                {
                    selectedDifficultyIndex = Mathf.Min(
                        difficulties.Count - 1,
                        selectedDifficultyIndex + 1);

                    selectedRaidIndex = 0;
                    selectedGroupIndex = 0;
                }

                GUILayout.EndHorizontal();

                var raids = difficulties[selectedDifficultyIndex].raidGroups;

                if (raids == null || raids.Count == 0)
                {
                    GUILayout.Label("No raids for this difficulty.");
                    GUI.DragWindow();
                    return;
                }

                // =========================
                // RAID INCURSION
                // =========================

                GUILayout.BeginHorizontal();

                GUILayout.Label("Raid:", boldLabel, GUILayout.Width(85));

                if (GUILayout.Button("<", GUILayout.Width(32)))
                {
                    selectedRaidIndex = Mathf.Max(0, selectedRaidIndex - 1);
                    selectedGroupIndex = 0;
                }

                selectedRaidIndex = Mathf.Clamp(
                    selectedRaidIndex,
                    0,
                    raids.Count - 1);

                GUILayout.Label(
                    CleanName(raids[selectedRaidIndex].raidIncursionSetupData.name),
                    GUILayout.Width(180));

                if (GUILayout.Button(">", GUILayout.Width(32)))
                {
                    selectedRaidIndex = Mathf.Min(
                        raids.Count - 1,
                        selectedRaidIndex + 1);

                    selectedGroupIndex = 0;
                }

                GUILayout.EndHorizontal();

                GUILayout.Space(6);

                if (GUILayout.Button("Spawn Selected Raid"))
                {
                    SpawnConfiguredRaid(
                        selectedDifficultyIndex,
                        selectedRaidIndex);
                }

                // =========================
                // RAID GROUPS
                // =========================

                var groupEntries =
                    raids[selectedRaidIndex]
                    .raidIncursionSetupData
                    .groupEntries;

                GUILayout.Space(12);

                GUILayout.BeginHorizontal();

                GUILayout.Label("Group:", boldLabel, GUILayout.Width(85));

                if (GUILayout.Button("<", GUILayout.Width(32)))
                {
                    selectedGroupIndex = Mathf.Max(0, selectedGroupIndex - 1);
                }

                selectedGroupIndex = Mathf.Clamp(
                    selectedGroupIndex,
                    0,
                    groupEntries.Count - 1);

                GUILayout.Label(
                    CleanName(groupEntries[selectedGroupIndex].group.name),
                    GUILayout.Width(180));

                if (GUILayout.Button(">", GUILayout.Width(32)))
                {
                    selectedGroupIndex = Mathf.Min(
                        groupEntries.Count - 1,
                        selectedGroupIndex + 1);
                }

                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();

                GUILayout.Label("Amount:", boldLabel, GUILayout.Width(85));

                groupAmountText = GUILayout.TextField(
                    groupAmountText,
                    GUILayout.Width(80));

                GUILayout.EndHorizontal();

                GUILayout.Space(6);

                if (GUILayout.Button("Spawn Selected Group"))
                {
                    SpawnSelectedGroup(combatManager, groupEntries);
                }

                // =========================
                // ENDLESS MODE
                // =========================

                GUILayout.Space(14);

                GUILayout.BeginHorizontal();

                GUILayout.Label("Endless Raid:", boldLabel, GUILayout.Width(85));

                GUILayout.Label(
                    endlessRaidMode ? "Status ON" : "Status OFF");

                GUILayout.EndHorizontal();

                GUILayout.Label(
                    $"Next Endless: {CleanName(GetEndlessRaidName())}");

                GUILayout.Label(
                    $"Next Raid In: {Mathf.Max(0, nextRaidTime - Time.time):0}s");

                GUILayout.BeginHorizontal();

                GUILayout.Label("Interval:", boldLabel, GUILayout.Width(85));

                string intervalString =
                    raidIntervalSecondsEntry.Value.ToString("0");

                intervalString = GUILayout.TextField(
                    intervalString,
                    GUILayout.Width(80));

                if (GUILayout.Button("Save", GUILayout.Width(60)))
                {
                    float parsedInterval;

                    if (float.TryParse(intervalString, out parsedInterval))
                    {
                        parsedInterval = Mathf.Clamp(parsedInterval, 10f, 3600f);

                        raidIntervalSecondsEntry.Value = parsedInterval;

                        config.SaveToFile();

                        MelonLogger.Msg(
                            $"Endless raid interval saved: {parsedInterval} seconds");
                    }
                }

                GUILayout.EndHorizontal();

                GUILayout.Space(6);

                if (GUILayout.Button(
                    endlessRaidMode
                        ? "Stop Endless Raids"
                        : "Start Endless Raids"))
                {
                    ToggleEndlessRaidMode();
                }

                // =========================
                // RAIDER CONTROLS
                // =========================

                GUILayout.Space(14);

                if (GUILayout.Button("Kill All Active Raiders"))
                {
                    KillAllRaiders();
                }
            }
            catch (System.Exception ex)
            {
                GUILayout.Label("Error. Check MelonLoader log.");
                MelonLogger.Error(ex.ToString());
            }

            GUI.DragWindow();
        }

        private void DrawDifficultySelector(System.Collections.Generic.List<RaiderDifficultyEntry> difficulties)
        {
            GUILayout.Label("Difficulty");
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("<", GUILayout.Width(32)))
            {
                selectedDifficultyIndex = Mathf.Max(0, selectedDifficultyIndex - 1);
                selectedRaidIndex = 0;
                selectedGroupIndex = 0;
            }

            selectedDifficultyIndex = Mathf.Clamp(selectedDifficultyIndex, 0, difficulties.Count - 1);
            GUILayout.Label(CleanName(difficulties[selectedDifficultyIndex].name));

            if (GUILayout.Button(">", GUILayout.Width(32)))
            {
                selectedDifficultyIndex = Mathf.Min(difficulties.Count - 1, selectedDifficultyIndex + 1);
                selectedRaidIndex = 0;
                selectedGroupIndex = 0;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawRaidSelector(System.Collections.Generic.List<RaidIncursion> raids)
        {
            GUILayout.Label("Raid Incursion");
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("<", GUILayout.Width(32)))
            {
                selectedRaidIndex = Mathf.Max(0, selectedRaidIndex - 1);
                selectedGroupIndex = 0;
            }

            selectedRaidIndex = Mathf.Clamp(selectedRaidIndex, 0, raids.Count - 1);
            GUILayout.Label(CleanName(raids[selectedRaidIndex].raidIncursionSetupData.name));

            if (GUILayout.Button(">", GUILayout.Width(32)))
            {
                selectedRaidIndex = Mathf.Min(raids.Count - 1, selectedRaidIndex + 1);
                selectedGroupIndex = 0;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawGroupSelector(System.Collections.Generic.List<RaidGroupEntry> groupEntries)
        {
            GUILayout.Label("Specific Raid Group");
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("<", GUILayout.Width(32)))
            {
                selectedGroupIndex = Mathf.Max(0, selectedGroupIndex - 1);
            }

            selectedGroupIndex = Mathf.Clamp(selectedGroupIndex, 0, groupEntries.Count - 1);
            GUILayout.Label(CleanName(groupEntries[selectedGroupIndex].group.name));

            if (GUILayout.Button(">", GUILayout.Width(32)))
            {
                selectedGroupIndex = Mathf.Min(groupEntries.Count - 1, selectedGroupIndex + 1);
            }

            GUILayout.EndHorizontal();
        }

        private void SpawnSelectedGroup(CombatManager combatManager, System.Collections.Generic.List<RaidGroupEntry> groupEntries)
        {
            if (!int.TryParse(groupAmountText, out int amount))
            {
                MelonLogger.Warning($"Invalid amount: {groupAmountText}");
                return;
            }

            amount = Mathf.Clamp(amount, 1, 500);
            selectedGroupIndex = Mathf.Clamp(selectedGroupIndex, 0, groupEntries.Count - 1);

            var group = groupEntries[selectedGroupIndex].group;

            MelonLogger.Msg($"Spawning group: {group.name}, Amount: {amount}");
            combatManager.DebugSpawnRaidGroup(group, amount);
        }

        private void SaveEndlessIntervalFromText()
        {
            if (!float.TryParse(endlessIntervalText, out float interval))
            {
                MelonLogger.Warning($"Invalid endless interval: {endlessIntervalText}");
                endlessIntervalText = Mathf.Clamp(raidIntervalSecondsEntry.Value, 10f, 3600f).ToString("0");
                return;
            }

            interval = Mathf.Clamp(interval, 10f, 3600f);
            raidIntervalSecondsEntry.Value = interval;
            endlessIntervalText = interval.ToString("0");
            config.SaveToFile();

            MelonLogger.Msg($"Endless raid interval saved: {interval:0} seconds");
        }

        private string GetEndlessRaidName()
        {
            try
            {
                var combatManager = GetCombatManager();

                if (combatManager == null || combatManager.raiderDifficulties == null || combatManager.raiderDifficulties.Count == 0)
                {
                    return "Unavailable";
                }

                endlessDifficultyIndex = Mathf.Clamp(endlessDifficultyIndex, 1, combatManager.raiderDifficulties.Count - 1);
                var raids = combatManager.raiderDifficulties[endlessDifficultyIndex].raidGroups;

                if (raids == null || raids.Count == 0)
                {
                    return "Unavailable";
                }

                endlessRaidIndex = Mathf.Clamp(endlessRaidIndex, 0, raids.Count - 1);

                return combatManager.raiderDifficulties[endlessDifficultyIndex].name + " / " + raids[endlessRaidIndex].raidIncursionSetupData.name;
            }
            catch
            {
                return "Unavailable";
            }
        }

        private string CleanName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName))
            {
                return string.Empty;
            }

            return rawName
                .Replace("RaiderDifficulty_", "")
                .Replace("RaidIncursion_", "")
                .Replace("RaidGroup_", "")
                .Replace("_", " ");
        }
    }
}
