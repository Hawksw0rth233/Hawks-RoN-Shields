using System;
using BepInEx.Configuration;
using UnityEngine;

namespace Hawks.RonShields
{
    internal sealed class ConfigurationManagerAttributes
    {
        public Action<ConfigEntryBase> CustomDrawer;
    }

    internal sealed class PositionButtons
    {
        private int stepIndex = 1;
        private static readonly float[] Steps = { 0.1f, 1f, 5f };
        private static readonly string[] StepLabels = { "0.1 cm", "1 cm", "5 cm" };

        private readonly bool rotation;
        private static readonly string[] AngleSteps = { "0.1°", "1°", "5°" };
        private PositionButtons(bool rotation) { this.rotation = rotation; }

        internal static ConfigDescription Describe(string description, bool rotation = false)
        {
            var panel = new PositionButtons(rotation);
            return new ConfigDescription(description, null,
                new ConfigurationManagerAttributes { CustomDrawer = panel.Draw });
        }

        private void Draw(ConfigEntryBase setting)
        {
            var entry = (ConfigEntry<Vector3>)setting;
            GUILayout.BeginVertical();
            stepIndex = GUILayout.Toolbar(stepIndex, rotation ? AngleSteps : StepLabels);
            DrawAxis(entry, 0, rotation ? "抬起" : "左", rotation ? "压低" : "右");
            DrawAxis(entry, 1, rotation ? "左转" : "下", rotation ? "右转" : "上");
            DrawAxis(entry, 2, rotation ? "右倾" : "后", rotation ? "左倾" : "前");
            if (GUILayout.Button("本组归零")) entry.Value = Vector3.zero;
            GUILayout.EndVertical();
        }

        private void DrawAxis(ConfigEntry<Vector3> entry, int axis, string minus, string plus)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(minus)) Move(entry, axis, -Steps[stepIndex]);
            GUILayout.Label(entry.Value[axis].ToString("0.##") + (rotation ? "°" : " cm"), GUILayout.Width(68));
            if (GUILayout.Button(plus)) Move(entry, axis, Steps[stepIndex]);
            GUILayout.EndHorizontal();
        }

        private static void Move(ConfigEntry<Vector3> entry, int axis, float distance)
        {
            var value = entry.Value;
            value[axis] = (float)Math.Round(value[axis] + distance, 4);
            entry.Value = value;
        }
    }
}
