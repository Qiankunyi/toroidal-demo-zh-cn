using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed class SpriteCaptions : MonoBehaviour
{
    private static readonly Color NameInk = new Color32(214, 14, 90, 255);
    private static readonly Color Pink = new Color32(237, 20, 111, 255);
    private static readonly string[] actions = { "", "睡觉", "上学", "学习", "锻炼", "散步", "对话", "睡觉",
        "学习", "玩耍", "移动", "回家", "交谈", "音乐室", "体育馆", "游泳池", "天台", "交易", "返回", "开始上学" };
    private static readonly string[] weekdays = { "周一", "周二", "周三", "周四", "周五", "周六", "周日" };
    private static readonly string[] periods = { "早晨", "上课前", "午休", "放学后", "傍晚", "夜晚" };
    private static readonly Color[] periodColors = { new Color32(251, 176, 59, 255), new Color32(0, 169, 157, 255),
        new Color32(41, 171, 226, 255), new Color32(140, 198, 63, 255), new Color32(247, 147, 30, 255),
        new Color32(27, 20, 100, 255) };
    private static readonly Dictionary<string, string> notices = new Dictionary<string, string>
    {
        { "popup-title-relation_card", "获得新对话技能！" }, { "popup-title-skill", "获得新循环能力！" },
        { "popup-title-talk_card_get_point", "获得新话题牌！" }, { "popup-title-talk_card_power_point", "话题牌强化成功！" },
    };
    private static readonly Dictionary<string, string> names = new Dictionary<string, string>
    {
        { "name-haru", "ハル" }, { "name-minto", "ミント" }, { "name-kojin", "コージン" },
        { "name-pagu", "パグ" }, { "name-kyojyu", "教授" }, { "name-natto", "ナット" },
        { "name-fuwako", "ふわ子" }, { "name-sian", "シアン" }, { "name-nagirin", "ナギ" },
        { "name-daina", "ダイナ" }, { "name-hacchi", "ハッチ" }, { "name-terina", "テリナ" },
        { "name-ryuka", "リューカ" }, { "name-akuma", "アクマ" }, { "name-kannusi", "神主" },
    };

    private Image source;
    private Sprite current;
    private bool hideOriginalText;
    private float originalAlpha;
    private readonly List<Graphic> graphics = new List<Graphic>();
    private readonly List<Color> colors = new List<Color>();

    internal static void Initialize(Harmony harmony)
    {
        var hook = new HarmonyMethod(typeof(SpriteCaptions), "Localize");
        harmony.Patch(AccessTools.PropertySetter(typeof(Image), "sprite"), postfix: hook);
        harmony.Patch(AccessTools.DeclaredMethod(typeof(Image), "OnEnable"), postfix: hook);
    }

    private static void Localize(Image __instance)
    {
        var sprite = __instance.sprite;
        var layer = __instance.transform.Find("Chinese sprite captions");
        var caption = layer == null ? null : layer.GetComponent<SpriteCaptions>();
        if (caption != null && caption.current == sprite) return;
        if (caption != null)
        {
            if (caption.hideOriginalText) __instance.canvasRenderer.SetAlpha(caption.originalAlpha);
            caption.gameObject.SetActive(false);
            caption.gameObject.name = "Retired sprite captions";
            Destroy(caption.gameObject);
        }
        if (sprite == null) return;

        var key = sprite.name;
        var size = sprite.rect.size;
        string name;
        int number;
        if (size == new Vector2(899, 81) && names.TryGetValue(key, out name))
        {
            Start(__instance).Add(new Rect(55, 5, 380, 72), ToroidalChinese.TranslateCharacterName(name),
                Color.white, NameInk, 50, TextAlignmentOptions.Left);
        }
        else if (key == "icon_my_name")
        {
            Start(__instance).Add(new Rect(72, 7, 169, 67), "我", Color.white, NameInk, 50);
        }
        else if (key.StartsWith("icon_action_", StringComparison.Ordinal) &&
            int.TryParse(key.Substring(12), out number) && number > 0 && number < actions.Length)
        {
            var arrow = number == 2 || number == 10 || number == 11;
            var bounds = number == 6 ? new Rect(128, 46, 117, 70) :
                new Rect(4, 112, arrow ? 84 : size.x - 8, 44);
            Start(__instance).Add(bounds, actions[number], number == 6 ? Pink : number == 18 ? Color.white : Color.black,
                number == 18 ? Color.black : Color.white, number == 6 ? 45 : 34);
        }
        else if (key == "select-area")
        {
            Start(__instance).Add(new Rect(12, 7, 313, 38), "请选择行动", new Color32(249, 249, 249, 255), NameInk,
                29, TextAlignmentOptions.Left);
        }
        else if (key.StartsWith("day", StringComparison.Ordinal) && int.TryParse(key.Substring(3), out number) &&
            size == new Vector2(1522, 121) && number >= 1 && number <= 11)
        {
            var calendar = Start(__instance);
            for (int day = number; day < 11; day++)
            {
                var ink = day == number ? Color.white : (Color)new Color32(84, 84, 84, 255);
                var x = 45 + (day - 1) * 136.8f;
                calendar.Add(new Rect(x, 70, 68, 33), weekdays[(day - 1) % 7], Color.black, ink, 26);
            }
            calendar.Add(new Rect(1377, 70, 132, 33), "周四 · 毕业", Color.black,
                number == 11 ? Color.white : (Color)new Color32(84, 84, 84, 255), 26);
        }
        else if (size == new Vector2(422, 121) && key.StartsWith("time0", StringComparison.Ordinal) &&
            key.Length >= 7 && int.TryParse(key.Substring(4, 2), out number) && number >= 1 && number <= 6)
        {
            var left = number >= 2 && number <= 4 ? 95 : 124;
            Start(__instance).Add(new Rect(left, 23, 334 - left, 76), periods[number - 1],
                periodColors[number - 1], Color.white, 62);
        }
        else if (key.StartsWith("deck-", StringComparison.Ordinal))
        {
            LocalizeDeck(__instance, key, size);
        }
        else if (notices.TryGetValue(key, out name))
        {
            var replacement = Start(__instance);
            // These four sprites consist entirely of text.
            replacement.hideOriginalText = true;
            replacement.originalAlpha = __instance.canvasRenderer.GetAlpha();
            __instance.canvasRenderer.SetAlpha(0);
            replacement.Add(new Rect(0, 0, size.x, size.y), name, Color.clear, Color.yellow, 52,
                TextAlignmentOptions.Left);
        }
        else if (key == "popup-deck-set" || key == "popup-deck-unset")
        {
            var selected = key == "popup-deck-set";
            Start(__instance).Add(new Rect(0, 24, 128, 27), selected ? "已编入牌组" : "编入牌组",
                selected ? Color.yellow : (Color)new Color32(230, 230, 230, 255), Color.black, 23,
                TextAlignmentOptions.Center, 27);
        }
    }

    private static void LocalizeDeck(Image image, string key, Vector2 size)
    {
        var tab = new Color32(153, 153, 153, 255);
        Color inactive = new Color32(179, 179, 179, 255);
        switch (key)
        {
            case "deck-tab-card":
            case "deck-tab-deck":
            case "deck-tab-skill":
                var text = key == "deck-tab-card" ? "循环能力" : key == "deck-tab-deck" ? "编辑牌组" : "对话技能";
                Start(image).Add(new Rect(10, 6, size.x - 20, size.y - 12), text, tab, Color.black, 35);
                break;
            case "deck-button-ok":
            case "deck-button-ok-active":
                Start(image).Add(new Rect(15, 14, size.x - 30, size.y - 28), "确认编组",
                    key.EndsWith("-active") ? (Color)new Color32(237, 30, 121, 255) : inactive, Color.white, 49);
                break;
            case "deck-button-reset":
            case "deck-button-reset-active":
                Start(image).Add(new Rect(12, 9, size.x - 24, size.y - 18), "重置变更",
                    key.EndsWith("-active") ? Color.black : inactive, Color.white, 33);
                break;
            case "deck-page01":
                var captions = Start(image);
                captions.Add(new Rect(58, 350, 47, 254), "话\n题\n牌", new Color32(249, 249, 249, 255), Color.black, 32);
                captions.Add(new Rect(58, 857, 47, 116), "牌\n组", new Color32(201, 201, 201, 255), Color.black, 32);
                break;
            case "deck-title-list":
                Start(image).Add(new Rect(16, 205, 52, 254), "话\n题\n牌", new Color32(249, 249, 249, 255), Color.black, 34);
                break;
        }
    }

    private static SpriteCaptions Start(Image image)
    {
        var layer = new GameObject("Chinese sprite captions", typeof(RectTransform), typeof(SpriteCaptions));
        layer.transform.SetParent(image.transform, false);
        var rect = (RectTransform)layer.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var captions = layer.GetComponent<SpriteCaptions>();
        captions.source = image;
        captions.current = image.sprite;
        return captions;
    }

    private void Add(Rect bounds, string text, Color background, Color foreground, float fontSize,
        TextAlignmentOptions alignment = TextAlignmentOptions.Center, float rotation = 0)
    {
        var size = current.rect.size;
        var mask = new GameObject("Text mask", typeof(RectTransform), typeof(Image));
        mask.transform.SetParent(transform, false);
        var rect = (RectTransform)mask.transform;
        rect.anchorMin = new Vector2(bounds.xMin / size.x, 1 - bounds.yMax / size.y);
        rect.anchorMax = new Vector2(bounds.xMax / size.x, 1 - bounds.yMin / size.y);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localRotation = Quaternion.Euler(0, 0, rotation);
        Track(mask.GetComponent<Image>(), background);

        var textObject = new GameObject("Chinese caption", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(mask.transform, false);
        var label = textObject.GetComponent<TextMeshProUGUI>();
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        label.font = ToroidalChinese.GetChineseFont();
        label.enableWordWrapping = false;
        label.enableAutoSizing = true;
        label.fontSizeMin = fontSize * 0.55f;
        label.fontSizeMax = fontSize;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.text = text;
        Track(label, foreground);
    }

    private void Track(Graphic graphic, Color color)
    {
        graphic.raycastTarget = false;
        graphic.color = color * source.color;
        graphics.Add(graphic);
        colors.Add(color);
    }

    private void LateUpdate()
    {
        if (hideOriginalText) source.canvasRenderer.SetAlpha(0);
        for (int i = 0; i < graphics.Count; i++)
        {
            var color = colors[i] * source.color;
            if (graphics[i].color != color) graphics[i].color = color;
        }
    }
}
