using AKI.VFX;
using UnityEngine;
using UnityEngine.Events;

namespace AKI.Weapons
{
    /// <summary>
    /// The speargun model: plays the shot animation, hides the loaded arrow and launches a <see cref="HarpoonProjectile"/>
    /// copy of it from the muzzle (the arrow tip), tied to the gun with a <see cref="HarpoonRope"/>. One arrow at a time:
    /// a new one only grows back into the gun (out of noise) once the shot one is gone, and never sooner than
    /// <see cref="cooldown"/> after the shot. The player drives it through <see cref="AKI.Player.PlayerSpeargun"/>.
    /// </summary>
    public class Speargun : MonoBehaviour
    {
        static readonly int ShootId = Animator.StringToHash("Shoot");

        public Animator animator;
        [Tooltip("The arrow mesh sitting in the gun, hidden while the shot arrow is away.")]
        public GameObject loadedArrow;
        [Tooltip("Tip of the loaded arrow, pointing where the gun shoots.")]
        public Transform muzzle;
        public HarpoonProjectile projectilePrefab;
        [Tooltip("Shortest time from a shot until the gun is loaded again (s).")]
        [Min(0f)] public float cooldown = 5f;
        [Tooltip("Seconds the new arrow takes to grow in out of the noise (at least).")]
        [Min(0.1f)] public float materializeSeconds = 1.2f;
        [Tooltip("Arrows never hit colliders under this (the player holding the gun). Empty = the gun itself.")]
        public Transform owner;

        [Header("Line")]
        public bool rope = true;
        [Tooltip("Where the line leaves the gun. Empty = on the arrow axis, Rope Anchor Back behind the muzzle.")]
        public Transform ropeAnchor;
        [Min(0f)] public float ropeAnchorBack = 0.3f;
        [Tooltip("Line thickness (m). Far away it is drawn at least ~2 px wide anyway.")]
        [Min(0.001f)] public float ropeWidth = 0.014f;
        [Tooltip("Total line on the spool (m): at full length it goes taut and holds the arrow.")]
        [Min(1f)] public float ropeLength = 25f;
        [Tooltip("Lit material for the line tube (M_HarpoonRope). Empty = plain coloured fallback.")]
        public Material ropeMaterial;

        [Header("Noise dissolve")]
        [Tooltip("AKI/DissolveLit: arrows melt away and grow back with it.")]
        public Shader dissolveShader;
        [Min(0.1f)] public float dissolveNoiseScale = 9f;
        [ColorUsage(false, true)] public Color dissolveEdgeColor = new Color(0.35f, 0.65f, 0.8f);

        public UnityEvent onShoot = new UnityEvent();
        public UnityEvent onReloaded = new UnityEvent();
        [Tooltip("An arrow from this gun stuck into something.")]
        public UnityEvent<Collider> onHit = new UnityEvent<Collider>();

        float shotAt = -999f;
        HarpoonProjectile lastShot;
        bool materializing;

        public bool IsLoaded { get; private set; } = true;

        /// <summary>Cooldown progress, 0 right after a shot .. 1 loaded (for UI).</summary>
        public float Reload01 => IsLoaded ? 1f : Mathf.Clamp01((Time.time - shotAt) / Mathf.Max(cooldown, 1e-4f));

        public Transform Owner => owner != null ? owner : transform;

        /// <summary>The line's end on the gun (created on demand), or null with the line off.</summary>
        public Transform RopeAnchor
        {
            get
            {
                if (!rope || muzzle == null) return null;
                if (ropeAnchor == null)
                {
                    ropeAnchor = new GameObject("RopeAnchor").transform;
                    ropeAnchor.SetParent(muzzle, false);
                    ropeAnchor.localPosition = new Vector3(0f, -0.012f, -ropeAnchorBack);
                }
                return ropeAnchor;
            }
        }

        /// <summary>Fires straight out of the muzzle if loaded. Returns the arrow in flight, or null.</summary>
        public HarpoonProjectile Shoot() => Launch(muzzle != null ? muzzle.rotation : Quaternion.identity);

        /// <summary>Fires at <paramref name="aimPoint"/> (e.g. what is under the crosshair) if loaded.</summary>
        public HarpoonProjectile Shoot(Vector3 aimPoint)
        {
            if (muzzle == null) return null;
            Vector3 toAim = aimPoint - muzzle.position;
            // a point right in front of / behind the muzzle would swing the arrow sideways: shoot straight then
            bool usable = toAim.sqrMagnitude > 0.25f && Vector3.Dot(toAim, muzzle.forward) > 0f;
            return Launch(usable ? Quaternion.LookRotation(toAim) : muzzle.rotation);
        }

        HarpoonProjectile Launch(Quaternion rotation)
        {
            if (!IsLoaded || projectilePrefab == null || muzzle == null) return null;

            IsLoaded = false;
            shotAt = Time.time;
            if (animator != null) animator.SetTrigger(ShootId);
            if (loadedArrow != null) loadedArrow.SetActive(false);

            if (lastShot != null) lastShot.CutRope();   // the new arrow takes the line
            HarpoonProjectile arrow = Instantiate(projectilePrefab, muzzle.position, rotation);
            arrow.transform.localScale = Vector3.one * transform.lossyScale.x;   // the shot arrow is as big as the one in the gun
            arrow.Launch(this);
            lastShot = arrow;
            onShoot.Invoke();
            return arrow;
        }

        void Update()
        {
            if (IsLoaded || materializing) return;
            if (lastShot != null) return;   // the shot arrow is still out there

            // the new arrow grows in so that it is complete when the cooldown is over (and never in a rush)
            float seconds = Mathf.Max(materializeSeconds, shotAt + cooldown - Time.time);
            if (loadedArrow == null)
            {
                if (Time.time >= shotAt + cooldown) Loaded();
                return;
            }
            materializing = true;
            loadedArrow.SetActive(true);
            Dissolver.Materialize(loadedArrow, dissolveShader, seconds, dissolveNoiseScale, dissolveEdgeColor, Loaded);
        }

        void Loaded()
        {
            materializing = false;
            IsLoaded = true;
            onReloaded.Invoke();
        }
    }
}
