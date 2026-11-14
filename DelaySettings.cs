using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BveDelayEx
{
    internal enum PlanMode
    {
        /// <summary>1 回の走行で、ランダムに選んだ N 駅で発生させる。</summary>
        Count,

        /// <summary>各駅で x% の確率で発生させる。</summary>
        Probability,
    }

    /// <summary>設定。DLL と同じフォルダの BveDelayEx.ini に保存する。</summary>
    internal sealed class DelaySettings
    {
        public bool Enabled = true;
        public PlanMode Mode = PlanMode.Count;
        public int Count = 1;
        public int ProbabilityPercent = 10;
        public int HoldScalePercent = 100;
        public bool ShowPlan = false;
        public bool[] IncidentEnabled;

        public DelaySettings()
        {
            IncidentEnabled = new bool[Incidents.All.Length];
            for (int i = 0; i < IncidentEnabled.Length; i++) IncidentEnabled[i] = true;
        }

        public static DelaySettings Load(string path)
        {
            DelaySettings s = new DelaySettings();
            try
            {
                if (!File.Exists(path)) return s;

                foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string value = line.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "Enabled": s.Enabled = value == "1"; break;
                        case "Mode": s.Mode = value == "Probability" ? PlanMode.Probability : PlanMode.Count; break;
                        case "Count": s.Count = ParseInt(value, s.Count, 1, 50); break;
                        case "ProbabilityPercent": s.ProbabilityPercent = ParseInt(value, s.ProbabilityPercent, 1, 100); break;
                        case "HoldScalePercent": s.HoldScalePercent = ParseInt(value, s.HoldScalePercent, 10, 500); break;
                        case "ShowPlan": s.ShowPlan = value == "1"; break;
                        case "Incidents":
                            for (int i = 0; i < s.IncidentEnabled.Length && i < value.Length; i++)
                            {
                                s.IncidentEnabled[i] = value[i] == '1';
                            }
                            break;
                    }
                }
            }
            catch (Exception)
            {
                // 読めなければ既定値のまま使う
            }
            return s;
        }

        public void Save(string path)
        {
            try
            {
                StringBuilder incidents = new StringBuilder();
                foreach (bool on in IncidentEnabled) incidents.Append(on ? '1' : '0');

                List<string> lines = new List<string>
                {
                    "; BveDelayEx の設定（設定ウィンドウから自動保存されます）",
                    "Enabled=" + (Enabled ? "1" : "0"),
                    "Mode=" + Mode,
                    "Count=" + Count.ToString(CultureInfo.InvariantCulture),
                    "ProbabilityPercent=" + ProbabilityPercent.ToString(CultureInfo.InvariantCulture),
                    "HoldScalePercent=" + HoldScalePercent.ToString(CultureInfo.InvariantCulture),
                    "ShowPlan=" + (ShowPlan ? "1" : "0"),
                    "; 事故の種類（並び順は 人身事故, 沿線火災, 車両故障, 非常ボタン動作, 踏切支障, 駅火災, 車両火災, 他列車の脱線事故, 動物支障）",
                    "Incidents=" + incidents,
                };
                File.WriteAllLines(path, lines, new UTF8Encoding(false));
            }
            catch (Exception)
            {
                // 書き込めなくても動作は続ける
            }
        }

        private static int ParseInt(string text, int fallback, int min, int max)
        {
            int value;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return fallback;
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
