using UnityEngine;
using UnityEngine.Events;

namespace AKI.Weapons
{
    /// <summary>
    /// The speargun model: plays the shot animation, hides the loaded arrow and launches a <see cref="HarpoonProjectile"/>
    /// copy of it from the muzzle (the arrow tip). After <see cref="reloadSeconds"/> the arrow is back in the gun.
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

        public UnityEvent onShoot = new UnityEvent();
        public UnityEvent onReloaded = new UnityEvent();
        [Tooltip("An arrow from this gun stuck into something.")]
        public UnityEvent<Collider> onHit = new UnityEvent<Collider>();

        float reloadAt;

        public bool IsLoaded { get; private set; } = true;

        /// <summary>Reload progress, 0 right after a shot .. 1 loaded (for UI).</summary>
        public float Reload01 => IsLoaded ? 1f : 1f - Mathf.Clamp01((reloadAt - Time.time) / Mathf.Max(reloadSeconds, 1e-4f));

        public Transform Owner => owner != null ? owner : transform;

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

            HarpoonProjectile arrow = Instantiate(projectilePrefab, muzzle.position, rotation);
            arrow.Launch(this);
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
