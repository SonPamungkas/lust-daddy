using System;
 using System.Collections.Generic;
 using System.IO;
 using System.Linq;
 using System.Reflection;
 using System.Text.RegularExpressions;
 using BepInEx;
 using BepInEx.Configuration;
 using HarmonyLib;
 using UnityEngine;
 using Object = UnityEngine.Object;


namespace LustDaddy
{
 [BepInPlugin("com.lustdaddy", "LUST-DADDY", "0.6.0")]
     public class LustDaddyPlugin : BaseUnityPlugin
     {
         public static ConfigEntry<KeyCode> ToggleKey;
         public static ConfigEntry<float> NuclearYieldThreshold;
         public static ConfigEntry<string> NuclearExplosionPrefab;
 
         private void Awake()
         {
             ToggleKey = Config.Bind("UI", "Toggle Window Key", KeyCode.F9, "Hotkey to show/hide the LUST-DADDY window");
             NuclearYieldThreshold = Config.Bind("Nuclear", "Warning Threshold Yield", 1500f, "Yield threshold to automate nuclear explosion prefab swapping.");
             NuclearExplosionPrefab = Config.Bind("Nuclear", "Nuclear Prefab Name", "NukeExplosion", "Name of the prefab to swap to when yield exceeds the threshold.");
             new Harmony("com.lustdaddy").PatchAll();
             gameObject.AddComponent<LustDaddyUI>();
             Logger.LogInfo("LUST-DADDY loaded successfully.");
         }
     }
 
     // ── Serializable config classes ──────────────────────────────────────────
     [System.Serializable]
     public class TurretSwapEntry
     {
         public string slotName;
         public string replacement;
     }
 
     [System.Serializable]
     public class FieldModEntry
     {
         public string componentType;
         public string fieldName;
         public string valueString;
     }

     [System.Serializable]
     public class UnitModConfig
     {
         public string unitId;
         public List<TurretSwapEntry> turretSwaps = new List<TurretSwapEntry>();
         public List<FieldModEntry> fieldMods = new List<FieldModEntry>();

         public string ToJson()
         {
             var sb = new System.Text.StringBuilder();
             sb.AppendLine("{");
             sb.AppendLine($"  \"unitId\": \"{Esc(unitId)}\",");
             sb.AppendLine("  \"turretSwaps\": [");
             for (int i = 0; i < turretSwaps.Count; i++)
             {
                 var e = turretSwaps[i];
                 sb.Append($"    {{ \"slotName\": \"{Esc(e.slotName)}\", \"replacement\": \"{Esc(e.replacement)}\" }}");
                 sb.AppendLine(i < turretSwaps.Count - 1 ? "," : "");
             }
             sb.AppendLine("  ],");
             sb.AppendLine("  \"fieldMods\": [");
             for (int i = 0; i < fieldMods.Count; i++)
             {
                 var f = fieldMods[i];
                 sb.Append($"    {{ \"componentType\": \"{Esc(f.componentType)}\", \"fieldName\": \"{Esc(f.fieldName)}\", \"valueString\": \"{Esc(f.valueString)}\" }}");
                 sb.AppendLine(i < fieldMods.Count - 1 ? "," : "");
             }
             sb.AppendLine("  ]");
             sb.Append("}");
             return sb.ToString();
         }

         static string Esc(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

         public static UnitModConfig FromJson(string json)
         {
             if (string.IsNullOrEmpty(json)) return null;
             var cfg = new UnitModConfig();
             cfg.unitId = ExtractString(json, "unitId");
             
             // Extract turretSwaps
             int arrayStart = json.IndexOf("\"turretSwaps\"");
             if (arrayStart >= 0)
             {
                 int braceDepth = 0; bool inArray = false;
                 var current = new System.Text.StringBuilder();
                 for (int i = arrayStart; i < json.Length; i++)
                 {
                     char c = json[i];
                     if (c == '[') { inArray = true; continue; }
                     if (!inArray) continue;
                     if (c == '{') { braceDepth++; current.Clear(); current.Append(c); }
                     else if (c == '}' && braceDepth > 0)
                     {
                         current.Append(c); braceDepth--;
                         if (braceDepth == 0)
                         {
                             string obj = current.ToString();
                             string slot = ExtractString(obj, "slotName");
                             string repl = ExtractString(obj, "replacement");
                             if (!string.IsNullOrEmpty(slot) && !string.IsNullOrEmpty(repl))
                                 cfg.turretSwaps.Add(new TurretSwapEntry { slotName = slot, replacement = repl });
                         }
                     }
                     else if (braceDepth > 0) current.Append(c);
                     if (c == ']' && braceDepth == 0) break;
                 }
             }
             
             // Extract fieldMods
             int fieldModsStart = json.IndexOf("\"fieldMods\"");
             if (fieldModsStart >= 0)
             {
                 int braceDepth = 0; bool inArray = false;
                 var current = new System.Text.StringBuilder();
                 for (int i = fieldModsStart; i < json.Length; i++)
                 {
                     char c = json[i];
                     if (c == '[') { inArray = true; continue; }
                     if (!inArray) continue;
                     if (c == '{') { braceDepth++; current.Clear(); current.Append(c); }
                     else if (c == '}' && braceDepth > 0)
                     {
                         current.Append(c); braceDepth--;
                         if (braceDepth == 0)
                         {
                             string obj = current.ToString();
                             string ctype = ExtractString(obj, "componentType");
                             string fname = ExtractString(obj, "fieldName");
                             string vstr = ExtractString(obj, "valueString");
                             if (!string.IsNullOrEmpty(ctype) && !string.IsNullOrEmpty(fname))
                                 cfg.fieldMods.Add(new FieldModEntry { componentType = ctype, fieldName = fname, valueString = vstr });
                         }
                     }
                     else if (braceDepth > 0) current.Append(c);
                     if (c == ']' && braceDepth == 0) break;
                 }
             }

             return cfg;
         }

         static string ExtractString(string json, string key)
         {
             string search = $"\"{key}\"";
             int idx = json.IndexOf(search);
             if (idx < 0) return null;
             idx = json.IndexOf('"', idx + search.Length);
             if (idx < 0) return null;
             idx++;
             int end = idx;
             while (end < json.Length && json[end] != '"') { if (json[end] == '\\') end++; end++; }
             return json.Substring(idx, end - idx).Replace("\\\"", "\"").Replace("\\\\", "\\");
         }
     }
 
     // ── Startup: apply all saved configs to prefabs before instances spawn ───
     public static class LustDaddyStartup
     {
         public static string ConfigDirectory =>
             Path.Combine(BepInEx.Paths.ConfigPath, "LustDaddy");
 
         private static bool _applied = false;
 
         public static void ApplyAllConfigs()
         {
             if (_applied) return;
             _applied = true;
 
             if (!Directory.Exists(ConfigDirectory)) return;
 
             // Index ALL non-scene GOs by name for fast lookup
             var allGOs = Resources.FindObjectsOfTypeAll<GameObject>()
                 .Where(g => !g.scene.IsValid())
                 .GroupBy(g => g.name)
                 .ToDictionary(g => g.Key, g => g.First());
 
             foreach (var file in Directory.GetFiles(ConfigDirectory, "*.json"))
             {
                 try
                 {
                     var config = UnitModConfig.FromJson(File.ReadAllText(file));
                     if (config == null || string.IsNullOrEmpty(config.unitId)) continue;
                     GameObject unitPrefab = null;
                     if (!TryGetPrefabByPath(config.unitId, out unitPrefab))
                     {
                         if (!allGOs.TryGetValue(config.unitId, out unitPrefab)) continue;
                     }
 
                     foreach (var swap in config.turretSwaps)
                     {
                         if (string.IsNullOrEmpty(swap.slotName) || string.IsNullOrEmpty(swap.replacement)) continue;
 
                         Transform oldSlot = null;
                         foreach (Transform t in unitPrefab.GetComponentsInChildren<Transform>(true))
                             if (t.name == swap.slotName) { oldSlot = t; break; }
                         if (oldSlot == null) { Debug.LogWarning($"[LustDaddy] Slot '{swap.slotName}' not found on '{config.unitId}'"); continue; }
 
                         GameObject newPrefab = null;
                         if (!TryGetPrefabByPath(swap.replacement, out newPrefab))
                         {
                             if (!allGOs.TryGetValue(swap.replacement, out newPrefab))
                             {
                                 Debug.LogWarning($"[LustDaddy] Replacement '{swap.replacement}' not found");
                                 continue;
                             }
                         }
 
                         var dummy = new GameObject("LustDaddyDummy");
                         dummy.SetActive(false);
                         
                         var newGO = Object.Instantiate(newPrefab, dummy.transform);
                         newGO.transform.SetParent(oldSlot.parent, false);
                         
                         newGO.name = newPrefab.name;
                         newGO.transform.localPosition = oldSlot.localPosition;
                         newGO.transform.localRotation = oldSlot.localRotation;
                         newGO.transform.localScale    = oldSlot.localScale;

                         Object.DestroyImmediate(dummy);

                         // Strip SetGlobalParticles to avoid NRE on instantiated clone
                         foreach (var sgp in newGO.GetComponentsInChildren<Component>(true))
                         {
                             if (sgp != null && sgp.GetType().Name == "SetGlobalParticles")
                                 Object.DestroyImmediate(sgp, true);
                         }

                         // Strip ShipPart if target is not a Ship (prevents ShipPart.Awake() NRE on land vehicles)
                         if (unitPrefab.GetComponent("Ship") == null)
                         {
                             foreach (var sp in newGO.GetComponentsInChildren<Component>(true))
                             {
                                 if (sp != null && sp.GetType().Name == "ShipPart")
                                     Object.DestroyImmediate(sp, true);
                             }
                         }

                         WireUnitRefs(newGO, unitPrefab);

                         Object.DestroyImmediate(oldSlot.gameObject, true);

                         Debug.Log($"[LustDaddy] Config applied: '{swap.slotName}' -> '{swap.replacement}' on '{config.unitId}'");
                     }

                     // Apply field modifications
                     foreach (var fieldMod in config.fieldMods)
                     {
                         if (string.IsNullOrEmpty(fieldMod.componentType) || string.IsNullOrEmpty(fieldMod.fieldName)) continue;
                         
                         var comps = unitPrefab.GetComponentsInChildren<Component>(true);
                         Component targetComp = null;
                         foreach (var c in comps)
                         {
                             if (c != null && c.GetType().Name == fieldMod.componentType)
                             {
                                 targetComp = c;
                                 break;
                             }
                         }
                         if (targetComp == null) continue;

                         var t = targetComp.GetType();
                         FieldInfo f = null;
                         while (t != null && t != typeof(object))
                         {
                             f = t.GetField(fieldMod.fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                             if (f != null) break;
                             t = t.BaseType;
                         }

                         if (f != null)
                         {
                             try
                             {
                                 object parsedVal = null;
                                 if (f.FieldType == typeof(float)) parsedVal = float.Parse(fieldMod.valueString);
                                 else if (f.FieldType == typeof(int)) parsedVal = int.Parse(fieldMod.valueString);
                                 else if (f.FieldType == typeof(bool)) parsedVal = bool.Parse(fieldMod.valueString);
                                 else if (f.FieldType == typeof(string)) parsedVal = fieldMod.valueString;
                                 else if (f.FieldType.IsEnum) parsedVal = Enum.Parse(f.FieldType, fieldMod.valueString, true);
                                 
                                 if (parsedVal != null) f.SetValue(targetComp, parsedVal);
                             }
                             catch (Exception ex) { Debug.LogWarning($"[LustDaddy] Failed to apply field mod {fieldMod.fieldName}: {ex.Message}"); }
                         }
                     }
                 }
                 catch (Exception ex) { Debug.LogError($"[LustDaddy] Config error in '{file}': {ex.Message}"); }
             }
         }
 
         static void WireUnitRefs(GameObject turretGO, GameObject unitGO)
         {
             var unitComp = unitGO.GetComponent("Unit") as Component;
             if (unitComp == null) return;
             var rb = unitGO.GetComponent<Rigidbody>();
             foreach (var comp in turretGO.GetComponentsInChildren<Component>(true))
             {
                 if (comp == null) continue;
                 var t = comp.GetType();
                 while (t != null && t != typeof(object))
                 {
                     if (t.Name == "UnitPart")   { SetField(comp, "parentUnit",    unitComp); break; }
                     if (t.Name == "Turret" || t.Name == "TargetDetector" ||
                         t.Name == "MissileLauncher" || t.Name == "WeaponStation")
                                                 { SetField(comp, "attachedUnit",  unitComp); break; }
                     if (t.Name == "Gun" && rb != null) { SetField(comp, "velocityInherit", rb); break; }
                     t = t.BaseType;
                 }
             }
         }
 
         static void SetField(object obj, string name, object value)
         {
             var t = obj.GetType();
             while (t != null && t != typeof(object))
             {
                 var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                 if (f != null) { f.SetValue(obj, value); return; }
                 t = t.BaseType;
             }
         }

         public static bool TryGetPrefabByPath(string path, out GameObject result)
         {
             result = null;
             if (string.IsNullOrEmpty(path)) return false;
             var parts = path.Split('/');
             if (parts.Length == 0) return false;
             var rootName = parts[0];
             foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
             {
                 if (go.scene.IsValid() || go.transform.parent != null || go.name != rootName) continue;
                 Transform curr = go.transform;
                 bool found = true;
                 for (int i = 1; i < parts.Length; i++)
                 {
                     Transform next = null;
                     for (int j = 0; j < curr.childCount; j++)
                     {
                         if (curr.GetChild(j).name == parts[i]) { next = curr.GetChild(j); break; }
                     }
                     if (next == null) { found = false; break; }
                     curr = next;
                 }
                 if (found) { result = curr.gameObject; return true; }
             }
             return false;
         }
     }
 
     // ── Harmony patch: apply configs right after Encyclopedia finishes loading ─
     [HarmonyPatch(typeof(Encyclopedia), "AfterLoad", new Type[0])]
     public static class EncyclopediaAfterLoadPatch
     {
         [HarmonyPostfix]
         static void Postfix() => LustDaddyStartup.ApplyAllConfigs();
     }

    public class LustDaddyUI : MonoBehaviour
    {
        // ── Window ──────────────────────────────────────────────────────────
        private bool _showWindow = false;
        private Rect _windowRect = new Rect(100, 100, 800, 650);
        private Vector2 _scrollPosition;

        private enum Tab { Payloads, Turrets, Units }
        private Tab _currentTab = Tab.Payloads;

        // ── Prefab Caches ────────────────────────────────────────────────────
        private List<Object> _payloadPrefabs         = new List<Object>();
        private List<Object> _turretPrefabs          = new List<Object>(); // WeaponInfo ScriptableObjects (for field editors)
        private List<Object> _unitPrefabs            = new List<Object>();
        private List<GameObject> _sourceTurretGOs    = new List<GameObject>(); // physical turret prefab GOs (for Turrets tab + cycler)
        private Dictionary<string, GameObject> _allPrefabsCache = new Dictionary<string, GameObject>();
        private bool _scanned = false;
        private GameObject _actualNukePrefab = null;

        // ── Selection ────────────────────────────────────────────────────────
        private int _selectedPayloadIndex = -1;
        private int _selectedTurretIndex  = -1; // indexes into _sourceTurretGOs
        private int _selectedUnitIndex    = -1;

        // ── Component Editor State ───────────────────────────────────────────
        private List<Object> _selectedComponents = new List<Object>();
        private Dictionary<Object, List<FieldInfo>> _editableFields = new Dictionary<Object, List<FieldInfo>>();
        private Dictionary<Object, Dictionary<FieldInfo, object>> _originalValues = new Dictionary<Object, Dictionary<FieldInfo, object>>();
        private bool _needsRefreshSelected = false;

        // ── Per-station cycler (Turrets tab): key = stationIndex, value = _sourceTurretGOs index ──
        private Dictionary<int, int> _stationSwapIndices = new Dictionary<int, int>();

        // ── Unit Turret Cycler (Units tab): swap entire child turret GO ──────
        private List<GameObject> _unitTurretSlots = new List<GameObject>(); // child turret GOs on selected unit
        private int _unitTurretSlotIndex  = 0; // which slot (child turret) the user is targeting
        private int _unitTurretSwapIndex  = 0; // which source turret GO to swap in
        private Dictionary<string, int> _unitSlotAssignments = new Dictionary<string, int>();

        // ════════════════════════════════════════════════════════════════════
        #region Unity Lifecycle
        private void Update()
        {
            if (Input.GetKeyDown(LustDaddyPlugin.ToggleKey.Value))
            {
                _showWindow = !_showWindow;
                if (_showWindow && !_scanned) ScanPrefabs();
            }
        }

        private void OnGUI()
        {
            if (!_showWindow) return;
            GUI.backgroundColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);
            _windowRect = GUI.Window(14500, _windowRect, WindowFunction, "LUST-DADDY (Loadout Utility & Specs Tweaker)");
        }
        #endregion

        // ════════════════════════════════════════════════════════════════════
        #region Window
        private void WindowFunction(int windowID)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Payloads", _currentTab == Tab.Payloads ? SelectedStyle() : GUI.skin.button)) SwitchTab(Tab.Payloads);
            if (GUILayout.Button("Turrets",  _currentTab == Tab.Turrets  ? SelectedStyle() : GUI.skin.button)) SwitchTab(Tab.Turrets);
            if (GUILayout.Button("Units",    _currentTab == Tab.Units    ? SelectedStyle() : GUI.skin.button)) SwitchTab(Tab.Units);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("Force Rescan")) { ScanPrefabs(); UpdateSelectedObject(); }
            GUILayout.Space(10);

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            switch (_currentTab)
            {
                case Tab.Payloads: DrawDropdown(ref _selectedPayloadIndex, _payloadPrefabs); break;
                case Tab.Turrets:  DrawSourceTurretDropdown(); break;
                case Tab.Units:    DrawDropdown(ref _selectedUnitIndex,    _unitPrefabs);    break;
            }

            GUILayout.Space(10);

            // Flush any pending refresh BEFORE drawing so lists are clean
            if (_needsRefreshSelected)
            {
                _needsRefreshSelected = false;
                UpdateSelectedObject();
            }

            DrawComponentEditors();
            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        private GUIStyle SelectedStyle()
        {
            var s = new GUIStyle(GUI.skin.button);
            s.normal.textColor = Color.yellow;
            return s;
        }

        private void SwitchTab(Tab tab)
        {
            if (_currentTab == tab) return;
            _currentTab = tab;
            _stationSwapIndices.Clear();
            _unitTurretSlots.Clear();
            _unitTurretSlotIndex = 0;
            _unitTurretSwapIndex = 0;
            UpdateSelectedObject();
        }
        #endregion

        // ════════════════════════════════════════════════════════════════════
        #region Scanning
        private void ScanPrefabs()
        {
            _payloadPrefabs.Clear();
            _turretPrefabs.Clear();
            _unitPrefabs.Clear();
            _sourceTurretGOs.Clear();
            _allPrefabsCache.Clear();

            var allGOs = Resources.FindObjectsOfTypeAll<GameObject>();

            foreach (var go in allGOs)
                if (!go.scene.IsValid() && go.transform.parent == null && !_allPrefabsCache.ContainsKey(go.name))
                    _allPrefabsCache[go.name] = go;

            foreach (var go in allGOs)
            {
                if (go.scene.IsValid()) continue;

                bool isPayload = go.GetComponent("Missile") != null
                              || go.GetComponent("OpticalSeekerBomb") != null
                              || go.GetComponent("Warhead") != null;
                bool isUnit = go.transform.parent == null && go.GetComponent("Unit") != null && !isPayload && go.GetComponent("Aircraft") == null;

                if (isPayload) _payloadPrefabs.Add(go);
                if (isUnit)    _unitPrefabs.Add(go);
            }

            // Extract child turrets from units instead of just root prefabs
            foreach (var unit in _unitPrefabs)
            {
                foreach (var comp in ((GameObject)unit).GetComponentsInChildren<Component>(true))
                {
                    if (comp == null) continue;
                    var t = comp.GetType();
                    bool isTurret = false;
                    while (t != null && t != typeof(object))
                    {
                        if (t.Name == "Turret" || t.Name == "Gun" || t.Name == "MissileLauncher")
                        {
                            isTurret = true;
                            break;
                        }
                        t = t.BaseType;
                    }
                    if (isTurret)
                    {
                        var go = comp.gameObject;
                        if (go != unit && !_sourceTurretGOs.Contains(go))
                            _sourceTurretGOs.Add(go);
                    }
                }
            }

            // WeaponInfo objects — kept for field editors that reference WeaponInfo
            Type wiType = Type.GetType("WeaponInfo, Assembly-CSharp");
            if (wiType != null)
                foreach (var w in Resources.FindObjectsOfTypeAll(wiType))
                    if (w != null && !_turretPrefabs.Contains(w))
                        _turretPrefabs.Add(w);

            DiscoverNukePrefab();

            _payloadPrefabs.Sort((a, b) => a.name.CompareTo(b.name));
            _turretPrefabs.Sort((a, b) => a.name.CompareTo(b.name));
            _unitPrefabs.Sort((a, b) => a.name.CompareTo(b.name));
            _sourceTurretGOs.Sort((a, b) => a.name.CompareTo(b.name));
            _scanned = true;
        }
        #endregion

        // ════════════════════════════════════════════════════════════════════
        #region Turrets Tab Dropdown
        /// <summary>Turrets tab shows physical turret GOs, not WeaponInfo objects.</summary>
        private void DrawSourceTurretDropdown()
        {
            if (_sourceTurretGOs.Count == 0) { GUILayout.Label("No turret prefabs found."); return; }
            GUILayout.Label("Select Physical Turret Prefab:");
            string[] names = _sourceTurretGOs.Select(g => GetGameObjectPath(g)).ToArray();
            int newIndex = GUILayout.SelectionGrid(_selectedTurretIndex, names, 1);
            if (newIndex != _selectedTurretIndex)
            {
                _selectedTurretIndex = newIndex;
                _stationSwapIndices.Clear();
                UpdateSelectedObject();
            }
        }
        #endregion

        // ════════════════════════════════════════════════════════════════════
        #region Selection & Dropdown
        private Object GetCurrentSelectedObject()
        {
            if (_currentTab == Tab.Payloads && _selectedPayloadIndex >= 0 && _selectedPayloadIndex < _payloadPrefabs.Count)
                return _payloadPrefabs[_selectedPayloadIndex];
            // Turrets tab now uses _sourceTurretGOs
            if (_currentTab == Tab.Turrets && _selectedTurretIndex >= 0 && _selectedTurretIndex < _sourceTurretGOs.Count)
                return _sourceTurretGOs[_selectedTurretIndex];
            if (_currentTab == Tab.Units && _selectedUnitIndex >= 0 && _selectedUnitIndex < _unitPrefabs.Count)
                return _unitPrefabs[_selectedUnitIndex];
            return null;
        }

        private void DrawDropdown(ref int selectedIndex, List<Object> list)
        {
            if (list.Count == 0) { GUILayout.Label("No items found in this category."); return; }
            GUILayout.Label("Select Entity:");
            string[] names = list.Select(GetPrefabDisplayName).ToArray();
            int newIndex = GUILayout.SelectionGrid(selectedIndex, names, 1);
            if (newIndex != selectedIndex)
            {
                selectedIndex = newIndex;
                _stationSwapIndices.Clear();
                UpdateSelectedObject();
            }
        }

        private string GetPrefabDisplayName(Object obj)
        {
            if (obj == null) return "Null";
            if (!(obj is GameObject go)) return obj.name;
            try
            {
                var uc = go.GetComponent("Unit");
                if (uc != null)
                {
                    string n = GetMemberValue(uc, "unitName") as string;
                    if (!string.IsNullOrEmpty(n)) return $"{n} ({go.name})";
                    object def = GetMemberValue(uc, "definition");
                    if (def != null)
                    {
                        n = GetMemberValue(def, "unitName") as string;
                        if (!string.IsNullOrEmpty(n)) return $"{n} ({go.name})";
                    }
                }
            }
            catch { }
            return go.name;
        }

        public static string GetGameObjectPath(GameObject obj)
        {
            if (obj == null) return "";
            string path = obj.name;
            Transform curr = obj.transform;
            while (curr.parent != null)
            {
                curr = curr.parent;
                path = curr.name + "/" + path;
            }
            return path;
        }

        private void UpdateSelectedObject()
        {
            _selectedComponents.Clear();
            _editableFields.Clear();
            _unitTurretSlots.Clear();
            _unitTurretSlotIndex = 0;
            _unitTurretSwapIndex = 0;

            var selected = GetCurrentSelectedObject();
            if (selected == null) return;

            if (selected is ScriptableObject scriptable)
            {
                var fields = GetEditableFields(scriptable.GetType());
                if (fields.Count > 0)
                {
                    _selectedComponents.Add(scriptable);
                    _editableFields[scriptable] = fields;
                    CacheOriginalValues(scriptable, fields);
                }
                return;
            }

            if (!(selected is GameObject go)) return;

            // Root components first, then relevant child components
            var all = go.GetComponents<Component>().ToList();
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c.gameObject == go) continue;
                var t = c.GetType();
                if (t.Namespace != null && t.Namespace.StartsWith("UnityEngine")) continue;
                if (t.Name == "Turret" || t.Name == "WeaponStation" || t.Name == "WeaponManager"
                    || GetFieldRecursively(t, "weaponStations") != null
                    || GetFieldRecursively(t, "WeaponInfo") != null
                    || GetPropertyRecursively(t, "WeaponInfo") != null)
                    all.Add(c);
            }

            foreach (var comp in all)
            {
                if (comp == null || comp is Transform) continue;
                var t = comp.GetType();
                if (t.Namespace != null && t.Namespace.StartsWith("UnityEngine")) continue;

                var wsField = GetFieldRecursively(t, "weaponStations");
                var fields  = GetEditableFields(t);
                if (fields.Count > 0 || wsField != null)
                {
                    _selectedComponents.Add(comp);
                    _editableFields[comp] = fields;
                    CacheOriginalValues(comp, fields);
                }
            }

            // For Units tab: discover child turret slot GOs
            if (_currentTab == Tab.Units)
            {
                Type turretType = Type.GetType("Turret, Assembly-CSharp");
                Type gunType = Type.GetType("Gun, Assembly-CSharp");
                Type launcherType = Type.GetType("MissileLauncher, Assembly-CSharp");
                
                var seen = new HashSet<GameObject>();
                var comps = new List<Component>();
                if (turretType != null) comps.AddRange(go.GetComponentsInChildren(turretType, true));
                if (gunType != null) comps.AddRange(go.GetComponentsInChildren(gunType, true));
                if (launcherType != null) comps.AddRange(go.GetComponentsInChildren(launcherType, true));

                foreach (var c in comps)
                {
                    if (c == null) continue;
                    var slotGO = c.gameObject;
                    
                    bool hasTurretParent = false;
                    Transform p = slotGO.transform.parent;
                    while (p != null)
                    {
                        if (p.GetComponent("Turret") != null) { hasTurretParent = true; break; }
                        p = p.parent;
                    }
                    if (hasTurretParent) continue;

                    if (seen.Add(slotGO)) _unitTurretSlots.Add(slotGO);
                }

                    // Fallback: search live scene instances if the prefab has no turrets
                    // (can happen after a swap destroys the prefab's original turret GO)
                    if (_unitTurretSlots.Count == 0)
                    {
                        Type unitType = Type.GetType("Unit, Assembly-CSharp");
                        if (unitType != null)
                        {
                            foreach (Component inst in FindObjectsOfType(unitType))
                            {
                                if (inst == null) continue;
                                if (inst.gameObject.name.Replace("(Clone)", "").Trim() != go.name) continue;
                                foreach (var c in inst.gameObject.GetComponentsInChildren(turretType, true))
                                {
                                    if (c == null) continue;
                                    var slotGO = ((Component)c).gameObject;
                                    if (seen.Add(slotGO)) _unitTurretSlots.Add(slotGO);
                                }
                                break; // one live instance is enough
                            }
                        }
                    }
                // Fallback: any GO with Gun or MissileLauncher directly attached
                if (_unitTurretSlots.Count == 0)
                {
                    var seen2 = new HashSet<GameObject>();
                    // Try prefab first, then live instances
                    IEnumerable<Component> searchComps = go.GetComponentsInChildren<Component>(true);
                    Type unitType2 = Type.GetType("Unit, Assembly-CSharp");
                    if (unitType2 != null)
                        foreach (Component inst in FindObjectsOfType(unitType2))
                        {
                            if (inst == null) continue;
                            if (inst.gameObject.name.Replace("(Clone)", "").Trim() != go.name) continue;
                            searchComps = searchComps.Concat(inst.gameObject.GetComponentsInChildren<Component>(true));
                            break;
                        }
                    foreach (var c in searchComps)
                    {
                        if (c == null) continue;
                        var cType = c.GetType();
                        if ((cType.Name == "Gun" || cType.Name == "MissileLauncher") && c.gameObject != go)
                        {
                            var slotGO = c.gameObject;
                            if (slotGO.transform.parent != null && slotGO.transform.parent.gameObject != go)
                                slotGO = slotGO.transform.parent.gameObject;
                            if (seen2.Add(slotGO)) _unitTurretSlots.Add(slotGO);
                        }
                    }
                }
            }
        }

        private List<FieldInfo> GetEditableFields(Type type) =>
            type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => !f.IsInitOnly && !f.IsLiteral)
                .Where(f => f.IsPublic || f.GetCustomAttribute<SerializeField>() != null)
                .Where(f => IsSupportedType(f.FieldType))
                .ToList();

        private void CacheOriginalValues(Object obj, List<FieldInfo> fields)
        {
            if (_originalValues.ContainsKey(obj)) return;
            var orig = new Dictionary<FieldInfo, object>();
            foreach (var f in fields)
                try { orig[f] = f.GetValue(obj); } catch { }
            _originalValues[obj] = orig;
        }

        private bool IsSupportedType(Type t) =>
            t == typeof(float) || t == typeof(int) || t == typeof(bool) || t == typeof(string) ||
            t == typeof(Vector3) || t.IsEnum || typeof(Object).IsAssignableFrom(t);
        #endregion

        // ════════════════════════════════════════════════════════════════════
        #region Component Editors
        private void DrawComponentEditors()
        {
            var selected = GetCurrentSelectedObject();
            if (selected == null) { GUILayout.Label("Select an item to edit its properties."); return; }

            GUILayout.Label($"Editing: {selected.name}", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });

             // ── Units tab: show config-based turret builder at bottom ─────
             if (_currentTab == Tab.Units)
                 DrawUnitConfigUI(selected as GameObject);

            // Snapshot to avoid collection-modified exception
            var snapshot = _selectedComponents.ToList();

            foreach (var comp in snapshot)
            {
                if (comp == null) continue;
                var fields = _editableFields.ContainsKey(comp) ? _editableFields[comp] : new List<FieldInfo>();

                // Show component header + field editors
                string compName = comp.GetType().Name;
                if (comp is Object o) compName += $" ({o.name})";
                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"--- {compName} ---",
                    new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = Color.cyan } });
                if (GUILayout.Button("Reset", GUILayout.Width(60)))
                {
                    if (_originalValues.TryGetValue(comp, out var origs))
                        foreach (var kvp in origs)
                        {
                            kvp.Key.SetValue(comp, kvp.Value);
                            if (comp is Component c) ApplyToInstances(c.gameObject, c.GetType(), kvp.Key, kvp.Value);
                        }
                }
                GUILayout.EndHorizontal();

                foreach (var f in fields) DrawFieldEditor(comp, f);

                if (_currentTab == Tab.Turrets)
                    DrawWeaponStations(comp);
            }
        }
        #endregion

        // ════════════════════════════════════════════════════════════════════
        #region Unit Turret Config UI
        private void DrawUnitConfigUI(GameObject unitGO)
        {
            if (unitGO == null) return;

            _unitTurretSlots.RemoveAll(g => g == null);
            _sourceTurretGOs.RemoveAll(g => g == null);
            if (_unitTurretSlotIndex >= _unitTurretSlots.Count)
                _unitTurretSlotIndex = Mathf.Max(0, _unitTurretSlots.Count - 1);

            GUILayout.Space(10);
            GUILayout.Label("─── Turret Config (restart to apply) ───",
                new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = Color.yellow } });

            string configPath = Path.Combine(LustDaddyStartup.ConfigDirectory, unitGO.name + ".json");
            bool hasConfig = File.Exists(configPath);
            if (hasConfig)
                GUILayout.Label("  \u2713 Config saved \u2014 restart game to apply",
                    new GUIStyle(GUI.skin.label) { normal = { textColor = Color.green } });

            if (_unitTurretSlots.Count == 0)
            {
                GUILayout.Label("  No child turrets found on this unit.",
                    new GUIStyle(GUI.skin.label) { normal = { textColor = Color.grey } });
            }
            else
            {
                bool isShip = unitGO.GetComponent("Ship") != null;
                var validSources = new List<GameObject>();
                foreach (var g in _sourceTurretGOs)
                {
                    bool hasShipPart = false;
                    foreach (var c in g.GetComponentsInChildren<Component>(true)) {
                        if (c != null && c.GetType().Name == "ShipPart") hasShipPart = true;
                    }
                    if (!isShip && hasShipPart) continue;
                    validSources.Add(g);
                }

                string[] sourceNames = new[] { "(no change)" }
                    .Concat(validSources.Select(g => GetGameObjectPath(g))).ToArray();

                GUILayout.Label("  Assign replacements per slot:",
                    new GUIStyle(GUI.skin.label) { normal = { textColor = Color.grey } });

                foreach (var slotGO in _unitTurretSlots)
                {
                    if (slotGO == null) continue;
                    string slotName = slotGO.name;
                    if (!_unitSlotAssignments.ContainsKey(slotName))
                        _unitSlotAssignments[slotName] = 0;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(slotName, GUILayout.Width(160));
                    int cur = _unitSlotAssignments[slotName];
                    _unitSlotAssignments[slotName] = DrawCycler("", cur, sourceNames, 400, Color.cyan);
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Save Config (restart to apply)", GUILayout.Height(28)))
                SaveUnitConfig(unitGO);

            if (hasConfig && GUILayout.Button("Clear Config"))
            {
                File.Delete(configPath);
                Debug.Log($"[LustDaddy] Cleared config for '{unitGO.name}'");
            }
        }

        private void SaveUnitConfig(GameObject unitGO)
        {
            var config = new UnitModConfig { unitId = unitGO.name };

            bool isShip = unitGO.GetComponent("Ship") != null;
            var validSources = new List<GameObject>();
            foreach (var g in _sourceTurretGOs)
            {
                bool hasShipPart = false;
                foreach (var c in g.GetComponentsInChildren<Component>(true)) {
                    if (c != null && c.GetType().Name == "ShipPart") hasShipPart = true;
                }
                if (!isShip && hasShipPart) continue;
                validSources.Add(g);
            }

            foreach (var kv in _unitSlotAssignments)
            {
                if (kv.Value <= 0) continue;
                int srcIdx = kv.Value - 1;
                if (srcIdx >= validSources.Count) continue;
                config.turretSwaps.Add(new TurretSwapEntry
                {
                    slotName    = kv.Key,
                    replacement = GetGameObjectPath(validSources[srcIdx])
                });
            }

            // Save field modifications
            foreach (var comp in _selectedComponents)
            {
                if (comp == null || !_editableFields.ContainsKey(comp) || !_originalValues.ContainsKey(comp)) continue;
                var compName = comp.GetType().Name;
                foreach (var field in _editableFields[comp])
                {
                    if (field.FieldType != typeof(float) && field.FieldType != typeof(int) && 
                        field.FieldType != typeof(bool) && field.FieldType != typeof(string) && !field.FieldType.IsEnum) continue;
                        
                    if (!_originalValues[comp].ContainsKey(field)) continue;
                    object origVal = _originalValues[comp][field];
                    object curVal = field.GetValue(comp);
                    if (curVal != null && !curVal.Equals(origVal))
                    {
                        config.fieldMods.Add(new FieldModEntry
                        {
                            componentType = compName,
                            fieldName = field.Name,
                            valueString = curVal.ToString()
                        });
                    }
                }
            }

            if (config.turretSwaps.Count == 0 && config.fieldMods.Count == 0) { Debug.Log("[LustDaddy] Nothing to save."); return; }
            Directory.CreateDirectory(LustDaddyStartup.ConfigDirectory);
            string json = config.ToJson();
            File.WriteAllText(Path.Combine(LustDaddyStartup.ConfigDirectory, unitGO.name + ".json"), json);
            Debug.Log($"[LustDaddy] Config saved for '{unitGO.name}':\n{json}");
        }
        #endregion

        // ════════════════════════════════════════════════════════════════════
        #region Weapon Stations (Turrets tab only)
        private void DrawWeaponStations(Object comp)
        {
            if (comp == null) return;
            var wsField = GetFieldRecursively(comp.GetType(), "weaponStations");
            if (wsField == null) return;
            var stations = wsField.GetValue(comp) as System.Collections.IList;
            if (stations == null || stations.Count == 0) return;

            string ownerName = (comp is Component ca && ca.gameObject != null) ? ca.gameObject.name : comp.name;

            GUILayout.Space(8);
            GUILayout.Label($"--- Weapon Stations on '{ownerName}' ---",
                new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = Color.yellow } });

            string[] prefabNames = _sourceTurretGOs.Select(t => t.name).ToArray();

            for (int i = 0; i < stations.Count; i++)
            {
                var station = stations[i];
                if (station == null) continue;

                string currentPhysical = GetPhysicalTurretName(station);

                if (!_stationSwapIndices.ContainsKey(i))
                {
                    int matchIdx = _sourceTurretGOs.FindIndex(t => t.name == currentPhysical);
                    _stationSwapIndices[i] = matchIdx >= 0 ? matchIdx : 0;
                }

                GUILayout.Space(4);
                int oldIdx = _stationSwapIndices[i];
                int newIdx = DrawCycler($"Station {i} Turret:", oldIdx, prefabNames, 200, Color.cyan);
                _stationSwapIndices[i] = newIdx;

                if (newIdx != oldIdx && prefabNames.Length > 0)
                    SwapStationTurret(comp as Component, i, _sourceTurretGOs[newIdx]);

                var wi     = GetMemberValue(station, "WeaponInfo") as Object;
                string wiName = wi != null ? wi.name : "None";
                GUILayout.Label($"  WeaponInfo: {wiName}  |  Physical: {currentPhysical}",
                    new GUIStyle(GUI.skin.label) { normal = { textColor = new Color(0.6f, 0.85f, 1f) } });
            }
        }

        private string GetPhysicalTurretName(object station)
        {
            var turretsList = GetMemberValue(station, "Turrets") as System.Collections.IList;
            if (turretsList != null && turretsList.Count > 0 && turretsList[0] is Component tc && tc != null)
                return tc.gameObject.name;

            var weaponsList = GetMemberValue(station, "Weapons") as System.Collections.IList;
            if (weaponsList != null && weaponsList.Count > 0 && weaponsList[0] is Component wc && wc != null)
            {
                Type tt = Type.GetType("Turret, Assembly-CSharp");
                if (tt != null)
                {
                    var pt = wc.GetComponentInParent(tt) as Component;
                    if (pt != null) return pt.gameObject.name;
                }
                return wc.gameObject.name;
            }
            return "None";
        }

        private int DrawCycler(string label, int index, string[] options, int valueWidth, Color valueColor)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(130));
            if (GUILayout.Button("<", GUILayout.Width(24))) index--;
            if (options.Length > 0)
            {
                if (index < 0) index = options.Length - 1;
                if (index >= options.Length) index = 0;
            }
            string text = (options != null && index >= 0 && index < options.Length) ? options[index] : "None";
            GUILayout.Label(text, new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = valueColor }
            }, GUILayout.Width(valueWidth));
            if (GUILayout.Button(">", GUILayout.Width(24))) index++;
            if (options.Length > 0)
            {
                if (index < 0) index = options.Length - 1;
                if (index >= options.Length) index = 0;
            }
            GUILayout.EndHorizontal();
            return index;
        }

        /// <summary>
        /// Swaps the physical turret on the given station.
        /// stationHolder = any Component that has a "weaponStations" field.
        /// </summary>
        private void SwapStationTurret(Component stationHolder, int stationIndex, GameObject newTurretPrefab)
        {
            if (stationHolder == null || newTurretPrefab == null) return;

            var wsField = GetFieldRecursively(stationHolder.GetType(), "weaponStations");
            if (wsField == null) return;
            var stations = wsField.GetValue(stationHolder) as System.Collections.IList;
            if (stations == null || stationIndex >= stations.Count) return;
            var station = stations[stationIndex];
            if (station == null) return;

            // 1. Locate old physical GO
            GameObject oldGO = null;
            var tList = GetMemberValue(station, "Turrets") as System.Collections.IList;
            var wList = GetMemberValue(station, "Weapons") as System.Collections.IList;

            if (tList != null && tList.Count > 0 && tList[0] is Component tComp && tComp != null)
                oldGO = tComp.gameObject;
            else if (wList != null && wList.Count > 0 && wList[0] is Component wComp && wComp != null)
            {
                Type tt = Type.GetType("Turret, Assembly-CSharp");
                var pt = (tt != null ? wComp.GetComponentInParent(tt) : null) as Component;
                oldGO = pt != null ? pt.gameObject : wComp.gameObject;
            }

            if (oldGO == null)
            {
                Debug.LogError($"[LustDaddy] SwapStationTurret: no existing turret GO on station {stationIndex} of '{stationHolder.gameObject.name}'");
                return;
            }

            // 2. Clone new turret at same transform
            Transform parent = oldGO.transform.parent;
            GameObject newGO = GameObject.Instantiate(newTurretPrefab, parent);
            newGO.name = newTurretPrefab.name;
            newGO.transform.localPosition = oldGO.transform.localPosition;
            newGO.transform.localRotation = oldGO.transform.localRotation;
            newGO.transform.localScale    = oldGO.transform.localScale;

            // 3. Wire unit refs (best-effort — doesn't block the swap if not found)
            Component unitComp = TryFindUnit(stationHolder);
            if (unitComp != null) SetTurretUnitReferences(newGO, unitComp);

            // 4. Collect new sub-components
            var newTurrets = GetComponentsByName(newGO, "Turret");
            var newGuns    = GetComponentsByName(newGO, "Gun");
            var newLaunch  = GetComponentsByName(newGO, "MissileLauncher");
            var newWeapons = newGuns.Concat(newLaunch).ToList();

            // 5. Update station collections
            ReplaceCollection(station, "Turrets", newTurrets);
            ReplaceCollection(station, "Weapons", newWeapons);

            // 6. Auto-extract WeaponInfo
            Object newWi = null;
            foreach (var wc in newWeapons) { newWi = GetMemberValue(wc, "WeaponInfo") as Object; if (newWi != null) break; }
            if (newWi == null)
                foreach (var tc in newTurrets) { newWi = GetMemberValue(tc, "WeaponInfo") as Object; if (newWi != null) break; }

            SetMemberValue(station, "WeaponInfo", newWi);
            foreach (var wc in newWeapons.Concat(newTurrets))
            {
                if (GetFieldRecursively(wc.GetType(), "WeaponInfo") != null || GetPropertyRecursively(wc.GetType(), "WeaponInfo") != null)
                    SetMemberValue(wc, "WeaponInfo", newWi);
            }

            // 7. Apply same swap to all matching scene instances
            ApplySwapToInstances(stationHolder, stationIndex, newTurretPrefab);

            // 8. Destroy old turret GO // could be destroying the new turret too, so to be safe, i disable it
            // GameObject.Destroy(oldGO);

            _needsRefreshSelected = true;
            Debug.Log($"[LustDaddy] Swapped station {stationIndex} on '{stationHolder.gameObject.name}' -> '{newTurretPrefab.name}' (WI: {(newWi != null ? newWi.name : "None")})");
        }

        private Component TryFindUnit(Component comp)
        {
            if (comp == null) return null;
            Type unitType = Type.GetType("Unit, Assembly-CSharp");
            if (unitType == null) return null;

            Transform t = comp.transform;
            while (t != null)
            {
                var c = t.GetComponent(unitType);
                if (c != null) return c as Component;
                t = t.parent;
            }

            var sel = GetCurrentSelectedObject() as GameObject;
            if (sel != null) { var c = sel.GetComponent(unitType); if (c != null) return c as Component; }
            return null;
        }

        // Overload for when we already have the unitGO directly
        private Component TryFindUnit(GameObject unitGO)
        {
            if (unitGO == null) return null;
            Type unitType = Type.GetType("Unit, Assembly-CSharp");
            if (unitType == null) return null;
            var c = unitGO.GetComponent(unitType);
            if (c != null) return c as Component;
            // Also check children (some units have Unit on a child)
            foreach (var cc in unitGO.GetComponentsInChildren(unitType, true))
                if (cc != null) return cc as Component;
            return null;
        }

        private void ApplySwapToInstances(Component stationHolder, int stationIndex, GameObject newTurretPrefab)
        {
            // Walk up to find the root prefab GO
            GameObject rootGO = stationHolder.gameObject;
            while (rootGO.transform.parent != null) rootGO = rootGO.transform.parent.gameObject;

            var rootUnitComp = rootGO.GetComponent("Unit");
            if (rootUnitComp == null) return;

            var instances = FindObjectsOfType(rootUnitComp.GetType());
            foreach (Component inst in instances)
            {
                if (inst.gameObject.name.Replace("(Clone)", "").Trim() != rootGO.name) continue;
                var holderOnInst = FindMatchingHolder(inst.gameObject, stationHolder);
                if (holderOnInst != null)
                    SwapStationTurret(holderOnInst, stationIndex, newTurretPrefab);
            }
        }

        private Component FindMatchingHolder(GameObject rootInst, Component holderPrefab)
        {
            var holderType   = holderPrefab.GetType();
            string holderGOName = holderPrefab.gameObject.name;
            foreach (var c in rootInst.GetComponentsInChildren(holderType, true))
                if (c.gameObject.name == holderGOName) return c;
            return null;
        }
        #endregion

        // ════════════════════════════════════════════════════════════════════
        #region Reflection Helpers
        private FieldInfo GetFieldRecursively(Type type, string fieldName)
        {
            string lower = char.ToLower(fieldName[0]) + fieldName.Substring(1);
            while (type != null && type != typeof(object))
            {
                var f = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f;
                f = type.GetField(lower, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return f;
                if (fieldName == "WeaponInfo" || fieldName == "weaponInfo")
                {
                    f = type.GetField("info", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (f != null) return f;
                }
                type = type.BaseType;
            }
            return null;
        }

        private PropertyInfo GetPropertyRecursively(Type type, string propName)
        {
            string lower = char.ToLower(propName[0]) + propName.Substring(1);
            while (type != null && type != typeof(object))
            {
                var p = type.GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null) return p;
                p = type.GetProperty(lower, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null) return p;
                if (propName == "WeaponInfo" || propName == "weaponInfo")
                {
                    p = type.GetProperty("info", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (p != null) return p;
                }
                type = type.BaseType;
            }
            return null;
        }

        private object GetMemberValue(object obj, string name)
        {
            var f = GetFieldRecursively(obj.GetType(), name);
            if (f != null) return f.GetValue(obj);
            var p = GetPropertyRecursively(obj.GetType(), name);
            if (p != null && p.CanRead) return p.GetValue(obj);
            return null;
        }

        private void SetMemberValue(object obj, string name, object value)
        {
            var f = GetFieldRecursively(obj.GetType(), name);
            if (f != null) { f.SetValue(obj, value); return; }
            var p = GetPropertyRecursively(obj.GetType(), name);
            if (p != null && p.CanWrite) p.SetValue(obj, value);
        }

        private List<Component> GetComponentsByName(GameObject go, string typeName)
        {
            var list = new List<Component>();
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var t = c.GetType();
                while (t != null && t != typeof(object))
                {
                    if (t.Name == typeName) { list.Add(c); break; }
                    t = t.BaseType;
                }
            }
            return list;
        }

        private void ReplaceCollection(object parent, string fieldName, List<Component> newItems)
        {
            var f = GetFieldRecursively(parent.GetType(), fieldName);
            if (f == null) return;
            var ft = f.FieldType;
            if (ft.IsArray)
            {
                var et  = ft.GetElementType();
                var arr = Array.CreateInstance(et, newItems.Count);
                for (int i = 0; i < newItems.Count; i++) arr.SetValue(newItems[i], i);
                f.SetValue(parent, arr);
            }
            else if (typeof(System.Collections.IList).IsAssignableFrom(ft))
            {
                var list = f.GetValue(parent) as System.Collections.IList;
                if (list != null)
                {
                    try { list.Clear(); foreach (var item in newItems) list.Add(item); }
                    catch
                    {
                        var ga = ft.GetGenericArguments();
                        if (ga.Length > 0)
                        {
                            var newList = Activator.CreateInstance(typeof(List<>).MakeGenericType(ga[0])) as System.Collections.IList;
                            if (newList != null) { foreach (var item in newItems) newList.Add(item); f.SetValue(parent, newList); }
                        }
                    }
                }
            }
        }
        #endregion

        // ════════════════════════════════════════════════════════════════════
        #region Field Editors
        private string PrettifyName(string name)
        {
            string r = Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
            return (r.Length > 0 ? char.ToUpper(r[0]) + r.Substring(1) : r).Replace("_", " ").Trim();
        }

        private void DrawFieldEditor(Object comp, FieldInfo field)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(PrettifyName(field.Name), GUILayout.Width(200));
            object value = field.GetValue(comp);
            Type t = field.FieldType;
            try
            {
                if (t == typeof(float))
                {
                    string input = GUILayout.TextField(((float)value).ToString("G9"), GUILayout.Width(200));
                    if (float.TryParse(input, out float result) && result != (float)value)
                    {
                        field.SetValue(comp, result);
                        if (comp is Component c) ApplyToInstances(c.gameObject, c.GetType(), field, result);
                        if ((field.Name.IndexOf("yield", StringComparison.OrdinalIgnoreCase) >= 0 ||
                             field.Name.IndexOf("damage", StringComparison.OrdinalIgnoreCase) >= 0) &&
                            result >= LustDaddyPlugin.NuclearYieldThreshold.Value)
                            if (comp is Component c2) AutomateNuclearSwap(c2);
                    }
                }
                else if (t == typeof(int))
                {
                    string input = GUILayout.TextField(value.ToString(), GUILayout.Width(200));
                    if (int.TryParse(input, out int result) && result != (int)value)
                    {
                        field.SetValue(comp, result);
                        if (comp is Component c) ApplyToInstances(c.gameObject, c.GetType(), field, result);
                    }
                }
                else if (t == typeof(bool))
                {
                    bool bv = (bool)value;
                    bool nb = GUILayout.Toggle(bv, bv ? "True" : "False", GUILayout.Width(100));
                    if (nb != bv)
                    {
                        field.SetValue(comp, nb);
                        if (comp is Component c) ApplyToInstances(c.gameObject, c.GetType(), field, nb);
                    }
                }
                else if (t == typeof(string))
                {
                    string sv = (string)value ?? "";
                    string nv = GUILayout.TextField(sv, GUILayout.Width(250));
                    if (nv != sv)
                    {
                        field.SetValue(comp, nv);
                        if (comp is Component c) ApplyToInstances(c.gameObject, c.GetType(), field, nv);
                    }
                }
                else if (t.IsEnum)
                {
                    string sv = value.ToString();
                    string nv = GUILayout.TextField(sv, GUILayout.Width(200));
                    if (nv != sv)
                    {
                        try
                        {
                            object ev = Enum.Parse(t, nv, true);
                            field.SetValue(comp, ev);
                            if (comp is Component c) ApplyToInstances(c.gameObject, c.GetType(), field, ev);
                        }
                        catch { }
                    }
                    GUILayout.Label($"(Enum: {string.Join(", ", Enum.GetNames(t))})");
                }
                else if (t == typeof(GameObject))
                {
                    var go   = (GameObject)value;
                    string gn = go != null ? go.name : "None";
                    string nn = GUILayout.TextField(gn, GUILayout.Width(200));
                    if (nn != gn)
                    {
                        _allPrefabsCache.TryGetValue(nn, out var ng);
                        var nv = (nn == "None" || string.IsNullOrEmpty(nn)) ? null : ng;
                        field.SetValue(comp, nv);
                        if (comp is Component c) ApplyToInstances(c.gameObject, c.GetType(), field, nv);
                    }
                    GUILayout.Label("(Type exact prefab name)");
                }
                else if (typeof(Object).IsAssignableFrom(t))
                {
                    var uObj    = (Object)value;
                    string objName = uObj != null ? uObj.name : "None";
                    if (t.Name == "WeaponInfo" || t.Name == "MissileDefinition" || t.Name == "WeaponStation")
                    {
                        GUILayout.BeginHorizontal();
                        int ci = _turretPrefabs.IndexOf(uObj);
                        if (GUILayout.Button("<", GUILayout.Width(20)) && _turretPrefabs.Count > 0)
                        {
                            ci = (ci <= 0 ? _turretPrefabs.Count : ci) - 1;
                            var nwi = _turretPrefabs[ci];
                            field.SetValue(comp, nwi);
                            if (comp is Component c) ApplyToInstances(c.gameObject, c.GetType(), field, nwi);
                        }
                        GUILayout.Label(objName, GUILayout.Width(150));
                        if (GUILayout.Button(">", GUILayout.Width(20)) && _turretPrefabs.Count > 0)
                        {
                            ci = (ci + 1) % _turretPrefabs.Count;
                            var nwi = _turretPrefabs[ci];
                            field.SetValue(comp, nwi);
                            if (comp is Component c) ApplyToInstances(c.gameObject, c.GetType(), field, nwi);
                        }
                        GUILayout.EndHorizontal();
                    }
                    else
                    {
                        GUILayout.Label(objName, GUILayout.Width(200));
                    }
                }
            }
            catch (Exception ex)
            {
                GUILayout.Label($"[Error: {ex.Message}]");
            }
            GUILayout.EndHorizontal();
        }

        private void ApplyToInstances(GameObject prefab, Type compType, FieldInfo field, object newValue)
        {
            foreach (var inst in FindObjectsOfType(compType))
            {
                var c = (Component)inst;
                if (c.gameObject.name.Replace("(Clone)", "").Trim() == prefab.name)
                    field.SetValue(c, newValue);
            }
        }
        #endregion

        // ════════════════════════════════════════════════════════════════════
        #region Nuclear Automation
        private void DiscoverNukePrefab()
        {
            if (_actualNukePrefab != null) return;
            if (!_allPrefabsCache.TryGetValue("NuclearBomb1_strategic", out var nukeBomb)) return;
            var warhead = nukeBomb.GetComponent("Warhead");
            if (warhead == null) return;
            var f = warhead.GetType().GetField("terrainEffect", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var fx = f?.GetValue(warhead) as GameObject;
            if (fx != null) { _actualNukePrefab = fx; Debug.Log("Discovered Nuke prefab: " + fx.name); }
        }

        private void AutomateNuclearSwap(Component comp)
        {
            DiscoverNukePrefab();
            GameObject nukeGo = _actualNukePrefab;
            if (nukeGo == null) _allPrefabsCache.TryGetValue(LustDaddyPlugin.NuclearExplosionPrefab.Value, out nukeGo);
            if (nukeGo == null) return;

            object warhead = comp.GetType().GetField("warhead", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(comp);
            if (warhead == null)
            {
                var missile = comp.gameObject.GetComponent("Missile");
                if (missile != null)
                    warhead = missile.GetType().GetField("warhead", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(missile);
            }
            if (warhead == null) return;

            var fields = warhead.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => f.FieldType == typeof(GameObject) &&
                    (f.Name.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     f.Name.IndexOf("explosion", StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            foreach (var f in fields) f.SetValue(warhead, nukeGo);

            foreach (Component ac in Resources.FindObjectsOfTypeAll(comp.GetType()))
            {
                if (ac.name.Replace("(Clone)", "") != comp.name.Replace("(Clone)", "")) continue;
                object aw = ac.GetType().GetField("warhead", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(ac);
                if (aw != null) foreach (var f in fields) f.SetValue(aw, nukeGo);
            }
        }
        #endregion

        // ════════════════════════════════════════════════════════════════════
        #region Turret Wiring
        private bool IsTypeOrSubclass(Type type, string name)
        {
            while (type != null && type != typeof(object)) { if (type.Name == name) return true; type = type.BaseType; }
            return false;
        }

        private void SetTurretUnitReferences(GameObject turretGO, Component parentUnit)
        {
            if (parentUnit == null) return;
            Rigidbody rb = parentUnit.GetComponent<Rigidbody>();
            foreach (var part in turretGO.GetComponentsInChildren<Component>(true))
            {
                if (part == null) continue;
                var type = part.GetType();
                if (IsTypeOrSubclass(type, "UnitPart"))
                    SetMemberValue(part, "parentUnit", parentUnit);
                else if (IsTypeOrSubclass(type, "Turret") || IsTypeOrSubclass(type, "TargetDetector") ||
                         IsTypeOrSubclass(type, "MissileLauncher") || IsTypeOrSubclass(type, "RadarJammer") ||
                         IsTypeOrSubclass(type, "WeaponStation"))
                    SetMemberValue(part, "attachedUnit", parentUnit);
                else if (IsTypeOrSubclass(type, "Gun") && rb != null)
                    SetMemberValue(part, "velocityInherit", rb);
            }
        }
        #endregion
    }

    [HarmonyPatch]
    public static class SetGlobalParticles_Start_Patch
    {
        [HarmonyTargetMethod]
        static MethodBase TargetMethod()
        {
            var type = Type.GetType("SetGlobalParticles, Assembly-CSharp");
            return type?.GetMethod("Start", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        [HarmonyFinalizer]
        static Exception Finalizer(Exception __exception)
        {
            if (__exception != null)
            {
                // Suppress the exception so it doesn't break unit spawning
                return null;
            }
            return null;
        }
    }
}
