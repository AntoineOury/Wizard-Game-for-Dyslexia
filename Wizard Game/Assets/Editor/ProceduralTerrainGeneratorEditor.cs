using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OtherwiseLabs.TerrainTools
{
    /// <summary>
    /// Inspector for ProceduralTerrainGenerator: build buttons, a drag & drop
    /// area for adding environment prefabs, mesh export, and live rebuild.
    /// </summary>
    [CustomEditor(typeof(ProceduralTerrainGenerator))]
    public class ProceduralTerrainGeneratorEditor : Editor
    {
        static bool _rebuildQueued;
        GUIStyle _dropAreaStyle;

        public override void OnInspectorGUI()
        {
            var generator = (ProceduralTerrainGenerator)target;

            DrawBuildButtons(generator);

            EditorGUILayout.Space(6);
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool settingsChanged = EditorGUI.EndChangeCheck();

            EditorGUILayout.Space(6);
            DrawZoneRuler(generator);

            EditorGUILayout.Space(6);
            DrawDropArea(generator);
            DrawUtilities(generator);
            DrawStats(generator);

            if (settingsChanged && generator.autoRebuild)
                QueueRebuild(generator);
        }

        // ------------------------------------------------------------------
        // Build buttons
        // ------------------------------------------------------------------

        void DrawBuildButtons(ProceduralTerrainGenerator generator)
        {
            EditorGUILayout.HelpBox(
                "1. Tune the noise settings (terrain rebuilds live while Auto Rebuild is on).\n" +
                "2. Drop prefabs into the area below, set each Density slider (0-1).\n" +
                                "3. Press Build All.",
                MessageType.Info);

            DrawMaterialWarning(generator);

            if (GUILayout.Button("Build All  (Terrain + Environment)", GUILayout.Height(34)))
                RunBuildStep(generator, generator.GenerateAll);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate Terrain", GUILayout.Height(26)))
                    RunBuildStep(generator, generator.GenerateTerrain);
                if (GUILayout.Button("Scatter Environment", GUILayout.Height(26)))
                    RunBuildStep(generator, generator.ScatterEnvironment);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Randomize Seeds"))
                {
                    Undo.RecordObject(generator, "Randomize Seeds");
                    generator.RandomizeSeeds();
                    EditorUtility.SetDirty(generator);
                    RunBuildStep(generator, generator.GenerateAll);
                }
                if (GUILayout.Button("Clear Environment"))
                    RunBuildStep(generator, generator.ClearEnvironment);
            }
        }

                /// <summary>
        /// The height gradient is stored in mesh vertex colors, which URP/Lit and
        /// Standard both ignore. Warn (and offer a one-click fix) when the terrain
        /// material isn't using the vertex color shader.
        /// </summary>
        static void DrawMaterialWarning(ProceduralTerrainGenerator generator)
        {
            var meshRenderer = generator.GetComponent<MeshRenderer>();
            if (meshRenderer == null) return;

            Material material = meshRenderer.sharedMaterial;
            bool shaderInProject = ProceduralTerrainGenerator.VertexColorShaderAvailable;
            bool materialUsesIt = material != null && material.shader != null
                && material.shader.name == ProceduralTerrainGenerator.TerrainShaderName;

            if (materialUsesIt) return;

            if (!shaderInProject)
            {
                EditorGUILayout.HelpBox(
                    $"Shader '{ProceduralTerrainGenerator.TerrainShaderName}' is not in this project.\n" +
                    "The terrain will render untinted, because URP/Lit ignores the vertex colors " +
                    "that carry the height gradient. Add TerrainVertexColor.shader under Assets/Shaders.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                "The terrain material is not using the vertex color shader, so the height " +
                "gradient (water > sand > grass > rock > snow) will not show.",
                MessageType.Warning);

            if (GUILayout.Button("Fix Terrain Material  (apply vertex color shader)"))
            {
                if (material != null) Undo.RecordObject(material, "Apply Vertex Color Shader");
                Undo.RecordObject(meshRenderer, "Apply Vertex Color Shader");
                if (generator.ApplyVertexColorShader())
                {
                    if (material != null) EditorUtility.SetDirty(material);
                    MarkDirty(generator);
                    SceneView.RepaintAll();
                }
            }
        }

        static void RunBuildStep(ProceduralTerrainGenerator generator, System.Action step)
        {
            step();
            MarkDirty(generator);
            SceneView.RepaintAll();
        }

        static void MarkDirty(ProceduralTerrainGenerator generator)
        {
            if (Application.isPlaying || generator == null) return;
            EditorUtility.SetDirty(generator);
            EditorSceneManager.MarkSceneDirty(generator.gameObject.scene);
        }

        // Coalesce slider-drag change events into a single rebuild per editor tick.
        static void QueueRebuild(ProceduralTerrainGenerator generator)
        {
            if (_rebuildQueued) return;
            _rebuildQueued = true;
            EditorApplication.delayCall += () =>
            {
                _rebuildQueued = false;
                if (generator == null) return;
                generator.GenerateTerrain();
                MarkDirty(generator);
                SceneView.RepaintAll();
            };
        }

        // ------------------------------------------------------------------
        // Terrain zone ruler
        // ------------------------------------------------------------------

        /// <summary>
        /// Draws the height gradient as a strip with the zone boundaries marked on
        /// it, so it's obvious which colors count as Water / Shore / Grass / Rock /
        /// Snow when assigning Allowed Zones to an asset.
        /// </summary>
        static void DrawZoneRuler(ProceduralTerrainGenerator generator)
        {
            var bands = generator.zoneBands;
            if (bands == null || generator.colorByHeight == null) return;
            bands.Sanitize();

            EditorGUILayout.LabelField("Terrain Zones (low → high)", EditorStyles.boldLabel);

            Rect strip = GUILayoutUtility.GetRect(0f, 26f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                // Sample the gradient across the strip.
                const int slices = 128;
                float sliceWidth = strip.width / slices;
                for (int i = 0; i < slices; i++)
                {
                    float t = i / (float)(slices - 1);
                    var sliceRect = new Rect(strip.x + i * sliceWidth, strip.y, sliceWidth + 1f, strip.height);
                    EditorGUI.DrawRect(sliceRect, generator.colorByHeight.Evaluate(t));
                }

                // Boundary ticks.
                var boundaries = new[] { bands.waterLevel, bands.shoreLevel, bands.grassLevel, bands.rockLevel };
                foreach (float b in boundaries)
                {
                    var tick = new Rect(strip.x + b * strip.width - 1f, strip.y, 2f, strip.height);
                    EditorGUI.DrawRect(tick, Color.black);
                }
            }

            // Zone name labels under their band.
            Rect labels = GUILayoutUtility.GetRect(0f, 14f, GUILayout.ExpandWidth(true));
            var zones = new[]
            {
                TerrainZone.Water, TerrainZone.Shore, TerrainZone.Grass,
                TerrainZone.Rock, TerrainZone.Snow,
            };
            var tiny = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperCenter };
            foreach (TerrainZone zone in zones)
            {
                Vector2 range = bands.GetRange(zone);
                float x = labels.x + range.x * labels.width;
                float w = (range.y - range.x) * labels.width;
                if (w < 24f) continue; // too narrow to label legibly
                GUI.Label(new Rect(x, labels.y, w, labels.height), zone.ToString(), tiny);
            }

            EditorGUILayout.HelpBox(
                "'Restrict To Zones' + 'Allowed Zones' is a hard yes/no — uncheck Water so trees never spawn in lakes.\n" +
                "'Use Zone Weights' biases how often instead — rocks at Shore 3 / Rock 1 appear three times as densely " +
                "on sand as on cliffs. Only the ratios matter; 0 excludes a zone.",
                MessageType.None);
        }

        // ------------------------------------------------------------------
        // Drag & drop area for environment prefabs
        // ------------------------------------------------------------------

        void DrawDropArea(ProceduralTerrainGenerator generator)
        {
            _dropAreaStyle ??= new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                richText = true,
            };

            Rect dropRect = GUILayoutUtility.GetRect(0f, 48f, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "<b>+ Drop prefabs here to add environment assets</b>\ntrees, buildings, rocks, grass ...", _dropAreaStyle);

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
                Undo.RecordObject(generator, "Add Environment Assets");
                var added = new System.Text.StringBuilder();
                foreach (Object dragged in DragAndDrop.objectReferences)
                {
                    if (dragged is not GameObject prefab) continue;
                    generator.AddEnvironmentAsset(prefab);
                    var category = ProceduralTerrainGenerator.GuessCategory(prefab.name);
                    added.Append($"\n  • {prefab.name} → {category}");
                    if (category == ProceduralTerrainGenerator.AssetCategory.Generic)
                        added.Append("  (no keyword matched — set its filters by hand)");
                }
                if (added.Length > 0)
                    Debug.Log($"[{generator.name}] Added environment assets:{added}", generator);
                MarkDirty(generator);
            }
            evt.Use();
        }

        // ------------------------------------------------------------------
        // Utilities + stats
        // ------------------------------------------------------------------

        void DrawUtilities(ProceduralTerrainGenerator generator)
        {
            if (GUILayout.Button("Save Terrain Mesh As Asset..."))
                SaveMeshAsset(generator);
        }

        static void SaveMeshAsset(ProceduralTerrainGenerator generator)
        {
            Mesh mesh = generator.GetComponent<MeshFilter>().sharedMesh;
            if (mesh == null)
            {
                EditorUtility.DisplayDialog("No mesh", "Generate the terrain first.", "OK");
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject(
                "Save Terrain Mesh", generator.name + "_Mesh", "asset",
                "Choose where to save the generated terrain mesh.");
            if (string.IsNullOrEmpty(path)) return;

            var copy = Instantiate(mesh);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(copy, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(copy);
        }

        void DrawStats(ProceduralTerrainGenerator generator)
        {
            if (generator.LastVertexCount <= 0) return;
            EditorGUILayout.HelpBox(
                $"Mesh: {generator.LastVertexCount:n0} vertices, {generator.LastTriangleCount:n0} triangles " +
                $"({generator.LastGenerateMilliseconds:0.#} ms)  •  Environment: {generator.LastScatterCount:n0} instances",
                MessageType.None);
        }

        // ------------------------------------------------------------------
        // Scene creation shortcut
        // ------------------------------------------------------------------

        [MenuItem("GameObject/3D Object/Procedural Terrain (Otherwise Labs)", false, 10)]
        static void CreateProceduralTerrain(MenuCommand menuCommand)
        {
            var go = new GameObject("Procedural Terrain");
            GameObjectUtility.SetParentAndAlign(go, menuCommand.context as GameObject);
            var generator = go.AddComponent<ProceduralTerrainGenerator>();
            Undo.RegisterCreatedObjectUndo(go, "Create Procedural Terrain");
            Selection.activeGameObject = go;
            generator.GenerateTerrain();
        }
    }
}