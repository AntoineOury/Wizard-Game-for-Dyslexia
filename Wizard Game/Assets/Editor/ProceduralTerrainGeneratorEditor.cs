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
                foreach (Object dragged in DragAndDrop.objectReferences)
                {
                    if (dragged is GameObject prefab)
                        generator.AddEnvironmentAsset(prefab);
                }
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