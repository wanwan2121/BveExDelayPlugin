using System;
using System.Collections.Generic;
using System.Linq;

namespace BveDelayEx
{
    /// <summary>抽選に必要な駅の情報（BVE のクラスから写し取ったもの）。</summary>
    internal sealed class StationInfo
    {
        public int Index;
        public double Location;
        public bool Pass;
        public bool IsTerminal;
        public int DepartureMilliseconds;
    }

    /// <summary>発生予定の 1 件。</summary>
    internal sealed class PlannedIncident
    {
        public int StationIndex;
        public double StationLocation;

        /// <summary>駅間停車型で、ブレーキを掛け始める位置（m）。駅構内型では使わない。</summary>
        public double TriggerLocation;

        public int DepartureMilliseconds;
        public IncidentDef Incident;

        /// <summary>停車保持時間（ミリ秒）</summary>
        public int HoldMilliseconds;

        public bool Finished;
    }

    internal static class Planner
    {
        private sealed class Candidate
        {
            public StationInfo Station;
            public double Trigger;
            public List<IncidentDef> Feasible;
        }

        /// <summary>
        /// 現在位置より先の駅から、設定に従って発生予定を抽選する。
        /// </summary>
        public static List<PlannedIncident> Build(IList<StationInfo> stations, double currentLocation, DelaySettings settings, Random rng)
        {
            List<PlannedIncident> result = new List<PlannedIncident>();

            List<IncidentDef> enabled = Incidents.All.Where(i => settings.IncidentEnabled[i.Id]).ToList();
            if (enabled.Count == 0) return result;

            // 1. 各駅について、発生させられる事故があるか調べる
            List<Candidate> candidates = new List<Candidate>();
            for (int k = 0; k < stations.Count; k++)
            {
                StationInfo st = stations[k];
                if (st.Index == 0) continue; // 始発駅では起こさない

                bool inStationOk = !st.Pass && !st.IsTerminal && st.Location > currentLocation + 100;

                double trigger = 0;
                bool interOk = false;
                if (k >= 1)
                {
                    double prev = stations[k - 1].Location;
                    trigger = Math.Max(prev + 300, st.Location - rng.Next(600, 1201));
                    interOk = trigger > currentLocation + 50 && trigger < st.Location - 150;
                }

                List<IncidentDef> feasible = enabled.Where(i => i.InStation ? inStationOk : interOk).ToList();
                if (feasible.Count == 0) continue;

                candidates.Add(new Candidate { Station = st, Trigger = trigger, Feasible = feasible });
            }

            // 2. 設定に応じて、発生させる駅を選ぶ
            List<Candidate> selected = new List<Candidate>();
            if (settings.Mode == PlanMode.Count)
            {
                Shuffle(candidates, rng);
                selected.AddRange(candidates.Take(settings.Count));
            }
            else
            {
                foreach (Candidate c in candidates)
                {
                    if (rng.Next(100) < settings.ProbabilityPercent) selected.Add(c);
                }
            }

            // 3. 駅ごとに事故の種類と停車時間を決める
            foreach (Candidate c in selected.OrderBy(c => c.Station.Location))
            {
                IncidentDef incident = PickWeighted(c.Feasible, rng);
                int holdSec = rng.Next(incident.HoldMinSec, incident.HoldMaxSec + 1);
                int holdMs = Math.Max(5000, (int)(holdSec * 1000L * settings.HoldScalePercent / 100));

                result.Add(new PlannedIncident
                {
                    StationIndex = c.Station.Index,
                    StationLocation = c.Station.Location,
                    TriggerLocation = c.Trigger,
                    DepartureMilliseconds = c.Station.DepartureMilliseconds,
                    Incident = incident,
                    HoldMilliseconds = holdMs,
                });
            }

            return result;
        }

        private static IncidentDef PickWeighted(List<IncidentDef> list, Random rng)
        {
            int total = list.Sum(i => i.Weight);
            int r = rng.Next(total);
            foreach (IncidentDef i in list)
            {
                r -= i.Weight;
                if (r < 0) return i;
            }
            return list[list.Count - 1];
        }

        private static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
