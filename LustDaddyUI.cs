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
    public class LustDaddyUI : MonoBehaviour
    {
        public class CustomField
        {
            public string Name;
            public string DisplayName;
            public Type FieldType;

            public object GetValue(object comp) => GetDeepFieldValue(comp, Name);
            public void SetValue(object comp, object value) => LustDaddyStartup.ApplyDeepFieldMod(comp, Name, value.ToString());
        }

        public static object GetDeepFieldValue(object rootTarget, string path)
        {
            var parts = path.Split('.');
            object current = rootTarget;
            
            for (int i = 0; i < parts.Length; i++)
            {
                if (current == null) return null;
                string part = parts[i].Trim();
                int bracketIndex = part.IndexOf('[');
                string memberName = bracketIndex >= 0 ? part.Substring(0, bracketIndex).Trim() : part;
                int? index = null;
                if (bracketIndex >= 0)
                {
                    int bracketEnd = part.IndexOf(']', bracketIndex);
                    if (bracketEnd >= 0)
                        index = int.Parse(part.Substring(bracketIndex + 1, bracketEnd - bracketIndex - 1).Trim());
                }

                Type t = current.GetType();
                MemberInfo member = null;
                while (t != null && t != typeof(object))
                {
                    member = t.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (member == null) member = t.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (member != null) break;
                    t = t.BaseType;
                }

                if (member == null) return null;

                object memberValue = null;
                if (member is FieldInfo fi) memberValue = fi.GetValue(current);
                else if (member is PropertyInfo pi) memberValue = pi.GetValue(current, null);

                if (index.HasValue && memberValue != null)
                {
                    if (memberValue is Array arr && index.Value < arr.Length) current = arr.GetValue(index.Value);
                    else if (memberValue is System.Collections.IList list && index.Value < list.Count) current = list[index.Value];
                    else return null;
                }
                else
                {
                    current = memberValue;
                }
            }
            return current;
        }

        private bool _showWindow = false;
        private Rect _windowRect = new Rect(100, 100, 800, 650);
        private Vector2 _scrollPosition;

        private enum Tab { Payloads, Turrets, Units, Aircraft, Hardpoints }
        private Tab _currentTab = Tab.Payloads;

        private enum GuiMode { Lite, Full }
        private GuiMode _guiMode = GuiMode.Lite;

        private static readonly string[] LiteFieldNames = { "ammo", "maxammo", "fuellevel", "hitpoints", "hp", "rcs" };

        private static bool IsLiteField(string fieldName)
        {
            string lower = fieldName.ToLowerInvariant();
            if (Array.IndexOf(LiteFieldNames, lower) >= 0) return true;
            return lower.EndsWith("capacity") || lower.EndsWith("count");
        }

        private List<Object> _payloadPrefabs         = new List<Object>();
        private List<Object> _turretPrefabs          = new List<Object>();
        private List<Object> _unitPrefabs            = new List<Object>();
        private List<Object> _aircraftPrefabs        = new List<Object>();
        private List<Object> _hardpointPrefabs       = new List<Object>();
        private List<GameObject> _sourceTurretGOs    = new List<GameObject>();
        private Dictionary<string, GameObject> _allPrefabsCache = new Dictionary<string, GameObject>();
        private bool _scanned = false;
        private GameObject _actualNukePrefab = null;

        private int _selectedPayloadIndex = -1;
        private int _selectedTurretIndex  = -1;
        private int _selectedUnitIndex    = -1;
        private int _selectedAircraftIndex = -1;
        private int _selectedHardpointIndex = -1;

        private string _payloadSearch = "";
        private string _turretListSearch = "";
        private string _unitSearch = "";
        private string _aircraftSearch = "";
        private string _hardpointSearch = "";
        private Dictionary<string, string> _unitSlotSearch = new Dictionary<string, string>();
        private Dictionary<string, string> _stationSwapSearch = new Dictionary<string, string>();

        private List<Object> _selectedComponents = new List<Object>();
        private Dictionary<Object, List<CustomField>> _editableFields = new Dictionary<Object, List<CustomField>>();
        private Dictionary<Object, Dictionary<string, object>> _originalValues = new Dictionary<Object, Dictionary<string, object>>();
        private bool _needsRefreshSelected = false;

        private Dictionary<int, int> _stationSwapIndices = new Dictionary<int, int>();

        private List<GameObject> _unitTurretSlots = new List<GameObject>();
        private int _unitTurretSlotIndex  = 0;
        private int _unitTurretSwapIndex  = 0;
        private Dictionary<string, int> _unitSlotAssignments = new Dictionary<string, int>();

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

        #region Window
        private void WindowFunction(int windowID)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Payloads", _currentTab == Tab.Payloads ? SelectedStyle() : GUI.skin.button)) SwitchTab(Tab.Payloads);
            if (GUILayout.Button("Turrets",  _currentTab == Tab.Turrets  ? SelectedStyle() : GUI.skin.button)) SwitchTab(Tab.Turrets);
            if (GUILayout.Button("Units",    _currentTab == Tab.Units    ? SelectedStyle() : GUI.skin.button)) SwitchTab(Tab.Units);
            if (GUILayout.Button("Aircraft", _currentTab == Tab.Aircraft ? SelectedStyle() : GUI.skin.button)) SwitchTab(Tab.Aircraft);
            if (GUILayout.Button("Hardpoints", _currentTab == Tab.Hardpoints ? SelectedStyle() : GUI.skin.button)) SwitchTab(Tab.Hardpoints);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Force Rescan")) { ScanPrefabs(); UpdateSelectedObject(); }
            string modeLabel = _guiMode == GuiMode.Lite ? "Mode: Lite (simple stats)" : "Mode: Full (all fields)";
            if (GUILayout.Button(modeLabel, GUILayout.Width(220)))
            {
                _guiMode = _guiMode == GuiMode.Lite ? GuiMode.Full : GuiMode.Lite;
                UpdateSelectedObject();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10);

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);

            switch (_currentTab)
            {
                case Tab.Payloads: DrawDropdown(ref _selectedPayloadIndex, _payloadPrefabs, ref _payloadSearch); break;
                case Tab.Turrets:  DrawSourceTurretDropdown(); break;
                case Tab.Units:    DrawDropdown(ref _selectedUnitIndex,    _unitPrefabs, ref _unitSearch);    break;
                case Tab.Aircraft: DrawDropdown(ref _selectedAircraftIndex, _aircraftPrefabs, ref _aircraftSearch); break;
                case Tab.Hardpoints: DrawDropdown(ref _selectedHardpointIndex, _hardpointPrefabs, ref _hardpointSearch); break;
            }

            GUILayout.Space(10);

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

        #region Scanning
        private void ScanPrefabs()
        {
            _payloadPrefabs.Clear();
            _turretPrefabs.Clear();
            _unitPrefabs.Clear();
            _aircraftPrefabs.Clear();
            _hardpointPrefabs.Clear();
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
                bool isAircraft = go.GetComponent("Aircraft") != null;
                bool isUnit = go.transform.parent == null && go.GetComponent("Unit") != null && !isPayload && !isAircraft;

                if (isPayload) _payloadPrefabs.Add(go);
                if (isUnit)    _unitPrefabs.Add(go);
                if (isAircraft) _aircraftPrefabs.Add(go);

                if (!isPayload && !isAircraft && !isUnit && IsHardpointEquipmentName(go.name))
                    _hardpointPrefabs.Add(go);
            }

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

            Type wiType = Type.GetType("WeaponInfo, Assembly-CSharp");
            if (wiType != null)
                foreach (var w in Resources.FindObjectsOfTypeAll(wiType))
                    if (w != null && !_turretPrefabs.Contains(w))
                        _turretPrefabs.Add(w);

            DiscoverNukePrefab();

            _payloadPrefabs.Sort((a, b) => a.name.CompareTo(b.name));
            _turretPrefabs.Sort((a, b) => a.name.CompareTo(b.name));
            _unitPrefabs.Sort((a, b) => a.name.CompareTo(b.name));
            _aircraftPrefabs.Sort((a, b) => a.name.CompareTo(b.name));
            _hardpointPrefabs.Sort((a, b) => a.name.CompareTo(b.name));
            _sourceTurretGOs.Sort((a, b) => a.name.CompareTo(b.name));
            _scanned = true;
        }

        private static readonly string[] HardpointEquipmentNamePatterns =
            { "radome", "pod", "jammer", "flare", "chaff", "ecm" };

        private static bool IsHardpointEquipmentName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLowerInvariant();
            foreach (var pattern in HardpointEquipmentNamePatterns)
                if (lower.Contains(pattern)) return true;
            return false;
        }
        #endregion

        #region Turrets Tab Dropdown
        private void DrawSourceTurretDropdown()
        {
            if (_sourceTurretGOs.Count == 0) { GUILayout.Label("No turret prefabs found."); return; }
            GUILayout.Label("Select Physical Turret Prefab:");
            _turretListSearch = GUILayout.TextField(_turretListSearch);

            var filtered = new List<(string name, int origIndex)>();
            for (int i = 0; i < _sourceTurretGOs.Count; i++)
            {
                string path = GetGameObjectPath(_sourceTurretGOs[i]);
                if (string.IsNullOrEmpty(_turretListSearch) || path.IndexOf(_turretListSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                    filtered.Add((path, i));
            }

            int filteredSelected = filtered.FindIndex(f => f.origIndex == _selectedTurretIndex);
            int newFilteredIndex = GUILayout.SelectionGrid(filteredSelected, filtered.Select(f => f.name).ToArray(), 1);
            if (newFilteredIndex != filteredSelected && newFilteredIndex >= 0 && newFilteredIndex < filtered.Count)
            {
                _selectedTurretIndex = filtered[newFilteredIndex].origIndex;
                _stationSwapIndices.Clear();
                UpdateSelectedObject();
            }
        }
        #endregion

        #region Selection & Dropdown
        private Object GetCurrentSelectedObject()
        {
            if (_currentTab == Tab.Payloads && _selectedPayloadIndex >= 0 && _selectedPayloadIndex < _payloadPrefabs.Count)
                return _payloadPrefabs[_selectedPayloadIndex];
            if (_currentTab == Tab.Turrets && _selectedTurretIndex >= 0 && _selectedTurretIndex < _sourceTurretGOs.Count)
                return _sourceTurretGOs[_selectedTurretIndex];
            if (_currentTab == Tab.Units && _selectedUnitIndex >= 0 && _selectedUnitIndex < _unitPrefabs.Count)
                return _unitPrefabs[_selectedUnitIndex];
            if (_currentTab == Tab.Aircraft && _selectedAircraftIndex >= 0 && _selectedAircraftIndex < _aircraftPrefabs.Count)
                return _aircraftPrefabs[_selectedAircraftIndex];
            if (_currentTab == Tab.Hardpoints && _selectedHardpointIndex >= 0 && _selectedHardpointIndex < _hardpointPrefabs.Count)
                return _hardpointPrefabs[_selectedHardpointIndex];
            return null;
        }

        private void DrawDropdown(ref int selectedIndex, List<Object> list, ref string searchFilter)
        {
            if (list.Count == 0) { GUILayout.Label("No items found in this category."); return; }
            GUILayout.Label("Select Entity:");
            searchFilter = GUILayout.TextField(searchFilter ?? "");

            var filtered = new List<(string name, int origIndex)>();
            for (int i = 0; i < list.Count; i++)
            {
                string display = GetPrefabDisplayName(list[i]);
                if (string.IsNullOrEmpty(searchFilter) || display.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    filtered.Add((display, i));
            }

            int currentSelectedIndex = selectedIndex;
            int filteredSelected = filtered.FindIndex(f => f.origIndex == currentSelectedIndex);
            int newFilteredIndex = GUILayout.SelectionGrid(filteredSelected, filtered.Select(f => f.name).ToArray(), 1);
            if (newFilteredIndex != filteredSelected && newFilteredIndex >= 0 && newFilteredIndex < filtered.Count)
            {
                selectedIndex = filtered[newFilteredIndex].origIndex;
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
                var fields = GetEditableFields(scriptable);
                if (fields.Count > 0)
                {
                    _selectedComponents.Add(scriptable);
                    _editableFields[scriptable] = fields;
                    CacheOriginalValues(scriptable, fields);
                }
                return;
            }

            if (!(selected is GameObject go)) return;

            var all = go.GetComponents<Component>().ToList();
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c.gameObject == go) continue;
                all.Add(c);
            }

            foreach (var comp in all)
            {
                if (comp == null || comp is Transform) continue;
                var t = comp.GetType();
                if (t.Namespace != null && t.Namespace.StartsWith("UnityEngine")) continue;

                var wsField = GetFieldRecursively(t, "weaponStations");
                var fields  = GetEditableFields(comp);
                if (fields.Count > 0 || wsField != null)
                {
                    _selectedComponents.Add(comp);
                    _editableFields[comp] = fields;
                    CacheOriginalValues(comp, fields);
                }
            }

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
                                break;
                            }
                        }
                    }
                if (_unitTurretSlots.Count == 0)
                {
                    var seen2 = new HashSet<GameObject>();
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

        private List<CustomField> GetEditableFields(Object comp)
        {
            var result = new List<CustomField>();
            ExtractFields(comp, comp, "", result);
            if (_guiMode == GuiMode.Lite)
                result = result.Where(f => IsLiteField(f.Name)).ToList();
            return result;
        }

        private void ExtractFields(object rootObj, object targetObj, string prefix, List<CustomField> result)
        {
            if (targetObj == null) return;
            Type t = targetObj.GetType();

            var fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(f => !f.IsInitOnly && !f.IsLiteral)
                .Where(f => f.IsPublic || f.GetCustomAttribute<SerializeField>() != null);

            foreach (var f in fields)
            {
                string path = string.IsNullOrEmpty(prefix) ? f.Name : prefix + "." + f.Name;
                Type ft = f.FieldType;
                object val = null;
                try { val = f.GetValue(targetObj); } catch { continue; }

                if (IsSupportedType(ft))
                {
                    result.Add(new CustomField { Name = path, DisplayName = f.Name, FieldType = ft });
                }
                else if (ft.IsArray || typeof(System.Collections.IList).IsAssignableFrom(ft))
                {
                    if (val is System.Collections.IList list)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            object elem = list[i];
                            if (elem != null)
                            {
                                string elemPath = path + "[" + i + "]";
                                ExtractFields(rootObj, elem, elemPath, result);
                            }
                        }
                    }
                }
                else if (ft.IsValueType || ft.IsClass)
                {
                    if (ft.GetCustomAttribute<SerializableAttribute>() != null || ft.Name.Contains("Params") || ft.Name.Contains("Parameters") || ft == typeof(AnimationCurve))
                    {
                        if (val != null)
                        {
                            if (ft == typeof(AnimationCurve))
                            {
                                var keysProp = ft.GetProperty("keys");
                                if (keysProp != null)
                                {
                                    var keys = keysProp.GetValue(val, null) as Array;
                                    if (keys != null)
                                    {
                                        for (int i = 0; i < keys.Length; i++)
                                        {
                                            object keyElem = keys.GetValue(i);
                                            string elemPath = path + ".keys[" + i + "]";
                                            ExtractProperties(rootObj, keyElem, elemPath, result);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                ExtractFields(rootObj, val, path, result);
                            }
                        }
                    }
                }
            }
        }

        private void ExtractProperties(object rootObj, object targetObj, string prefix, List<CustomField> result)
        {
            if (targetObj == null) return;
            Type t = targetObj.GetType();

            var props = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite);

            foreach (var p in props)
            {
                string path = string.IsNullOrEmpty(prefix) ? p.Name : prefix + "." + p.Name;
                Type pt = p.PropertyType;

                if (IsSupportedType(pt))
                {
                    result.Add(new CustomField { Name = path, DisplayName = p.Name, FieldType = pt });
                }
            }
        }

        private void CacheOriginalValues(Object obj, List<CustomField> fields)
        {
            if (!_originalValues.ContainsKey(obj)) _originalValues[obj] = new Dictionary<string, object>();
            var orig = _originalValues[obj];
            foreach (var f in fields)
            {
                if (!orig.ContainsKey(f.Name))
                {
                    try { orig[f.Name] = f.GetValue(obj); } catch { }
                }
            }
        }

        private bool IsSupportedType(Type t) =>
            t == typeof(float) || t == typeof(int) || t == typeof(bool) || t == typeof(string) ||
            t == typeof(Vector3) || t.IsEnum || typeof(Object).IsAssignableFrom(t);
        #endregion

        #region Component Editors
        private void DrawComponentEditors()
        {
            var selected = GetCurrentSelectedObject();
            if (selected == null) { GUILayout.Label("Select an item to edit its properties."); return; }

            GUILayout.Label($"Editing: {selected.name}", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold });

             DrawConfigUI(selected as GameObject);

            var snapshot = _selectedComponents.ToList();

            foreach (var comp in snapshot)
            {
                if (comp == null) continue;
                var fields = _editableFields.ContainsKey(comp) ? _editableFields[comp] : new List<CustomField>();

                string compName = comp.GetType().Name;
                if (comp is Object o) compName += $" ({o.name})";
                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"--- {compName} ---",
                    new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = Color.cyan } });
                if (GUILayout.Button("Reset", GUILayout.Width(60)))
                {
                    if (_originalValues.TryGetValue(comp, out var origs))
                    {
                        foreach(var f in fields) {
                            if (origs.TryGetValue(f.Name, out var ov)) {
                                f.SetValue(comp, ov);
                                if (comp is Component c) ApplyToInstances(c.gameObject, c.GetType(), f, ov);
                            }
                        }
                    }
                }
                GUILayout.EndHorizontal();

                string lastHeader = "";
                foreach (var f in fields)
                {
                    string currentHeader = "";
                    int dotIndex = f.Name.LastIndexOf('.');
                    if (dotIndex > 0)
                    {
                        currentHeader = f.Name.Substring(0, dotIndex);
                    }
                    
                    if (currentHeader != lastHeader)
                    {
                        if (!string.IsNullOrEmpty(currentHeader))
                        {
                            GUILayout.Space(5);
                            GUILayout.Label($"  └─ {currentHeader}", new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Italic, normal = { textColor = new Color(0.8f, 0.8f, 0.8f) } });
                        }
                        else
                        {
                            GUILayout.Space(5);
                        }
                        lastHeader = currentHeader;
                    }
                    
                    DrawFieldEditor(comp, f);
                }

                if (_currentTab == Tab.Turrets)
                    DrawWeaponStations(comp);

                if (_needsRefreshSelected) break;
            }
        }
        #endregion

        #region Config UI
        private void DrawConfigUI(GameObject rootGO)
        {
            if (rootGO == null) return;

            GUILayout.Space(10);
            GUILayout.Label("─── Configuration (restart to apply) ───",
                new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = Color.yellow } });

            string configPath = Path.Combine(LustDaddyStartup.ConfigDirectory, rootGO.name + ".json");
            bool hasConfig = File.Exists(configPath);
            if (hasConfig)
                GUILayout.Label("  \u2713 Config saved \u2014 restart game to apply",
                    new GUIStyle(GUI.skin.label) { normal = { textColor = Color.green } });

            if (_currentTab == Tab.Units)
            {
                _unitTurretSlots.RemoveAll(g => g == null);
                _sourceTurretGOs.RemoveAll(g => g == null);
                if (_unitTurretSlotIndex >= _unitTurretSlots.Count)
                    _unitTurretSlotIndex = Mathf.Max(0, _unitTurretSlots.Count - 1);

                if (_unitTurretSlots.Count == 0)
                {
                    GUILayout.Label("  No child turrets found on this unit.",
                        new GUIStyle(GUI.skin.label) { normal = { textColor = Color.grey } });
                }
                else
                {
                    bool isShip = rootGO.GetComponent("Ship") != null;
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
                        _unitSlotAssignments[slotName] = DrawCyclerWithSearch("", cur, sourceNames, 400, Color.cyan, _unitSlotSearch, slotName);
                        GUILayout.EndHorizontal();
                    }
                }
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Save Config (restart to apply)", GUILayout.Height(28)))
                SaveConfig(rootGO);

            if (hasConfig && GUILayout.Button("Clear Config"))
            {
                File.Delete(configPath);
                Debug.Log($"[LustDaddy] Cleared config for '{rootGO.name}'");
            }
        }

        private void SaveConfig(GameObject rootGO)
        {
            var config = new UnitModConfig { unitId = rootGO.name };

            if (_currentTab == Tab.Units)
            {
                bool isShip = rootGO.GetComponent("Ship") != null;
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
            }

            if (_currentTab == Tab.Turrets)
            {
                var validSources = _sourceTurretGOs;
                int stationUiIndex = 0;
                foreach (var comp in _selectedComponents)
                {
                    var wsField = GetFieldRecursively(comp.GetType(), "weaponStations");
                    if (wsField == null) continue;
                    var stations = wsField.GetValue(comp) as System.Collections.IList;
                    if (stations == null) continue;
                    string compName = comp.GetType().Name;
                    
                    for (int i = 0; i < stations.Count; i++)
                    {
                        if (_stationSwapIndices.ContainsKey(stationUiIndex))
                        {
                            int newIdx = _stationSwapIndices[stationUiIndex];
                            if (newIdx >= 0 && newIdx < validSources.Count)
                            {
                                config.stationSwaps.Add(new WeaponStationSwapEntry
                                {
                                    componentType = compName,
                                    stationIndex = i,
                                    replacement = GetGameObjectPath(validSources[newIdx])
                                });
                            }
                        }
                        stationUiIndex++;
                    }
                }
            }

            foreach (var comp in _selectedComponents)
            {
                if (comp == null || !_editableFields.ContainsKey(comp) || !_originalValues.ContainsKey(comp)) continue;
                var compName = comp.GetType().Name;
                foreach (var field in _editableFields[comp])
                {
                    if (field.FieldType != typeof(float) && field.FieldType != typeof(int) && 
                        field.FieldType != typeof(bool) && field.FieldType != typeof(string) && !field.FieldType.IsEnum) continue;
                        
                    if (!_originalValues[comp].ContainsKey(field.Name)) continue;
                    object origVal = _originalValues[comp][field.Name];
                    object curVal = field.GetValue(comp);
                    if (curVal != null && !curVal.Equals(origVal))
                    {
                        config.fieldMods.Add(new FieldModEntry
                        {
                            componentType = compName,
                            gameObjectName = comp is Component c2 ? c2.gameObject.name : comp.name,
                            fieldName = field.Name,
                            valueString = curVal.ToString()
                        });
                    }
                }
            }

            if (config.turretSwaps.Count == 0 && config.fieldMods.Count == 0 && config.stationSwaps.Count == 0) { Debug.Log("[LustDaddy] Nothing to save."); return; }
            Directory.CreateDirectory(LustDaddyStartup.ConfigDirectory);
            string json = config.ToJson();
            File.WriteAllText(Path.Combine(LustDaddyStartup.ConfigDirectory, rootGO.name + ".json"), json);
            Debug.Log($"[LustDaddy] Config saved for '{rootGO.name}':\n{json}");
        }
        #endregion

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

            int stationUiIndex = 0;
            foreach(var c in _selectedComponents)
            {
                if (c == comp) break;
                var cwf = GetFieldRecursively(c.GetType(), "weaponStations");
                if (cwf != null && cwf.GetValue(c) is System.Collections.IList cl) stationUiIndex += cl.Count;
            }

            for (int i = 0; i < stations.Count; i++)
            {
                var station = stations[i];
                if (station == null) { stationUiIndex++; continue; }

                string currentPhysical = GetPhysicalTurretName(station);

                if (!_stationSwapIndices.ContainsKey(stationUiIndex))
                {
                    int matchIdx = _sourceTurretGOs.FindIndex(t => t.name == currentPhysical);
                    _stationSwapIndices[stationUiIndex] = matchIdx >= 0 ? matchIdx : 0;
                }

                GUILayout.Space(4);
                int oldIdx = _stationSwapIndices[stationUiIndex];
                int newIdx = DrawCyclerWithSearch($"Station {i} Turret:", oldIdx, prefabNames, 200, Color.cyan, _stationSwapSearch, stationUiIndex.ToString());
                _stationSwapIndices[stationUiIndex] = newIdx;

                if (newIdx != oldIdx && prefabNames.Length > 0)
                {
                    SwapStationTurret(comp as Component, i, _sourceTurretGOs[newIdx]);
                    return;
                }

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

        private int DrawCyclerWithSearch(string label, int index, string[] options, int valueWidth, Color valueColor, IDictionary<string, string> searchStore, string searchKey)
        {
            if (!searchStore.TryGetValue(searchKey, out string search)) search = "";

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(130));
            string newSearch = GUILayout.TextField(search, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            if (newSearch != search && !string.IsNullOrEmpty(newSearch))
            {
                int matchIdx = Array.FindIndex(options, o => o.IndexOf(newSearch, StringComparison.OrdinalIgnoreCase) >= 0);
                if (matchIdx >= 0) index = matchIdx;
            }
            searchStore[searchKey] = newSearch;

            return DrawCycler(label, index, options, valueWidth, valueColor);
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

        public static void SwapStationTurretStatic(Component stationHolder, int stationIndex, GameObject newTurretPrefab)
        {
            if (stationHolder == null || newTurretPrefab == null) return;

            var wsField = GetFieldRecursivelyStatic(stationHolder.GetType(), "weaponStations");
            if (wsField == null) return;
            var stations = wsField.GetValue(stationHolder) as System.Collections.IList;
            if (stations == null || stationIndex >= stations.Count) return;
            var station = stations[stationIndex];
            if (station == null) return;

            GameObject oldGO = null;
            var tList = GetMemberValueStatic(station, "Turrets") as System.Collections.IList;
            var wList = GetMemberValueStatic(station, "Weapons") as System.Collections.IList;

            if (tList != null && tList.Count > 0 && tList[0] is Component tComp && tComp != null)
                oldGO = tComp.gameObject;
            else if (wList != null && wList.Count > 0 && wList[0] is Component wComp && wComp != null)
            {
                Type tt = Type.GetType("Turret, Assembly-CSharp");
                var pt = (tt != null ? wComp.GetComponentInParent(tt) : null) as Component;
                oldGO = pt != null ? pt.gameObject : wComp.gameObject;
            }

            if (oldGO == null) return;

            Transform parent = oldGO.transform.parent;
            GameObject newGO = GameObject.Instantiate(newTurretPrefab, parent);
            newGO.name = newTurretPrefab.name;
            newGO.transform.localPosition = oldGO.transform.localPosition;
            newGO.transform.localRotation = oldGO.transform.localRotation;
            newGO.transform.localScale    = oldGO.transform.localScale;

            Component unitComp = TryFindUnitStatic(stationHolder);
            if (unitComp != null) SetTurretUnitReferencesStatic(newGO, unitComp);

            var newTurrets = GetComponentsByNameStatic(newGO, "Turret");
            var newGuns    = GetComponentsByNameStatic(newGO, "Gun");
            var newLaunch  = GetComponentsByNameStatic(newGO, "MissileLauncher");
            var newWeapons = newGuns.Concat(newLaunch).ToList();

            ReplaceCollectionStatic(station, "Turrets", newTurrets);
            ReplaceCollectionStatic(station, "Weapons", newWeapons);

            Object newWi = null;
            foreach (var wc in newWeapons) { newWi = GetMemberValueStatic(wc, "WeaponInfo") as Object; if (newWi != null) break; }
            if (newWi == null)
                foreach (var tc in newTurrets) { newWi = GetMemberValueStatic(tc, "WeaponInfo") as Object; if (newWi != null) break; }

            SetMemberValueStatic(station, "WeaponInfo", newWi);
            foreach (var wc in newWeapons.Concat(newTurrets))
            {
                if (GetFieldRecursivelyStatic(wc.GetType(), "WeaponInfo") != null || GetPropertyRecursivelyStatic(wc.GetType(), "WeaponInfo") != null)
                    SetMemberValueStatic(wc, "WeaponInfo", newWi);
            }

            GameObject.DestroyImmediate(oldGO, true);
        }

        private void SwapStationTurret(Component stationHolder, int stationIndex, GameObject newTurretPrefab)
        {
            if (stationHolder == null || newTurretPrefab == null) return;
            SwapStationTurretStatic(stationHolder, stationIndex, newTurretPrefab);
            
            var wsField = GetFieldRecursivelyStatic(stationHolder.GetType(), "weaponStations");
            var stations = wsField?.GetValue(stationHolder) as System.Collections.IList;
            var station = stations != null && stationIndex < stations.Count ? stations[stationIndex] : null;
            Object newWi = station != null ? GetMemberValueStatic(station, "WeaponInfo") as Object : null;
            
            ApplySwapToInstances(stationHolder, stationIndex, newTurretPrefab);
            
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

        private Component TryFindUnit(GameObject unitGO)
        {
            if (unitGO == null) return null;
            Type unitType = Type.GetType("Unit, Assembly-CSharp");
            if (unitType == null) return null;
            var c = unitGO.GetComponent(unitType);
            if (c != null) return c as Component;
            foreach (var cc in unitGO.GetComponentsInChildren(unitType, true))
                if (cc != null) return cc as Component;
            return null;
        }

        private void ApplySwapToInstances(Component stationHolder, int stationIndex, GameObject newTurretPrefab)
        {
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

        #region Reflection Helpers
        private static FieldInfo GetFieldRecursivelyStatic(Type type, string fieldName)
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
        
        private FieldInfo GetFieldRecursively(Type type, string fieldName) => GetFieldRecursivelyStatic(type, fieldName);

        private static PropertyInfo GetPropertyRecursivelyStatic(Type type, string propName)
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
        
        private PropertyInfo GetPropertyRecursively(Type type, string propName) => GetPropertyRecursivelyStatic(type, propName);

        private static object GetMemberValueStatic(object obj, string name)
        {
            var f = GetFieldRecursivelyStatic(obj.GetType(), name);
            if (f != null) return f.GetValue(obj);
            var p = GetPropertyRecursivelyStatic(obj.GetType(), name);
            if (p != null && p.CanRead) return p.GetValue(obj);
            return null;
        }

        private object GetMemberValue(object obj, string name) => GetMemberValueStatic(obj, name);

        private static void SetMemberValueStatic(object obj, string name, object value)
        {
            var f = GetFieldRecursivelyStatic(obj.GetType(), name);
            if (f != null) { f.SetValue(obj, value); return; }
            var p = GetPropertyRecursivelyStatic(obj.GetType(), name);
            if (p != null && p.CanWrite) p.SetValue(obj, value);
        }

        private void SetMemberValue(object obj, string name, object value) => SetMemberValueStatic(obj, name, value);

        private static List<Component> GetComponentsByNameStatic(GameObject go, string typeName)
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

        private List<Component> GetComponentsByName(GameObject go, string typeName) => GetComponentsByNameStatic(go, typeName);

        private static void ReplaceCollectionStatic(object parent, string fieldName, List<Component> newItems)
        {
            var f = GetFieldRecursivelyStatic(parent.GetType(), fieldName);
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
        
        private void ReplaceCollection(object parent, string fieldName, List<Component> newItems) => ReplaceCollectionStatic(parent, fieldName, newItems);
        #endregion

        #region Field Editors
        private string PrettifyName(string name)
        {
            string r = Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
            return (r.Length > 0 ? char.ToUpper(r[0]) + r.Substring(1) : r).Replace("_", " ").Trim();
        }

        private void DrawFieldEditor(Object comp, CustomField field)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(PrettifyName(field.DisplayName), GUILayout.Width(200));
            object value = field.GetValue(comp);
            Type t = field.FieldType;
            try
            {
                if (t == typeof(float))
                {
                    string input = GUILayout.TextField(((float)value).ToString("G9", System.Globalization.CultureInfo.InvariantCulture), GUILayout.Width(200));
                    if (float.TryParse(input.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result) && result != (float)value)
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

        private void ApplyToInstances(GameObject prefab, Type compType, CustomField field, object newValue)
        {
            foreach (var inst in FindObjectsOfType(compType))
            {
                var c = (Component)inst;
                if (c.gameObject.name.Replace("(Clone)", "").Trim() == prefab.name)
                    field.SetValue(c, newValue);
            }
        }
        #endregion

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

        #region Turret Wiring
        private static bool IsTypeOrSubclassStatic(Type type, string name)
        {
            while (type != null && type != typeof(object)) { if (type.Name == name) return true; type = type.BaseType; }
            return false;
        }

        private static void SetTurretUnitReferencesStatic(GameObject turretGO, Component parentUnit)
        {
            if (parentUnit == null) return;
            Rigidbody rb = parentUnit.GetComponent<Rigidbody>();
            foreach (var part in turretGO.GetComponentsInChildren<Component>(true))
            {
                if (part == null) continue;
                var type = part.GetType();
                if (IsTypeOrSubclassStatic(type, "UnitPart"))
                    SetMemberValueStatic(part, "parentUnit", parentUnit);
                else if (IsTypeOrSubclassStatic(type, "Turret") || IsTypeOrSubclassStatic(type, "TargetDetector") ||
                         IsTypeOrSubclassStatic(type, "MissileLauncher") || IsTypeOrSubclassStatic(type, "RadarJammer") ||
                         IsTypeOrSubclassStatic(type, "WeaponStation"))
                    SetMemberValueStatic(part, "attachedUnit", parentUnit);
                else if (IsTypeOrSubclassStatic(type, "Gun") && rb != null)
                    SetMemberValueStatic(part, "velocityInherit", rb);

                if (IsTypeOrSubclassStatic(type, "Turret"))
                    TryAttachTurretToWeaponManager(part, parentUnit);
            }
        }

        public static void TryAttachTurretToWeaponManager(Component turretComp, Component unitComp)
        {
            if (turretComp == null || unitComp == null) return;
            try
            {
                var turretType = turretComp.GetType();
                MethodInfo attachMethod = null;
                var t = turretType;
                while (t != null && t != typeof(object))
                {
                    attachMethod = t.GetMethod("AttachToWeaponManager", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (attachMethod != null) break;
                    t = t.BaseType;
                }
                if (attachMethod == null) return;

                var pars = attachMethod.GetParameters();
                if (pars.Length != 1 || !pars[0].ParameterType.IsInstanceOfType(unitComp)) return;
                attachMethod.Invoke(turretComp, new object[] { unitComp });

                object acqMode = GetMemberValueStatic(turretComp, "targetAcquisitionMode");
                if (acqMode != null && acqMode.ToString() == "parentUnitTargetDetector")
                {
                    object radar = GetMemberValueStatic(unitComp, "radar");
                    if (radar != null)
                    {
                        var registerMethod = FindMethodRecursive(turretType, "RegisterTargetDetector");
                        registerMethod?.Invoke(turretComp, new object[] { radar });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LustDaddy] Failed to attach turret to weapon manager: {ex.Message}");
            }
        }

        private static MethodInfo FindMethodRecursive(Type type, string methodName)
        {
            while (type != null && type != typeof(object))
            {
                var m = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m != null) return m;
                type = type.BaseType;
            }
            return null;
        }
        
        private static Component TryFindUnitStatic(Component comp)
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
            return null;
        }
        #endregion
    }
}
