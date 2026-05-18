using BepInEx;
using UnityEngine;
using HarmonyLib;
using TMPro;
using System.Collections.Generic;

namespace ValheimPerformanceDisplay
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class PerformanceDisplayPlugin : BaseUnityPlugin
    {
        public const string ModGUID = "nl.playhere.valheimperformancedisplay";
        public const string ModName = "Valheim Performance Display";
        public const string ModVersion = "1.0.0";

        private static PingDisplayPlugin Instance;

        private Harmony _harmony;

        private GameObject _canvasObject;
        private TextMeshProUGUI _pingText;

        private float _currentPing;
        private readonly Queue<float> _samples = new Queue<float>();

        private const int MaxSamples = 10;
        private const float PingInterval = 2f;

        private void Awake()
        {
            Instance = this;

            _harmony = new Harmony(ModGUID);
            _harmony.PatchAll();

            Logger.LogInfo($"{ModName} loaded");
        }

        private void Start()
        {
            InvokeRepeating(nameof(SendPing), 2f, PingInterval);
        }

        private void Update()
        {
            EnsureUI();

            if (_pingText != null)
            {
                _pingText.text = $"Ping: {_currentPing:0} ms";

                if (_currentPing < 50)
                    _pingText.color = Color.green;
                else if (_currentPing < 100)
                    _pingText.color = Color.yellow;
                else
                    _pingText.color = Color.red;
            }
        }

        private void EnsureUI()
        {
            if (_canvasObject != null)
                return;

            if (Hud.instance == null)
                return;

            _canvasObject = new GameObject("PingDisplayCanvas");

            Canvas canvas = _canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;

            _canvasObject.AddComponent<CanvasScaler>();
            _canvasObject.AddComponent<GraphicRaycaster>();

            DontDestroyOnLoad(_canvasObject);

            GameObject textObj = new GameObject("PingText");

            textObj.transform.SetParent(_canvasObject.transform, false);

            _pingText = textObj.AddComponent<TextMeshProUGUI>();

            _pingText.fontSize = 26;
            _pingText.alignment = TextAlignmentOptions.TopLeft;
            _pingText.text = "Ping: -- ms";

            RectTransform rect = _pingText.rectTransform;

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);

            rect.anchoredPosition = new Vector2(20f, -20f);
            rect.sizeDelta = new Vector2(300f, 50f);
        }

        private void SendPing()
        {
            if (ZRoutedRpc.instance == null)
                return;

            if (ZNet.instance == null)
                return;

            ZNetPeer peer = ZNet.instance.GetServerPeer();

            if (peer == null)
                return;

            double timestamp = Time.realtimeSinceStartupAsDouble;

            peer.m_rpc.Invoke("VPD_Ping", timestamp);
        }

        private void ReceivePing(double sentTime)
        {
            double now = Time.realtimeSinceStartupAsDouble;

            float rtt = (float)((now - sentTime) * 1000.0);

            _samples.Enqueue(rtt);

            while (_samples.Count > MaxSamples)
                _samples.Dequeue();

            float total = 0f;

            foreach (float sample in _samples)
                total += sample;

            _currentPing = total / _samples.Count;
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class RegisterRpcPatch
        {
            private static void Postfix()
            {
                if (ZRoutedRpc.instance == null)
                    return;

                ZRoutedRpc.instance.Register<double>(
                    "VPD_Ping",
                    RPC_Ping
                );
            }
        }

        private static void RPC_Ping(long sender, double sentTime)
        {
            if (ZNet.instance == null)
                return;

            // If we are server, echo back to sender
            if (ZNet.instance.IsServer())
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(
                    sender,
                    "VPD_PingResponse",
                    sentTime
                );

                return;
            }
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class RegisterResponseRpcPatch
        {
            private static void Postfix()
            {
                if (ZRoutedRpc.instance == null)
                    return;

                ZRoutedRpc.instance.Register<double>(
                    "VPD_PingResponse",
                    RPC_PingResponse
                );
            }
        }

        private static void RPC_PingResponse(long sender, double sentTime)
        {
            Instance?.ReceivePing(sentTime);
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
