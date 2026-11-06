using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

internal sealed class GuideOverlay : MonoBehaviour
{
    private const int Width = 1920;
    private const int Height = 1080;

    private static readonly Dictionary<int, List<Caption>> pages = new Dictionary<int, List<Caption>>();
    private static FieldInfo buttonType;
    private static FieldInfo buttonPage;
    private static FieldInfo buttonSerial;
    private static FieldInfo serialIcon;
    private static ManualLogSource logger;

    private int currentPage;
    private Texture2D mask;

    private sealed class Caption
    {
        public RectInt Bounds;
        public float FontSize;
        public string Ink;
        public bool LeftAligned;
        public string Text;
    }

    internal static void Initialize(Harmony harmony, string path, ManualLogSource log)
    {
        if (!File.Exists(path))
        {
            log.LogWarning("guide.tsv is missing. Guide pages will use the original artwork.");
            return;
        }
        foreach (var line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = line.Split(new[] { '\t' }, 9);
            var number = int.Parse(fields[0], CultureInfo.InvariantCulture);
            var bounds = new RectInt(int.Parse(fields[1]), int.Parse(fields[2]), int.Parse(fields[3]), int.Parse(fields[4]));
            if (bounds.xMin < 1 || bounds.yMin < 1 || bounds.xMax >= Width || bounds.yMax >= Height)
                throw new InvalidDataException("Guide caption is outside the original sprite.");
            List<Caption> captions;
            if (!pages.TryGetValue(number, out captions))
                pages.Add(number, captions = new List<Caption>());
            captions.Add(new Caption
            {
                Bounds = bounds,
                FontSize = float.Parse(fields[5], CultureInfo.InvariantCulture),
                Ink = fields[6],
                LeftAligned = fields[7] == "L",
                Text = fields[8].Replace("\\n", "\n")
            });
        }

        logger = log;
        var setting = AccessTools.TypeByName("SettingButton");
        buttonType = AccessTools.Field(setting, "Type");
        buttonPage = AccessTools.Field(setting, "intType");
        buttonSerial = AccessTools.Field(setting, "p_serial");
        serialIcon = AccessTools.Field(AccessTools.TypeByName("Serial"), "icon");
        harmony.Patch(AccessTools.DeclaredMethod(setting, "OnPointerClick"),
            postfix: new HarmonyMethod(typeof(GuideOverlay), "AfterGuideClick"));
    }

    private static void AfterGuideClick(object __instance)
    {
        if ((string)buttonType.GetValue(__instance) != "asobi") return;
        var page = (int)buttonPage.GetValue(__instance);
        var serial = buttonSerial.GetValue(__instance);
        if (serial == null) return;
        var icon = (Image)serialIcon.GetValue(serial);
        if (icon == null || icon.sprite == null || icon.sprite.name != page.ToString(CultureInfo.InvariantCulture)) return;
        try
        {
            Show(icon, page);
        }
        catch (Exception error)
        {
            logger.LogWarning("Guide page " + page + " could not be localized: " + error.Message);
        }
    }

    internal static void Show(Image icon, int page)
    {
        List<Caption> captions;
        if (!pages.TryGetValue(page, out captions)) return;
        var font = ToroidalChinese.GetChineseFont();
        if (font == null) return;

        var previous = icon.GetComponentInChildren<GuideOverlay>(true);
        if (previous != null && previous.currentPage == page) return;
        var overlay = new GameObject("Simplified Chinese guide", typeof(RectTransform), typeof(GuideOverlay));
        overlay.SetActive(false);
        overlay.transform.SetParent(icon.transform, false);
        Fit((RectTransform)overlay.transform, new RectInt(0, 0, Width, Height));
        var instance = overlay.GetComponent<GuideOverlay>();
        instance.currentPage = page;
        try
        {
            instance.mask = BuildMask(icon.sprite, captions);
            var image = NewChild(overlay.transform, "Japanese text mask", new RectInt(0, 0, Width, Height))
                .AddComponent<RawImage>();
            image.texture = instance.mask;
            image.raycastTarget = false;
            foreach (var caption in captions)
                AddCaption(overlay.transform, caption, font);
            if (previous != null)
            {
                previous.gameObject.SetActive(false);
                Destroy(previous.gameObject);
            }
            overlay.SetActive(true);
        }
        catch
        {
            Destroy(overlay);
            throw;
        }
    }

    private static void AddCaption(Transform parent, Caption caption, TMP_FontAsset font)
    {
        var label = NewChild(parent, "Chinese caption", caption.Bounds).AddComponent<TextMeshProUGUI>();
        label.font = font;
        label.color = caption.Ink == "K" ? Color.black :
            caption.Ink == "Y" ? Color.yellow :
            caption.Ink == "O" ? (Color)new Color32(255, 151, 35, 255) : Color.white;
        label.alignment = caption.LeftAligned ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.enableAutoSizing = true;
        label.fontSizeMin = caption.FontSize * 0.62f;
        label.fontSizeMax = caption.FontSize;
        label.fontSize = caption.FontSize;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        label.text = caption.Text;
    }

    private static GameObject NewChild(Transform parent, string name, RectInt bounds)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(parent, false);
        Fit((RectTransform)child.transform, bounds);
        return child;
    }

    private static void Fit(RectTransform rect, RectInt bounds)
    {
        rect.anchorMin = new Vector2((float)bounds.xMin / Width, 1f - (float)bounds.yMax / Height);
        rect.anchorMax = new Vector2((float)bounds.xMax / Width, 1f - (float)bounds.yMin / Height);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Texture2D BuildMask(Sprite sprite, List<Caption> captions)
    {
        var rect = sprite.textureRect;
        if (rect.width != Width || rect.height != Height)
            throw new InvalidDataException("The guide sprite dimensions differ from the supported build.");
        var source = sprite.texture;
        var buffer = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
        var active = RenderTexture.active;
        var pixels = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        try
        {
            Graphics.Blit(source, buffer);
            RenderTexture.active = buffer;
            pixels.ReadPixels(rect, 0, 0);
            var original = pixels.GetPixels32();
            var overlay = new Color32[original.Length];
            foreach (var caption in captions)
                MaskCaption(caption, original, overlay);
            pixels.SetPixels32(overlay);
            pixels.Apply(false, true);
            return pixels;
        }
        catch
        {
            Destroy(pixels);
            throw;
        }
        finally
        {
            RenderTexture.active = active;
            RenderTexture.ReleaseTemporary(buffer);
        }
    }

    private static void MaskCaption(Caption caption, Color32[] original, Color32[] overlay)
    {
        var bounds = caption.Bounds;
        var covered = new bool[bounds.width * bounds.height];
        // Include the anti-aliased edges and merge gaps before sampling the background.
        for (int y = 0; y < bounds.height; y++)
        {
            int sourceRow = (Height - 1 - bounds.y - y) * Width + bounds.x;
            for (int x = 0; x < bounds.width; x++)
            {
                if (!IsInk(original[sourceRow + x], caption.Ink)) continue;
                int left = Math.Max(0, x - 3);
                int right = Math.Min(bounds.width - 1, x + 3);
                int bottom = Math.Min(bounds.height - 1, y + 2);
                for (int dy = Math.Max(0, y - 2); dy <= bottom; dy++)
                {
                    for (int dx = left; dx <= right; dx++)
                        covered[dy * bounds.width + dx] = true;
                }
            }
        }
        for (int y = bounds.yMin; y < bounds.yMax; y++)
        {
            int row = (Height - 1 - y) * Width;
            int maskRow = (y - bounds.yMin) * bounds.width;
            int x = 0;
            while (x < bounds.width)
            {
                if (!covered[maskRow + x])
                {
                    x++;
                    continue;
                }
                int start = bounds.xMin + x;
                do { x++; } while (x < bounds.width && covered[maskRow + x]);
                int end = bounds.xMin + x - 1;
                var left = original[row + start - 1];
                var right = original[row + end + 1];
                for (int i = start; i <= end; i++)
                {
                    var color = Color32.Lerp(left, right, (i - start + 1f) / (end - start + 2f));
                    color.a = 255;
                    overlay[row + i] = color;
                }
            }
        }
        // Vertical samples soften horizontal streaks over the blurred scene artwork.
        for (int x = 0; x < bounds.width; x++)
        {
            int column = (Height - 1 - bounds.yMin) * Width + bounds.xMin + x;
            int y = 0;
            while (y < bounds.height)
            {
                if (!covered[y * bounds.width + x])
                {
                    y++;
                    continue;
                }
                int start = y;
                do { y++; } while (y < bounds.height && covered[y * bounds.width + x]);
                var top = original[column - (start - 1) * Width];
                var bottom = original[column - y * Width];
                for (int i = start; i < y; i++)
                {
                    int index = column - i * Width;
                    var color = Color32.Lerp(top, bottom, (i - start + 1f) / (y - start + 1f));
                    color.a = 255;
                    overlay[index] = Color32.Lerp(overlay[index], color, 0.5f);
                }
            }
        }
    }

    private static bool IsInk(Color32 color, string ink)
    {
        switch (ink)
        {
            case "K": return Math.Max(color.r, Math.Max(color.g, color.b)) < 242;
            case "Y": return color.r > 125 && color.g > 125 && color.b + 50 < color.g;
            case "O": return color.r > 125 && color.g > 54 && color.r > color.g + 30 && color.g > color.b + 20;
            default: return color.r > 105 && color.g > 105 && color.b > 105;
        }
    }

    private void OnDestroy()
    {
        if (mask != null) Destroy(mask);
    }
}
