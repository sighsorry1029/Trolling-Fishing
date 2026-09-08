using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace TrollingFishing;

internal static class FishingRodInputIcons
{
    private const string AssetName = "TrollingFishing_InputIcons";
    private const string MiddleMouseSpriteName = "mouse3_icon";
    private const string MiddleMouseTag =
        "<sprite=\"" + AssetName + "\" name=\"" + MiddleMouseSpriteName + "\" tint=0>";

    private static TMP_SpriteAsset? _asset;
    private static Material? _material;
    private static Texture2D? _texture;
    private static Dictionary<int, TMP_SpriteAsset>? _spriteLookup;
    private static Dictionary<int, Material>? _materialLookup;
    private static int _assetHash;
    private static GameObject? _searchedMapRoot;

    private static void EnsureInitialized()
    {
        if (_asset != null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        {
            return;
        }

        // Vanilla uses this sprite for the large map's ping hint, not the combat KeyHints.
        // Resolve it when an item tooltip needs it, after the HUD has had time to initialize.
        GameObject? mapRoot = Minimap.instance != null ? Minimap.instance.m_largeRoot : null;
        if (mapRoot == null || ReferenceEquals(_searchedMapRoot, mapRoot))
        {
            return;
        }

        try
        {
            Sprite? sprite = mapRoot.GetComponentsInChildren<Image>(includeInactive: true)
                .Select(image => image.sprite)
                .FirstOrDefault(IsMiddleMouseSprite);
            if (sprite == null || sprite.texture == null ||
                (sprite.packed && sprite.packingRotation != SpritePackingRotation.None))
            {
                return;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo? versionField = typeof(TMP_Asset).GetField("m_Version", flags);
            MaterialReferenceManager manager = MaterialReferenceManager.instance;
            _spriteLookup = typeof(MaterialReferenceManager).GetField("m_SpriteAssetReferenceLookup", flags)
                ?.GetValue(manager) as Dictionary<int, TMP_SpriteAsset>;
            _materialLookup = typeof(MaterialReferenceManager).GetField("m_FontMaterialReferenceLookup", flags)
                ?.GetValue(manager) as Dictionary<int, Material>;
            _assetHash = TMP_TextUtilities.GetHashCode(AssetName);
            if (versionField?.FieldType != typeof(string) || _spriteLookup == null || _materialLookup == null ||
                _spriteLookup.ContainsKey(_assetHash) || _materialLookup.ContainsKey(_assetHash))
            {
                return;
            }

            Shader? shader = Shader.Find("TextMeshPro/Sprite");
            if (shader == null || !shader.isSupported)
            {
                return;
            }

            _texture = CopySpriteTexture(sprite);
            _material = new Material(shader)
            {
                name = AssetName + "_Material",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = _texture
            };
            _asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            _asset.name = AssetName;
            _asset.hideFlags = HideFlags.HideAndDontSave;
            versionField.SetValue(_asset, "1.1.0");
            _asset.spriteSheet = _texture;
            _asset.material = _material;

            float width = _texture.width;
            float height = _texture.height;
            float padding = height * 0.06f;
            TMP_SpriteGlyph glyph = new(
                0,
                new GlyphMetrics(width, height, padding, height * 0.85f, width + padding * 2f),
                new GlyphRect(0, 0, _texture.width, _texture.height),
                1.25f,
                0);
            _asset.spriteGlyphTable.Add(glyph);
            _asset.spriteCharacterTable.Add(new TMP_SpriteCharacter(0xFFFE, _asset, glyph)
            {
                name = MiddleMouseSpriteName
            });
            _asset.UpdateLookupTables();
            MaterialReferenceManager.AddSpriteAsset(_assetHash, _asset);
        }
        catch (Exception exception)
        {
            Shutdown();
            TrollingFishingPlugin.ModLogger.LogDebug(
                "Middle mouse tooltip icon unavailable; keeping the bound-key text. " +
                exception.GetBaseException().Message);
        }
        finally
        {
            // Missing/replaced vanilla UI keeps text fallback without rescanning on every hover.
            // A new HUD/map root permits another attempt; a successful copy outlives its source.
            _searchedMapRoot = mapRoot;
        }
    }

    internal static string LocalizeMultiLineHint()
    {
        const string key = FishingLocalization.FishingRodMultiLineHintKey;
        Localization localization = Localization.instance;
        if (localization == null)
        {
            return key;
        }

        // The first pass expands the translation but leaves its $KEY_ token for the second pass.
        // Replace that token, not a localized key name that may also occur in ordinary prose.
        string text = localization.Localize(key);
        if (ZInput.instance != null && !ZInput.IsGamepadActive() &&
            string.Equals(ZInput.instance.GetBoundKeyString("SecondaryAttack", emptyStringOnMissing: true),
                "$button_mouse2", StringComparison.Ordinal))
        {
            int position = 0;
            while (localization.FindNextWord(text, position, out string word, out int start, out int end))
            {
                position = end;
                if (!string.Equals(word, "KEY_SecondaryAttack", StringComparison.Ordinal))
                {
                    continue;
                }

                EnsureInitialized();
                if (_asset == null)
                {
                    break;
                }

                text = text.Remove(start, end - start).Insert(start, MiddleMouseTag);
                position = start + MiddleMouseTag.Length;
            }
        }

        // Rebound keys and controller glyphs continue through the game's normal input localization.
        return text.Contains('$') ? localization.Localize(text) : text;
    }

    internal static void Shutdown()
    {
        if (_asset != null)
        {
            string fallback = Localization.instance != null
                ? Localization.instance.Localize("$button_mouse2")
                : "Mouse-3";
            foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (text != null && text.text != null && text.text.Contains(MiddleMouseTag))
                {
                    Localization.instance?.RemoveTextFromCache(text);
                    text.text = text.text.Replace(MiddleMouseTag, fallback);
                }
            }
        }

        if (_spriteLookup != null && _spriteLookup.TryGetValue(_assetHash, out TMP_SpriteAsset spriteEntry) &&
            ReferenceEquals(spriteEntry, _asset))
        {
            _spriteLookup.Remove(_assetHash);
        }

        if (_materialLookup != null && _materialLookup.TryGetValue(_assetHash, out Material materialEntry) &&
            ReferenceEquals(materialEntry, _material))
        {
            _materialLookup.Remove(_assetHash);
        }

        if (_asset != null) Object.Destroy(_asset);
        if (_material != null) Object.Destroy(_material);
        if (_texture != null) Object.Destroy(_texture);
        _asset = null;
        _material = null;
        _texture = null;
        _spriteLookup = null;
        _materialLookup = null;
        _assetHash = 0;
        _searchedMapRoot = null;
    }

    private static bool IsMiddleMouseSprite(Sprite? sprite)
    {
        return sprite != null &&
               string.Equals(sprite.name, MiddleMouseSpriteName, StringComparison.OrdinalIgnoreCase);
    }

    private static Texture2D CopySpriteTexture(Sprite sprite)
    {
        Vector2[] uv = sprite.uv;
        ushort[] triangles = sprite.triangles;
        if (uv.Length < 3 || triangles.Length < 3)
        {
            throw new InvalidOperationException("The middle mouse sprite has no usable geometry.");
        }

        Vector2 minimum = new(uv.Min(point => point.x), uv.Min(point => point.y));
        Vector2 maximum = new(uv.Max(point => point.x), uv.Max(point => point.y));
        Vector2 size = maximum - minimum;
        int width = Mathf.CeilToInt(size.x * sprite.texture.width);
        int height = Mathf.CeilToInt(size.y * sprite.texture.height);
        if (width <= 0 || height <= 0 || width > 512 || height > 512)
        {
            throw new InvalidOperationException("The middle mouse sprite has invalid atlas bounds.");
        }

        RenderTexture? target = null;
        Texture2D? copy = null;
        RenderTexture previous = RenderTexture.active;
        bool previousSrgbWrite = GL.sRGBWrite;
        try
        {
            target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            GL.sRGBWrite = target.sRGB;
            Graphics.Blit(sprite.texture, target, size, minimum);
            RenderTexture.active = target;
            copy = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                name = AssetName + "_Texture",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            copy.ReadPixels(new Rect(0, 0, width, height), 0, 0, recalculateMipMaps: false);
            Color32[] pixels = copy.GetPixels32();
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Vector2 point = minimum + new Vector2((x + 0.5f) / width * size.x, (y + 0.5f) / height * size.y);
                bool inside = false;
                for (int i = 0; i + 2 < triangles.Length && !inside; i += 3)
                {
                    inside = IsInsideTriangle(point, uv[triangles[i]], uv[triangles[i + 1]], uv[triangles[i + 2]]);
                }

                if (!inside)
                {
                    pixels[y * width + x] = default;
                }
            }

            copy.SetPixels32(pixels);
            copy.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return copy;
        }
        catch
        {
            if (copy != null) Object.Destroy(copy);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            GL.sRGBWrite = previousSrgbWrite;
            if (target != null) RenderTexture.ReleaseTemporary(target);
        }
    }

    private static bool IsInsideTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
    {
        float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        if (Mathf.Abs(area) <= 1e-12f)
        {
            return false;
        }

        float ab = (b.x - a.x) * (point.y - a.y) - (b.y - a.y) * (point.x - a.x);
        float bc = (c.x - b.x) * (point.y - b.y) - (c.y - b.y) * (point.x - b.x);
        float ca = (a.x - c.x) * (point.y - c.y) - (a.y - c.y) * (point.x - c.x);
        return ab >= 0f && bc >= 0f && ca >= 0f || ab <= 0f && bc <= 0f && ca <= 0f;
    }
}

internal static class FishingRodKeyHintSystem
{
    private const string HintName = "TrollingFishing_FishingRodSecondaryHint";
    private static KeyHints? _activeHints;
    private static GameObject? _hint;
    private static GameObject? _template;
    private static RectTransform? _hintRow;
    private static TMP_Text? _actionText;
    private static TMP_Text? _keyText;
    private static bool _gamepadActive;
    private static string _lastAction = string.Empty;
    private static string _lastKey = string.Empty;

    internal static void Initialize(KeyHints hints)
    {
        ShutdownHint();
        _activeHints = hints;
        Update(hints);
    }

    internal static void Update(KeyHints hints)
    {
        if (hints == null)
        {
            return;
        }

        _activeHints = hints;
        if (!ShouldShow(hints))
        {
            Hide();
            return;
        }

        bool gamepadActive = ZInput.IsGamepadActive();
        GameObject? template = gamepadActive ? hints.m_secondaryAttackGP : hints.m_secondaryAttackKB;
        if (template == null || hints.m_fishingHints == null)
        {
            Hide();
            return;
        }

        EnsureHint(hints, template, gamepadActive);
        if (_hint == null || _actionText == null || _keyText == null)
        {
            return;
        }

        string action = FishingLocalization.Localize(FishingLocalization.FishingRodMultiLineActionKey);
        string key = ResolveSecondaryAttackKey(gamepadActive);
        bool layoutChanged = false;
        if (!string.Equals(_lastAction, action, StringComparison.Ordinal) ||
            !string.Equals(_lastKey, key, StringComparison.Ordinal))
        {
            if (_actionText == _keyText)
            {
                // The gamepad template is one combined label; the keyboard template has a keycap.
                SetText(_actionText, action + "  <mspace=0.6em>" + key + "</mspace>");
            }
            else
            {
                SetText(_actionText, action);
                SetText(_keyText, key);
            }

            _lastAction = action;
            _lastKey = key;
            layoutChanged = true;
        }

        if (!_hint.activeSelf)
        {
            _hint.SetActive(true);
            layoutChanged = true;
        }

        if (layoutChanged)
        {
            RebuildFishingHintLayout();
        }
    }

    internal static void Shutdown()
    {
        ShutdownHint();
        _activeHints = null;
        FishingRodInputIcons.Shutdown();
    }

    internal static void OnKeyHintsDestroyed(KeyHints hints)
    {
        if (_activeHints == hints)
        {
            ShutdownHint();
            _activeHints = null;
        }
    }

    private static bool ShouldShow(KeyHints hints)
    {
        Player? player = Player.m_localPlayer;
        return TrollingFishingPlugin.FishingRodMultiLine != null &&
               TrollingFishingPlugin.FishingRodMultiLine.Value.IsOn() &&
               hints.m_keyHintsEnabled &&
               hints.m_fishingHints != null &&
               hints.m_fishingHints.activeInHierarchy &&
               player != null &&
               !player.IsDead() &&
               FishingOverrideSystem.IsFishingRod(player.GetCurrentWeapon());
    }

    private static void EnsureHint(KeyHints hints, GameObject template, bool gamepadActive)
    {
        if (_hint != null && _template == template && _gamepadActive == gamepadActive &&
            _hintRow != null && _hint.transform.parent == _hintRow &&
            _hintRow.parent == hints.m_fishingHints.transform)
        {
            return;
        }

        ShutdownHint();
        UIInputHint? inputHints = hints.m_fishingHints.GetComponent<UIInputHint>();
        GameObject? row = gamepadActive ? inputHints?.m_gamepadHint : inputHints?.m_mouseKeyboardHint;
        if (row == null || row.transform is not RectTransform rowRect ||
            row.GetComponent<HorizontalLayoutGroup>() is not HorizontalLayoutGroup layout)
        {
            return;
        }

        // FishingHints itself has no layout. Its input-specific rows align right with native spacing.
        _hintRow = rowRect;
        _hint = Object.Instantiate(template, _hintRow, worldPositionStays: false);
        _hint.name = HintName;
        _template = template;
        _gamepadActive = gamepadActive;
        ResolveHintTexts(_hint);
        if (layout.reverseArrangement)
        {
            _hint.transform.SetAsLastSibling();
        }
        else
        {
            _hint.transform.SetAsFirstSibling();
        }

        _hint.SetActive(false);
    }

    private static void ResolveHintTexts(GameObject hint)
    {
        TMP_Text[] texts = hint.GetComponentsInChildren<TMP_Text>(includeInactive: true);
        foreach (TMP_Text text in texts)
        {
            if (text is TextMeshProUGUI textMesh)
            {
                textMesh.raycastTarget = false;
            }
        }

        TMP_Text? combinedText = hint.GetComponent<TMP_Text>();
        if (combinedText != null)
        {
            _actionText = _keyText = combinedText;
            return;
        }

        _actionText = hint.transform.Find("Text")?.GetComponent<TMP_Text>();
        _keyText = texts.FirstOrDefault(text =>
            string.Equals(text.name, "Key", StringComparison.OrdinalIgnoreCase) &&
            IsEnabledWithinHint(text.transform, hint.transform));
    }

    private static bool IsEnabledWithinHint(Transform child, Transform hint)
    {
        // The template/root can be inactive while being prepared. Ignore it, but not hidden keycaps.
        for (Transform current = child; current != hint; current = current.parent)
        {
            if (current == null || !current.gameObject.activeSelf)
            {
                return false;
            }
        }

        return true;
    }

    private static string ResolveSecondaryAttackKey(bool gamepadActive)
    {
        string buttonName = gamepadActive ? "JoySecondaryAttack" : "SecondaryAttack";
        string key = ZInput.instance?.GetBoundKeyString(buttonName, emptyStringOnMissing: true) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return gamepadActive ? "RB" : "MMB";
        }

        return Localization.instance != null ? Localization.instance.Localize(key) : key;
    }

    private static void SetText(TMP_Text text, string value)
    {
        Localization.instance?.RemoveTextFromCache(text);
        text.text = value;
    }

    private static void Hide()
    {
        if (_hint != null && _hint.activeSelf)
        {
            _hint.SetActive(false);
            RebuildFishingHintLayout();
        }
    }

    private static void ShutdownHint()
    {
        if (_hint != null)
        {
            _hint.SetActive(false);
            Object.Destroy(_hint);
            if (_hintRow != null)
            {
                LayoutRebuilder.MarkLayoutForRebuild(_hintRow);
            }
        }

        _hint = null;
        _template = null;
        _hintRow = null;
        _actionText = null;
        _keyText = null;
        _lastAction = string.Empty;
        _lastKey = string.Empty;
    }

    private static void RebuildFishingHintLayout()
    {
        if (_hintRow != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_hintRow);
        }
    }
}

[HarmonyPatch(typeof(KeyHints), "Awake")]
internal static class KeyHintsAwakeFishingRodHintPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(KeyHints __instance)
    {
        FishingRodKeyHintSystem.Initialize(__instance);
    }
}

[HarmonyPatch(typeof(KeyHints), "UpdateHints")]
internal static class KeyHintsUpdateFishingRodHintPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(KeyHints __instance)
    {
        FishingRodKeyHintSystem.Update(__instance);
    }
}

[HarmonyPatch(typeof(KeyHints), "OnDestroy")]
internal static class KeyHintsDestroyFishingRodHintPatch
{
    [HarmonyPrefix]
    private static void Prefix(KeyHints __instance)
    {
        FishingRodKeyHintSystem.OnKeyHintsDestroyed(__instance);
    }
}
