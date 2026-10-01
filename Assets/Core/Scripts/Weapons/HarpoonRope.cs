using AKI.Water;
using UnityEngine;

namespace AKI.Weapons
{
    /// <summary>
    /// The line between the speargun and its shot arrow: paid out behind the arrow as it flies, sagging and drifting
    /// in the water once it stops. Verlet rope drawn with a LineRenderer. When the gun reloads the line is cut at the
    /// gun: the loose end sinks and the whole line fades away. Added by <see cref="HarpoonProjectile"/> on launch.
    /// </summary>
    [DefaultExecutionOrder(200)]   // after the player has placed the gun for this frame (LateUpdate)
    [RequireComponent(typeof(LineRenderer))]
    public class HarpoonRope : MonoBehaviour
    {
        const int Points = 32;
        const int Iterations = 12;
        const float Slack = 1.04f;           // the line is paid out a little longer than the straight distance
        const float WaterGravity = 0.6f;     // a wet line barely sinks
        const float WaterDamping = 0.9f;     // velocity kept per step under water
        const float AirDamping = 0.99f;
        const float CutFadeSeconds = 1.2f;

        static Material sharedMaterial;

        readonly Vector3[] pos = new Vector3[Points];
        readonly Vector3[] prev = new Vector3[Points];
        LineRenderer line;
        Transform gunAnchor;
        Vector3 tailLocal;
        float length;
        float fade = 1f;
        float baseWidth;
        bool attached;

        public bool IsAttached => attached;

        /// <summary>Starts the line at <paramref name="anchor"/> on the gun, tied to <paramref name="tail"/> (arrow local).</summary>
        public void Init(Transform anchor, Vector3 tail, float width, Color color)
        {
            gunAnchor = anchor;
            tailLocal = tail;
            attached = anchor != null;
            baseWidth = width;

            line = GetComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = Points;
            line.widthMultiplier = width;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = RopeMaterial();
            line.startColor = line.endColor = color;

            // coiled at the gun
            Vector3 start = attached ? gunAnchor.position : Tail;
            for (int i = 0; i < Points; i++) pos[i] = prev[i] = start;
            length = Vector3.Distance(start, Tail) * Slack;
            line.SetPositions(pos);
        }

        /// <summary>Cuts the line at the gun (on reload): the loose end sinks and the line fades out.</summary>
        public void Cut()
        {
            attached = false;
        }

        Vector3 Tail => transform.TransformPoint(tailLocal);

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f || line == null) return;

            Vector3 head = attached && gunAnchor != null ? gunAnchor.position : pos[0];
            Vector3 tail = Tail;
            if (attached) length = Mathf.Max(length, Vector3.Distance(head, tail) * Slack);   // spool pays out, never takes in
            float rest = length / (Points - 1);

            // free points: inertia + sinking, heavier damping under water
            for (int i = 0; i < Points; i++)
            {
                bool inWater = WaterSurface.IsPointUnderwater(pos[i]);
                Vector3 velocity = (pos[i] - prev[i]) * (inWater ? WaterDamping : AirDamping);
                prev[i] = pos[i];
                pos[i] += velocity + Physics.gravity * ((inWater ? WaterGravity / 9.81f : 1f) * dt * dt);
            }

            for (int k = 0; k < Iterations; k++)
            {
                if (attached) pos[0] = head;
                pos[Points - 1] = tail;
                for (int i = 0; i < Points - 1; i++)
                {
                    Vector3 d = pos[i + 1] - pos[i];
                    float dist = d.magnitude;
                    if (dist < 1e-6f) continue;
                    Vector3 fix = d * (0.5f * (dist - rest) / dist);
                    bool pinA = i == 0 && attached;
                    bool pinB = i + 1 == Points - 1;
                    if (pinA) pos[i + 1] -= fix * 2f;
                    else if (pinB) pos[i] += fix * 2f;
                    else
                    {
                        pos[i] += fix;
                        pos[i + 1] -= fix;
                    }
                }
            }
            if (attached) pos[0] = head;
            pos[Points - 1] = tail;
            line.SetPositions(pos);

            if (!attached)
            {
                fade -= dt / CutFadeSeconds;
                line.widthMultiplier = baseWidth * Mathf.Clamp01(fade);
                if (fade <= 0f) Destroy(this);
            }
        }

        void OnDestroy()
        {
            if (line != null) line.enabled = false;
        }

        // Same unlit underwater look as the bubbles (fades into the haze), but stays visible above the water.
        static Material RopeMaterial()
        {
            if (sharedMaterial != null) return sharedMaterial;
            Shader shader = Shader.Find("AKI/UnderwaterParticle");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            sharedMaterial = new Material(shader) { name = "HarpoonRope (runtime)" };
            sharedMaterial.SetFloat("_Additive", 0f);
            sharedMaterial.SetFloat("_FadeDensity", 0.1f);
            sharedMaterial.SetVector("_Absorption", new Vector4(0.25f, 0.07f, 0.03f, 0f));
            sharedMaterial.SetFloat("_ClipAboveWater", 0f);
            return sharedMaterial;
        }
    }
}
