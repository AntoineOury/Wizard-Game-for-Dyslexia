using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OtherwiseLabs.TerrainTools
{
    /// <summary>
    /// Inspector for the streaming world: a prefab drop area, live chunk counters
    /// and a budget estimate, since the settings that matter most here are the
    /// ones that decide how much is on screen at once.
    /// </summary>
    [CustomEditor(typeof(InfiniteTerrainStreamer))]
    public class InfiniteTerrainStreamerEditor : Editor
    {
        GUIStyle _dropAreaStyle;

        // Which list the drop area adds to: 0 = global, 1..N = biomes[N-1].
        int _dropTargetIndex;

        // Rebuilding on every camera nudge would be unusable, so the preview only
        // refreshes when the camera actually crosses into a different chunk.
        Vector2Int _lastPreviewCoord;
        bool _hasPreviewCoord;

        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;

            // Deselecting shouldn't leave a preview behind: it is throwaway state,
            // and an invisible half-world is worse than no world.
            if (target is InfiniteTerrainStreamer streamer && streamer != null && !Application.isPlaying)
                streamer.ClearScenePreview();
            _hasPreviewCoord = false;
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            // The preview must not survive into play mode, or it would sit on top
            // of the chunks the streamer builds for real.
            if (change != PlayModeStateChange.ExitingEditMode) return;
            if (target is InfiniteTerrainStreamer streamer && streamer != null)
                streamer.ClearScenePreview();
            _hasPreviewCoord = false;
        }

        void OnSceneGUI(SceneView sceneView)
        {
            if (Application.isPlaying) return;
            if (target is not InfiniteTerrainStreamer streamer || streamer == null) return;

            if (!streamer.previewInScene)
            {
                if (_hasPreviewCoord)
                {
                    streamer.ClearScenePreview();
                    _hasPreviewCoord = false;
                }
                return;
            }

            Vector3 center = streamer.previewFollowsSceneCamera && sceneView.camera != null
                ? sceneView.camera.transform.position
                : streamer.transform.position;

            Vector2Int coord = streamer.ChunkCoordOf(center);
            if (_hasPreviewCoord && coord == _lastPreviewCoord) return;

            streamer.BuildScenePreview(center);
            _lastPreviewCoord = coord;
            _hasPreviewCoord = true;
        }

        public override void OnInspectorGUI()
        {
            var streamer = (InfiniteTerrainStreamer)target;

            DrawHeaderBox(streamer);
            DrawPreviewControls(streamer);

            EditorGUILayout.Space(4);
            DrawDefaultInspector();

            EditorGUILayout.Space(6);
            DrawBiomeStrip(streamer);
            DrawDropArea(streamer);

            EditorGUILayout.Space(6);
            DrawBudget(streamer);
            DrawRuntimeStats(streamer);
        }

        void DrawHeaderBox(InfiniteTerrainStreamer streamer)
        {
            EditorGUILayout.HelpBox(
                "Streams terrain around the viewer. Chunks are regenerated from the seed rather than saved, " +
                "so walking away and back reproduces the same landscape exactly.\n\n" +
                "Assign a Viewer (the player), a Terrain Material, drop in prefabs, then press Play.",
                MessageType.Info);

            if (streamer.viewer == null)
            {
                EditorGUILayout.HelpBox(
                    "No Viewer assigned — will fall back to Camera.main at runtime.",
                    MessageType.Warning);
            }

            if (streamer.terrainMaterial == null)
            {
                EditorGUILayout.HelpBox(
                    "No Terrain Material assigned. Chunks will render with the default magenta error material. " +
                    "Create one using the 'OtherwiseLabs/Terrain Vertex Color' shader.",
                    MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button(Application.isPlaying
                        ? "Regenerate World"
                        : "Regenerate World  (play mode only)", GUILayout.Height(26)))
                {
                    streamer.RegenerateWorld();
                }
            }

            if (GUILayout.Button("Randomize Seeds"))
            {
                Undo.RecordObject(streamer, "Randomize Seeds");
                streamer.RandomizeSeeds();
                EditorUtility.SetDirty(streamer);
                if (Application.isPlaying) streamer.RegenerateWorld();
            }
        }

        /// <summary>
        /// Preview toggle and refresh. Kept at the top because composing the world
        /// in the Scene view is the main reason to select this component at all.
        /// </summary>
        void DrawPreviewControls(InfiniteTerrainStreamer streamer)
        {
            if (Application.isPlaying) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                bool wanted = GUILayout.Toggle(
                    streamer.previewInScene,
                    streamer.previewInScene ? "Scene Preview: ON" : "Scene Preview: OFF",
                    "Button", GUILayout.Height(26));

                if (wanted != streamer.previewInScene)
                {
                    Undo.RecordObject(streamer, "Toggle Scene Preview");
                    streamer.previewInScene = wanted;
                    EditorUtility.SetDirty(streamer);

                    if (!wanted) streamer.ClearScenePreview();
                    _hasPreviewCoord = false;
                    SceneView.RepaintAll();
                }

                using (new EditorGUI.DisabledScope(!streamer.previewInScene))
                {
                    if (GUILayout.Button("Refresh", GUILayout.Height(26), GUILayout.Width(80)))
                    {
                        streamer.ClearScenePreview();
                        _hasPreviewCoord = false;
                        SceneView.RepaintAll();
                    }
                }
            }

            if (streamer.previewInScene)
            {
                EditorGUILayout.HelpBox(
                    "Preview is live in the Scene view. It is generated from the same seed as runtime, " +
                    "so what you see is exactly what the player will walk through.\n" +
                    "Preview objects are never saved into the scene, and clear on Play or deselect. " +
                    "Press Refresh after changing noise or asset settings.",
                    MessageType.None);
            }
        }

        void DrawDropArea(InfiniteTerrainStreamer streamer)
        {
            _dropAreaStyle ??= new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                richText = true,
            };

            // With biomes defined, dropped prefabs need a home: the global list
            // (appears everywhere) or one specific biome.
            string targetLabel = "Global (every biome)";
            if (streamer.biomes != null && streamer.biomes.Count > 0)
            {
                var labels = new string[streamer.biomes.Count + 1];
                labels[0] = "Global (every biome)";
                for (int i = 0; i < streamer.biomes.Count; i++)
                {
                    string biomeName = streamer.biomes[i] != null && !string.IsNullOrWhiteSpace(streamer.biomes[i].name)
                        ? streamer.biomes[i].name : $"Biome {i}";
                    labels[i + 1] = $"{biomeName} (only)";
                }
                _dropTargetIndex = Mathf.Clamp(_dropTargetIndex, 0, labels.Length - 1);
                _dropTargetIndex = EditorGUILayout.Popup("Add Dropped Assets To", _dropTargetIndex, labels);
                targetLabel = labels[_dropTargetIndex];
            }
            else
            {
                _dropTargetIndex = 0;
            }

            Rect dropRect = GUILayoutUtility.GetRect(0f, 46f, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, $"<b>+ Drop prefabs here \u2192 {targetLabel}</b>\nMax Instances is per chunk", _dropAreaStyle);

            Event evt = Event.current;
            if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform) return;
            if (!dropRect.Contains(evt.mousePosition)) return;

            bool anyGameObject = false;
            foreach (Object dragged in DragAndDrop.objectReferences)
                if (dragged is GameObject) { anyGameObject = true; break; }

            DragAndDrop.visualMode = anyGameObject ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

            if (evt.type == EventType.DragPerform && anyGameObject)
            {
                DragAndDrop.AcceptDrag();
                Undo.RecordObject(streamer, "Add Environment Assets");

                List<EnvironmentAssetRule> targetList = streamer.environmentAssets;
                if (_dropTargetIndex > 0 && streamer.biomes != null && _dropTargetIndex <= streamer.biomes.Count
                    && streamer.biomes[_dropTargetIndex - 1] != null)
                {
                    targetList = streamer.biomes[_dropTargetIndex - 1].environmentAssets ??= new List<EnvironmentAssetRule>();
                }

                foreach (Object dragged in DragAndDrop.objectReferences)
                {
                    if (dragged is not GameObject prefab) continue;

                    var rule = new EnvironmentAssetRule
                    {
                        prefab = prefab,
                        displayName = prefab.name,
                        // Per-chunk counts, so a much smaller number than the finite
                        // generator's per-world default.
                        maxInstances = 40,
                    };
                    ProceduralTerrainGenerator.ApplyCategoryDefaults(
                        rule, ProceduralTerrainGenerator.GuessCategory(prefab.name));
                    targetList.Add(rule);
                }

                EditorUtility.SetDirty(streamer);
            }
            evt.Use();
        }

        /// <summary>
        /// Rough cost of the current radii. The point is to make the quadratic
        /// nature of view distance visible before it is discovered by profiler.
        /// </summary>
        static void DrawBudget(InfiniteTerrainStreamer streamer)
        {
            int terrainChunks = (streamer.viewDistanceInChunks * 2 + 1);
            terrainChunks *= terrainChunks;

            int propChunks = (streamer.assetDistanceInChunks * 2 + 1);
            propChunks *= propChunks;

            int trisPerChunk = streamer.chunkResolution * streamer.chunkResolution * 2;
            long totalTris = (long)trisPerChunk * terrainChunks;

            float propsPerChunk = 0f;
            foreach (EnvironmentAssetRule rule in streamer.environmentAssets)
            {
                if (rule == null || rule.prefab == null) continue;
                propsPerChunk += rule.density * rule.maxInstances;
            }
            // Biome rules only spawn on their share of the world, so weight them
            // by coverage rather than counting every biome's list in full.
            if (streamer.biomes != null && streamer.biomes.Count > 0)
            {
                float totalCoverage = 0f;
                foreach (BiomeDefinition biome in streamer.biomes)
                    totalCoverage += Mathf.Max(0.01f, biome != null ? biome.coverage : 1f);
                foreach (BiomeDefinition biome in streamer.biomes)
                {
                    if (biome?.environmentAssets == null) continue;
                    float share = Mathf.Max(0.01f, biome.coverage) / totalCoverage;
                    foreach (EnvironmentAssetRule rule in biome.environmentAssets)
                    {
                        if (rule == null || rule.prefab == null) continue;
                        propsPerChunk += share * rule.density * rule.maxInstances;
                    }
                }
            }
            long totalProps = (long)(propsPerChunk * propChunks);

            float loadedSpan = (streamer.viewDistanceInChunks * 2 + 1) * streamer.chunkSize;

            var message =
                $"Loaded area: {loadedSpan:n0} x {loadedSpan:n0} units  ({terrainChunks} chunks)\n" +
                $"Terrain: ~{totalTris:n0} triangles\n" +
                $"Props: ~{totalProps:n0} instances across {propChunks} chunks";

            MessageType severity = MessageType.None;
            if (totalTris > 4000000 || totalProps > 20000) severity = MessageType.Warning;
            if (totalTris > 12000000 || totalProps > 60000) severity = MessageType.Error;

            if (severity != MessageType.None)
                message += "\n\nThat is heavy. Reduce View Distance, Asset Distance, Chunk Resolution, or per-rule Density.";

            EditorGUILayout.HelpBox(message, severity);
        }

        static void DrawRuntimeStats(InfiniteTerrainStreamer streamer)
        {
            if (!Application.isPlaying) return;

            EditorGUILayout.HelpBox(
                $"Active chunks: {streamer.ActiveChunkCount}   " +
                $"Pooled: {streamer.PooledChunkCount}   " +
                $"Queued: {streamer.QueuedChunkCount}",
                MessageType.None);

            // Counters change as the player moves, so keep the inspector live.
            EditorUtility.SetDirty(streamer);
        }

        /// <summary>
        /// The climate axis with each biome's share of it, colored from its own
        /// gradient. Makes coverage and list order tangible: neighbours on this
        /// strip are the biomes that will actually border each other in the world.
        /// </summary>
        static void DrawBiomeStrip(InfiniteTerrainStreamer streamer)
        {
            List<BiomeDefinition> biomes = streamer.biomes;
            if (biomes == null || biomes.Count == 0) return;

            EditorGUILayout.LabelField("Biome Layout (climate low \u2192 high)", EditorStyles.boldLabel);
            Rect strip = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));

            float totalCoverage = 0f;
            foreach (BiomeDefinition biome in biomes)
                totalCoverage += Mathf.Max(0.01f, biome != null ? biome.coverage : 1f);

            if (Event.current.type == EventType.Repaint)
            {
                float x = strip.x;
                foreach (BiomeDefinition biome in biomes)
                {
                    float share = Mathf.Max(0.01f, biome != null ? biome.coverage : 1f) / totalCoverage;
                    float width = share * strip.width;
                    Color color = biome != null && biome.colorByHeight != null && biome.colorByHeight.colorKeys.Length > 0
                        ? biome.colorByHeight.Evaluate(0.55f)
                        : Color.gray;
                    EditorGUI.DrawRect(new Rect(x, strip.y, width + 1f, strip.height), color);
                    x += width;
                }
            }

            Rect labels = GUILayoutUtility.GetRect(0f, 14f, GUILayout.ExpandWidth(true));
            var tiny = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter };
            float labelX = labels.x;
            foreach (BiomeDefinition biome in biomes)
            {
                float share = Mathf.Max(0.01f, biome != null ? biome.coverage : 1f) / totalCoverage;
                float width = share * labels.width;
                if (width >= 30f)
                    GUI.Label(new Rect(labelX, labels.y, width, labels.height), biome != null ? biome.name : "?", tiny);
                labelX += width;
            }
            EditorGUILayout.Space(4);
        }

        [MenuItem("GameObject/3D Object/Infinite Terrain Streamer (Otherwise Labs)", false, 11)]
        static void CreateStreamer(MenuCommand menuCommand)
        {
            var go = new GameObject("Infinite Terrain");
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            go.AddComponent<InfiniteTerrainStreamer>();
            Undo.RegisterCreatedObjectUndo(go, "Create Infinite Terrain Streamer");
            Selection.activeGameObject = go;
        }
    }
}
