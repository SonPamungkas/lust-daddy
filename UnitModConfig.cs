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
     public class TurretSwapEntry
     {
         public string slotName;
         public string replacement;
     }
 
     [System.Serializable]
     public class FieldModEntry
     {
         public string componentType;
         public string gameObjectName;
         public string fieldName;
         public string valueString;
     }

     [System.Serializable]
     public class WeaponStationSwapEntry
     {
         public string componentType;
         public int stationIndex;
         public string replacement;
     }

     [System.Serializable]
     public class UnitModConfig
     {
         public string unitId;
         public List<TurretSwapEntry> turretSwaps = new List<TurretSwapEntry>();
         public List<WeaponStationSwapEntry> stationSwaps = new List<WeaponStationSwapEntry>();
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
             sb.AppendLine("  \"stationSwaps\": [");
             for (int i = 0; i < stationSwaps.Count; i++)
             {
                 var e = stationSwaps[i];
                 sb.Append($"    {{ \"componentType\": \"{Esc(e.componentType)}\", \"stationIndex\": \"{e.stationIndex}\", \"replacement\": \"{Esc(e.replacement)}\" }}");
                 sb.AppendLine(i < stationSwaps.Count - 1 ? "," : "");
             }
             sb.AppendLine("  ],");
             sb.AppendLine("  \"fieldMods\": [");
             for (int i = 0; i < fieldMods.Count; i++)
             {
                 var f = fieldMods[i];
                 sb.Append($"    {{ \"componentType\": \"{Esc(f.componentType)}\", \"gameObjectName\": \"{Esc(f.gameObjectName)}\", \"fieldName\": \"{Esc(f.fieldName)}\", \"valueString\": \"{Esc(f.valueString)}\" }}");
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

             // Extract stationSwaps
             int stationSwapsStart = json.IndexOf("\"stationSwaps\"");
             if (stationSwapsStart >= 0)
             {
                 int braceDepth = 0; bool inArray = false;
                 var current = new System.Text.StringBuilder();
                 for (int i = stationSwapsStart; i < json.Length; i++)
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
                             string idxStr = ExtractString(obj, "stationIndex");
                             string repl = ExtractString(obj, "replacement");
                             if (!string.IsNullOrEmpty(ctype) && !string.IsNullOrEmpty(idxStr) && !string.IsNullOrEmpty(repl))
                             {
                                 if (int.TryParse(idxStr, out int idx))
                                     cfg.stationSwaps.Add(new WeaponStationSwapEntry { componentType = ctype, stationIndex = idx, replacement = repl });
                             }
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
                             string gname = ExtractString(obj, "gameObjectName");
                             string fname = ExtractString(obj, "fieldName");
                             string vstr = ExtractString(obj, "valueString");
                             if (!string.IsNullOrEmpty(ctype) && !string.IsNullOrEmpty(fname))
                                 cfg.fieldMods.Add(new FieldModEntry { componentType = ctype, gameObjectName = gname, fieldName = fname, valueString = vstr });
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
}
