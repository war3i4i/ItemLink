using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using Jewelcrafting;
using TMPro;
using UnityEngine; 
using UnityEngine.EventSystems;
using UnityEngine.UI;
 
namespace ItemLink
{
    public static class LinkContainer
    {
        
        private static readonly List<ItemLink_Data> _itemLinks = new List<ItemLink_Data>(50);
        private static int _lastLink = -1;
        private static int _lastLinkIndex = -1;
        static LinkContainer()
        { 
            for (int i = 0; i < 50; i++)
                _itemLinks.Add(null);
        }
        public static int AddLink(ItemLink_Data itemLinkData)
        {
            _lastLink = (_lastLink + 1) % 50;
            _itemLinks[_lastLink] = itemLinkData;
            return _lastLink;
        } 
        public static ItemLink_Data GetLink(int index)
        {
            if (_lastLinkIndex == index) return null;
            return _itemLinks[index];
        }
        public static void SetLastLinkIndex(int index) => _lastLinkIndex = index;
        public static void ResetLastLinkIndex() => _lastLinkIndex = -1;
    }
    
    public class ItemLink_Data
    { 
        public string Prefab;
        public int Count;
        public int Quality;
        public int Variant;
        public string CustomData = "{}";
        public string CrafterName = "";
        public long CrafterID;
        public byte DurabilityPercent;
    } 

    [HarmonyPatch(typeof(Chat), nameof(Chat.Awake))]
    public static class Chat_Awake_Patch
    {
        [UsedImplicitly] 
        private static void Postfix(Chat __instance)
        {
            if (Chat.instance.m_input is {} chat && !chat.GetComponent<ItemLinkProcessor>())
            {
                __instance.m_output.raycastTarget = true;
                chat.gameObject.AddComponent<ItemLinkProcessor>();
            }
        }
    }

    [HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.OnLeftClick))]
    public static class InventoryGrid_OnLeftClick_Patch
    {
        [UsedImplicitly]
        private static bool Prefix(InventoryGrid __instance, UIInputHandler clickHandler)
        {
            if (Input.GetKey(KeyCode.LeftShift) && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.LeftAlt)))
            {
                Talker.Type type = Input.GetKey(KeyCode.LeftControl) ? Talker.Type.Normal : Talker.Type.Shout;
                Vector2i buttonPos = __instance.GetButtonPos(clickHandler.gameObject);
                ItemDrop.ItemData itemAt = __instance.m_inventory.GetItemAt(buttonPos.x, buttonPos.y);
                if (itemAt == null) return true;
                ItemLink_Data toCollect = new ItemLink_Data
                {
                    Prefab = itemAt.m_dropPrefab.name,
                    Count = itemAt.m_stack,
                    Quality = itemAt.m_quality,
                    Variant = itemAt.m_variant,
                    CustomData = fastJSON.JSON.ToJSON(itemAt.m_customData),
                    CrafterName = itemAt.m_crafterName,
                    CrafterID = itemAt.m_crafterID,
                    DurabilityPercent = (byte)(itemAt.m_durability / itemAt.GetMaxDurability() * 100f)
                }; 
                string json = fastJSON.JSON.ToJSON(toCollect);
                UserInfo userInfo = UserInfo.GetLocalUser();
                userInfo.Name += $" [{type}]";
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "RPC_KG_ItemLink", [userInfo, json]);
                return false;
            }
            return true;
        }
    }
    
    [HarmonyPatch(typeof(ZNetScene),nameof(ZNetScene.Awake))]
    public static class ZNetScene_Awake_Patch
    {
        private static void Postfix(ZNetScene __instance) => ZRoutedRpc.instance.Register<UserInfo, string>("RPC_KG_ItemLink", RPC_Receiver.ReceiveItemLink);
    }
    
    public static class RPC_Receiver
    {
        public const string KEY = "kg.ItemLinkChatKey:"; 
        public static void ReceiveItemLink(long sender, UserInfo userInfo, string json)
        {
            string text = "";
            try
            {
                ItemLink_Data itemLinkData = fastJSON.JSON.ToObject<ItemLink_Data>(json);
                int newLink = LinkContainer.AddLink(itemLinkData);
                string addStack = itemLinkData.Count > 1 ? $" x{itemLinkData.Count}" : "";
                ItemLinkProcessor.GetItemData(itemLinkData, out string hookName, out string hookText, out Sprite hookIcon, out ItemDrop.ItemData item);
                text = $"<link=\"{KEY}{newLink}\"><color=#ffbf00><u><b>[•{hookName}{addStack}]</b></u></color></link>";
            }
            catch (Exception ex)
            {
                text = "<color=red>[kg.ItemLink] Wrong Data</color>";
                MonoBehaviour.print($"[kg.ItemLink] Error: {ex}");
            }
            Chat.instance.m_hideTimer = 0f;
            Chat.instance.AddString(userInfo.Name + UserInfo.GamertagSuffix(userInfo.Gamertag), text, Talker.Type.Normal, false);
        }
    }
    
    public class ItemLinkProcessor : MonoBehaviour
    {
        public static GameObject TooltipPrefab;
        private static GameObject CurrentTooltip;
        private static List<GameObject> CompareTooltips = new();
        
        private int linkStartTextIndex = -1;
        private int linkTextLength = 0;
        private bool changeColor => linkStartTextIndex != -1 && linkTextLength != 0;
        private TMP_Text _text;
        private TMP_InputField _input;
 
        private void Awake()
        {
            _text = Chat.instance.m_output.GetComponent<TMP_Text>();
            _input = Chat.instance.m_input.GetComponent<TMP_InputField>();
        }

        private void OnDisable()
        {
            if (changeColor) ChangeVertexColors(false);
            linkStartTextIndex = -1; 
            linkTextLength = 0;
            LinkContainer.ResetLastLinkIndex();
            if (CurrentTooltip) Destroy(CurrentTooltip);
            CompareTooltips.ForEach(Destroy);
            CompareTooltips.Clear();
        }

        public static List<ItemDrop.ItemData> GetCompareItems(ItemDrop.ItemData item)
        {
            if (!Player.m_localPlayer) return null;
            return Player.m_localPlayer.m_inventory.m_inventory.Where(x => x.m_equipped && x.m_shared.m_itemType == item.m_shared.m_itemType).ToList();
        }

        private void ProcessTMP(int tryFindLink)
        {
            TMP_LinkInfo linkInfo = _text.textInfo.linkInfo[tryFindLink];
            string linkId = linkInfo.GetLinkID();
            if (linkId.Contains(RPC_Receiver.KEY))
            {
                try
                {
                    int linkIndex = int.Parse(linkId.Split(':')[1]);
                    ItemLink_Data itemLinkData = LinkContainer.GetLink(linkIndex);
                    if (itemLinkData == null) return;
                    if (changeColor) ChangeVertexColors(false);
                    if (CurrentTooltip) Destroy(CurrentTooltip);
                    CompareTooltips.ForEach(Destroy);
                    CompareTooltips.Clear();
                    LinkContainer.SetLastLinkIndex(linkIndex);
                    linkStartTextIndex = linkInfo.linkTextfirstCharacterIndex;
                    linkTextLength = linkInfo.linkTextLength;
                    CurrentTooltip = CreateTooltipForItemLinkData(itemLinkData);
                    ChangeVertexColors(true);
                }  
                catch(Exception ex)
                {
                    linkStartTextIndex = -1;
                    linkTextLength = 0;
                    MonoBehaviour.print($"[ItemLink] Error: {ex}");
                }
            }
        }
        
        private void LateUpdate()
        {
            if (!_input.isActiveAndEnabled) return;
            int tryFindLink = TMP_TextUtilities.FindIntersectingLink(_text, Input.mousePosition, null);
            if (tryFindLink == -1)
            {
                OnDisable();
                return;
            } 
            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyUp(KeyCode.LeftShift))
                OnDisable();
            
            ProcessTMP(tryFindLink);
        } 

        private GameObject CreateTooltipForItemLinkData(ItemLink_Data itemLinkData,  string additionalText = "")
        {
            GetItemData(itemLinkData, out string hookName, out string hookText, out Sprite hookIcon, out ItemDrop.ItemData item);
            GameObject result = Instantiate(TooltipPrefab, Hud.instance.transform.parent);
            result.transform.Find("Bkg/Icon").GetComponent<Image>().sprite = hookIcon;
            result.transform.Find("Bkg/Icon").GetComponent<Image>().type = Image.Type.Sliced;
            result.transform.Find("Bkg/Topic").GetComponent<Text>().text = Localization.instance.Localize(hookName) + additionalText;
            result.transform.Find("Bkg/Text").GetComponent<Text>().text = Localization.instance.Localize(hookText);
            Transform trannyHoles = result.transform.Find("Bkg/TrannyHoles");
            bool isJC = Jewelcrafting.API.FillItemContainerTooltip(item, trannyHoles.parent, false);
            trannyHoles.gameObject.SetActive(isJC);
            AdjustPosition(result.transform.GetChild(0).transform as RectTransform);
            
            if (Input.GetKey(KeyCode.LeftShift) && GetCompareItems(item) is {} compareItems)
            {
                for (int i = 0; i < compareItems.Count; ++i)
                {
                    GameObject compareTooltip = CreateCompareTooltip(compareItems[i], i," <color=green><b>[Equipped]</b></color>");
                    CompareTooltips.Add(compareTooltip);
                }
            }
            return result;
        }
         
        private GameObject CreateCompareTooltip(ItemDrop.ItemData item, int index, string additionalText = "")
        {
            GetItemData(item, out string hookName, out string hookText, out Sprite hookIcon);
            GameObject result = Instantiate(TooltipPrefab, Hud.instance.transform.parent);
            result.transform.Find("Bkg/Icon").GetComponent<Image>().sprite = hookIcon; 
            result.transform.Find("Bkg/Icon").GetComponent<Image>().type = Image.Type.Sliced;
            result.transform.Find("Bkg/Topic").GetComponent<Text>().text = Localization.instance.Localize(hookName) + additionalText;
            result.transform.Find("Bkg/Text").GetComponent<Text>().text = Localization.instance.Localize(hookText);
            Transform trannyHoles = result.transform.Find("Bkg/TrannyHoles");
            bool isJC = Jewelcrafting.API.FillItemContainerTooltip(item, trannyHoles.parent, false);
            trannyHoles.gameObject.SetActive(isJC);
            AdjustPosition(result.transform.GetChild(0).transform as RectTransform, -result.transform.GetChild(0).GetComponent<RectTransform>().sizeDelta.x * (index + 1) - 4f);
            return result;
        }

        public static void GetItemData(ItemLink_Data itemLinkData, out string topic, out string text, out Sprite icon, out ItemDrop.ItemData item)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(itemLinkData.Prefab);
            if (!prefab)
            {
                item = null!;
                topic = "Unknown";
                text = "Unknown";
                icon = null!;
                return;
            }
            ItemDrop.ItemData origData = prefab.GetComponent<ItemDrop>().m_itemData;
            ItemDrop.ItemData.SharedData mainSharedData = origData.m_shared;
            Dictionary<string, string> mainCustomData = origData.m_customData;
            origData.m_shared = (ItemDrop.ItemData.SharedData)AccessTools.DeclaredMethod(typeof(object), "MemberwiseClone").Invoke(mainSharedData, Array.Empty<object>());
            origData.m_customData = fastJSON.JSON.ToObject<Dictionary<string, string>>(itemLinkData.CustomData);
            item = origData.Clone(); 
            origData.m_customData = mainCustomData;
            origData.m_shared = mainSharedData;
            
            item.m_stack = itemLinkData.Count;
            item.m_quality = itemLinkData.Quality;
            item.m_variant = itemLinkData.Variant;
            item.m_crafterID = itemLinkData.CrafterID;
            item.m_crafterName = itemLinkData.CrafterName;
            item.m_durability = item.GetMaxDurability() * itemLinkData.DurabilityPercent / 100f;
            
            UITooltip littleTrick = ItemLink.TooltipTrick.GetComponent<UITooltip>();
            bool canBeRepairedOld = item.m_shared.m_canBeReparied;
            item.m_shared.m_canBeReparied = false;
            InventoryGui.instance.m_playerGrid.CreateItemTooltip(item, littleTrick);
            item.m_shared.m_canBeReparied = canBeRepairedOld;
            topic = Localization.instance.Localize(littleTrick.m_topic);
            text = Localization.instance.Localize(littleTrick.m_text);
            icon = item.GetIcon(); 
        }
        
        public static void GetItemData(ItemDrop.ItemData item, out string topic, out string text, out Sprite icon)
        {
            ItemLink.PatchTooltip.SkipPatch = true;
            UITooltip littleTrick = ItemLink.TooltipTrick.GetComponent<UITooltip>();
            bool canBeRepairedOld = item.m_shared.m_canBeReparied;
            item.m_shared.m_canBeReparied = false; 
            InventoryGui.instance.m_playerGrid.CreateItemTooltip(item, littleTrick);
            item.m_shared.m_canBeReparied = canBeRepairedOld;
            ItemLink.PatchTooltip.SkipPatch = false;
            topic = Localization.instance.Localize(littleTrick.m_topic);
            text = Localization.instance.Localize(littleTrick.m_text);
            icon = item.GetIcon();
        }

        private void AdjustPosition(RectTransform bkg, float minusX = 0f)
        {
            ContentSizeFitter sizeFitter = bkg.GetComponent<ContentSizeFitter>();   
            sizeFitter.enabled = false; 
            sizeFitter.enabled = true;
            Canvas.ForceUpdateCanvases();
            Vector3 topLeft = _text.transform.TransformPoint(_text.textInfo.characterInfo[linkStartTextIndex].topLeft);
            Vector3 topRight = _text.transform.TransformPoint(_text.textInfo.characterInfo[linkStartTextIndex + linkTextLength - 1].topRight);
            Vector3 spawnPos = new Vector3((topLeft.x + topRight.x) / 2f, topLeft.y, 0f);
            bkg.transform.parent.position = spawnPos;
            (bkg.transform.parent as RectTransform).anchoredPosition += new Vector2(minusX, bkg.sizeDelta.y);
        }

        private List<Color> _oldColors = null!;
        private Color defaultHoverColor = new Color(1f, 0.96f, 0.99f);
        private void ChangeVertexColors(bool isHover)
        {
            if (isHover) _oldColors = new List<Color>(linkTextLength);
            for (int i = 0; i < linkTextLength; ++i)
            {
                int charIndex = linkStartTextIndex + i;
                if (_text.textInfo.characterInfo[charIndex].character == ' ')
                {
                    _oldColors.Add(defaultHoverColor);
                    _oldColors.Add(defaultHoverColor);
                    _oldColors.Add(defaultHoverColor);
                    _oldColors.Add(defaultHoverColor);
                    continue;
                }
                int meshIndex = _text.textInfo.characterInfo[charIndex].materialReferenceIndex;
                int vertexIndex = _text.textInfo.characterInfo[charIndex].vertexIndex;
                Color32[] vertexColors = _text.textInfo.meshInfo[meshIndex].colors32;
                if (isHover)
                { 
                    _oldColors.Add(vertexColors[vertexIndex + 0]);
                    _oldColors.Add(vertexColors[vertexIndex + 1]);
                    _oldColors.Add(vertexColors[vertexIndex + 2]);
                    _oldColors.Add(vertexColors[vertexIndex + 3]);
                    vertexColors[vertexIndex + 0] = defaultHoverColor;
                    vertexColors[vertexIndex + 1] = defaultHoverColor;
                    vertexColors[vertexIndex + 2] = defaultHoverColor;
                    vertexColors[vertexIndex + 3] = defaultHoverColor;
                }
                else
                {
                    vertexColors[vertexIndex + 0] = _oldColors[i * 4 + 0];
                    vertexColors[vertexIndex + 1] = _oldColors[i * 4 + 1];
                    vertexColors[vertexIndex + 2] = _oldColors[i * 4 + 2];
                    vertexColors[vertexIndex + 3] = _oldColors[i * 4 + 3];
                }
            }
            _text.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
        }
    }
}