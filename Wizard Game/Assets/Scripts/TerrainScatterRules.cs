using System;
using System.Collections.Generic;
using UnityEngine;

namespace OtherwiseLabs.TerrainTools
{
    // Shared vocabulary of both terrain systems: the finite Procedural Terrain
    // Generator and the Infinite Terrain Streamer each consume these types, and
    // neither may reference the other (they must stay independently deletable).
    // These classes used to live inside ProceduralTerrainGenerator.cs, which
    // made the streamer depend on the generator just to reuse a data type.
    // Moving them is serialization-safe: Unity identifies plain [Serializable]
    // classes by type name, not by source file.

    /// <summary>
    /// Named surface bands of the terrain, ordered low to high. Assets pick the
    /// zones they are allowed to spawn in, so "keep trees out of the water" is a
    /// checkbox rather than a guess at a normalized height number.
    /// </summary>
    [Flags]
    public enum TerrainZone
    {
        None = 0,
        Water = 1 << 0,
        Shore = 1 << 1,
        Grass = 1 << 2,
        Rock = 1 << 3,
        Snow = 1 << 4,
        Everything = Water | Shore | Grass | Rock | Snow,
    }

    /// <summary>
    /// Normalized height thresholds that split the terrain into zones. Defaults
    /// line up with the default height gradient, so the blue part of the terrain
    /// really is the Water zone.
    /// </summary>
    [Serializable]
    public class TerrainZoneBands
    {
        [Tooltip("Everything below this normalized height is Water.")]
        [Range(0f, 1f)] public float waterLevel = 0.33f;

        [Tooltip("Water up to here is Shore (beach / lake edge).")]
        [Range(0f, 1f)] public float shoreLevel = 0.42f;

        [Tooltip("Shore up to here is Grass (the main habitable band).")]
        [Range(0f, 1f)] public float grassLevel = 0.68f;

        [Tooltip("Grass up to here is Rock. Above it is Snow.")]
        [Range(0f, 1f)] public float rockLevel = 0.86f;

        /// <summary>Forces the thresholds to stay in ascending order.</summary>
        public void Sanitize()
        {
            waterLevel = Mathf.Clamp01(waterLevel);
            shoreLevel = Mathf.Clamp(shoreLevel, waterLevel, 1f);
            grassLevel = Mathf.Clamp(grassLevel, shoreLevel, 1f);
            rockLevel = Mathf.Clamp(rockLevel, grassLevel, 1f);
        }

        /// <summary>Zone that a normalized terrain height falls into.</summary>
        public TerrainZone GetZone(float normalizedHeight)
        {
            if (normalizedHeight < waterLevel) return TerrainZone.Water;
            if (normalizedHeight < shoreLevel) return TerrainZone.Shore;
            if (normalizedHeight < grassLevel) return TerrainZone.Grass;
            if (normalizedHeight < rockLevel) return TerrainZone.Rock;
            return TerrainZone.Snow;
        }

        /// <summary>Normalized height range a zone covers, for previews.</summary>
        public Vector2 GetRange(TerrainZone zone)
        {
            switch (zone)
            {
                case TerrainZone.Water: return new Vector2(0f, waterLevel);
                case TerrainZone.Shore: return new Vector2(waterLevel, shoreLevel);
                case TerrainZone.Grass: return new Vector2(shoreLevel, grassLevel);
                case TerrainZone.Rock: return new Vector2(grassLevel, rockLevel);
                case TerrainZone.Snow: return new Vector2(rockLevel, 1f);
                default: return new Vector2(0f, 1f);
            }
        }
    }

    /// <summary>
    /// Relative likelihood of an asset appearing in each zone. Unlike Allowed
    /// Zones (a hard yes/no), these bias how often it shows up: rocks set to
    /// 3 on Shore and 1 on Rock appear roughly three times as densely on sand
    /// as they do on cliffs. 0 excludes a zone entirely.
    /// </summary>
    [Serializable]
    public class ZoneWeights
    {
        [Min(0f)] public float water = 1f;
        [Min(0f)] public float shore = 1f;
        [Min(0f)] public float grass = 1f;
        [Min(0f)] public float rock = 1f;
        [Min(0f)] public float snow = 1f;

        public float Get(TerrainZone zone)
        {
            switch (zone)
            {
                case TerrainZone.Water: return Mathf.Max(0f, water);
                case TerrainZone.Shore: return Mathf.Max(0f, shore);
                case TerrainZone.Grass: return Mathf.Max(0f, grass);
                case TerrainZone.Rock: return Mathf.Max(0f, rock);
                case TerrainZone.Snow: return Mathf.Max(0f, snow);
                default: return 0f;
            }
        }

        /// <summary>
        /// Largest weight among the zones an asset is actually allowed into.
        /// Acceptance is scaled by this so the favourite zone always accepts,
        /// keeping the sampler efficient while preserving the ratios.
        /// </summary>
        public float MaxAmong(TerrainZone allowed, bool restrict)
        {
            float max = 0f;
            foreach (TerrainZone zone in AllZones)
            {
                if (restrict && (allowed & zone) == 0) continue;
                max = Mathf.Max(max, Get(zone));
            }
            return max;
        }

        public static readonly TerrainZone[] AllZones =
        {
            TerrainZone.Water, TerrainZone.Shore, TerrainZone.Grass,
            TerrainZone.Rock, TerrainZone.Snow,
        };
    }

    /// <summary>
    /// One scatterable environment asset (tree, rock, building, ...) and the rules
    /// that control where and how it gets placed on the generated terrain.
    /// </summary>
    [Serializable]
    public class EnvironmentAssetRule
    {
        [Tooltip("Prefab to scatter across the terrain.")]
        public GameObject prefab;

        [Tooltip("Name used for the container object and spawned instances (e.g. \"Trees\").")]
        public string displayName = "New Asset";

        [Tooltip("Tag applied to every spawned instance. Missing tags are added to the project automatically in the Editor. Leave empty for Untagged.")]
        public string instanceTag = "";

        [Tooltip("Randomization amount, 0-1. Multiplied by Max Instances to get the target count (0 = none, 1 = Max Instances).")]
        [Range(0f, 1f)] public float density = 0.5f;

        [Tooltip("Instance count when Density is 1.")]
        [Min(1)] public int maxInstances = 150;

        [Header("Transform Randomization")]
        [Tooltip("Random uniform scale range applied on top of the prefab's own scale.")]
        [Min(0.01f)] public float minScale = 0.85f;
        [Min(0.01f)] public float maxScale = 1.2f;

        [Tooltip("Give each instance a random rotation around its Y axis.")]
        public bool randomYRotation = true;

        [Tooltip("How much instances tilt to match the ground slope. 0 = always upright (buildings), 1 = fully aligned to the surface (rocks).")]
        [Range(0f, 1f)] public float alignToNormal = 0.25f;

        [Tooltip("How deep instances sink into the ground, in world units. Useful so rocks and trunks don't float on slopes.")]
        public float embedDepth = 0.1f;

        [Header("Terrain Zone Filter")]
        [Tooltip("Restrict this asset to specific terrain zones (water / shore / grass / rock / snow). " +
                 "Leave off to place anywhere the height and slope filters allow.")]
        public bool restrictToZones = false;

        [Tooltip("Zones this asset may spawn in. Uncheck Water to keep trees out of lakes; " +
                 "check only Shore and Grass to keep them on the beach and meadow.")]
        public TerrainZone allowedZones = TerrainZone.Shore | TerrainZone.Grass;

        [Tooltip("Bias how often this asset appears per zone instead of just allowing/forbidding. " +
                 "e.g. rocks at 3 on Shore and 1 on Rock cluster on the sand. 0 excludes a zone.")]
        public bool useZoneWeights = false;

        [Tooltip("Relative frequency per zone. Only the ratios matter: 2/1 and 8/4 behave the same.")]
        public ZoneWeights zoneWeights = new ZoneWeights();

        [Header("Placement Filters")]
        [Tooltip("Reject spots steeper than this angle in degrees (e.g. keep buildings on flat ground).")]
        [Range(0f, 90f)] public float maxSlopeAngle = 45f;

        [Tooltip("Only place on terrain whose normalized height (0 = lowest, 1 = highest) is at or above this value. Use to keep assets out of lakes/beaches.")]
        [Range(0f, 1f)] public float minHeight = 0f;

        [Tooltip("Only place on terrain whose normalized height is at or below this value. Use to keep assets off mountain peaks.")]
        [Range(0f, 1f)] public float maxHeight = 1f;

        [Tooltip("Minimum distance in world units between two instances of this rule. 0 = no spacing check.")]
        [Min(0f)] public float minSpacing = 2f;

        [Tooltip("Space this asset claims against OTHER assets, in world units, so a rock can't spawn inside " +
                 "a tree. 0 = estimate from the prefab's bounds. Min Spacing only separates an asset from " +
                 "copies of itself; this is what separates it from everything else.")]
        [Min(0f)] public float footprintRadius = 0f;

        // Resolved once per scatter so the bounds estimate isn't recomputed per instance.
        [NonSerialized] public float resolvedFootprint;
    }

    /// <summary>
    /// Rule helpers shared by both terrain systems and their inspectors:
    /// footprint estimation, and the name-based category presets applied when a
    /// prefab is dropped into an inspector.
    /// </summary>
    public static class ScatterRules
    {
        /// <summary>
        /// Category guessed from a prefab's name, used to pick sensible scatter
        /// defaults on drop.
        /// </summary>
        public enum AssetCategory { Generic, Tree, Rock, Building, GroundCover, Debris }

        // Species names are included, not just the generic words: this project's
        // nature kit ships prefabs like "Cedar03" and "Larch01" that contain no
        // "tree" at all, and would otherwise fall through to Generic.
        static readonly string[] TreeKeywords =
        {
            "tree", "pine", "palm", "cedar", "larch", "spruce", "fir", "oak",
            "birch", "willow", "maple", "aspen", "poplar", "redwood", "sequoia",
        };

        static readonly string[] RockKeywords =
        {
            "rock", "stone", "boulder", "cliff", "pebble", "crystal",
        };

        static readonly string[] BuildingKeywords =
        {
            "build", "house", "hut", "tower", "ruin", "wall", "castle",
            "cottage", "shed", "bridge", "well", "fence", "tent",
        };

        static readonly string[] GroundCoverKeywords =
        {
            "grass", "bush", "fern", "flower", "dandelion", "plantain", "clover",
            "weed", "shrub", "moss", "reed", "sapling", "mushroom", "boletus",
            "toadstool", "herb", "nettle", "thistle",
        };

        static readonly string[] DebrisKeywords =
        {
            "stump", "log", "branch", "trunk", "root", "twig", "deadwood", "debris",
        };

        /// <summary>
        /// Horizontal room a prefab needs, from its renderer bounds. Halved because
        /// the full extent is the canopy: tree crowns are meant to interlock, it's
        /// the trunks that must not share ground with a boulder.
        /// </summary>
        public static float EstimateFootprintRadius(GameObject prefab)
        {
            if (prefab == null) return 0.5f;

            var renderers = prefab.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 0.5f;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

            float horizontal = Mathf.Max(bounds.extents.x, bounds.extents.z);
            return Mathf.Clamp(horizontal * 0.5f, 0.15f, 20f);
        }

        /// <summary>
        /// Guesses a category from an asset name. Case-insensitive substring match.
        /// </summary>
        public static AssetCategory GuessCategory(string assetName)
        {
            if (string.IsNullOrWhiteSpace(assetName)) return AssetCategory.Generic;
            string n = assetName.ToLowerInvariant();

            // Debris is checked before Tree so "Stump"/"Tree_Log" don't become trees.
            if (ContainsAny(n, DebrisKeywords)) return AssetCategory.Debris;
            if (ContainsAny(n, BuildingKeywords)) return AssetCategory.Building;
            if (ContainsAny(n, TreeKeywords)) return AssetCategory.Tree;
            if (ContainsAny(n, RockKeywords)) return AssetCategory.Rock;
            if (ContainsAny(n, GroundCoverKeywords)) return AssetCategory.GroundCover;
            return AssetCategory.Generic;
        }

        static bool ContainsAny(string haystack, string[] needles)
        {
            for (int i = 0; i < needles.Length; i++)
                if (haystack.Contains(needles[i])) return true;
            return false;
        }

        /// <summary>
        /// Applies the default placement rules for a category. Public so presets
        /// can be re-applied from the Inspector after renaming a rule.
        /// </summary>
        public static void ApplyCategoryDefaults(EnvironmentAssetRule rule, AssetCategory category)
        {
            if (rule == null) return;

            // Every preset opts into zone filtering: it is the setting that most
            // often makes a scatter look wrong (trees standing in lakes), and the
            // zone names are far easier to reason about than raw height numbers.
            rule.restrictToZones = true;

            // Weights give each category a natural falloff toward the edges of its
            // range instead of a hard uniform band, which reads far less "placed
            // by a script" in the scene.
            rule.useZoneWeights = true;
            rule.zoneWeights = new ZoneWeights { water = 0f, shore = 1f, grass = 1f, rock = 1f, snow = 1f };

            switch (category)
            {
                case AssetCategory.Tree:
                    rule.maxSlopeAngle = 32f;
                    rule.alignToNormal = 0.15f;
                    rule.minSpacing = 4f;
                    rule.minHeight = 0f;
                    rule.maxHeight = 1f;
                    rule.embedDepth = 0.2f;
                    rule.allowedZones = TerrainZone.Shore | TerrainZone.Grass;
                    // Thin out on the sand so the treeline fades toward the beach.
                    rule.zoneWeights = new ZoneWeights { water = 0f, shore = 0.35f, grass = 1f, rock = 0f, snow = 0f };
                    break;

                case AssetCategory.Rock:
                    rule.maxSlopeAngle = 60f;
                    rule.alignToNormal = 1f;
                    rule.embedDepth = 0.25f;
                    rule.minSpacing = 1.5f;
                    rule.allowedZones = TerrainZone.Shore | TerrainZone.Grass | TerrainZone.Rock | TerrainZone.Snow;
                    // Clusters on sand and grass, sparser up on the crags.
                    rule.zoneWeights = new ZoneWeights { water = 0f, shore = 3f, grass = 2f, rock = 1f, snow = 0.5f };
                    break;

                case AssetCategory.Building:
                    rule.maxSlopeAngle = 10f;
                    rule.alignToNormal = 0f;
                    rule.minScale = 1f;
                    rule.maxScale = 1f;
                    rule.maxInstances = 30;
                    rule.minSpacing = 15f;
                    rule.minHeight = 0f;
                    rule.maxHeight = 1f;
                    rule.allowedZones = TerrainZone.Grass;
                    rule.zoneWeights = new ZoneWeights { water = 0f, shore = 0f, grass = 1f, rock = 0f, snow = 0f };
                    break;

                case AssetCategory.GroundCover:
                    rule.maxInstances = 600;
                    rule.minSpacing = 0.5f;
                    rule.embedDepth = 0.05f;
                    rule.alignToNormal = 0.6f;
                    rule.maxSlopeAngle = 40f;
                    rule.maxHeight = 1f;
                    rule.allowedZones = TerrainZone.Shore | TerrainZone.Grass;
                    // Densest in the meadow, sparse on the sand.
                    rule.zoneWeights = new ZoneWeights { water = 0f, shore = 0.5f, grass = 1f, rock = 0f, snow = 0f };
                    // Deliberately tiny: grass and flowers tucked under a tree canopy
                    // look right, so ground cover shouldn't reserve real estate.
                    rule.footprintRadius = 0.2f;
                    break;

                case AssetCategory.Debris:
                    rule.maxInstances = 60;
                    rule.minSpacing = 6f;
                    rule.alignToNormal = 0.8f;
                    rule.embedDepth = 0.15f;
                    rule.maxSlopeAngle = 35f;
                    rule.maxHeight = 1f;
                    rule.allowedZones = TerrainZone.Grass | TerrainZone.Rock;
                    rule.zoneWeights = new ZoneWeights { water = 0f, shore = 0f, grass = 1f, rock = 0.4f, snow = 0f };
                    break;

                default:
                    rule.allowedZones = TerrainZone.Shore | TerrainZone.Grass | TerrainZone.Rock;
                    break;
            }
        }
    }
}
