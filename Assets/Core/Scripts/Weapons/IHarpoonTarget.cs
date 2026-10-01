using UnityEngine;

namespace AKI.Weapons
{
    /// <summary>
    /// Something that reacts to a harpoon sticking into it (a fish). <see cref="HarpoonProjectile"/> looks for it on the
    /// hit collider and its parents: it decides how deep the arrow goes in, then hears about the hit.
    /// </summary>
    public interface IHarpoonTarget
    {
        /// <summary>
        /// How deep the tip goes in (m) for a hit at <paramref name="point"/> travelling along <paramref name="direction"/>.
        /// <paramref name="defaultPenetration"/> is what the arrow would use on anything else.
        /// </summary>
        float GetPenetration(Vector3 point, Vector3 direction, float defaultPenetration);

        /// <summary>The arrow is in: already placed and (if possible) parented to the target.</summary>
        void OnHarpoonHit(HarpoonProjectile arrow, Vector3 point, Vector3 direction);
    }
}
