﻿using System;
using System.Reflection;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using UnityEngine;
using UnityEngine.Events;
using Oculus.Newtonsoft.Json;
using Oculus.Newtonsoft.Json.Linq;
using System.IO;

namespace ExtraOptions
{
    class Config
    {
        public float Murkiness = 100.0f;
        public int   TextureQuality = 3;
        public bool  Console = false;
        public bool  LightShafts = true;
        public bool  VariablePhysicsStep = true;
    }

    class Main
    {
        static HarmonyInstance harmony;
        static readonly string configPath = @"./QMods/ExtraOptions/config.json";
        public static Config currentConfig;
        static readonly string logPath = @"./QMods/ExtraOptions/ExtraOptions.log";
        static bool hasError = false;
        static bool hasShownError = false;

        public static void Patch()
        {
            File.WriteAllText(logPath, "");
            try
            {
                Info("Harmony? {0}", Assembly.GetAssembly(typeof(HarmonyInstance)).GetName().Version);
                Info("Unity? {0}", UnityEngine.Application.unityVersion);
                Info("Product? {0}-{1}", UnityEngine.Application.productName, UnityEngine.Application.version);
                Info("ExtraOptions-{0}", Assembly.GetExecutingAssembly().GetName().Version);

                LoadSettings();
                
                harmony = HarmonyInstance.Create("com.m22spencer.extraoptions");
                harmony.Patch(AccessTools.Method(typeof(MainMenuController), "Start")
                             , null
                             , new HarmonyMethod(typeof(Main).GetMethod(nameof(Reload))));
                // When a preset is selected, the texture quality is also set, reload settings here to override this
                harmony.Patch(AccessTools.Method(typeof(uGUI_OptionsPanel), "SyncQualityPresetSelection")
                             , null
                             , new HarmonyMethod(typeof(Main).GetMethod(nameof(Reload))));

                harmony.Patch(AccessTools.Method(typeof(uGUI_OptionsPanel), "AddGraphicsTab")
                             , new HarmonyMethod(typeof(Main).GetMethod(nameof(AddGraphicsTab_Prefix)))
                             , null);

                harmony.Patch(AccessTools.Method(typeof(WaterscapeVolume.Settings), "GetExtinctionAndScatteringCoefficients")
                             , new HarmonyMethod(typeof(Main).GetMethod(nameof(Patch_GetExtinctionAndScatteringCoefficients)))
                             , null);
            } catch(Exception e)
            {
                Error("Patching failed with: {0}\n", e);
            }
        }

        public static void Info(string fmt, params object[] items)
        {
            File.AppendAllText(logPath, "[INFO] " + string.Format(fmt, items) + "\n");
        }

        public static void Error(string fmt, params object[] items)
        {
            hasError = true;
            File.AppendAllText(logPath, "[ERROR] " + string.Format(fmt, items) + "\n");
        }

        public static void LoadSettings()
        {
            var json = File.Exists(configPath) ? File.ReadAllText(configPath) : "{}";
            currentConfig = JsonConvert.DeserializeObject<Config>(json);
        }

        public static void SaveSettings()
        {
            File.WriteAllText(configPath, JsonConvert.SerializeObject(currentConfig));
        }

        public static void TryAlertUser()
        {
            try
            {
                if (hasError && !hasShownError)
                {
                    hasShownError = true;
                    Subtitles.main.Add("ExtraOptions error, check QMods/ExtraOptions/ExtraOptions.log for more information");
                }
            }
            catch { }
        }

        public static void Reload()
        {
            TryAlertUser();   
            try
            {
                foreach (var w in GameObject.FindObjectsOfType<WaterBiomeManager>())
                    w.Rebuild();

                QualitySettings.masterTextureLimit = 4 - currentConfig.TextureQuality;

                DevConsole.disableConsole = !currentConfig.Console;

                foreach (var s in UnityEngine.Object.FindObjectsOfType<WaterSunShaftsOnCamera>())
                    s.enabled = currentConfig.LightShafts;

                Time.matchFixedTimeToDeltaTime = currentConfig.VariablePhysicsStep;
                if (!Time.matchFixedTimeToDeltaTime)
                {
                    Time.fixedDeltaTime = 0.02f;
                    Time.maximumDeltaTime = 0.33333f;
                    Time.maximumParticleDeltaTime = 0.03f;
                }

                SaveSettings();
            } catch(Exception e)
            {
                Error("Reload failed with: {0}\n", e);
            }
        }

        public static void AddGraphicsTab_Prefix(uGUI_OptionsPanel __instance)
        {
            var t = __instance;
            var idx = t.AddTab("ExtraOptions");

            t.AddSliderOption(idx, "Murkiness", currentConfig.Murkiness, 0, 200, 100, new UnityAction<float>(v => { currentConfig.Murkiness = v; Reload(); }));
            t.AddChoiceOption(idx, "Texture Quality", new int[] { 0, 1, 2, 3, 4 }, currentConfig.TextureQuality, new UnityAction<int>(v => { currentConfig.TextureQuality = v; Reload(); }));
            t.AddToggleOption(idx, "Console", currentConfig.Console, new UnityAction<bool>(v => { currentConfig.Console = v; Reload(); }));
            t.AddToggleOption(idx, "LightShaft", currentConfig.LightShafts, new UnityAction<bool>(v => { currentConfig.LightShafts = v; Reload(); }));
            t.AddToggleOption(idx, "Variable Physics Step", currentConfig.VariablePhysicsStep, new UnityAction<bool>(v => { currentConfig.VariablePhysicsStep = v; Reload(); }));
        }

        // Ref - https://forums.unknownworlds.com/discussion/154099/mod-pc-murky-waters-v2-with-dll-patcher-wip
        public static bool Patch_GetExtinctionAndScatteringCoefficients(WaterscapeVolume.Settings __instance, ref Vector4 __result)
        {
            var t = __instance;
            var m = 1.0f - Mathf.Clamp(currentConfig.Murkiness / 200.0f, 0.0f, 1.0f);
            var mv = m * 180.0f + 10.0f;
            float d = t.murkiness / mv;
            Vector3 vector = t.absorption + t.scattering * Vector3.one;
            __result = new Vector4(vector.x, vector.y, vector.z, t.scattering) * d;
            return false;
        }
    }
}
