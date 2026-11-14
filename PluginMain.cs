using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;

using BveTypes.ClassWrappers;

using BveEx.Extensions.ContextMenuHacker;
using BveEx.Extensions.Native;
using BveEx.Extensions.SoundFactory;
using BveEx.PluginHost;
using BveEx.PluginHost.Plugins;
using BveEx.PluginHost.Plugins.Extensions;

namespace BveDelayEx
{
    /// <summary>
    /// ランダムな駅で事故・遅延を発生させる BveEX 拡張機能。
    ///
    /// 流れ
    ///   1. シナリオ開始時（駅ジャンプ時を含む）に、設定に従って発生予定を抽選する
    ///   2. 駅間停車型 : 駅の手前でブレーキ → 停車 → 保持 → 再開
    ///      駅構内型   : 駅に停車 → 発車時刻から保持 → 再開
    ///   3. 発生中はパネル配列・放送・窓割れ画像で表現する
    /// </summary>
    [Plugin(PluginType.Extension)]
    public class PluginMain : AssemblyPluginBase, IExtension
    {
        private enum Phase
        {
            Idle,
            Braking,
            Holding,
        }

        // 駅間停車型でブレーキを掛けてから、この時間（ミリ秒）たっても止まらなければ諦める
        private const int BrakingTimeoutMilliseconds = 180 * 1000;

        // 窓割れ画像を隠すとき・出すときの位置（パネル座標）
        private static readonly SlimDX.Matrix HiddenMatrix = SlimDX.Matrix.Translation(0f, 10000f, 0f);
        private static readonly SlimDX.Matrix ShownMatrix = SlimDX.Matrix.Translation(0f, 0f, 0f);

        private readonly string PluginDirectory;
        private readonly string SettingsPath;
        private readonly DelaySettings Settings;
        private readonly Random Rng = new Random();

        private readonly INative Native;
        private readonly SettingsForm Form;
        private readonly ToolStripMenuItem EnableItem;
        private readonly ToolStripMenuItem SettingsItem;

        private readonly Sound[] AnnounceSounds = new Sound[Incidents.All.Length];
        private Sound ResumeSound;
        private VehiclePanelElement CrackElement;
        private bool CrackVisible;

        private List<PlannedIncident> Plan = new List<PlannedIncident>();
        private PlannedIncident Active;
        private Phase CurrentPhase = Phase.Idle;
        private int HoldEnd;
        private int BrakingStart;
        private bool SuppressMenuEvents;

        public PluginMain(PluginBuilder builder) : base(builder)
        {
            PluginDirectory = Path.GetDirectoryName(Location);
            SettingsPath = Path.Combine(PluginDirectory, "BveDelayEx.ini");
            Settings = DelaySettings.Load(SettingsPath);

            Native = Extensions.GetExtension<INative>();
            Native.Started += OnStarted;
            BveHacker.ScenarioCreated += OnScenarioCreated;

            Form = new SettingsForm(Settings);
            Form.Applied += OnSettingsApplied;
            Form.FormClosing += OnFormClosing;

            IContextMenuHacker contextMenuHacker = Extensions.GetExtension<IContextMenuHacker>();
            EnableItem = contextMenuHacker.AddCheckableMenuItem("遅延・事故の発生を有効にする", OnEnableChanged, ContextMenuItemType.Plugins);
            SettingsItem = contextMenuHacker.AddCheckableMenuItem("遅延・事故の設定ウィンドウを表示", OnSettingsItemChanged, ContextMenuItemType.Plugins);

            SuppressMenuEvents = true;
            EnableItem.Checked = Settings.Enabled;
            SettingsItem.Checked = false;
            SuppressMenuEvents = false;

            if (BveHacker.IsScenarioCreated)
            {
                PrepareScenario(BveHacker.Scenario);
                Replan();
            }
        }

        public override void Dispose()
        {
            Native.Started -= OnStarted;
            BveHacker.ScenarioCreated -= OnScenarioCreated;

            ClearPanel();
            DisposeSounds();

            Form.FormClosing -= OnFormClosing;
            Form.Close();
            Form.Dispose();

            EnableItem.Dispose();
            SettingsItem.Dispose();
        }

        // ------------------------------------------------------------------
        // シナリオの準備・抽選
        // ------------------------------------------------------------------

        private void OnScenarioCreated(ScenarioCreatedEventArgs e)
        {
            PrepareScenario(e.Scenario);
        }

        /// <summary>シナリオ開始直後と駅ジャンプ時に呼ばれる。</summary>
        private void OnStarted(object sender, StartedEventArgs e)
        {
            AbortIncident();
            Replan();
        }

        private void PrepareScenario(Scenario scenario)
        {
            // 前のシナリオの状態を捨てる（パネルは作り直されているので、旧い窓割れ画像は触らない）
            CrackElement = null;
            CrackVisible = false;
            Active = null;
            CurrentPhase = Phase.Idle;
            Plan = new List<PlannedIncident>();

            try
            {
                string crackPath = Path.Combine(PluginDirectory, "crack.png");
                if (File.Exists(crackPath))
                {
                    VehiclePanelElement element = new VehiclePanelElement()
                    {
                        DaytimeImagePath = crackPath,
                    };
                    element.CreateModels();
                    element.Matrix = HiddenMatrix;
                    scenario.Vehicle.Panel.Elements.Add(element);
                    CrackElement = element;
                }
            }
            catch (Exception)
            {
                CrackElement = null; // 窓割れ画像が使えなくても、他の動作は続ける
            }

            LoadSounds();
        }

        private void LoadSounds()
        {
            DisposeSounds();

            try
            {
                ISoundFactory soundFactory = Extensions.GetExtension<ISoundFactory>();
                string directory = Path.Combine(PluginDirectory, "sounds");

                for (int i = 0; i < AnnounceSounds.Length; i++)
                {
                    string path = Path.Combine(directory, $"announce_{i}.wav");
                    if (File.Exists(path)) AnnounceSounds[i] = soundFactory.LoadFrom(path, 1, Sound.SoundPosition.Cab);
                }

                string resumePath = Path.Combine(directory, "resume.wav");
                if (File.Exists(resumePath)) ResumeSound = soundFactory.LoadFrom(resumePath, 1, Sound.SoundPosition.Cab);
            }
            catch (Exception)
            {
                // 放送が読めなくても、他の動作は続ける
            }
        }

        private void DisposeSounds()
        {
            for (int i = 0; i < AnnounceSounds.Length; i++)
            {
                try { AnnounceSounds[i]?.Dispose(); } catch (Exception) { }
                AnnounceSounds[i] = null;
            }

            try { ResumeSound?.Dispose(); } catch (Exception) { }
            ResumeSound = null;
        }

        /// <summary>現在位置より先の駅から、発生予定を抽選し直す。</summary>
        private void Replan()
        {
            if (!BveHacker.IsScenarioCreated) return;

            Scenario scenario = BveHacker.Scenario;
            StationList stations = scenario.Map.Stations;

            List<StationInfo> infos = new List<StationInfo>();
            for (int i = 0; i < stations.Count; i++)
            {
                Station station = stations[i] as Station;
                if (station is null) continue;

                infos.Add(new StationInfo()
                {
                    Index = infos.Count,
                    Location = station.Location,
                    Pass = station.Pass,
                    IsTerminal = station.IsTerminal,
                    DepartureMilliseconds = station.DepartureTimeMilliseconds,
                });
            }

            Plan = Planner.Build(infos, scenario.VehicleLocation.Location, Settings, Rng);
            UpdatePlanText();
        }

        private void UpdatePlanText()
        {
            StringBuilder text = new StringBuilder();
            if (Plan.Count == 0)
            {
                text.Append("発生予定はありません。");
            }
            else
            {
                foreach (PlannedIncident p in Plan)
                {
                    string kind = p.Incident.InStation ? "駅構内" : "駅間停車";
                    text.AppendLine($"{p.StationIndex + 1} 駅目（{p.StationLocation:F0} m）：{p.Incident.Name}［{kind}・{p.HoldMilliseconds / 1000} 秒］");
                }
            }

            Form.SetPlanText(text.ToString());
        }

        // ------------------------------------------------------------------
        // 毎フレームの処理
        // ------------------------------------------------------------------

        public override void Tick(TimeSpan elapsed)
        {
            if (!BveHacker.IsScenarioCreated) return;

            if (!Settings.Enabled)
            {
                if (CurrentPhase != Phase.Idle) AbortIncident();
                return;
            }

            Scenario scenario = BveHacker.Scenario;
            double location = scenario.VehicleLocation.Location;
            double speed = Math.Abs(scenario.VehicleLocation.Speed); // m/s
            int now = scenario.TimeManager.TimeMilliseconds;

            switch (CurrentPhase)
            {
                case Phase.Idle:
                    TryStart(location, speed, now);
                    break;

                case Phase.Braking:
                    ApplyBrake(scenario);
                    if (speed < 0.05)
                    {
                        BeginHold(now);
                    }
                    else if (now - BrakingStart > BrakingTimeoutMilliseconds)
                    {
                        EndIncident(false);
                    }
                    break;

                case Phase.Holding:
                    ApplyBrake(scenario);
                    if (now >= HoldEnd) EndIncident(true);
                    break;
            }

            UpdatePanel();
        }

        /// <summary>発生予定のうち、条件を満たしたものがあれば開始する。</summary>
        private void TryStart(double location, double speed, int now)
        {
            foreach (PlannedIncident p in Plan)
            {
                if (p.Finished) continue;

                if (p.Incident.InStation)
                {
                    // 駅構内型：駅の付近で停車したら開始
                    if (location > p.StationLocation + 250)
                    {
                        p.Finished = true; // 停車せずに通り過ぎた
                        continue;
                    }

                    if (location >= p.StationLocation - 250 && speed < 0.1)
                    {
                        Active = p;
                        BeginHold(now);
                        return;
                    }
                }
                else
                {
                    // 駅間停車型：ブレーキ開始位置を過ぎたら開始
                    if (location < p.TriggerLocation) continue;

                    if (location >= p.StationLocation - 100)
                    {
                        p.Finished = true; // 開始位置を飛ばして駅の近くまで来てしまった
                        continue;
                    }

                    Active = p;
                    CurrentPhase = Phase.Braking;
                    BrakingStart = now;
                    return;
                }
            }
        }

        private void BeginHold(int now)
        {
            CurrentPhase = Phase.Holding;

            // 駅構内型は発車時刻から数えるので、確実に遅延になる
            long start = Active.Incident.InStation ? Math.Max(now, Active.DepartureMilliseconds) : now;
            HoldEnd = (int)Math.Min(start + Active.HoldMilliseconds, int.MaxValue);

            Play(AnnounceSounds[Active.Incident.Id]);
        }

        private void EndIncident(bool playResume)
        {
            if (Active != null) Active.Finished = true;
            Active = null;
            CurrentPhase = Phase.Idle;

            SetCrack(false);
            ClearPanel();
            if (playResume) Play(ResumeSound);
        }

        private void AbortIncident()
        {
            EndIncident(false);
        }

        private void ApplyBrake(Scenario scenario)
        {
            AtsPlugin atsPlugin = scenario.Vehicle.Instruments.AtsPlugin;
            int emergency = atsPlugin.AtsHandles.NotchInfo.EmergencyBrakeNotch;
            int notch = Active.Incident.Emergency ? emergency : emergency - 1;

            atsPlugin.AtsHandles.PowerNotch = 0;
            atsPlugin.AtsHandles.BrakeNotch = Math.Max(notch, atsPlugin.Handles.BrakeNotch);
            atsPlugin.AtsHandles.ConstantSpeedMode = ConstantSpeedMode.Disable;
        }

        // ------------------------------------------------------------------
        // パネル・窓割れ・放送
        // ------------------------------------------------------------------

        private void UpdatePanel()
        {
            bool holding = CurrentPhase == Phase.Holding && Active != null;

            for (int i = 0; i < Incidents.All.Length; i++)
            {
                Native.AtsPanelArray[Incidents.PanelBase + i] = holding && Active.Incident.Id == i ? 1 : 0;
            }

            SetCrack(holding && Active.Incident.Id == Incidents.Jinshin);
        }

        private void ClearPanel()
        {
            for (int i = 0; i < Incidents.All.Length; i++)
            {
                Native.AtsPanelArray[Incidents.PanelBase + i] = 0;
            }
        }

        private void SetCrack(bool show)
        {
            if (CrackElement is null || show == CrackVisible) return;

            try
            {
                CrackElement.Matrix = show ? ShownMatrix : HiddenMatrix;
                CrackVisible = show;
            }
            catch (Exception)
            {
            }
        }

        private static void Play(Sound sound)
        {
            try
            {
                sound?.Play(1, 1, 0);
            }
            catch (Exception)
            {
            }
        }

        // ------------------------------------------------------------------
        // メニュー・設定ウィンドウ
        // ------------------------------------------------------------------

        private void OnEnableChanged(object sender, EventArgs e)
        {
            if (SuppressMenuEvents) return;

            Settings.Enabled = EnableItem.Checked;
            Settings.Save(SettingsPath);
            if (!Settings.Enabled) AbortIncident();
        }

        private void OnSettingsItemChanged(object sender, EventArgs e)
        {
            if (SuppressMenuEvents) return;

            if (SettingsItem.Checked)
            {
                Form.Show(BveHacker.MainFormSource);
            }
            else
            {
                Form.Hide();
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            SettingsItem.Checked = false; // メニューのチェックが外れ、ウィンドウは非表示になる
        }

        private void OnSettingsApplied(object sender, EventArgs e)
        {
            Settings.Save(SettingsPath);
            AbortIncident();
            Replan();
        }
    }
}
