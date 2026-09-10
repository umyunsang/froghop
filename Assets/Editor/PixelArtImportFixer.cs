using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// <summary>
/// The Pixel Adventure pack ships with inconsistent import settings: some sheets are 16 pixels
/// per unit and some are 100, so a tile and a trap placed side by side come out at wildly
/// different scales. That is the root cause of the oversized character in the original scene.
///
/// This normalises everything under the pack to 16 PPU with point filtering and no compression,
/// and re-pivots the character sheets to bottom-centre so <c>SpriteRenderer.flipX</c> mirrors
/// around the body instead of teleporting the sprite sideways.
///
/// The Terrain folder is deliberately skipped: its 144 sliced sprites are referenced by
/// existing Tile assets, and re-slicing would invalidate those references.
/// </summary>
public static class PixelArtImportFixer
{
    public const int TargetPPU = 16;
    private const string PackRoot = "Assets/Pixel Adventure 1";

    [MenuItem("Tools/2D Game/1. Fix Pixel Art Import Settings")]
    public static void FixAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { PackRoot });
        int changed = 0;

        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.Contains("/Terrain/")) continue;   // see class summary

                EditorUtility.DisplayProgressBar("Fixing pixel art import",
                    Path.GetFileName(path), i / (float)guids.Length);

                if (Fix(path)) changed++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.Refresh();
        Debug.Log($"[PixelArtImportFixer] Normalised {changed} texture(s) to {TargetPPU} PPU.");
    }

    private static bool Fix(string path)
    {
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return false;

        bool isCharacter = path.Contains("/Main Characters/");
        bool isBackground = path.Contains("/Background/");
        // Props that rest on the floor get a bottom-centre pivot so the builder can place them
        // at the exact ground Y. Free-floating hazards (saw, spike head) and pickups stay centred.
        bool sitsOnGround = path.Contains("/Traps/Trampoline/")
                         || path.Contains("/Traps/Spikes/")
                         || path.Contains("/Traps/Fire/")
                         || path.Contains("/Items/Boxes/")
                         || path.Contains("/Items/Checkpoints/");
        bool bottomPivot = isCharacter || sitsOnGround;
        bool dirty = false;

        if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; dirty = true; }
        if (ti.spritePixelsPerUnit != TargetPPU) { ti.spritePixelsPerUnit = TargetPPU; dirty = true; }
        if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; dirty = true; }
        if (ti.textureCompression != TextureImporterCompression.Uncompressed)
        {
            ti.textureCompression = TextureImporterCompression.Uncompressed; dirty = true;
        }
        if (ti.mipmapEnabled) { ti.mipmapEnabled = false; dirty = true; }
        if (ti.maxTextureSize < 2048) { ti.maxTextureSize = 2048; dirty = true; }

        // Backgrounds are drawn with a tiled SpriteRenderer, which needs full-rect geometry.
        TextureImporterSettings settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        SpriteMeshType wantMesh = isBackground ? SpriteMeshType.FullRect : SpriteMeshType.Tight;
        if (settings.spriteMeshType != wantMesh) { settings.spriteMeshType = wantMesh; dirty = true; }
        if (settings.spriteExtrude != 1) { settings.spriteExtrude = 1; dirty = true; }

        // Bottom-centre pivot: feet sit on the transform, and flipX mirrors in place.
        if (bottomPivot)
        {
            if (settings.spriteAlignment != (int)SpriteAlignment.Custom
             || settings.spritePivot != new Vector2(0.5f, 0f))
            {
                settings.spriteAlignment = (int)SpriteAlignment.Custom;
                settings.spritePivot = new Vector2(0.5f, 0f);
                dirty = true;
            }
        }
        ti.SetTextureSettings(settings);

        TextureImporterPlatformSettings ps = ti.GetDefaultPlatformTextureSettings();
        if (ps.format != TextureImporterFormat.RGBA32 || ps.textureCompression != TextureImporterCompression.Uncompressed)
        {
            ps.overridden = true;
            ps.format = TextureImporterFormat.RGBA32;
            ps.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SetPlatformTextureSettings(ps);
            dirty = true;
        }

        if (bottomPivot && ti.spriteImportMode == SpriteImportMode.Multiple)
            dirty |= RepivotSlices(ti, new Vector2(0.5f, 0f));

        if (dirty) ti.SaveAndReimport();
        return dirty;
    }

    /// <summary>Set a custom pivot on every slice of a multi-sprite sheet.</summary>
    private static bool RepivotSlices(TextureImporter ti, Vector2 pivot)
    {
        SpriteDataProviderFactories factories = new SpriteDataProviderFactories();
        factories.Init();
        ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(ti);
        if (provider == null) return false;

        provider.InitSpriteEditorDataProvider();
        SpriteRect[] rects = provider.GetSpriteRects();
        if (rects == null || rects.Length == 0) return false;

        bool changed = false;
        for (int i = 0; i < rects.Length; i++)
        {
            if (rects[i].alignment == SpriteAlignment.Custom && rects[i].pivot == pivot) continue;
            rects[i].alignment = SpriteAlignment.Custom;
            rects[i].pivot = pivot;
            changed = true;
        }
        if (!changed) return false;

        provider.SetSpriteRects(rects);
        provider.Apply();
        return true;
    }

    // ------------------------------------------------------------------ shared loaders

    /// <summary>
    /// All sprites in a sheet, ordered by the numeric suffix Unity appends when slicing
    /// (<c>Idle (32x32)_0</c>, <c>_1</c>, ...). Plain string sorting would put _10 before _2.
    /// </summary>
    public static Sprite[] LoadFrames(string assetPath)
    {
        Object[] all = AssetDatabase.LoadAllAssetsAtPath(assetPath);
        List<Sprite> sprites = new List<Sprite>();
        foreach (Object o in all)
        {
            Sprite s = o as Sprite;
            if (s != null) sprites.Add(s);
        }
        if (sprites.Count == 0)
        {
            Debug.LogWarning($"[PixelArtImportFixer] No sprites found at {assetPath}");
            return new Sprite[0];
        }
        sprites.Sort((a, b) => FrameIndex(a.name).CompareTo(FrameIndex(b.name)));
        return sprites.ToArray();
    }

    public static Sprite LoadSingle(string assetPath)
    {
        Sprite[] f = LoadFrames(assetPath);
        return f.Length > 0 ? f[0] : null;
    }

    private static int FrameIndex(string name)
    {
        int u = name.LastIndexOf('_');
        int n;
        if (u >= 0 && int.TryParse(name.Substring(u + 1), out n)) return n;
        return 0;
    }
}
