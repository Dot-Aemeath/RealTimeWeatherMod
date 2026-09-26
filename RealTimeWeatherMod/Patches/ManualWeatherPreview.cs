using System;
using ChillWithYou.EnvSync.Core;
using UnityEngine;

namespace ChillWithYou.EnvSync.Patches
{
    public partial class ModSettingsIntegration
    {
        private Rect _previewRect = new Rect(24, 80, 420, 315);
        private static readonly int[] PreviewWeatherCodes = { 1, 4, 7, 9, 30, 31, 28, 13, 14, 15, 11, 16 };
        private static readonly string[] PreviewWeatherLabels = { "晴 / Clear", "多云 / Cloudy", "大部多云 / Mostly", "阴 / Overcast",
            "雾 / Fog", "霾 / Haze", "沙尘暴 / Dust", "小雨 / Light", "中雨 / Moderate", "大雨 / Heavy", "雷阵雨 / Thunder",
            "暴雨 / Storm" };

        private void Update()
        {
            if (ChillEnvPlugin.Cfg_DebugMode == null || !ChillEnvPlugin.Cfg_DebugMode.Value)
            {
                WeatherDebugControl.StopManualPreview();
                return;
            }
            if (!Input.GetKeyDown(KeyCode.F6)) return;
            if (WeatherDebugControl.ManualPreviewActive)
                WeatherDebugControl.StopManualPreview();
            else
                WeatherDebugControl.StartManualPreview();
        }

        private void OnGUI()
        {
            if (ChillEnvPlugin.Cfg_DebugMode == null || !ChillEnvPlugin.Cfg_DebugMode.Value ||
                !WeatherDebugControl.ManualPreviewActive) return;
            _previewRect.x = Mathf.Clamp(_previewRect.x, 0, Mathf.Max(0, Screen.width - _previewRect.width));
            _previewRect.y = Mathf.Clamp(_previewRect.y, 0, Mathf.Max(0, Screen.height - _previewRect.height));
            var color = GUI.color;
            GUI.color = new Color(0.08f, 0.1f, 0.14f, 1f);
            GUI.DrawTexture(_previewRect, Texture2D.whiteTexture);
            GUI.color = color;
            _previewRect = GUI.Window(GetInstanceID(), _previewRect, DrawWeatherPreview,
                "天气预览 / Weather preview (F6)");
        }

        private void DrawWeatherPreview(int id)
        {
            var playback = WeatherDebugControl.Playback;
            int minute = (int)playback.Now(Time.realtimeSinceStartupAsDouble).TimeOfDay.TotalMinutes;
            int weatherIndex = Array.IndexOf(PreviewWeatherCodes, playback.Inputs.Weather.Code);
            GUILayout.Label($"时间 / Time: {minute / 60:00}:{minute % 60:00}");
            int selected = Mathf.RoundToInt(GUILayout.HorizontalSlider(minute, 0, 1439,
                GUILayout.Height(24)));
            GUILayout.BeginHorizontal();
            GUILayout.Label("00:00");
            GUILayout.FlexibleSpace();
            GUILayout.Label("12:00");
            GUILayout.FlexibleSpace();
            GUILayout.Label("23:59");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("−15 min")) selected = Math.Max(0, minute - 15);
            if (GUILayout.Button("+15 min")) selected = Math.Min(1439, minute + 15);
            GUILayout.EndHorizontal();
            int selectedWeather = GUILayout.SelectionGrid(weatherIndex, PreviewWeatherLabels, 4);
            if (selectedWeather >= 0 && (selected != minute || selectedWeather != weatherIndex))
                WeatherDebugControl.SetManualWeatherPreview(selected, PreviewWeatherCodes[selectedWeather]);
            GUILayout.Label("日出 06:30 · 日落 18:30 · 平滑预览\nSunrise 06:30 · Sunset 18:30 · Smooth preview");
            if (GUILayout.Button("退出预览，恢复同步 / Exit preview"))
                WeatherDebugControl.StopManualPreview();
            GUI.DragWindow(new Rect(0, 0, _previewRect.width, 24));
        }

        private void OnDisable()
        {
            WeatherDebugControl.StopManualPreview();
        }
    }
}
