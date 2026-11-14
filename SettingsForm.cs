using System;
using System.Drawing;
using System.Windows.Forms;

namespace BveDelayEx
{
    /// <summary>設定ウィンドウ。デザイナーは使わず、コードで組み立てている。</summary>
    internal sealed class SettingsForm : Form
    {
        private readonly RadioButton CountRadio = new RadioButton();
        private readonly RadioButton ProbabilityRadio = new RadioButton();
        private readonly NumericUpDown CountValue = new NumericUpDown();
        private readonly NumericUpDown ProbabilityValue = new NumericUpDown();
        private readonly NumericUpDown ScaleValue = new NumericUpDown();
        private readonly CheckedListBox IncidentList = new CheckedListBox();
        private readonly CheckBox ShowPlanCheck = new CheckBox();
        private readonly TextBox PlanText = new TextBox();
        private readonly Button ApplyButton = new Button();
        private readonly Button CloseButton = new Button();

        /// <summary>「適用」が押されたときに発生する。設定値はすでに DelaySettings へ反映済み。</summary>
        public event EventHandler Applied;

        private readonly DelaySettings Settings;

        public SettingsForm(DelaySettings settings)
        {
            Settings = settings;

            Text = "遅延・事故プラグインの設定";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 610);

            int y = 12;

            // --- 発生のさせ方 ---
            Controls.Add(new Label { Text = "発生のさせ方", Left = 12, Top = y, AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
            y += 24;

            CountRadio.Text = "個数指定：1回の走行で";
            CountRadio.Left = 24; CountRadio.Top = y; CountRadio.AutoSize = true;
            Controls.Add(CountRadio);
            CountValue.Left = 200; CountValue.Top = y - 2; CountValue.Width = 60; CountValue.Minimum = 1; CountValue.Maximum = 50;
            Controls.Add(CountValue);
            Controls.Add(new Label { Text = "駅で発生（ランダムな駅）", Left = 268, Top = y + 2, AutoSize = true });
            y += 30;

            ProbabilityRadio.Text = "確率指定：各駅で";
            ProbabilityRadio.Left = 24; ProbabilityRadio.Top = y; ProbabilityRadio.AutoSize = true;
            Controls.Add(ProbabilityRadio);
            ProbabilityValue.Left = 200; ProbabilityValue.Top = y - 2; ProbabilityValue.Width = 60; ProbabilityValue.Minimum = 1; ProbabilityValue.Maximum = 100;
            Controls.Add(ProbabilityValue);
            Controls.Add(new Label { Text = "% の確率で発生", Left = 268, Top = y + 2, AutoSize = true });
            y += 40;

            // --- 事故の種類 ---
            Controls.Add(new Label { Text = "発生させる事故の種類", Left = 12, Top = y, AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
            y += 24;

            IncidentList.Left = 24; IncidentList.Top = y; IncidentList.Width = 390; IncidentList.Height = 168;
            IncidentList.CheckOnClick = true;
            foreach (IncidentDef incident in Incidents.All)
            {
                string kind = incident.InStation ? "駅構内" : "駅間停車";
                IncidentList.Items.Add(incident.Name + "　（" + kind + "）");
            }
            Controls.Add(IncidentList);
            y += 180;

            // --- 停車時間の倍率 ---
            Controls.Add(new Label { Text = "停車時間の倍率", Left = 12, Top = y + 3, AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
            ScaleValue.Left = 140; ScaleValue.Top = y; ScaleValue.Width = 70; ScaleValue.Minimum = 10; ScaleValue.Maximum = 500; ScaleValue.Increment = 10;
            Controls.Add(ScaleValue);
            Controls.Add(new Label { Text = "%（100%で通常、動作確認のときは小さく）", Left = 218, Top = y + 3, AutoSize = true });
            y += 40;

            // --- デバッグ表示 ---
            ShowPlanCheck.Text = "今回の発生予定を表示する（動作確認用。ネタバレになります）";
            ShowPlanCheck.Left = 12; ShowPlanCheck.Top = y; ShowPlanCheck.AutoSize = true;
            ShowPlanCheck.CheckedChanged += (s, e) => PlanText.Visible = ShowPlanCheck.Checked;
            Controls.Add(ShowPlanCheck);
            y += 26;

            PlanText.Left = 24; PlanText.Top = y; PlanText.Width = 390; PlanText.Height = 84;
            PlanText.Multiline = true; PlanText.ReadOnly = true; PlanText.ScrollBars = ScrollBars.Vertical;
            Controls.Add(PlanText);
            y += 96;

            // --- ボタン ---
            ApplyButton.Text = "適用して予定を作り直す";
            ApplyButton.Left = 130; ApplyButton.Top = y; ApplyButton.Width = 190; ApplyButton.Height = 30;
            ApplyButton.Click += OnApplyClicked;
            Controls.Add(ApplyButton);

            CloseButton.Text = "閉じる";
            CloseButton.Left = 330; CloseButton.Top = y; CloseButton.Width = 84; CloseButton.Height = 30;
            CloseButton.Click += (s, e) => Close();
            Controls.Add(CloseButton);

            LoadFromSettings();
        }

        /// <summary>画面の内容を DelaySettings に書き戻す。</summary>
        public void SaveToSettings()
        {
            Settings.Mode = ProbabilityRadio.Checked ? PlanMode.Probability : PlanMode.Count;
            Settings.Count = (int)CountValue.Value;
            Settings.ProbabilityPercent = (int)ProbabilityValue.Value;
            Settings.HoldScalePercent = (int)ScaleValue.Value;
            Settings.ShowPlan = ShowPlanCheck.Checked;
            for (int i = 0; i < Settings.IncidentEnabled.Length; i++)
            {
                Settings.IncidentEnabled[i] = IncidentList.GetItemChecked(i);
            }
        }

        /// <summary>発生予定の表示を更新する。</summary>
        public void SetPlanText(string text)
        {
            PlanText.Text = text;
        }

        private void LoadFromSettings()
        {
            CountRadio.Checked = Settings.Mode == PlanMode.Count;
            ProbabilityRadio.Checked = Settings.Mode == PlanMode.Probability;
            CountValue.Value = Clamp(Settings.Count, CountValue);
            ProbabilityValue.Value = Clamp(Settings.ProbabilityPercent, ProbabilityValue);
            ScaleValue.Value = Clamp(Settings.HoldScalePercent, ScaleValue);
            ShowPlanCheck.Checked = Settings.ShowPlan;
            PlanText.Visible = Settings.ShowPlan;
            for (int i = 0; i < Settings.IncidentEnabled.Length; i++)
            {
                IncidentList.SetItemChecked(i, Settings.IncidentEnabled[i]);
            }
        }

        private static decimal Clamp(int value, NumericUpDown control)
        {
            return Math.Max(control.Minimum, Math.Min(control.Maximum, value));
        }

        private void OnApplyClicked(object sender, EventArgs e)
        {
            SaveToSettings();
            Applied?.Invoke(this, EventArgs.Empty);
        }
    }
}
