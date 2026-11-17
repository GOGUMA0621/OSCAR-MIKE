using System;
using UnityEngine;

namespace OskarMike.Core
{
    /// <summary>조준 입력 방식.</summary>
    public enum AimInputMode
    {
        Hold = 0,   // 우클릭을 누르고 있는 동안 조준
        Toggle = 1  // 우클릭 한 번에 조준 시작/해제
    }

    /// <summary>
    /// 로컬 플레이어 설정 (PlayerPrefs 저장, 네트워크 동기화 없음).
    /// </summary>
    public static class GameSettings
    {
        private const string AimModeKey = "OM.Settings.AimMode";
        private const string AimSensitivityKey = "OM.Settings.AimSensitivity";

        public const float MinAimSensitivity = 0.3f;
        public const float MaxAimSensitivity = 1.5f;

        private static bool loaded;
        private static AimInputMode aimMode = AimInputMode.Hold;
        private static float aimSensitivity = 1f;

        /// <summary>설정이 바뀌면 호출.</summary>
        public static event Action Changed;

        public static AimInputMode AimMode
        {
            get { Load(); return aimMode; }
            set
            {
                Load();
                if (aimMode == value) return;
                aimMode = value;
                PlayerPrefs.SetInt(AimModeKey, (int)value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>조준 중 감도 배율 (화면 확대 비율 보정 위에 곱해진다. 1 = 기본).</summary>
        public static float AimSensitivity
        {
            get { Load(); return aimSensitivity; }
            set
            {
                Load();
                value = Mathf.Clamp(value, MinAimSensitivity, MaxAimSensitivity);
                if (Mathf.Approximately(aimSensitivity, value)) return;
                aimSensitivity = value;
                PlayerPrefs.SetFloat(AimSensitivityKey, value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            int mode = PlayerPrefs.GetInt(AimModeKey, (int)AimInputMode.Hold);
            aimMode = Enum.IsDefined(typeof(AimInputMode), mode) ? (AimInputMode)mode : AimInputMode.Hold;
            aimSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(AimSensitivityKey, 1f), MinAimSensitivity, MaxAimSensitivity);
        }

        // 도메인 리로드 비활성(Enter Play Mode Options) 환경에서도 매 플레이마다 다시 읽는다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            loaded = false;
            Changed = null;
        }
    }
}
