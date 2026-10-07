using System;
using UnityEngine;
using Dustline;
using HarmonyLib;

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

        /// <summary>供控制台调用：强制设置第三人称开关。</summary>
        public static void SetThirdPerson(bool value)
        {
            if (Instance == null && value) _ = Instance;
            Instance.thirdPersonOn = value;
            Instance.toastText = value ? "第三人称：开" : "第三人称：关";
            Instance.toastUntil = Time.unscaledTime + 1.6f;
        }

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
        private string toastText = string.Empty;
        private float toastUntil;

        private void Update()
        {
            ResolveToggleKey();
            if (Input.GetKeyDown(toggleKey))
            {
                thirdPersonOn = !thirdPersonOn;
                toastText = thirdPersonOn ? "第三人称：开" : "第三人称：关";
                toastUntil = Time.unscaledTime + 1.6f;
                MikuModPlugin.Log?.LogInfo("Third person " + (thirdPersonOn ? "ON" : "OFF"));
            }
            Active = Evaluate();
            if (!Active) RestoreFirstPersonViewmodel();
        }

        /// <summary>从第三人称切回来时，兜底恢复第一人称手臂+武器视图模型。</summary>
        private static void RestoreFirstPersonViewmodel()
        {
            try
            {
                Game game = Game.Instance;
                if (game == null || game.Camera == null || game.Scoped) return;
                Dustline.WeaponView view = game.Camera.GetComponent<Dustline.WeaponView>();
                if (view == null) return;
                Transform pivot = ViewmodelPivot(view);
                if (pivot != null && !pivot.gameObject.activeSelf) pivot.gameObject.SetActive(true);
            }
            catch (Exception e)
            {
                MikuModPlugin.Log?.LogWarning("Restore viewmodel failed: " + e.Message);
            }
        }

        private static readonly AccessTools.FieldRef<Dustline.WeaponView, Transform> ViewmodelPivotRef =
            AccessTools.FieldRefAccess<Dustline.WeaponView, Transform>("pivot");

        private static Transform ViewmodelPivot(Dustline.WeaponView view) => ViewmodelPivotRef(view);

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(toastText) || Time.unscaledTime > toastUntil) return;
            float remain = toastUntil - Time.unscaledTime;
            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(remain));
            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 26,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            GUI.Label(new Rect(0f, Screen.height * 0.18f, Screen.width, 40f), toastText, style);
            GUI.color = previous;
        }

        private static bool Evaluate()
        {
            if (!MikuConfig.ThirdPersonEnabled) return false;
            if (!Instance.thirdPersonOn) return false;
            Game game = Game.Instance;
            if (game == null || game.Camera == null) return false;
            if (!game.Playing || game.Paused) return false;
            if (game.Scoped) return false;          // 开镜保持第一人称
            return true;
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
            // 控制台 fov 命令的覆盖值（-1 表示不覆盖）
            if (MikuConfig.FovOverride > 0f)
            {
                Game game = Game.Instance;
                if (game?.Camera != null && Mathf.Abs(game.Camera.fieldOfView - MikuConfig.FovOverride) > 0.01f)
                {
                    game.Camera.fieldOfView = MikuConfig.FovOverride;
                }
            }

            if (!Active) return;
            Game camGame = Game.Instance;
            if (camGame == null || camGame.Camera == null) return;

            Transform cameraTransform = camGame.Camera.transform;
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