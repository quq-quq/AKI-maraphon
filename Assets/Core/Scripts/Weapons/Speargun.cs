using UnityEngine;
using UnityEngine.Events;

namespace AKI.Weapons
{
    /// <summary>
    /// The speargun model: plays the shot animation, hides the loaded arrow and launches a <see cref="HarpoonProjectile"/>
    /// copy of it from the muzzle (the arrow tip), tied to the gun with a <see cref="HarpoonRope"/>. After
    /// <see cref="reloadSeconds"/> the arrow is back in the gun; the line stays on the last arrow until the next shot.
    /// The player drives it through <see cref="AKI.Player.PlayerSpeargun"/>.
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
        [Min(0f)] public float reloadSeconds = 1.5f;
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

        public UnityEvent onShoot = new UnityEvent();
        public UnityEvent onReloaded = new UnityEvent();
        [Tooltip("An arrow from this gun stuck into something.")]
        public UnityEvent<Collider> onHit = new UnityEvent<Collider>();

        float reloadAt;
        HarpoonProjectile lastShot;

        public bool IsLoaded { get; private set; } = true;

        /// <summary>Reload progress, 0 right after a shot .. 1 loaded (for UI).</summary>
        public float Reload01 => IsLoaded ? 1f : 1f - Mathf.Clamp01((reloadAt - Time.time) / Mathf.Max(reloadSeconds, 1e-4f));

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
            reloadAt = Time.time + reloadSeconds;
            if (animator != null) animator.SetTrigger(ShootId);
            if (loadedArrow != null) loadedArrow.SetActive(false);

            if (lastShot != null) lastShot.CutRope();   // the new arrow takes the line
            HarpoonProjectile arrow = Instantiate(projectilePrefab, muzzle.position, rotation);
            arrow.Launch(this);
            lastShot = arrow;
            onShoot.Invoke();
            return arrow;
        }

        void Update()
        {
            if (IsLoaded || Time.time < reloadAt) return;
            IsLoaded = true;
            if (loadedArrow != null) loadedArrow.SetActive(true);
            onReloaded.Invoke();
        }
    }
}
