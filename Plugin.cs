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
    [BepInPlugin("com.lustdaddy", "LUST-DADDY", "1.2.0")]
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
                return null;
            }
            return null;
        }
    }
}
