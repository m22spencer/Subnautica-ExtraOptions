using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Harmony;
using UnityEngine;
using UnityEngine.Events;
using Oculus.Newtonsoft.Json;
using Oculus.Newtonsoft.Json.Linq;
using System.IO;
using UnityEngine;
using Harmony;
using BiomeSettings = WaterBiomeManager.BiomeSettings;

namespace ExtraOptions
{
    class Config
    {
        public float Murkiness = 100.0f;
        public int   TextureQuality = 3;
        public bool  Console = false;
        public bool  LightShafts = true;
        public bool  VariablePhysicsStep = true;
        public bool  FogFix = false;
    }

    class Main
    {
        static HarmonyInstance harmony;
        static readonly string configPath = @"./QMods/ExtraOptions/config.json";
        public static Config currentConfig;
        static readonly string logPath = @"./QMods/ExtraOptions/ExtraOptions.log";
        static readonly string themesPath = @"./QMods/ExtraOptions/theme.json";
        static bool hasError = false;
        static bool hasShownError = false;
        static bool canLog = false;

        public static void Patch()
        {
            try
            {
                File.WriteAllText(logPath, "");
                canLog = true;
            } catch {
                Debug.LogError("[ExtraOptions] Unable to write ExtraOptions logfile.");
            } // Possible permissions error, don't abort loading if we can't write the log file.

            try {
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

                harmony.Patch( AccessTools.Method(typeof(WaterscapeVolume), nameof(WaterscapeVolume.RenderImage))
                             , new HarmonyMethod(typeof(Main).GetMethod(nameof(Patch_RenderImage)))
                             , null);

                try { 
                harmony.Patch( AccessTools.Method(typeof(Player), "Update")
                             , new HarmonyMethod(typeof(Main).GetMethod(nameof(Patch_PlayerUpdate)))
                             , null);
                } catch (Exception e) {
                    Error("{0}", e);
                }
            } catch(Exception e)
            {
                Error("Patching failed with: {0}\n", e);
            }
        }

        public static void Info(string fmt, params object[] items)
        {
            var str = "[INFO] " + string.Format(fmt, items);
            if (canLog) File.AppendAllText(logPath, str + "\n");
            Debug.Log("[ExtraOptions] " + str);
        }

        public static void Error(string fmt, params object[] items)
        {
            hasError = true;
            var str = "[ERROR] " + string.Format(fmt, items);
            if (canLog) File.AppendAllText(logPath, str + "\n");
            Debug.LogError("[ExtraOptions] " + str);
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
                    var logpath = canLog ? "QMods/ExtraOptions/ExtraOptions.log" : "Subnautica_Data/output_log.txt";
                    Subtitles.main.Add($"ExtraOptions error, check {logpath} for more information");
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

        public static WaterBiomeManager wbm;
        public static BiomeSettings GetBiome(Player player = null) {
            var pos = (player ?? GameObject.FindObjectOfType<Player>())?.gameObject?.transform?.position;
            wbm = wbm ?? GameObject.FindObjectOfType<WaterBiomeManager>();

            if (null != pos && wbm) {
                return wbm.biomeSettings.FirstOrDefault(b => b.name == wbm.GetBiome(pos.Value));
            }
            return null;
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
            t.AddToggleOption(idx, "Fog \"Fix\"", currentConfig.FogFix, new UnityAction<bool>(v => { currentConfig.FogFix = v; Reload(); }));

            var biome = GetBiome();
            var name  = biome?.name ?? "???";
            t.AddHeading(idx, $"Biome Config ({name})");
            if (null != biome) {
                Dictionary<string, WaterscapeVolume.Settings> themes;
                if (File.Exists(themesPath)) themes = JsonConvert.DeserializeObject<Dictionary<string, WaterscapeVolume.Settings>>(File.ReadAllText(themesPath));
                else themes = new Dictionary<string, WaterscapeVolume.Settings>();

                if (themes.ContainsKey(name)) biome.settings = themes[name];
                else themes[name] = biome.settings;

                void Apply() {
                    Reload();
                    File.WriteAllText(themesPath, JsonConvert.SerializeObject(themes, Formatting.Indented, new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore }));
                }

                void AddVectorRawOption<T>(string fieldName, float min, float max, Func<T,Vector3> from, Func<Vector3,T> to) {
                    float scale = 255f / (max - min);
                    float nmin = min * scale;
                    float nmax = max * scale;
                    var fld = Traverse.Create(biome.settings).Field(fieldName);
                    Func<Vector3> get = () => from(fld.GetValue<T>()) * scale;
                    Action<Vector3> set = (iv) => fld.SetValue(to(iv / scale));
                    t.AddSliderOption(idx, $"{fieldName}.x/r", get().x, nmin, nmax, get().x, new UnityAction<float>(v => { set(new Vector3(v, get().y, get().z)); Apply(); }));
                    t.AddSliderOption(idx, $"{fieldName}.y/g", get().y, nmin, nmax, get().y, new UnityAction<float>(v => { set(new Vector3(get().x, v, get().z)); Apply(); }));
                    t.AddSliderOption(idx, $"{fieldName}.z/b", get().z, nmin, nmax, get().z, new UnityAction<float>(v => { set(new Vector3(get().x, get().y, v)); Apply(); }));
                }

                void Vector(string fieldName, float min, float max) {
                    AddVectorRawOption(fieldName, min, max, x => x, y => y);
                }

                void Color(string fieldName) {
                    AddVectorRawOption(fieldName, 0, 1, xx => new Vector3(xx.r, xx.g, xx.b), yy => new Color(yy.x, yy.y, yy.z));
                }

                void Range(string fieldName, float min, float max) {
                    float scale = 255f / (max - min);
                    float nmin = min * scale;
                    float nmax = max * scale;
                    var fld = Traverse.Create(biome.settings).Field(fieldName);
                    Func<float> get = () => fld.GetValue<float>() * scale;
                    Action<float> set = (iv) => fld.SetValue(iv / scale);
                    t.AddSliderOption(idx, $"{fieldName}.x/r", get(), nmin, nmax, get(), new UnityAction<float>(v => { set(v); Apply(); }));
                }

                Vector("absorption", 0, 200);
                Range("scattering", 0, 2);
                Color("scatteringColor");
                Range("murkiness", 0, 20);
                Color("emissive");
                Range("emissiveScale", 0, 1);
                Range("startDistance", 0, 100);
                Range("sunlightScale", 0, 1);
                Range("ambientScale", 0, 1);
            }
        }

        public static string inBiome;
        public static void Patch_PlayerUpdate(Player __instance) {
            try {
                var biome = GetBiome(__instance);
                if (biome != null && biome.name != inBiome) {
                    inBiome = biome.name;
                    var themes = JsonConvert.DeserializeObject<Dictionary<string, WaterscapeVolume.Settings>>(File.ReadAllText(themesPath));
                    if (themes.TryGetValue(biome.name, out var theme)) {
                        biome.settings = theme;
                        Reload();
                    }
                }
            } catch (Exception e) {
                Error("Player update issue: {0}", e);
            }
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

        public static void Patch_RenderImage(ref bool cameraInside) {
            if (currentConfig.FogFix)
                cameraInside = false;
        }
    }
}
