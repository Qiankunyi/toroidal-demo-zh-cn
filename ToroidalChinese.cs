using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

[BepInPlugin("local.toroidal.zh-cn", "Toroidal Simplified Chinese", "0.3.4")]
public sealed class ToroidalChinese : BaseUnityPlugin
{
    private static Dictionary<string, string> menu;
    private static Dictionary<string, string> story;
    [ThreadStatic] private static SHA256 translationHasher;
    private static HashSet<string> profileTranslations;
    private static readonly HashSet<string> characterNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "ハル", "ミント", "コージン", "パグ", "教授", "ナット", "ふわ子", "シアン",
        "ナギリン", "ナギ", "ダイナ", "ハッチ", "テリナ", "リューカ", "アクマ", "神主"
    };
    private static string saveDirectory;
    private static TMP_FontAsset fallback;
    private static Font fontSource;
    private static byte[] fontData;
    private static readonly ConditionalWeakTable<TMP_Text, DialogueLayout> dialogueLabels =
        new ConditionalWeakTable<TMP_Text, DialogueLayout>();
    private static readonly Regex sentenceEnd = new Regex(
        @"(?<end>[。！？!?]+[」』”’）】》)]*)[^\S\r\n]*(?=[^\s。！？!?」』”’）】》)])");
    private static readonly Regex sentenceBoundary = new Regex(@"[。！？!?]+[」』”’）】》)]*");
    private static readonly Regex clauseBoundary = new Regex(@"[\n；;，、：:]+");
    private static FieldInfo dialogueCharacters;
    private static FieldInfo dialogueNextIcon;
    private static FieldInfo dialogueCoroutine;
    private static FieldInfo stopFlag;
    private static FieldInfo menuFlag;
    private static FieldInfo outputFlag;
    private static MethodInfo restartDialogue;
    private static Type outputType;
    private static readonly Regex statusLine = new Regex(
        @"^(?<name>.+?) (?:が (?<amount>[0-9]+) (?<change>増えた|減った)|は(?<limit>既に最大値だ|これ以上下がらない))$");
    private static readonly Regex scoreLine = new Regex(@"^(?<name>.+?) {2,}(?<calculation>[0-9]+ \* [0-9]+)$");
    private static readonly Regex dayLabel = new Regex(@"^(?<day>[0-9]+)日目$");
    private static readonly Regex dayAndPeriod = new Regex(@"^(?<day>[0-9]+)日目[ \t]+(?<period>.+)$");
    private static readonly Regex skillUpgrade = new Regex(
        @"^(?<name>この初期トークの数値|メンタル最大値|やる気の初期値)を\n(?<values>[0-9]+(?: → [0-9]+)?)\n(?:にアップするわ?|(?:に|まで)上げ(?:てあげ)?る)。$");
    private static readonly Regex skillMultiplier = new Regex(
        @"^(?<name>話題発掘ポイント|トーク力強化ポイント)の\n上がりやすさを\n(?<values>[\d.]+倍 → [\d.]+倍)\nに上げ(?:てあげ)?る。$");
    private static readonly Regex skillCost = new Regex(@"^(?<name>所持ポイント:|消費ポイント:)\s*(?<amount>[0-9]+P?)$");
    private static readonly Regex relationshipPoints = new Regex(@"^(?<name>.+?)との関係性ポイント$");
    private static readonly Dictionary<Material, Material> chineseMaterials =
        new Dictionary<Material, Material>();

    private void Awake()
    {
        var directory = Path.GetDirectoryName(Info.Location);
        menu = ReadTranslations(Path.Combine(directory, "menu.tsv"));
        story = ReadTranslations(Path.Combine(directory, "story.tsv"));
        profileTranslations = new HashSet<string>(StringComparer.Ordinal);
        fontData = File.ReadAllBytes(Path.Combine(directory, "fonts", "fusion-pixel-12px-proportional-zh_hans.otf"));

        var db = AccessTools.TypeByName("DBHandler");
        var constructor = AccessTools.Constructor(db, new[] { typeof(string), typeof(string) });
        saveDirectory = Path.Combine(directory, "Save");
        Directory.CreateDirectory(saveDirectory);
        foreach (var name in new[] { "wdsm.tmp", "ldsm.tmp" })
        {
            var target = Path.Combine(saveDirectory, name);
            if (!File.Exists(target))
                File.Copy(Path.Combine(Application.streamingAssetsPath, name), target);
        }

        var harmony = new Harmony(Info.Metadata.GUID);
        harmony.Patch(constructor, prefix: Hook("RedirectWorkingSave"), transpiler: Hook("RedirectLoopSave"));
        harmony.Patch(AccessTools.Method(db, "ReplText", new[] { typeof(string) }), prefix: Hook("TranslateScriptText"));
        harmony.Patch(AccessTools.Method(db, "ReplText", new[] { typeof(string), typeof(int) }),
            prefix: Hook("TranslateStory"), transpiler: Hook("LocalizeCharacterVariable"));
        harmony.Patch(AccessTools.Method(db, "CallStartText"), postfix: Hook("TranslateStartText"));
        harmony.Patch(AccessTools.Method(db, "CallActionStartText"), postfix: Hook("TranslateTextResult"));
        harmony.Patch(AccessTools.Method(db, "GetAisatsuStr"), postfix: Hook("TranslateTextResult"));
        harmony.Patch(AccessTools.Method(db, "GetProfileText"), postfix: Hook("TranslateProfileText"));
        var output = AccessTools.TypeByName("OutputText");
        var view = AccessTools.TypeByName("ViewManager");
        var flags = AccessTools.TypeByName("Flag_class");
        outputType = output;
        dialogueCharacters = AccessTools.Field(output, "_charQueue");
        dialogueNextIcon = AccessTools.Field(output, "nextPageIcon");
        dialogueCoroutine = AccessTools.Field(output, "_coroutine");
        stopFlag = AccessTools.Field(flags, "is_stop_flag");
        menuFlag = AccessTools.Field(flags, "flag_menu");
        outputFlag = AccessTools.Field(flags, "OutputTextFlag");
        restartDialogue = AccessTools.Method(output, "RestartCorutine");
        harmony.Patch(AccessTools.Method(output, "Init"), prefix: Hook("RegisterDialogueLabel"));
        harmony.Patch(AccessTools.Method(output, "ReadLine"), prefix: Hook("TranslateDialogue"));
        harmony.Patch(AccessTools.Method(output, "BattleReadLine"), prefix: Hook("TranslateTimedDialogue"));
        harmony.Patch(AccessTools.Method(view, "OnClick"), prefix: Hook("AdvanceDialogue"));
        harmony.Patch(AccessTools.Method(view, "TutorialOnClick"), prefix: Hook("AdvanceTutorialDialogue"));
        harmony.Patch(AccessTools.Method(output, "ReadLine2"), prefix: Hook("TranslateStatus"));
        harmony.Patch(AccessTools.Method(view, "DisplayNotAnimationSpeech"), prefix: Hook("TranslateImmediateSpeech"));
        harmony.Patch(AccessTools.Method(view, "DisplayOldText"), postfix: Hook("LocalizeBacklog"));
        harmony.Patch(AccessTools.DeclaredMethod(AccessTools.TypeByName("ChoiceButton"), "Init"),
            prefix: Hook("ApplyChoiceFont"));
        foreach (var method in AccessTools.GetDeclaredMethods(AccessTools.TypeByName("SimpleOutputText")))
        {
            var parameters = method.GetParameters();
            if (parameters.Length >= 2 && parameters[0].ParameterType == typeof(TextMeshProUGUI) &&
                parameters[1].ParameterType == typeof(string))
                harmony.Patch(method, prefix: Hook("TranslateSimpleLine"));
        }
        var loadFace = AccessTools.Method(typeof(FontEngine), "LoadFontFace", new[] { typeof(Font), typeof(int) });
        harmony.Patch(loadFace, prefix: Hook("LoadChineseFontFace"));
        harmony.Patch(AccessTools.PropertySetter(typeof(TMP_Text), "text"), prefix: Hook("SetText"));
        harmony.Patch(AccessTools.Method(typeof(TextMeshProUGUI), "OnEnable"), postfix: Hook("LocalizeLabel"));
        SpriteCaptions.Initialize(harmony);
        GuideOverlay.Initialize(harmony, Path.Combine(directory, "guide.tsv"), Logger);
        Logger.LogInfo("Loaded " + menu.Count + " interface strings and " + story.Count + " story strings.");
    }

    private static HarmonyMethod Hook(string name)
    {
        return new HarmonyMethod(typeof(ToroidalChinese), name);
    }

    private static Dictionary<string, string> ReadTranslations(string path)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        bool hashedKeys = false;
        foreach (var line in File.ReadLines(path))
        {
            if (line == "// key-format=sha256-base64")
            {
                hashedKeys = true;
                continue;
            }
            if (line.Length == 0) continue;
            int tab = line.IndexOf('\t');
            if (line.StartsWith("//") && (!hashedKeys || tab == -1)) continue;
            if (tab <= 0 || tab == line.Length - 1)
                throw new InvalidDataException("Invalid translation line in " + path);
            var key = line.Substring(0, tab);
            entries.Add(hashedKeys ? key : TranslationKey(Decode(key)), Decode(line.Substring(tab + 1)));
        }
        return entries;
    }

    private static string TranslationKey(string value)
    {
        if (translationHasher == null) translationHasher = SHA256.Create();
        return Convert.ToBase64String(translationHasher.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private static string Decode(string value)
    {
        return value.Replace("\\n", "\n");
    }

    private static string Normalize(string value)
    {
        return value.Replace("\r\n", "\n").Trim();
    }

    private static void RedirectWorkingSave(ref string __1)
    {
        __1 = Path.Combine(saveDirectory, "wdsm.tmp");
    }

    private static string GetSaveDirectory()
    {
        return saveDirectory;
    }

    private static IEnumerable<CodeInstruction> RedirectLoopSave(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.PropertyGetter(typeof(Application), "streamingAssetsPath");
        var replacement = AccessTools.Method(typeof(ToroidalChinese), "GetSaveDirectory");
        var code = instructions.ToList();
        var call = code.Single(i => i.Calls(original));
        call.operand = replacement;
        return code;
    }

    private static void TranslateStory(ref string __0)
    {
        string translated;
        if (!string.IsNullOrEmpty(__0) && story.TryGetValue(TranslationKey(Normalize(__0)), out translated))
            __0 = translated;
    }

    private static void TranslateScriptText(ref string __0)
    {
        // This overload also resolves names used as event lookup keys.
        if (__0 != null && !characterNames.Contains(Normalize(__0)))
            TranslateStory(ref __0);
    }

    private static IEnumerable<CodeInstruction> LocalizeCharacterVariable(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var key = code.FindIndex(i => i.opcode == System.Reflection.Emit.OpCodes.Ldstr &&
            (string)i.operand == "#character_name");
        if (key < 0) throw new InvalidDataException("Character name placeholder not found.");
        code.Insert(key + 2, new CodeInstruction(System.Reflection.Emit.OpCodes.Call,
            AccessTools.Method(typeof(ToroidalChinese), "TranslateCharacterName")));
        return code;
    }

    internal static string TranslateCharacterName(string name)
    {
        string translated;
        return name != null && menu.TryGetValue(TranslationKey(name), out translated) ? translated : name;
    }

    private static void TranslateStartText(ref ValueTuple<string, int, int> __result)
    {
        TranslateStory(ref __result.Item1);
    }

    private static void TranslateTextResult(ref string __result)
    {
        TranslateStory(ref __result);
    }

    private static void TranslateProfileText(List<ValueTuple<string, int>> __result)
    {
        if (__result == null) return;
        for (int i = 0; i < __result.Count; i++)
        {
            var entry = __result[i];
            var source = entry.Item1;
            TranslateStory(ref entry.Item1);
            if (entry.Item1 != source)
            {
                __result[i] = entry;
                profileTranslations.Add(entry.Item1);
            }
        }
    }

    private static void LocalizeLabel(TextMeshProUGUI __instance)
    {
        var current = __instance.text;
        var translated = TranslateMenu(__instance, current);
        if (translated != current)
        {
            ApplyChineseFont(__instance, translated);
            __instance.text = translated;
        }
    }

    private sealed class DialogueLayout
    {
        private readonly Vector4 margin;
        private float width;
        private float height;
        public readonly Queue<string> Pages = new Queue<string>();

        public DialogueLayout(TMP_Text label)
        {
            margin = label.margin;
        }

        public void Prepare(TMP_Text label, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            ApplyChineseFont(label, text);
            label.enableWordWrapping = true;
            label.enableAutoSizing = false;
            var inset = margin;
            // The rectangular text area extends into the transparent space around the speech sprite.
            var bounds = label.rectTransform.rect;
            var parent = label.rectTransform.parent as RectTransform;
            var bubble = parent == null ? null : parent.GetComponent<UnityEngine.UI.Image>();
            if (bubble != null && bubble.sprite != null &&
                (bubble.sprite.name.StartsWith("icon_messagebox", StringComparison.Ordinal) ||
                 bubble.sprite.name == "messagebox-large-box" || bubble.sprite.name == "still-message"))
            {
                var edge = label.rectTransform.InverseTransformPoint(parent.TransformPoint(
                    new Vector3(parent.rect.xMax, parent.rect.yMin, 0)));
                inset.z = Mathf.Max(inset.z, bounds.xMax - edge.x + 14);
                inset.w = Mathf.Max(inset.w, bounds.yMin - edge.y + 12);
            }
            label.margin = inset;
            width = bounds.width - inset.x - inset.z;
            height = bounds.height - inset.y - inset.w - 4;
        }

        private bool Fits(TMP_Text label, string text)
        {
            var visible = text.Replace("Ա", "").Replace("Բ", "").TrimEnd();
            var preferred = label.GetPreferredValues(visible, width, 0);
            return preferred.x <= width + 0.1f && preferred.y <= height;
        }

        public string Wrap(TMP_Text label, string text, bool paginate)
        {
            Pages.Clear();
            Prepare(label, text);
            if (string.IsNullOrWhiteSpace(text) || width < 1 || height < 1) return text;

            if (!paginate || Fits(label, text)) return FormatPage(label, text);
            while (!Fits(label, text))
            {
                var limit = LastFittingCharacter(label, text);
                var count = LastBoundary(sentenceBoundary, text, limit);
                if (count == 0)
                {
                    // A sentence that fills a fresh page gets a clause break where possible.
                    count = LastBoundary(clauseBoundary, text, limit);
                    if (count == 0) count = limit;
                    while (count > 1 && count < text.Length && "。！？!?，、：；」』”’）】》)".IndexOf(text[count]) >= 0)
                        count--;
                    if (count < text.Length && char.IsHighSurrogate(text[count - 1])) count--;
                    if (count > 1 && count < text.Length && text[count - 1] == 'Ա' && text[count] == 'Բ') count--;
                }
                count = Math.Max(count, 1);
                Pages.Enqueue(FormatPage(label, text.Substring(0, count).TrimEnd()));
                text = text.Substring(count).TrimStart();
            }
            if (text.Length > 0) Pages.Enqueue(FormatPage(label, text));
            return Pages.Dequeue();
        }

        private string FormatPage(TMP_Text label, string text)
        {
            var wrapped = sentenceEnd.Replace(text, "${end}\n");
            if (wrapped == text || Fits(label, wrapped)) return wrapped;
            var offset = 0;
            foreach (Match mark in sentenceEnd.Matches(text))
            {
                var end = mark.Groups["end"].Value + "\n";
                var candidate = text.Remove(mark.Index + offset, mark.Length).Insert(mark.Index + offset, end);
                if (!Fits(label, candidate)) break;
                text = candidate;
                offset += end.Length - mark.Length;
            }
            return text;
        }

        private static int LastBoundary(Regex pattern, string text, int limit)
        {
            var result = 0;
            foreach (Match match in pattern.Matches(text))
            {
                if (match.Index + match.Length > limit) break;
                result = match.Index + match.Length;
            }
            return result;
        }

        private int LastFittingCharacter(TMP_Text label, string text)
        {
            var first = 1;
            var last = text.Length - 1;
            while (first <= last)
            {
                var middle = (first + last) / 2;
                if (Fits(label, text.Substring(0, middle))) first = middle + 1;
                else last = middle - 1;
            }
            return Math.Max(1, last);
        }
    }

    private static void RegisterDialogueLabel(TextMeshProUGUI __0)
    {
        if (__0 != null)
            dialogueLabels.GetValue(__0, label => new DialogueLayout(label));
    }

    private static void TranslateDialogue(ref string __0, TextMeshProUGUI ___mainText)
    {
        TranslateStory(ref __0);
        if (___mainText != null)
            __0 = dialogueLabels.GetValue(___mainText, label => new DialogueLayout(label)).Wrap(___mainText, __0, true);
    }

    private static void TranslateTimedDialogue(ref string __0, TextMeshProUGUI ___mainText)
    {
        TranslateStory(ref __0);
        if (___mainText != null)
            __0 = dialogueLabels.GetValue(___mainText, label => new DialogueLayout(label)).Wrap(___mainText, __0, false);
    }

    private static bool AdvanceDialogue(GameObject ___text)
    {
        return (bool)stopFlag.GetValue(null) || (bool)menuFlag.GetValue(null) ||
            (bool)outputFlag.GetValue(null) || !ShowNextDialoguePage(___text);
    }

    private static bool AdvanceTutorialDialogue(GameObject ___battle_enemy_text, GameObject ___battle_my_text)
    {
        return (bool)stopFlag.GetValue(null) || (bool)menuFlag.GetValue(null) ||
            HasPendingCharacters(___battle_enemy_text) || HasPendingCharacters(___battle_my_text) ||
            (!ShowNextDialoguePage(___battle_enemy_text) && !ShowNextDialoguePage(___battle_my_text));
    }

    private static bool HasPendingCharacters(GameObject box)
    {
        var output = box == null ? null : box.GetComponent(outputType);
        return output != null && ((Queue<char>)dialogueCharacters.GetValue(output)).Count > 0;
    }

    private static bool ShowNextDialoguePage(GameObject box)
    {
        if (box == null || !box.activeInHierarchy) return false;
        var label = box.GetComponent<TMP_Text>();
        DialogueLayout layout;
        if (label == null || !dialogueLabels.TryGetValue(label, out layout) || layout.Pages.Count == 0) return false;
        var output = box.GetComponent(outputType) as MonoBehaviour;
        if (output == null || ((Queue<char>)dialogueCharacters.GetValue(output)).Count != 0) return false;
        var icon = (GameObject)dialogueNextIcon.GetValue(output);
        if (icon == null || !icon.activeSelf) return false;
        var page = layout.Pages.Dequeue();
        var coroutine = (Coroutine)dialogueCoroutine.GetValue(output);
        if (coroutine != null) output.StopCoroutine(coroutine);
        label.text = "";
        icon.SetActive(false);
        dialogueCharacters.SetValue(output, new Queue<char>(page));
        restartDialogue.Invoke(output, null);
        return true;
    }

    private static void TranslateStatus(ref string __0, bool __1, TextMeshProUGUI ___mainText)
    {
        __0 = TranslateInterface(__0);
        if (___mainText != null)
        {
            var layout = dialogueLabels.GetValue(___mainText, label => new DialogueLayout(label));
            if (__1) layout.Pages.Clear();
            layout.Prepare(___mainText, (__1 ? "" : ___mainText.text) + __0);
        }
    }

    private static void TranslateImmediateSpeech(GameObject __0, ref string __1)
    {
        __1 = TranslateInterface(__1);
        TranslateStory(ref __1);
        var label = __0.GetComponent<TextMeshProUGUI>();
        if (label != null)
            __1 = dialogueLabels.GetValue(label, item => new DialogueLayout(item)).Wrap(label, __1, false);
    }

    private static void ApplyChoiceFont(TextMeshProUGUI __3, string __4)
    {
        if (__3 != null && !string.IsNullOrEmpty(__4))
            ApplyChineseFont(__3, __4);
    }

    private static void LocalizeBacklog(GameObject ___text_log_scroll)
    {
        if (___text_log_scroll == null) return;
        var rowType = AccessTools.TypeByName("SerialBLContent");
        var body = AccessTools.Field(rowType, "characterMainText");
        var name = AccessTools.Field(rowType, "characterName");
        var status = AccessTools.Field(rowType, "paramText");
        var time = AccessTools.Field(rowType, "dayTimeText");
        foreach (var row in ___text_log_scroll.GetComponentsInChildren(rowType, true))
        {
            var dialogue = (TMP_Text)body.GetValue(row);
            if (dialogue != null && !string.IsNullOrEmpty(dialogue.text))
            {
                var visible = dialogue.text.Replace("Ա", "").Replace("Բ", "").Replace("Գ", "");
                ApplyChineseFont(dialogue, visible);
                if (visible != dialogue.text) dialogue.text = visible;
            }
            foreach (var field in new[] { name, status, time })
            {
                var label = (TMP_Text)field.GetValue(row);
                if (label != null && !string.IsNullOrEmpty(label.text)) ApplyChineseFont(label, label.text);
            }
        }
    }

    private static void TranslateSimpleLine(TextMeshProUGUI __0, ref string __1)
    {
        RegisterDialogueLabel(__0);
        var source = __1;
        __1 = TranslateInterface(__1);
        if (__1 == source) TranslateStory(ref __1);
        if (__0 != null && __1 != source) ApplyChineseFont(__0, __1);
    }

    private static bool LoadChineseFontFace(Font __0, int __1, ref FontEngineError __result)
    {
        if (fontSource == null || __0 != fontSource) return true;
        __result = FontEngine.LoadFontFace(fontData, __1);
        return false;
    }

    private static string TranslateInterface(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        string translated;
        var key = Normalize(value);
        if (menu.TryGetValue(TranslationKey(key), out translated)) return translated;

        var match = statusLine.Match(key);
        if (match.Success && (translated = TranslateInterface(match.Groups["name"].Value)) != match.Groups["name"].Value)
        {
            var ending = value.EndsWith("\r\n") ? "\r\n" : value.EndsWith("\n") ? "\n" : "";
            if (match.Groups["amount"].Success)
                return translated + (match.Groups["change"].Value == "増えた" ? "增加 " : "减少 ") +
                    match.Groups["amount"].Value + ending;
            return translated + (match.Groups["limit"].Value == "既に最大値だ" ? "已达上限" : "已降至最低") + ending;
        }
        match = scoreLine.Match(key);
        if (match.Success && (translated = TranslateInterface(match.Groups["name"].Value)) != match.Groups["name"].Value)
            return translated + "   " + match.Groups["calculation"].Value + value.Substring(value.TrimEnd().Length);
        match = dayLabel.Match(key);
        if (match.Success) return "第" + match.Groups["day"].Value + "天" + value.Substring(value.TrimEnd().Length);
        match = dayAndPeriod.Match(key);
        if (match.Success && menu.TryGetValue(TranslationKey(match.Groups["period"].Value), out translated))
            return "第" + match.Groups["day"].Value + "天 " + translated + value.Substring(value.TrimEnd().Length);

        match = skillUpgrade.Match(key);
        if (match.Success && menu.TryGetValue(TranslationKey(match.Groups["name"].Value), out translated))
            return translated + "提升\n" + match.Groups["values"].Value;
        match = skillMultiplier.Match(key);
        if (match.Success && menu.TryGetValue(TranslationKey(match.Groups["name"].Value), out translated))
            return translated + "增长倍率\n" + match.Groups["values"].Value;
        match = skillCost.Match(key);
        if (match.Success && menu.TryGetValue(TranslationKey(match.Groups["name"].Value), out translated))
            return translated + match.Groups["amount"].Value;

        match = relationshipPoints.Match(key);
        if (match.Success && menu.TryGetValue(TranslationKey(match.Groups["name"].Value), out translated))
            return "与" + translated + "的关系点数";

        if (value.IndexOf('\n') >= 0)
        {
            // Log entries and result columns concatenate several previously rendered rows.
            var lines = value.Replace("\r\n", "\n").Split('\n');
            var localized = lines.Select(TranslateInterface).ToArray();
            if (!lines.SequenceEqual(localized)) return string.Join("\n", localized);
        }
        return value;
    }

    private static string TranslateMenu(TMP_Text label, string value)
    {
        DialogueLayout layout;
        if (string.IsNullOrEmpty(value) || dialogueLabels.TryGetValue(label, out layout)) return value;
        var translated = TranslateInterface(value);
        if (translated == value) return value;
        var input = label.GetComponentInParent<TMP_InputField>();
        if (input != null && input.textComponent == label) return value;
        if (Normalize(value) == "遊び方")
        {
            label.enableWordWrapping = false;
            label.overflowMode = TextOverflowModes.Overflow;
        }
        return translated;
    }

    private static void SetText(TMP_Text __instance, ref string __0)
    {
        if (string.IsNullOrEmpty(__0)) return;
        var translated = TranslateMenu(__instance, __0);
        DialogueLayout marker;
        if (translated != __0 || dialogueLabels.TryGetValue(__instance, out marker) ||
            profileTranslations.Contains(Normalize(translated)))
            ApplyChineseFont(__instance, translated);
        __0 = translated;
    }

    private static void ApplyChineseFont(TMP_Text label, string text)
    {
        var currentFont = label.font;
        if (currentFont == null || currentFont == fallback ||
            !text.Any(c => c >= '\u3400' && c <= '\u9fff')) return;
        if (GetChineseFont() == null) return;

        // Render each localized label with one face, retaining its original outline and weight.
        var originalMaterial = label.fontSharedMaterial;
        label.font = fallback;
        if (originalMaterial == null) return;
        Material styled;
        if (!chineseMaterials.TryGetValue(originalMaterial, out styled))
        {
            styled = new Material(TMP_MaterialManager.GetFallbackMaterial(originalMaterial, fallback.material));
            DontDestroyOnLoad(styled);
            chineseMaterials.Add(originalMaterial, styled);
        }
        label.fontSharedMaterial = styled;
    }

    internal static TMP_FontAsset GetChineseFont()
    {
        if (fallback != null || fontSource != null) return fallback;
        fontSource = Font.CreateDynamicFontFromOSFont("Arial", 36);
        if (fontSource == null) return null;
        DontDestroyOnLoad(fontSource);
        fallback = TMP_FontAsset.CreateFontAsset(fontSource, 72, 8, GlyphRenderMode.SDFAA, 1024, 1024,
            AtlasPopulationMode.Dynamic, true);
        if (fallback == null) return null;
        fallback.name = "Fusion Pixel 12px Simplified Chinese SDF";
        DontDestroyOnLoad(fallback);
        return fallback;
    }
}
