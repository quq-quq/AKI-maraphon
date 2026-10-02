using System.Collections.Generic;
using AKI.Water;
using UnityEngine;

namespace AKI.Fish
{
    /// <summary>
    /// Small fish that bring the reef to life, drawn as particles (<see cref="ParticleFishRenderer"/>: one particle
    /// system per kind of fish, the fish are its mesh particles; this script moves them). Three kinds of groups:
    ///   Reef fish   a handful of fish of mixed kinds darting about around some of the coral clusters (far from every
    ///               cluster has them).
    ///   Loners      a few fish of any kind cruising alone across the whole sea floor (rare).
    ///   Schools     2-3 big shoals, each of one kind, wandering over the sea floor in formation.
    /// Loners and schools keep to the lower two thirds of the water (never up to the surface), reef fish keep to their
    /// reef. Nobody goes into the ground. Groups far from the camera are not moved (the water hides them anyway).
    /// </summary>
    public class AmbientFish : MonoBehaviour
    {
        enum Kind { Reef, Loner, School }

        class Agent
        {
            public Kind kind;
            public int group;        // reef or school index
            public int species;
            public Vector3 position, velocity, target;
            public Quaternion rotation;
            public float length;
            public float speed;      // current cruising / darting speed
            public float retarget;   // seconds until a new target (reef fish, loners)
            public float phase;      // tail wag and wobble
            public Vector3 slot;     // place in the school, in the school's space
        }

        class Reef
        {
            public Vector3 centre;
            public float radius, bottom, top;
            public bool active;
        }

        class School
        {
            public Vector3 position, velocity, target;
            public Quaternion heading = Quaternion.identity;
            public int species;
            public bool active;
        }

        [Header("Fish")]
        [Tooltip("The fish prefabs (a mesh + material each), e.g. Models/TropicalFish/Prefabs.")]
        public GameObject[] fishPrefabs;
        [Tooltip("Axis of the fish meshes the head points along (TropicalFish: -Y, the snout is the single vertex there).")]
        public Vector3 meshHeadAxis = new Vector3(0f, -1f, 0f);
        [Tooltip("Axis of the fish meshes the back (dorsal fin) points along (TropicalFish: +Z).")]
        public Vector3 meshBackAxis = new Vector3(0f, 0f, 1f);
        [Tooltip("Tick if the fish swim belly up.")]
        public bool upsideDown;
        [Tooltip("How much the fish shine by themselves in their own colours, to be seen in the deep (0 = not at all).")]
        [Range(0f, 2f)] public float glow = 0.35f;
        [Tooltip("Strength of the sun's glints on the scales (0 = matt).")]
        [Range(0f, 2f)] public float glint = 1f;
        [Tooltip("Size of the glints: near 1 = small and sharp flashes, lower = broad sheen.")]
        [Range(0f, 1f)] public float glintSmoothness = 0.85f;
        [Tooltip("Random seed of the whole population (same seed = same reefs, schools and fish).")]
        public int seed = 1234;

        [Header("Where")]
        [Tooltip("Parent of the coral clusters the reef fish live around. Empty = the object called 'Hill Coral Clusters'.")]
        public Transform reefRoot;
        [Tooltip("Margin kept from the edge of the terrain (m).")]
        [Min(0f)] public float edgeMargin = 12f;
        [Tooltip("Never closer to the ground than this (m).")]
        [Min(0.1f)] public float groundClearance = 0.8f;
        [Tooltip("Loners and schools stay in this share of the water from the bottom up (2/3 = never in the top third).")]
        [Range(0.1f, 1f)] public float swimBand = 2f / 3f;
        [Tooltip("Groups further than this from the camera are not moved (m).")]
        [Min(10f)] public float simulateDistance = 90f;

        [Header("Richness")]
        [Tooltip("The richest spot: fish get fewer the further from it. Empty = the middle of the terrain.")]
        public Transform centre;
        [Tooltip("Fish live only within this share of the distance from the centre to the terrain's edge (1/3 = a circle a third of the way out).")]
        [Range(0.05f, 1f)] public float habitatShare = 1f / 3f;
        [Tooltip("Distance from the centre where the fish thin out to Edge Density (m). 0 = the edge of the habitat.")]
        [Min(0f)] public float richRadius = 0f;
        [Tooltip("How many fish are left far from the centre, as a share of the centre's.")]
        [Range(0f, 1f)] public float edgeDensity = 0.1f;
        [Tooltip("How quickly the fish thin out (1 = evenly with distance, more = rich only right in the middle).")]
        [Min(0.1f)] public float falloff = 1.5f;

        [Header("Reef fish")]
        [Tooltip("Share of the coral clusters that have fish.")]
        [Range(0f, 1f)] public float reefShare = 0.06f;
        [Min(0)] public int maxReefs = 22;
        [Tooltip("Fish around one reef (min far from the centre, max at the centre).")]
        public Vector2Int fishPerReef = new Vector2Int(4, 7);
        [Tooltip("Kinds of fish mixed around one reef (min, max).")]
        public Vector2Int kindsPerReef = new Vector2Int(1, 3);
        [Tooltip("How far above the corals' top the reef fish go (m).")]
        [Min(0f)] public float reefHeadroom = 1.5f;
        [Tooltip("Reefs with fish are at least this far apart (m); half that at the centre.")]
        [Min(0f)] public float reefSpacing = 12f;
        [Tooltip("Darting speed (min, max m/s).")]
        public Vector2 reefSpeed = new Vector2(0.4f, 1.6f);
        [Tooltip("Fish length (min, max m).")]
        public Vector2 reefFishLength = new Vector2(0.15f, 0.3f);

        [Header("Loners")]
        [Min(0)] public int loners = 12;
        [Tooltip("How far a loner swims to its next spot (m).")]
        [Min(1f)] public float lonerRange = 30f;
        public Vector2 lonerSpeed = new Vector2(0.7f, 1.3f);
        public Vector2 lonerLength = new Vector2(0.2f, 0.35f);

        [Header("Schools")]
        [Tooltip("Number of schools (min, max).")]
        public Vector2Int schools = new Vector2Int(2, 3);
        [Tooltip("Fish in one school (min, max).")]
        public Vector2Int fishPerSchool = new Vector2Int(60, 90);
        [Tooltip("Size of the shoal: length, width and height of the ellipsoid the fish keep to (m). Long and thin.")]
        public Vector3 schoolSize = new Vector3(10f, 1.8f, 1.2f);
        [Tooltip("How much each fish drifts about its place in the shoal (m). Small = tight shoal.")]
        [Min(0f)] public float schoolLooseness = 0.1f;
        [Tooltip("Swimming speed of the shoal (m/s).")]
        [Min(0.1f)] public float schoolSpeed = 1.3f;
        [Tooltip("How far the shoal swims to its next spot (m).")]
        [Min(1f)] public float schoolRange = 45f;
        public Vector2 schoolFishLength = new Vector2(0.12f, 0.2f);

        [Header("Swimming")]
        [Tooltip("How quickly a fish changes speed / direction (m/s²).")]
        [Min(0.1f)] public float acceleration = 2.5f;
        [Tooltip("How quickly a fish turns to where it swims (higher = snappier).")]
        [Min(0.1f)] public float turnSharpness = 6f;
        [Tooltip("Largest nose up / down (degrees).")]
        [Range(0f, 89f)] public float maxPitch = 30f;
        [Tooltip("Tail wag (degrees) and wags per second.")]
        public float wiggleDegrees = 10f;
        public float wiggleFrequency = 3f;

        [Header("Gizmos")]
        public bool drawGizmos = true;

        readonly List<Agent> agents = new List<Agent>();
        readonly List<Reef> reefs = new List<Reef>();
        readonly List<School> schoolList = new List<School>();
        readonly List<ParticleFishRenderer> renderers = new List<ParticleFishRenderer>();
        readonly List<List<Agent>> bySpecies = new List<List<Agent>>();
        Vector3[] positions = new Vector3[0];
        Quaternion[] rotations = new Quaternion[0];
        float[] lengths = new float[0];
        System.Random random;
        Terrain[] terrains;   // the sea floor
        Rect area;            // where the loners and schools may go (XZ)
        Vector2 richCentre;
        float richDistance;
        float habitatRadius;  // nobody lives further than this from richCentre
        float waterLevel;
        bool ready;

        public int FishCount => agents.Count;

        void Start()
        {
            random = new System.Random(seed);
            if (reefRoot == null)
            {
                GameObject found = GameObject.Find("Hill Coral Clusters");
                if (found != null) reefRoot = found.transform;
            }
            if (!SetUpSpecies()) return;
            terrains = Terrain.activeTerrains;   // a new array on every call: once
            waterLevel = WaterLevel();
            area = Area();
            richCentre = centre != null ? new Vector2(centre.position.x, centre.position.z) : area.center;
            habitatRadius = (Mathf.Min(area.width, area.height) * 0.5f + edgeMargin) * habitatShare;
            richDistance = richRadius > 0f ? richRadius : habitatRadius;

            SpawnReefFish();
            SpawnLoners();
            SpawnSchools();
            ready = true;
            Draw();
        }

        // ------------------------------------------------------------------ setting up

        bool SetUpSpecies()
        {
            if (fishPrefabs == null) return false;
            foreach (GameObject prefab in fishPrefabs)
            {
                if (prefab == null) continue;
                var filter = prefab.GetComponentInChildren<MeshFilter>();
                var meshRenderer = prefab.GetComponentInChildren<MeshRenderer>();
                if (filter == null || filter.sharedMesh == null || meshRenderer == null) continue;
                renderers.Add(new ParticleFishRenderer(transform, prefab.name, filter.sharedMesh, meshRenderer.sharedMaterial, meshHeadAxis, meshBackAxis, upsideDown, glow, glint, glintSmoothness));
                bySpecies.Add(new List<Agent>());
            }
            if (renderers.Count == 0) Debug.LogWarning("AmbientFish: no usable fish prefabs.", this);
            return renderers.Count > 0;
        }

        void SpawnReefFish()
        {
            if (reefRoot == null || reefRoot.childCount == 0 || maxReefs == 0) return;

            // pick some of the coral clusters, more of them near the centre (weighted shuffle), spread apart
            var clusters = new List<Transform>();
            var keys = new Dictionary<Transform, double>();
            foreach (Transform child in reefRoot)
            {
                clusters.Add(child);
                keys[child] = System.Math.Pow(random.NextDouble(), 1.0 / Mathf.Max(1e-3f, Richness(child.position)));
            }
            clusters.Sort((a, b) => keys[b].CompareTo(keys[a]));
            int wanted = Mathf.Min(maxReefs, Mathf.RoundToInt(clusters.Count * reefShare));
            foreach (Transform cluster in clusters)
            {
                if (reefs.Count >= wanted) break;
                if (!InHabitat(cluster.position)) continue;
                if (!ClusterBounds(cluster, out Bounds b)) continue;
                Vector3 centre = new Vector3(b.center.x, 0f, b.center.z);
                float richness = Richness(centre);
                float spacing = reefSpacing * Mathf.Lerp(1f, 0.5f, richness);
                bool tooClose = false;
                foreach (Reef other in reefs)
                    if ((Flat(other.centre) - centre).sqrMagnitude < spacing * spacing) { tooClose = true; break; }
                if (tooClose) continue;

                var reef = new Reef
                {
                    centre = new Vector3(b.center.x, b.min.y, b.center.z),
                    radius = Mathf.Max(b.extents.x, b.extents.z) + 1.5f,
                    bottom = b.min.y + 0.3f,
                    top = Mathf.Min(b.max.y + reefHeadroom, waterLevel - 1f),
                };
                reefs.Add(reef);

                int kinds = Range(kindsPerReef.x, Mathf.RoundToInt(Mathf.Lerp(kindsPerReef.x, kindsPerReef.y, richness)));
                var species = new int[kinds];
                for (int k = 0; k < kinds; k++) species[k] = random.Next(renderers.Count);
                int most = Mathf.RoundToInt(Mathf.Lerp(fishPerReef.x, fishPerReef.y, richness));
                int count = Range(Mathf.Max(fishPerReef.x, most - 2), most);
                for (int i = 0; i < count; i++)
                {
                    var fish = NewAgent(Kind.Reef, reefs.Count - 1, species[random.Next(kinds)], Range(reefFishLength));
                    fish.position = ReefPoint(reef);
                    fish.target = ReefPoint(reef);
                    fish.speed = Range(reefSpeed);
                }
            }
        }

        void SpawnLoners()
        {
            for (int i = 0; i < loners; i++)
            {
                var fish = NewAgent(Kind.Loner, -1, random.Next(renderers.Count), Range(lonerLength));
                fish.position = BandPoint(RichPoint());
                fish.target = LonerTarget(fish.position);
                fish.speed = Range(lonerSpeed);
            }
        }

        void SpawnSchools()
        {
            int count = Range(schools.x, schools.y);
            var used = new List<int>();
            for (int s = 0; s < count; s++)
            {
                // a different kind for every school while there are enough kinds
                int species;
                do species = random.Next(renderers.Count);
                while (used.Contains(species) && used.Count < renderers.Count);
                used.Add(species);

                var school = new School
                {
                    species = species,
                    position = BandPoint(RichPoint()),
                };
                school.target = SchoolTarget(school.position);
                school.heading = Quaternion.LookRotation(Flat(school.target - school.position).normalized + new Vector3(0f, 0f, 1e-4f));
                schoolList.Add(school);

                float length = Range(schoolFishLength);
                int fishCount = Range(fishPerSchool.x, fishPerSchool.y);
                for (int i = 0; i < fishCount; i++)
                {
                    var fish = NewAgent(Kind.School, schoolList.Count - 1, species, length * Range(0.9f, 1.1f));
                    fish.slot = Vector3.Scale(InsideSphere(), new Vector3(schoolSize.y, schoolSize.z, schoolSize.x) * 0.5f);   // school space: x across, y up, z along
                    fish.position = school.position + school.heading * fish.slot;
                    fish.velocity = school.heading * Vector3.forward * schoolSpeed;
                }
            }
        }

        Agent NewAgent(Kind kind, int group, int species, float length)
        {
            var fish = new Agent
            {
                kind = kind,
                group = group,
                species = species,
                length = length,
                phase = Range(0f, 100f),
                retarget = Range(1f, 4f),
                rotation = Quaternion.Euler(0f, Range(0f, 360f), 0f),
            };
            agents.Add(fish);
            bySpecies[species].Add(fish);
            return fish;
        }

        // ------------------------------------------------------------------ moving

        void Update()
        {
            if (!ready) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 camera = Camera.main != null ? Camera.main.transform.position : transform.position;
            float far = simulateDistance * simulateDistance;
            foreach (Reef reef in reefs) reef.active = (reef.centre - camera).sqrMagnitude < far;
            foreach (School school in schoolList)
            {
                school.active = (school.position - camera).sqrMagnitude < far;
                if (school.active) MoveSchool(school, dt);
            }

            foreach (Agent fish in agents)
            {
                switch (fish.kind)
                {
                    case Kind.Reef:
                        if (!reefs[fish.group].active) continue;
                        SwimReef(fish, reefs[fish.group], dt);
                        break;
                    case Kind.Loner:
                        if ((fish.position - camera).sqrMagnitude > far) continue;
                        SwimLoner(fish, dt);
                        break;
                    case Kind.School:
                        if (!schoolList[fish.group].active) continue;
                        SwimInSchool(fish, schoolList[fish.group], dt);
                        break;
                }
                Turn(fish, dt);
            }
            Draw();
        }

        // Darting about the reef: a quick spurt to a random spot, then the next one.
        void SwimReef(Agent fish, Reef reef, float dt)
        {
            fish.retarget -= dt;
            if (fish.retarget <= 0f || (fish.target - fish.position).sqrMagnitude < 0.09f)
            {
                fish.target = ReefPoint(reef);
                fish.speed = Range(reefSpeed);
                fish.retarget = Range(0.8f, 3.5f);
            }
            Steer(fish, (fish.target - fish.position).normalized * fish.speed, acceleration * 1.6f, dt);
            Move(fish, dt, reef.bottom, reef.top);
        }

        // Cruising alone to a spot further on, then the next.
        void SwimLoner(Agent fish, float dt)
        {
            fish.retarget -= dt;
            if (fish.retarget <= 0f || Flat(fish.target - fish.position).sqrMagnitude < 4f)
            {
                fish.target = LonerTarget(fish.position);
                fish.speed = Range(lonerSpeed);
                fish.retarget = Range(15f, 40f);
            }
            Steer(fish, (fish.target - fish.position).normalized * fish.speed, acceleration, dt);
            Band(fish.position, out float low, out float high);
            Move(fish, dt, low, high);
        }

        // The shoal itself: a point wandering from spot to spot over the sea floor, turning smoothly.
        void MoveSchool(School school, float dt)
        {
            if (Flat(school.target - school.position).sqrMagnitude < 9f) school.target = SchoolTarget(school.position);
            Vector3 desired = (school.target - school.position).normalized * schoolSpeed;
            school.velocity = Vector3.MoveTowards(school.velocity, desired, 0.6f * dt);
            school.position += school.velocity * dt;
            Band(school.position, out float low, out float high);
            float margin = schoolSize.z * 0.5f;
            school.position.y = Mathf.Clamp(school.position.y, low + margin, Mathf.Max(low + margin, high - margin));
            Vector3 flat = Flat(school.velocity);
            if (flat.sqrMagnitude > 1e-4f)
                school.heading = Quaternion.Slerp(school.heading, Quaternion.LookRotation(flat), 1f - Mathf.Exp(-1.5f * dt));
        }

        // Each fish keeps to its own place in the shoal (which drifts a little), swimming along with it.
        void SwimInSchool(Agent fish, School school, float dt)
        {
            float t = Time.time * 0.3f + fish.phase;
            Vector3 drift = new Vector3(Mathf.Sin(t * 1.3f), Mathf.Sin(t * 0.9f + 1.7f) * 0.5f, Mathf.Sin(t * 0.7f + 3.1f)) * schoolLooseness;
            Vector3 place = school.position + school.heading * (fish.slot + drift);
            Vector3 desired = school.velocity + (place - fish.position) * 1.2f;
            Steer(fish, Vector3.ClampMagnitude(desired, schoolSpeed * 2f), acceleration * 2f, dt);
            Band(fish.position, out float low, out float high);
            Move(fish, dt, low, high);
        }

        void Steer(Agent fish, Vector3 desiredVelocity, float accel, float dt)
        {
            fish.velocity = Vector3.MoveTowards(fish.velocity, desiredVelocity, accel * dt);
        }

        // Moves along the velocity, never into the ground and between the heights it may use.
        void Move(Agent fish, float dt, float low, float high)
        {
            Vector3 p = fish.position + fish.velocity * dt;
            float ground = SeabedHeight(p) + groundClearance;
            low = Mathf.Max(low, ground);
            high = Mathf.Max(high, low);
            if (p.y < low || p.y > high)
            {
                p.y = Mathf.Clamp(p.y, low, high);
                fish.velocity.y = 0f;
            }
            fish.position = p;
        }

        // Faces where it swims (nose up / down limited) and wags its tail with its speed.
        void Turn(Agent fish, float dt)
        {
            Vector3 v = fish.velocity;
            float flat = new Vector2(v.x, v.z).magnitude;
            if (flat > 1e-3f)
            {
                float limit = Mathf.Tan(maxPitch * Mathf.Deg2Rad) * flat;
                v.y = Mathf.Clamp(v.y, -limit, limit);
                fish.rotation = Quaternion.Slerp(fish.rotation, Quaternion.LookRotation(v), 1f - Mathf.Exp(-turnSharpness * dt));
            }
            fish.phase += dt * wiggleFrequency * Mathf.Lerp(0.5f, 2f, Mathf.Clamp01(fish.velocity.magnitude / 1.5f)) * 2f * Mathf.PI;
        }

        void Draw()
        {
            for (int s = 0; s < renderers.Count; s++)
            {
                List<Agent> list = bySpecies[s];
                if (positions.Length < list.Count)
                {
                    positions = new Vector3[list.Count];
                    rotations = new Quaternion[list.Count];
                    lengths = new float[list.Count];
                }
                for (int i = 0; i < list.Count; i++)
                {
                    Agent fish = list[i];
                    positions[i] = fish.position;
                    rotations[i] = fish.rotation * Quaternion.Euler(0f, Mathf.Sin(fish.phase) * wiggleDegrees, 0f);
                    lengths[i] = fish.length;
                }
                renderers[s].Write(positions, rotations, lengths, list.Count);
            }
        }

        // ------------------------------------------------------------------ places

        Vector3 ReefPoint(Reef reef)
        {
            Vector2 d = InsideCircle() * reef.radius;
            return new Vector3(reef.centre.x + d.x, Range(reef.bottom, Mathf.Max(reef.bottom, reef.top)), reef.centre.z + d.y);
        }

        Vector3 LonerTarget(Vector3 from)
        {
            return BandPoint(RichStep(from, () => InsideCircle() * lonerRange));
        }

        Vector3 SchoolTarget(Vector3 from)
        {
            return BandPoint(RichStep(from, () => InsideCircle().normalized * Range(schoolRange * 0.5f, schoolRange)));
        }

        // ------------------------------------------------------------------ richness

        /// <summary>1 at the rich centre, falling off to edgeDensity at richRadius and beyond.</summary>
        public float Richness(Vector3 p)
        {
            float d = Vector2.Distance(new Vector2(p.x, p.z), richCentre) / Mathf.Max(1f, richDistance);
            return Mathf.Lerp(edgeDensity, 1f, Mathf.Pow(1f - Mathf.Clamp01(d), falloff));
        }

        // A spot anywhere in the area, more likely the richer it is there.
        Vector2 RichPoint()
        {
            Vector2 p = Vector2.zero;
            for (int i = 0; i < 30; i++)
            {
                p = ClampToArea(richCentre + InsideCircle() * habitatRadius);
                if (random.NextDouble() < Richness(new Vector3(p.x, 0f, p.y))) break;
            }
            return p;
        }

        // The next spot from 'from', more likely towards richer water, so the wanderers stay mostly near the centre.
        Vector2 RichStep(Vector3 from, System.Func<Vector2> step)
        {
            Vector2 p = Vector2.zero;
            for (int i = 0; i < 12; i++)
            {
                Vector2 d = step();
                p = ClampToArea(new Vector2(from.x + d.x, from.z + d.y));
                if (random.NextDouble() < Richness(new Vector3(p.x, 0f, p.y))) break;
            }
            return p;
        }

        // A random height in the band the loners and schools use at this spot.
        Vector3 BandPoint(Vector2 xz)
        {
            var p = new Vector3(xz.x, 0f, xz.y);
            Band(p, out float low, out float high);
            p.y = Range(low, Mathf.Max(low, high));
            return p;
        }

        // From just above the ground up to swimBand of the water's depth here.
        void Band(Vector3 p, out float low, out float high)
        {
            float ground = SeabedHeight(p);
            low = ground + groundClearance;
            high = ground + (waterLevel - ground) * swimBand;
        }

        float SeabedHeight(Vector3 p)
        {
            Terrain terrain = TerrainAt(p);
            if (terrain != null) return terrain.SampleHeight(p) + terrain.transform.position.y;
            if (Physics.Raycast(new Vector3(p.x, waterLevel, p.z), Vector3.down, out RaycastHit hit, 200f, ~((1 << 2) | (1 << 4)), QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return waterLevel - 30f;
        }

        Terrain TerrainAt(Vector3 p)
        {
            foreach (Terrain t in terrains)
            {
                Vector3 o = t.transform.position, s = t.terrainData.size;
                if (p.x >= o.x && p.x <= o.x + s.x && p.z >= o.z && p.z <= o.z + s.z) return t;
            }
            return null;
        }

        Rect Area()
        {
            Terrain terrain = terrains.Length > 0 ? terrains[0] : null;
            if (terrain != null)
            {
                Vector3 o = terrain.transform.position, s = terrain.terrainData.size;
                return Rect.MinMaxRect(o.x + edgeMargin, o.z + edgeMargin, o.x + s.x - edgeMargin, o.z + s.z - edgeMargin);
            }
            Vector3 c = transform.position;
            return Rect.MinMaxRect(c.x - 100f, c.z - 100f, c.x + 100f, c.z + 100f);
        }

        // Inside the terrain and inside the habitat circle.
        Vector2 ClampToArea(Vector2 p)
        {
            p = new Vector2(Mathf.Clamp(p.x, area.xMin, area.xMax), Mathf.Clamp(p.y, area.yMin, area.yMax));
            Vector2 fromCentre = p - richCentre;
            return fromCentre.sqrMagnitude > habitatRadius * habitatRadius ? richCentre + fromCentre.normalized * habitatRadius : p;
        }

        bool InHabitat(Vector3 p)
        {
            return (new Vector2(p.x, p.z) - richCentre).sqrMagnitude <= habitatRadius * habitatRadius;
        }

        static bool ClusterBounds(Transform cluster, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer r in cluster.GetComponentsInChildren<Renderer>())
            {
                if (any) bounds.Encapsulate(r.bounds);
                else bounds = r.bounds;
                any = true;
            }
            return any;
        }

        float WaterLevel()
        {
            WaterSurface water = WaterSurface.FindAt(transform.position);
            if (water == null && WaterSurface.Instances.Count > 0) water = WaterSurface.Instances[0];
            return water != null ? water.WaterLevel : 0f;
        }

        // ------------------------------------------------------------------ random helpers

        float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);
        float Range(Vector2 r) => Range(r.x, r.y);
        int Range(int min, int maxInclusive) => random.Next(min, Mathf.Max(min, maxInclusive) + 1);

        Vector2 InsideCircle()
        {
            float a = Range(0f, 2f * Mathf.PI);
            float r = Mathf.Sqrt((float)random.NextDouble());
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        Vector3 InsideSphere()
        {
            Vector3 p;
            do p = new Vector3(Range(-1f, 1f), Range(-1f, 1f), Range(-1f, 1f));
            while (p.sqrMagnitude > 1f);
            return p;
        }

        void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        // ------------------------------------------------------------------ gizmos

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos || !ready) return;
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.8f);
            foreach (Reef reef in reefs)
                Gizmos.DrawWireCube(new Vector3(reef.centre.x, (reef.bottom + reef.top) * 0.5f, reef.centre.z),
                    new Vector3(reef.radius * 2f, reef.top - reef.bottom, reef.radius * 2f));
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            foreach (School school in schoolList)
            {
                Gizmos.DrawWireSphere(school.position, schoolSize.z * 0.5f);
                Gizmos.DrawLine(school.position, school.target);
            }
            Gizmos.color = new Color(1f, 1f, 1f, 0.4f);
            Vector3 last = Vector3.zero;
            for (int i = 0; i <= 64; i++)
            {
                float a = i * 2f * Mathf.PI / 64;
                Vector3 q = new Vector3(richCentre.x + Mathf.Cos(a) * habitatRadius, waterLevel - 10f, richCentre.y + Mathf.Sin(a) * habitatRadius);
                if (i > 0) Gizmos.DrawLine(last, q);
                last = q;
            }
        }
    }
}
