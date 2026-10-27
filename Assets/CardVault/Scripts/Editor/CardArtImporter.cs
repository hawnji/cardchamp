using System.Collections.Generic;
using System.Linq;
using CardVault.Core.Content;
using UnityEditor;
using UnityEngine;

namespace CardVault.EditorTools
{
    /// <summary>
    /// Import settings for pre-generated art in Resources/Art (written by tools/art/generate_art.py).
    /// The images are UI-only, so they skip mipmaps and read/write, keep their exact size
    /// (512x720 cards, 1024x400 set banners) and compress to ASTC 6x6 on Android, about 160 KB
    /// per card in memory.
    /// </summary>
    public sealed class CardArtImporter : AssetPostprocessor
    {
        public const string ArtFolder = "Assets/CardVault/Resources/Art/";

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtFolder)) return;
            var importer = (TextureImporter)assetImporter;
            bool banner = assetPath.StartsWith(ArtFolder + "sets/");
            int maxSize = banner ? 1024 : 512;

            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.Compressed;

            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android",
                overridden = true,
                maxTextureSize = maxSize,
                format = TextureImporterFormat.ASTC_6x6,
                compressionQuality = 50,
            });
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "iPhone",
                overridden = true,
                maxTextureSize = maxSize,
                format = TextureImporterFormat.ASTC_6x6,
                compressionQuality = 50,
            });
        }

        /// <summary>Lists cards and sets that still show procedural art, so gaps are easy to spot before a build.</summary>
        [MenuItem("Card Vault/Report Missing Card Art")]
        public static void ReportMissingArt()
        {
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/CardVault/Resources/Content/cardvault_content.json");
            if (json == null)
            {
                Debug.LogError("Card Vault content JSON not found.");
                return;
            }
            var content = ContentDatabase.FromJson(json.text);
            var missing = new List<string>();
            foreach (var card in content.Cards.Values.OrderBy(c => c.Id))
                if (!Exists(card.ArtKey)) missing.Add(card.ArtKey);
            foreach (var set in content.Sets.Values.OrderBy(s => s.Id))
                if (!Exists("sets/" + set.ArtKey)) missing.Add("sets/" + set.ArtKey);

            int total = content.Cards.Count + content.Sets.Count;
            if (missing.Count == 0) Debug.Log("Card Vault: all " + total + " card and set images are present.");
            else Debug.LogWarning("Card Vault: " + missing.Count + " of " + total + " images missing (procedural art is shown instead):\n" + string.Join("\n", missing));
        }

        private static bool Exists(string artKey)
        {
            return !string.IsNullOrEmpty(artKey) && Resources.Load<Texture2D>("Art/" + artKey) != null;
        }
    }
}
