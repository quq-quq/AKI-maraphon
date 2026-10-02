using AKI.Fish;
using AKI.Water;
using UnityEngine;

namespace AKI.VFX
{
    /// <summary>
    /// A light wake behind a swimming fish, so its motion reads in the murky water: a faint soft ribbon from the tail
    /// and a few specks of stirred-up water swirling off it. Put the VFX_FishTrail prefab under the fish: it moves
    /// itself to the back end of the fish's mesh and gets stronger the faster the fish moves; it dies away once the
    /// fish stops swimming (a <see cref="FishAI"/> above it that is stopped, e.g. by a harpoon) or leaves the water.
    /// The fish itself needs nothing for it.
    /// </summary>
    public class FishTrail : MonoBehaviour
    {
        [Tooltip("The soft ribbon from the tail (its start colour and width are the full-speed look).")]
        public TrailRenderer ribbon;
        [Tooltip("Specks of stirred-up water, emitted by distance (its rate is the full-speed one).")]
        public ParticleSystem specks;
        [Tooltip("Move to the back of the fish's mesh on start.")]
        public bool snapToTail = true;
        [Tooltip("Where along the fish: 0 = its middle, 1 = the tip of the tail.")]
        [Range(0f, 1f)] public float tailPoint = 0.75f;
        [Tooltip("Speed (m/s) at which the wake is at full strength.")]
        [Min(0.1f)] public float fullSpeed = 2f;

        FishAI ai;
        Color ribbonColor;
        float ribbonWidth;
        float speckRate;
        float strength;
        Vector3 lastPosition;
        float speed;

        void Awake()
        {
            ai = GetComponentInParent<FishAI>();
            if (snapToTail) SnapToTail();
            if (ribbon != null)
            {
                ribbonColor = ribbon.startColor;
                ribbonWidth = ribbon.widthMultiplier;
                ribbon.emitting = false;
            }
            if (specks != null) speckRate = specks.emission.rateOverDistanceMultiplier;
            lastPosition = transform.position;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 position = transform.position;
            speed = Mathf.Lerp(speed, (position - lastPosition).magnitude / dt, 1f - Mathf.Exp(-dt * 8f));
            lastPosition = position;

            bool swimming = (ai == null || !ai.IsStopped) && WaterSurface.IsPointUnderwater(position);
            float target = swimming ? Mathf.Clamp01(speed / fullSpeed) : 0f;
            // fades in and out instead of switching: a stopped fish's wake thins away
            strength = Mathf.MoveTowards(strength, target, dt * 2f);

            if (ribbon != null)
            {
                ribbon.emitting = strength > 0.02f;
                Color head = ribbonColor;
                head.a *= strength;
                Color end = head;
                end.a = 0f;
                ribbon.startColor = head;
                ribbon.endColor = end;
                ribbon.widthMultiplier = ribbonWidth * Mathf.Lerp(0.6f, 1f, strength);
            }
            if (specks != null)
            {
                var emission = specks.emission;
                emission.rateOverDistanceMultiplier = speckRate * strength;
                WaterCurrent.Drift(specks, position);   // the stirred water drifts away with the current
            }
        }

        // The rearmost point of the fish's mesh (the parent's renderers, not this effect's own).
        void SnapToTail()
        {
            Transform fish = transform.parent;
            if (fish == null) return;
            float back = 0f;
            foreach (Renderer r in fish.GetComponentsInChildren<Renderer>())
            {
                if (r.transform.IsChildOf(transform)) continue;
                Mesh mesh = r is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                          : r.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                if (mesh == null) continue;
                Bounds b = mesh.bounds;
                for (int c = 0; c < 8; c++)
                {
                    Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
                    back = Mathf.Min(back, fish.InverseTransformPoint(r.transform.TransformPoint(corner)).z);
                }
            }
            transform.localPosition = new Vector3(0f, 0f, back * tailPoint);
            transform.localRotation = Quaternion.identity;
        }
    }
}
