using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using fastJSON;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
 
namespace ItemLink
{
    [BepInPlugin(GUID, NAME, VERSION)]
    [BepInDependency("org.bepinex.plugins.jewelcrafting", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInIncompatibility("redseiko.valheim.chatter")]
    public class ItemLink : BaseUnityPlugin 
    {
        private const string GUID = "kg.ItemLink";
        private const string NAME = "ItemLink";
        private const string VERSION = "1.1.0";

        private static ItemLink _thistype;
        private static AssetBundle asset;
        public static GameObject TooltipTrick;

        private void Awake()
        {
            JSON.Parameters = new JSONParameters
            {
                UseExtensions = false,
                SerializeNullValues = false,
                DateTimeMilliseconds = false,
                UseUTCDateTime = true,
                UseOptimizedDatasetSchema = true,
                UseValuesOfEnums = true,
            };
            _thistype = this;
            TooltipTrick = new GameObject("kg_ItemLink_TooltipTrick") { hideFlags = HideFlags.HideAndDontSave };
            TooltipTrick.AddComponent<UITooltip>(); 
            TooltipTrick.SetActive(false);
            asset = GetAssetBundle("kg_itemlink");
            ItemLinkProcessor.TooltipPrefab = asset.LoadAsset<GameObject>("kg_InventoryTooltipLink");
            new Harmony(GUID).PatchAll();
        }
        
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int))]
        public static class PatchTooltip
        {
            public static bool SkipPatch = false; 
            
            private static void Postfix(ItemDrop.ItemData item, bool crafting, ref string __result)
            {
                if (crafting || !Player.m_localPlayer || !Player.m_localPlayer.m_inventory.ContainsItem(item) || SkipPatch) return;
                __result += "\n\n<color=yellow>[L.Shift + L.Control]</color> Link in <color=#808080>General</color> chat" 
                            + "\n<color=yellow>[L.Shift + L.Alt]</color> Link in <color=yellow>Shout</color> chat";
            }
        }
        
        [HarmonyPatch(typeof(TextInput), nameof(TextInput.IsVisible))]
        private static class Menu_Patch
        {
            [UsedImplicitly]
            private static void Postfix(ref bool __result) => __result |= Chat.instance?.m_input.isActiveAndEnabled ?? false;
        }
        
        private static AssetBundle GetAssetBundle(string filename)
        {
            Assembly execAssembly = Assembly.GetExecutingAssembly();
            string resourceName = execAssembly.GetManifestResourceNames().Single(str => str.EndsWith(filename));
            using Stream stream = execAssembly.GetManifestResourceStream(resourceName)!;
            return AssetBundle.LoadFromStream(stream);
        }
    }
}