using AKI.Weapons;
using UnityEngine;

namespace AKI.Rhythm
{
    /// <summary>The launch beat is judged at the click, not after the arrow's physical travel time.</summary>
    public sealed class RhythmSpecialTarget : MonoBehaviour, IHarpoonTarget
    {
        public RhythmGameFlow game;
        public bool goldenFish;
        public float GetPenetration(Vector3 point, Vector3 direction, float defaultPenetration) => defaultPenetration;
        public void OnHarpoonHit(HarpoonProjectile arrow, Vector3 point, Vector3 direction)
        {
            if (game == null || arrow == null) return;
            if (goldenFish) { AKI.Fish.FishAI.ReportHarpooned(transform); game.OnGoldenFishHit(arrow); }
            else game.OnNagaHit(arrow);
            arrow.RetireAfterSpecialHit();
        }
    }
}
