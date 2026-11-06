using System.Collections.Generic;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace PrestigePerks.Active
{
    /// <summary>
    /// 음파 탐지 잔상 (시험 플러그인 PulseTest 의 Echo 모드를 옮김, 실현성조사 5.1): 파동이 닿은 대상의 그 순간 자세를 본떠(BakeMesh) 세상 안에 남기고 서서히 지운다.
    /// 잔상은 보통 물체라 메인 카메라와 PiP 조준경 카메라가 각자 그린다. 대상 봇은 지속 동안 컬링을 Visible 로, 가림 컬링을 끄고, 끝나면 되돌린다.
    /// 지금 그려지는 LOD 하나만 본뜬다 (여러 단계를 함께 본뜨면 다리가 여러 개로 보였다).
    /// </summary>
    internal sealed class SonarEchoes
    {
        public const float NearCut = 0.8f, MaxOffset = 0.5f;

        private sealed class Entry
        {
            public Renderer Renderer;
            public LODGroup Group;
            public int Lod = -1;
        }

        /// <summary>지속 동안 손댄 대상 하나. 끝날 때 원래 값으로 되돌린다.</summary>
        private sealed class Target
        {
            public Player Player;
            public OfflinePlayerCulling Culling;
            public BasePlayerCulling.EMode OrigMode;
            public bool CullingChanged;
            public readonly Dictionary<Renderer, bool> OrigOcclusion = new Dictionary<Renderer, bool>();
            public readonly List<Entry> Entries = new List<Entry>();
        }

        private sealed class Echo
        {
            public GameObject Root;
            public readonly List<Mesh> Baked = new List<Mesh>();
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public float Born;
        }

        private static readonly AccessTools.FieldRef<LocalPlayer, OfflinePlayerCulling> BotCulling =
            AccessTools.FieldRefAccess<LocalPlayer, OfflinePlayerCulling>("botPlayerCulling");

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int FillAlphaId = Shader.PropertyToID("_FillAlpha");
        private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int UseDepthId = Shader.PropertyToID("_SonarUseDepth");
        private static readonly int NearCutId = Shader.PropertyToID("_SonarNearCut");
        private static readonly int LensId = Shader.PropertyToID("_SonarLens");
        private static readonly int LensFlipId = Shader.PropertyToID("_SonarLensFlip");

        private readonly Dictionary<Player, Target> targets = new Dictionary<Player, Target>();
        private readonly List<Echo> echoes = new List<Echo>();
        private readonly List<Renderer> buffer = new List<Renderer>(256);
        private readonly Dictionary<LODGroup, int> currentLod = new Dictionary<LODGroup, int>();
        private readonly Dictionary<LODGroup, LOD[]> lodCache = new Dictionary<LODGroup, LOD[]>();
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private readonly Material material;
        private Camera camera;
        private bool hooked;

        public SonarEchoes(Color color, float fillAlpha, float rimPower)
        {
            material = new Material(SonarShaders.Echo);
            material.SetColor(ColorId, color);
            material.SetFloat(FillAlphaId, fillAlpha);
            material.SetFloat(RimPowerId, rimPower);
        }

        public int Alive => echoes.Count;

        /// <summary>파동을 시작할 때: 반경 안의 살아 있는 대상을 잡아 컬링을 풀고 렌더러 목록을 새로 만든다. 반경 안 대상 수.</summary>
        public int Gather(GameWorld world, Player me, Vector3 origin, float radius)
        {
            var count = 0;
            foreach (var player in world.AllAlivePlayersList)
            {
                if (player == null || ReferenceEquals(player, me) || player.IsYourPlayer || player.HealthController == null || !player.HealthController.IsAlive)
                {
                    continue;
                }

                if ((player.Position - origin).sqrMagnitude > radius * radius)
                {
                    continue;
                }

                if (!targets.TryGetValue(player, out var target))
                {
                    target = new Target { Player = player };
                    if (player is LocalPlayer local)
                    {
                        target.Culling = BotCulling(local);
                    }

                    targets[player] = target;
                }

                if (target.Culling != null && !target.CullingChanged)
                {
                    target.OrigMode = target.Culling.Mode;
                    target.Culling.SetMode(BasePlayerCulling.EMode.Visible);
                    target.CullingChanged = true;
                }

                Refresh(target);
                count++;
            }

            return count;
        }

        /// <summary>파동 반경 안에 들어온 대상을 거리순으로 본뜬다 (이번 파동에 이미 본뜬 대상은 hit 에 있다). 이번 프레임에 만든 잔상 수.</summary>
        public int Hit(Vector3 origin, float radius, HashSet<Player> hit, ref float maxOffset)
        {
            camera = CameraManager.Instance.Camera;
            currentLod.Clear();
            var made = 0;
            foreach (var target in targets.Values)
            {
                var player = target.Player;
                if (player == null || hit.Contains(player) || player.HealthController == null || !player.HealthController.IsAlive)
                {
                    continue;
                }

                if ((player.Position - origin).sqrMagnitude > radius * radius)
                {
                    continue;
                }

                hit.Add(player);
                if (Make(target, ref maxOffset))
                {
                    made++;
                }
            }

            return made;
        }

        /// <summary>매 프레임: 잔상을 흐리게 하고 다 사라진 것은 지운다. 밝기는 맨눈·야간 투시경·열화상에 따라.</summary>
        public void Fade(float now, float life, float intensity)
        {
            material.SetFloat(IntensityId, intensity);
            var hold = life * 0.3f;
            for (var i = echoes.Count - 1; i >= 0; i--)
            {
                var echo = echoes[i];
                var age = now - echo.Born;
                if (age >= life || echo.Root == null)
                {
                    Destroy(echo);
                    echoes.RemoveAt(i);
                    continue;
                }

                // 앞 30% 는 그대로, 나머지 동안 서서히 사라진다
                block.SetFloat(FadeId, age <= hold ? 1f : 1f - (age - hold) / (life - hold));
                foreach (var renderer in echo.Renderers)
                {
                    if (renderer != null)
                    {
                        renderer.SetPropertyBlock(block);
                    }
                }
            }

            Hook(echoes.Count > 0);
        }

        /// <summary>끝: 잔상을 모두 지우고 대상의 컬링·가림 컬링을 되돌린다. 되돌리지 못한 대상 수 (죽은 대상은 게임이 컬링을 바꾸므로 세지 않는다).</summary>
        public int Clear()
        {
            foreach (var echo in echoes)
            {
                Destroy(echo);
            }

            echoes.Clear();
            Hook(false);
            var wrong = 0;
            foreach (var target in targets.Values)
            {
                var alive = target.Player != null && target.Player.HealthController != null && target.Player.HealthController.IsAlive;
                if (target.CullingChanged && target.Culling != null && alive && target.Culling.Mode == BasePlayerCulling.EMode.Visible)
                {
                    target.Culling.SetMode(target.OrigMode);
                    if (target.Culling.Mode != target.OrigMode)
                    {
                        wrong++;
                    }
                }

                target.CullingChanged = false;
                foreach (var pair in target.OrigOcclusion)
                {
                    if (pair.Key != null)
                    {
                        pair.Key.allowOcclusionWhenDynamic = pair.Value;
                        if (pair.Key.allowOcclusionWhenDynamic != pair.Value)
                        {
                            wrong++;
                        }
                    }
                }

                target.OrigOcclusion.Clear();
            }

            targets.Clear();
            lodCache.Clear();
            return wrong;
        }

        public void Destroy()
        {
            Clear();
            if (material != null)
            {
                Object.Destroy(material);
            }
        }

        // 몸 렌더러 + 손에 든 물건. 렌더러마다 LODGroup 과 단계를 적어 둔다. 가림 컬링은 처음 볼 때 원래 값을 적고 끈다
        private void Refresh(Target target)
        {
            buffer.Clear();
            var player = target.Player;
            player.PlayerBody?.GetRenderersNonAlloc(buffer);
            var hands = player.HandsController?.ControllerGameObject;
            if (hands != null)
            {
                foreach (var renderer in hands.GetComponentsInChildren<Renderer>(false))
                {
                    if (renderer != null && renderer.enabled && !buffer.Contains(renderer))
                    {
                        buffer.Add(renderer);
                    }
                }
            }

            var lodOf = new Dictionary<Renderer, KeyValuePair<LODGroup, int>>();
            var groups = new List<LODGroup>();
            if (player.PlayerBody != null)
            {
                groups.AddRange(player.PlayerBody.GetComponentsInChildren<LODGroup>(true));
            }

            if (hands != null)
            {
                groups.AddRange(hands.GetComponentsInChildren<LODGroup>(false));
            }

            foreach (var group in groups)
            {
                var lods = group.GetLODs();
                lodCache[group] = lods;
                for (var i = 0; i < lods.Length; i++)
                {
                    foreach (var renderer in lods[i].renderers)
                    {
                        if (renderer != null && !lodOf.ContainsKey(renderer))
                        {
                            lodOf[renderer] = new KeyValuePair<LODGroup, int>(group, i);
                        }
                    }
                }
            }

            target.Entries.Clear();
            foreach (var renderer in buffer)
            {
                if (renderer == null || !(renderer is SkinnedMeshRenderer || renderer is MeshRenderer))
                {
                    continue;
                }

                var entry = new Entry { Renderer = renderer };
                if (lodOf.TryGetValue(renderer, out var lod))
                {
                    entry.Group = lod.Key;
                    entry.Lod = lod.Value;
                }

                target.Entries.Add(entry);
                if (!target.OrigOcclusion.ContainsKey(renderer))
                {
                    target.OrigOcclusion[renderer] = renderer.allowOcclusionWhenDynamic;
                    renderer.allowOcclusionWhenDynamic = false;
                }
            }
        }

        // 그 순간 그리던 렌더러(지금 LOD, 손에 든 물건 포함)를 본떠 잔상 하나로 묶는다
        private bool Make(Target target, ref float maxOffset)
        {
            var echo = new Echo { Born = Time.time, Root = new GameObject("PrestigePerks Sonar Echo") };
            foreach (var entry in target.Entries)
            {
                var source = entry.Renderer;
                if (source == null || !source.enabled || !source.gameObject.activeInHierarchy || (entry.Group != null && entry.Lod != CurrentLod(entry.Group)))
                {
                    continue;
                }

                Mesh mesh;
                var scale = Vector3.one;
                if (source is SkinnedMeshRenderer skinned)
                {
                    if (skinned.sharedMesh == null)
                    {
                        continue;
                    }

                    mesh = new Mesh { name = "PrestigePerks Sonar Echo" };
                    // 렌더러 크기까지 넣어 굽는다 -> 잔상은 위치·회전만 따른다
                    skinned.BakeMesh(mesh, true);
                    echo.Baked.Add(mesh);
                }
                else
                {
                    var filter = source.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null)
                    {
                        continue;
                    }

                    mesh = filter.sharedMesh;
                    scale = source.transform.lossyScale;
                }

                var part = new GameObject("part") { layer = source.gameObject.layer };
                part.transform.SetParent(echo.Root.transform, false);
                part.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
                part.transform.localScale = scale;
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = part.AddComponent<MeshRenderer>();
                var materials = new Material[Mathf.Max(1, mesh.subMeshCount)];
                for (var i = 0; i < materials.Length; i++)
                {
                    materials[i] = material;
                }

                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.allowOcclusionWhenDynamic = false;
                echo.Renderers.Add(renderer);
                // 놓인 자리 검사: 잔상 경계 중심과 원래 렌더러 경계 중심 (스킨 렌더러 경계는 대략이라 0.5m 까지 허용)
                maxOffset = Mathf.Max(maxOffset, Vector3.Distance(renderer.bounds.center, source.bounds.center));
            }

            if (echo.Renderers.Count == 0)
            {
                Destroy(echo);
                return false;
            }

            echoes.Add(echo);
            Hook(true);
            return true;
        }

        // LODGroup 의 현재 단계 (Unity 와 같은 화면 높이 비율 계산). -1 = 컬링
        private int CurrentLod(LODGroup group)
        {
            if (group == null || camera == null)
            {
                return -1;
            }

            if (currentLod.TryGetValue(group, out var cached))
            {
                return cached;
            }

            if (!lodCache.TryGetValue(group, out var lods))
            {
                lodCache[group] = lods = group.GetLODs();
            }

            var reference = group.transform.TransformPoint(group.localReferencePoint);
            var s = group.transform.lossyScale;
            var size = group.size * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            var distance = Vector3.Distance(camera.transform.position, reference);
            var relative = size / (2f * Mathf.Max(0.01f, distance) * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad)) * QualitySettings.lodBias;
            var result = -1;
            for (var i = QualitySettings.maximumLODLevel; i < lods.Length; i++)
            {
                if (relative >= lods[i].screenRelativeTransitionHeight)
                {
                    result = i;
                    break;
                }
            }

            currentLod[group] = result;
            return result;
        }

        private static void Destroy(Echo echo)
        {
            if (echo.Root != null)
            {
                Object.Destroy(echo.Root);
            }

            foreach (var mesh in echo.Baked)
            {
                if (mesh != null)
                {
                    Object.Destroy(mesh);
                }
            }
        }

        private void Hook(bool on)
        {
            if (on == hooked)
            {
                return;
            }

            hooked = on;
            if (on)
            {
                Camera.onPreRender += OnPreRender;
            }
            else
            {
                Camera.onPreRender -= OnPreRender;
            }
        }

        // 카메라마다 셰이더 전역 값: 메인 카메라만 가까운 물체 뒤·PiP 렌즈 영역을 비운다
        private static void OnPreRender(Camera cam)
        {
            if (cam == null)
            {
                return;
            }

            try
            {
                var main = cam == CameraManager.Instance.Camera;
                Shader.SetGlobalFloat(UseDepthId, main ? 1f : 0f);
                Shader.SetGlobalFloat(NearCutId, NearCut);
                Shader.SetGlobalFloat(LensFlipId, 0f);
                Shader.SetGlobalVector(LensId, main ? LensEllipse(cam) : Vector4.zero);
            }
            catch (System.Exception e)
            {
                ActivePerkLog.Error("sonar pre render", e);
            }
        }

        // PiP 렌즈 렌더러 경계를 메인 카메라 뷰포트에 투영한 타원 (중심 xy, 반지름 zw). 렌즈가 없으면 0
        private static Vector4 LensEllipse(Camera cam)
        {
            var sight = CameraManager.Instance.OpticCameraManager?.CurrentOpticSight;
            var lens = sight != null ? sight.LensRenderer : null;
            if (lens == null || !lens.enabled || !lens.gameObject.activeInHierarchy)
            {
                return Vector4.zero;
            }

            var bounds = lens.bounds;
            float x0 = 1f, y0 = 1f, x1 = 0f, y1 = 0f;
            for (var i = 0; i < 8; i++)
            {
                var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var v = cam.WorldToViewportPoint(corner);
                if (v.z <= 0f)
                {
                    return Vector4.zero;
                }

                x0 = Mathf.Min(x0, v.x);
                y0 = Mathf.Min(y0, v.y);
                x1 = Mathf.Max(x1, v.x);
                y1 = Mathf.Max(y1, v.y);
            }

            return new Vector4((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (x1 - x0) * 0.5f, (y1 - y0) * 0.5f);
        }
    }
}
