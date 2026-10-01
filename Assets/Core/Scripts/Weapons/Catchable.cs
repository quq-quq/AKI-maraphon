using System.Collections;
using AKI.VFX;
using UnityEngine;
using UnityEngine.Events;

namespace AKI.Weapons
{
    /// <summary>
    /// Something the harpoon catches (a fish): put it on the fish root, next to its collider. When an arrow sticks in,
    /// after <see cref="delay"/> the fish, the arrow and its line melt away in noise and the fish is removed.
    /// Blood already in the water stays and fades on its own.
    /// </summary>
    public class Catchable : MonoBehaviour
    {
        [Tooltip("AKI/DissolveLit (referenced here so it is included in builds).")]
        public Shader dissolveShader;
        [Tooltip("Seconds between the hit and the start of the dissolve.")]
        [Min(0f)] public float delay = 1.2f;
        [Min(0.1f)] public float dissolveSeconds = 1.6f;
        [Tooltip("Noise patches per metre on the fish and the arrow.")]
        [Min(0.1f)] public float noiseScale = 7f;
        [Tooltip("Noise patches per metre along the line (thin, so finer).")]
        [Min(0.1f)] public float ropeNoiseScale = 12f;
        [Tooltip("Glow along the melting edge (black = none).")]
        [ColorUsage(false, true)] public Color edgeColor = new Color(0.35f, 0.65f, 0.8f);

        [Tooltip("The fish is caught (end of the dissolve, right before it is removed).")]
        public UnityEvent onCaught = new UnityEvent();

        public bool IsCaught { get; private set; }

        /// <summary>Called by the arrow that stuck in. Later hits are ignored.</summary>
        public void Catch(HarpoonProjectile arrow)
        {
            if (IsCaught) return;
            IsCaught = true;
            StartCoroutine(Take(arrow));
        }

        IEnumerator Take(HarpoonProjectile arrow)
        {
            yield return new WaitForSeconds(delay);

            // what is already in the water stays there: blood and the arrow's last bubbles
            foreach (TunaBlood blood in GetComponentsInChildren<TunaBlood>()) blood.StopAndDetach();
            if (arrow != null) arrow.DetachEffects();

            Dissolver.Begin(gameObject, dissolveShader, dissolveSeconds, noiseScale, edgeColor);
            bool arrowInFish = arrow != null && arrow.transform.IsChildOf(transform);
            if (arrow != null && !arrowInFish) Dissolver.Begin(arrow.gameObject, dissolveShader, dissolveSeconds, noiseScale, edgeColor);
            HarpoonRope rope = arrow != null ? arrow.Rope : null;
            if (rope != null) Dissolver.Begin(rope.gameObject, dissolveShader, dissolveSeconds, ropeNoiseScale, edgeColor);

            yield return new WaitForSeconds(dissolveSeconds);

            onCaught.Invoke();
            if (rope != null) Destroy(rope.gameObject);
            if (arrow != null && !arrowInFish) Destroy(arrow.gameObject);
            Destroy(gameObject);
        }
    }
}
