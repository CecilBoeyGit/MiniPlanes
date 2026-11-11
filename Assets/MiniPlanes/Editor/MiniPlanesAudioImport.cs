using UnityEditor;
using UnityEngine;

namespace MiniPlanes.Editor
{
    /// <summary>
    /// First-time import settings for the game's audio. Music stays compressed in memory, loaded in the
    /// background, so six long loops don't cost tens of megabytes or a hitch at start; short effects are
    /// decompressed on load for instant playback. Settings you change later in the Inspector are kept.
    /// </summary>
    public class MiniPlanesAudioImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/MiniPlanes/Audio/")) return;
            if (!assetImporter.importSettingsMissing) return;
            var importer = (AudioImporter)assetImporter;
            var s = importer.defaultSampleSettings;
            if (assetPath.StartsWith("Assets/MiniPlanes/Audio/Music/"))
            {
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.6f;
                importer.loadInBackground = true;
            }
            else
            {
                s.loadType = AudioClipLoadType.DecompressOnLoad;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                s.quality = 0.7f;
                importer.forceToMono = true;
            }
            importer.defaultSampleSettings = s;
        }
    }
}
