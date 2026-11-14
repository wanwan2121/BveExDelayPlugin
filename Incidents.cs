using System;

namespace BveDelayEx
{
    /// <summary>事故の定義。</summary>
    internal sealed class IncidentDef
    {
        /// <summary>0 から始まる ID。パネル・サウンドの番号にも使う。</summary>
        public int Id { get; }
        public string Name { get; }

        /// <summary>true: 駅構内型（駅に停車してから保持）／ false: 駅間停車型（駅の手前でブレーキ）</summary>
        public bool InStation { get; }

        /// <summary>true: 非常ブレーキで止める／ false: 常用最大ブレーキで止める</summary>
        public bool Emergency { get; }

        /// <summary>抽選の重み</summary>
        public int Weight { get; }

        /// <summary>停車保持時間（秒）の範囲</summary>
        public int HoldMinSec { get; }
        public int HoldMaxSec { get; }

        public IncidentDef(int id, string name, bool inStation, bool emergency, int weight, int holdMinSec, int holdMaxSec)
        {
            Id = id;
            Name = name;
            InStation = inStation;
            Emergency = emergency;
            Weight = weight;
            HoldMinSec = holdMinSec;
            HoldMaxSec = holdMaxSec;
        }
    }

    internal static class Incidents
    {
        /// <summary>ATSパネル配列の先頭番号。PanelBase + 事故ID が、その事故の発生中だけ 1 になる。</summary>
        public const int PanelBase = 200;

        /// <summary>人身事故の ID（窓割れ表示の対象）</summary>
        public const int Jinshin = 0;

        // 並び順は設定画面のチェックリストと同じ。ID は並び順（0 始まり）と一致させること。
        public static readonly IncidentDef[] All =
        {
            //                id  名前                 駅構内  非常   重み  最短  最長
            new IncidentDef(0, "人身事故",           false, true,  10, 300, 600),
            new IncidentDef(1, "沿線火災",           false, false,  8, 240, 480),
            new IncidentDef(2, "車両故障",           false, false, 20, 120, 300),
            new IncidentDef(3, "非常ボタン動作",     false, true,  15,  60, 150),
            new IncidentDef(4, "踏切支障",           false, true,  15,  90, 240),
            new IncidentDef(5, "駅火災",             true,  false,  5, 240, 480),
            new IncidentDef(6, "車両火災",           true,  false,  5, 240, 480),
            new IncidentDef(7, "他列車の脱線事故",   false, false,  3, 300, 600),
            new IncidentDef(8, "動物支障",           false, true,  19,  60, 150),
        };
    }
}
