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
                                 if (string.IsNullOrEmpty(fieldMod.gameObjectName) || c.gameObject.name == fieldMod.gameObjectName)
                                 {
                                     targetComp = c;
                                     break;
                                 }
                             }
                         }
                         if (targetComp == null) continue;

                         var t = targetComp.GetType();
                         var parts = fieldMod.fieldName.Split('.');
                         FieldInfo f1 = null;
                         while (t != null && t != typeof(object))
                         {
                             f1 = t.GetField(parts[0], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                             if (f1 != null) break;
                             t = t.BaseType;
                         }

                         if (f1 != null)
                         {
                             try
                             {
                                 FieldInfo targetField = f1;
                                 object subObj = null;
                                 if (parts.Length == 2)
                                 {
                                     subObj = f1.GetValue(targetComp);
                                     if (subObj != null)
                                     {
                                         targetField = subObj.GetType().GetField(parts[1], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                     }
                                 }

                                 if (targetField != null)
                                 {
                                     object parsedVal = null;
                                     if (targetField.FieldType == typeof(float)) parsedVal = float.Parse(fieldMod.valueString.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
                                     else if (targetField.FieldType == typeof(int)) parsedVal = int.Parse(fieldMod.valueString);
                                     else if (targetField.FieldType == typeof(bool)) parsedVal = bool.Parse(fieldMod.valueString);
                                     else if (targetField.FieldType == typeof(string)) parsedVal = fieldMod.valueString;
                                     else if (targetField.FieldType.IsEnum) parsedVal = Enum.Parse(targetField.FieldType, fieldMod.valueString, true);
                                     
                                     if (parsedVal != null)
                                     {
                                         if (parts.Length == 1) f1.SetValue(targetComp, parsedVal);
                                         else if (parts.Length == 2)
                                         {
                                             targetField.SetValue(subObj, parsedVal);
                                             f1.SetValue(targetComp, subObj);
                                         }
                                     }
                                 }
                             }
                             catch (Exception ex) { Debug.LogWarning($"[LustDaddy] Failed to apply field mod {fieldMod.fieldName}: {ex.Message}"); }
                         }
                     }

                     // Apply station swaps on the prefab itself, so that instantiated clones get them automatically
                     foreach (var stationSwap in config.stationSwaps)
                     {
                         if (string.IsNullOrEmpty(stationSwap.replacement)) continue;
                         GameObject newTurretPrefab = null;
                         if (!TryGetPrefabByPath(stationSwap.replacement, out newTurretPrefab))
                         {
                             if (!allGOs.TryGetValue(stationSwap.replacement, out newTurretPrefab)) continue;
                         }

                         var comps = unitPrefab.GetComponentsInChildren<Component>(true);
                         Component targetComp = null;
                         foreach (var c in comps)
                         {
                             if (c != null && c.GetType().Name == stationSwap.componentType)
                             {
                                 targetComp = c;
                                 break;
                             }
                         }
                         if (targetComp == null) continue;

                         LustDaddyUI.SwapStationTurretStatic(targetComp, stationSwap.stationIndex, newTurretPrefab);
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

}
