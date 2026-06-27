using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Diagnostics;

namespace FocusShield.Core
{
    public class AppConfig
    {
        public bool IsFirstRun { get; set; } = true;
        public string Language { get; set; } = "zh-CN"; 
        public string GlowColor { get; set; } = "#FFFF0000"; 

        // [兼容性修复] 保留字段但设为过时，防止编译错误
        public bool IsActivated { get; set; } = true; 
        public string LicenseKey { get; set; } = ""; 

        // [兼容性保留] 旧版白名单字段，已不再参与判定逻辑
        public HashSet<string> Whitelist { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "WeChat", "Weixin", "Code", "Obsidian", "Devenv", "DingTalk", "SearchHost"
        };

        // [已废弃] 旧版 DCC 列表，仅用于配置迁移
        public HashSet<string> DccList { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // v3 黑名单：列表中的软件将强制英文输入法（语义更正：原来是"白名单"实际是锁定列表）
        public HashSet<string> Blacklist { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "blender", "maya", "3dsmax", "zbrush", "houdini", "cinema4d", "substancepainter"
        };

        // 深度心流规则
        public List<DeepFlowRule> DeepRules { get; set; } = new List<DeepFlowRule>();

        public HotkeySetting HotkeySwitch { get; set; } = new HotkeySetting { Modifier = 1, Key = 81, DisplayText = "Alt + Q" };
        public HotkeySetting HotkeySettings { get; set; } = new HotkeySetting { Modifier = 1, Key = 83, DisplayText = "Alt + S" };
    }

    public class DeepFlowRule
    {
        public string ProcessName { get; set; } = "";
        public string TitleRegex { get; set; } = ""; 
    }

    public class HotkeySetting
    {
        public uint Modifier { get; set; } 
        public uint Key { get; set; }      
        public string DisplayText { get; set; } = ""; 
    }

    public static class ConfigManager
    {
        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "focus_shield_config.json");

        public static AppConfig Current { get; private set; } = new AppConfig();

        public static void Load()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    if (config != null) Current = config;

                    // v3 迁移：旧 DccList → 新 Blacklist（向后兼容）
                    if (Current.Blacklist.Count == 0 && Current.DccList.Count > 0)
                    {
                        Current.Blacklist = new HashSet<string>(Current.DccList, StringComparer.OrdinalIgnoreCase);
                        Current.DccList.Clear();
                        Save(); // 立即持久化迁移结果
                    }
                }
                catch { }
            }
            else
            {
                Save();
            }
        }

        public static void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Current, options);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Save failed: " + ex.Message);
            }
        }
    }
}
