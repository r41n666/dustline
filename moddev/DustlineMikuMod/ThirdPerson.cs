using System;
using UnityEngine;
using Dustline;

namespace DustlineMikuMod
{
    /// <summary>
    /// 第三人称相机：V 键在第一/第三人称之间切换。
    /// 相机位置在游戏每帧设置完 eye/pitch/yaw 之后（LateUpdate）沿视线向后拉，
    /// 并用 SphereCast 做贴墙收缩；开镜（Scoped）时自动回到第一人称。
    /// </summary>
    internal sealed class ThirdPersonController : MonoBehaviour
    {
        private static ThirdPersonController instance;

        private bool thirdPersonOn;
        private float smoothedDistance;
        private bool initialized;

        public static bool Active { get; private set; }

        public static ThirdPersonController Instance
        {
            get
            {
                if (instance != null) return instance;
                GameObject host = new GameObject("DustlineMikuMod.ThirdPerson");
                UnityEngine.Object.DontDestroyOnLoad(host);
                instance = host.AddComponent<ThirdPersonController>();
                return instance;
            }
        }

        private static KeyCode toggleKey = KeyCode.V;
        private static bool toggleKeyResolved;

        private void Update()
        {
            ResolveToggleKey();
            if (Input.GetKeyDown(toggleKey))
            {
                thirdPersonOn = !thirdPersonOn;
                MikuModPlugin.Log?.LogInfo("Third person " + (thirdPersonOn ? "ON" : "OFF"));
            }
            Active = Evaluate();
        }

        private static bool Evaluate()
        {
            if (!MikuConfig.ThirdPersonEnabled) return false;
            Game game = Game.Instance;
            if (game == null || !game.Playing || game.Paused) return false;
            if (game.Local == null || !game.Local.Alive) return false;
            if (game.Viewing != game.Local) return false;      // 观战/死亡时不切换
            if (game.Scoped) return false;                     // 开镜保持第一人称
            Camera camera = game.Camera;
            return camera != null;
        }

        private static void ResolveToggleKey()
        {
            if (toggleKeyResolved) return;
            if (Enum.TryParse(MikuConfig.ThirdPersonKey, true, out KeyCode parsed))
            {
                toggleKey = parsed;
                toggleKeyResolved = true;
            }
            else
            {
                MikuModPlugin.Log?.LogWarning("Invalid ThirdPerson.ToggleKey: " + MikuConfig.ThirdPersonKey);
            }
        }

        private void LateUpdate()
        {
            if (!Active) return;
            Game game = Game.Instance;
            if (game == null || game.Camera == null) return;

            Transform cameraTransform = game.Camera.transform;
            Vector3 eye = cameraTransform.position;
            Quaternion rotation = cameraTransform.rotation;

            Vector3 offset = new Vector3(MikuConfig.ThirdPersonShoulder, MikuConfig.ThirdPersonHeight, -MikuConfig.ThirdPersonDistance);
            Vector3 desired = eye + rotation * offset;

            Vector3 delta = desired - eye;
            float distance = delta.magnitude;
            if (distance > 0.001f)
            {
                Vector3 direction = delta / distance;
                float allowed = distance;
                int mask = ~(1 << 31) & ~(1 << 2);
                if (Physics.SphereCast(eye, 0.22f, direction, out RaycastHit hit, distance, mask, QueryTriggerInteraction.Ignore))
                {
                    allowed = Mathf.Max(MikuConfig.ThirdPersonMinDistance, hit.distance - 0.12f);
                }
                smoothedDistance = initialized
                    ? Mathf.Lerp(smoothedDistance, allowed, 1f - Mathf.Exp(-24f * Time.unscaledDeltaTime))
                    : allowed;
                smoothedDistance = Mathf.Clamp(smoothedDistance, MikuConfig.ThirdPersonMinDistance, distance);
                cameraTransform.position = eye + direction * smoothedDistance;
            }
            initialized = true;
        }
    }
}