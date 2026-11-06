using UnityEngine;
using UnityEngine.Rendering;

namespace PrestigePerks.Active
{
    /// <summary>
    /// 음파 탐지 파동 고리: 내 발 높이의 얇은 평면 고리 (64분할, 폭 0.25m). 파동 반경만큼 키우고 반경에 가까울수록 흐리게.
    /// 벽·지형에 가려진다. F12 "Sonar wave ring" 으로 끈다. 꼭짓점 배열은 재사용한다.
    /// </summary>
    internal sealed class SonarRing
    {
        private const int Segments = 64;
        private const float Width = 0.25f, Lift = 0.05f;
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly Vector3[] vertices = new Vector3[(Segments + 1) * 2];
        private GameObject root;
        private Mesh mesh;
        private Material material;

        public static SonarRing Create(Color color)
        {
            if (SonarShaders.Ring == null)
            {
                return null;
            }

            var ring = new SonarRing();
            ring.Build(color);
            return ring;
        }

        private void Build(Color color)
        {
            mesh = new Mesh { name = "PrestigePerks Sonar Ring" };
            mesh.MarkDynamic();
            var uv = new Vector2[vertices.Length];
            var triangles = new int[Segments * 6];
            for (var i = 0; i <= Segments; i++)
            {
                uv[i * 2] = new Vector2(i / (float)Segments, 0);
                uv[i * 2 + 1] = new Vector2(i / (float)Segments, 1);
                if (i < Segments)
                {
                    var t = i * 6;
                    triangles[t] = i * 2;
                    triangles[t + 1] = i * 2 + 1;
                    triangles[t + 2] = i * 2 + 2;
                    triangles[t + 3] = i * 2 + 1;
                    triangles[t + 4] = i * 2 + 3;
                    triangles[t + 5] = i * 2 + 2;
                }
            }

            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;

            material = new Material(SonarShaders.Ring);
            material.SetColor(ColorId, color);
            root = new GameObject("PrestigePerks Sonar Ring");
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            root.SetActive(false);
        }

        /// <summary>파동 하나의 한 프레임. radius 가 0 이하이면 숨긴다.</summary>
        public void Show(Vector3 origin, float radius, float maxRadius, float intensity)
        {
            if (root == null)
            {
                return;
            }

            if (radius <= 0 || maxRadius <= 0)
            {
                root.SetActive(false);
                return;
            }

            var inner = Mathf.Max(0, radius - Width);
            for (var i = 0; i <= Segments; i++)
            {
                var angle = i * Mathf.PI * 2 / Segments;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                vertices[i * 2] = direction * inner;
                vertices[i * 2 + 1] = direction * radius;
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            root.transform.position = origin + Vector3.up * Lift;
            material.SetFloat(FadeId, Mathf.Clamp01(1 - radius / maxRadius));
            material.SetFloat(IntensityId, intensity);
            root.SetActive(true);
        }

        public void Hide()
        {
            if (root != null)
            {
                root.SetActive(false);
            }
        }

        public void Destroy()
        {
            if (root != null)
            {
                Object.Destroy(root);
            }

            if (mesh != null)
            {
                Object.Destroy(mesh);
            }

            if (material != null)
            {
                Object.Destroy(material);
            }

            root = null;
            mesh = null;
            material = null;
        }
    }
}
