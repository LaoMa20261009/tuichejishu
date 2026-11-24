using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Windows.Forms;

namespace CrabWeighing
{
    sealed class MainForm : Form
    {
        static readonly Color Ink = Color.FromArgb(22, 40, 66);
        static readonly Color Blue = Color.FromArgb(30, 91, 183);
        static readonly Color PaleBlue = Color.FromArgb(232, 241, 255);
        static readonly Color Muted = Color.FromArgb(74, 94, 121);
        static readonly Color[] RopeInk = { Color.FromArgb(172, 40, 56), Color.FromArgb(25, 113, 79), Color.FromArgb(32, 81, 172), Color.FromArgb(78, 90, 107) };
        static readonly Color[] RopeFill = { Color.FromArgb(254, 234, 237), Color.FromArgb(227, 247, 238), PaleBlue, Color.FromArgb(233, 238, 246) };
        static readonly int[] DemoCounts = { 0, 1, 2, 4, 3 };

        readonly VideoSurface preview = new VideoSurface();
        readonly ComboBox cameraType = Choice("网络视频", "USB 摄像头", "本地视频", "演示画面");
        readonly TextBox address = new TextBox { Width = 290 };
        readonly TextBox ffmpeg = new TextBox { Width = 112, Text = "ffmpeg.exe" };
        readonly Button browseVideo = ActionButton("选择视频");
        readonly Button cameraButton = ActionButton("连接视频");
        readonly Label cameraStatus = Note("视频未连接");
        readonly Label referenceStatus = Note("");
        double[] appliedReferences;
        string recentColors = "";
        readonly NumericUpDown[] references = { Amount(200, 1, 10, 10000, 1),
            Amount(250, 1, 10, 10000, 1), Amount(300, 1, 10, 10000, 1), Amount(0, 1, 0, 10000, 1) };
        readonly Label[] countCells = { Note("—"), Note("—"), Note("—"), Note("—") };
        readonly Label[] massCells = { Note("—"), Note("—"), Note("—"), Note("—") };
        readonly Button confirmCounts = ActionButton("核对数量");
        WeightSample currentWeight;
        bool confirmingCounts;
        string basketState = "";
        readonly ComboBox ports = new ComboBox { Width = 142, DropDownStyle = ComboBoxStyle.DropDown };
        readonly ComboBox protocol = Choice("主动上报", "被动查询", "数字 OP 盒");
        readonly CheckBox demoScale = new CheckBox { Text = "模拟秤", AutoSize = true, Margin = new Padding(8, 7, 4, 3) };
        readonly Button scaleButton = ActionButton("连接秤");
        readonly NumericUpDown crabCount = Amount(0, 0, 0, 6, 1);
        readonly CheckBox cycleLoads = new CheckBox { Text = "自动放蟹", AutoSize = true,
            Margin = new Padding(10, 7, 4, 3) };
        FlowLayoutPanel simulationControls;
        FlowLayoutPanel decoderControls;
        readonly SimulatedScale simulatedScale = new SimulatedScale();
        readonly Label weightValue = Note("—");
        readonly Label scaleStatus = Note("秤未连接");
        readonly Label captureStatus = Note("等待视频与称重数据");
        readonly CheckBox automatic = new CheckBox { Text = "自动记录", Checked = true, AutoSize = true, Margin = new Padding(8, 7, 4, 3) };
        readonly NumericUpDown emptyLimit = Amount(5, 1, 0, 100000, 1);
        readonly NumericUpDown tolerance = Amount(1, 1, 0, 100000, 0.1M);
        readonly ColorGallery gallery = new ColorGallery(RopeInk, RopeFill);
        readonly Timer timer = new Timer { Interval = 100 };
        readonly ToolTip hints = new ToolTip();
        readonly Os2Scale scale = new Os2Scale();
        readonly History history = new History(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrabWeighing", "measurements-grams.tsv"));
        readonly CaptureGate liveGate = new CaptureGate();
        readonly CaptureGate demoGate = new CaptureGate();
        CameraSession camera;
        VideoFrame displayed;
        VideoFrame analyzed;
        Rectangle area = new Rectangle(0, 0, VideoFrame.Width, VideoFrame.Height);
        Point? selectionStart;
        Rectangle selecting;
        CrabObservation latestObservation;
        CrabObservation stableObservation;
        int matchingFrames;
        bool simulationOpen;
        volatile int simulationCount;
        int demoStep;
        long nextDemoStep;
        bool changingDemoCount;
        long cameraStarted;
        long lastPoll;
        long lastAnalysis;
        long lastWriteAttempt;
        bool polling;
        bool refreshing;
        bool closing;
        bool writeFailed;

        public MainForm()
        {
            Text = "螃蟹称重与绳色识别";
            AutoSize = false;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            ClientSize = new Size(1232, 716);
            MinimumSize = new Size(1050, 680);
            WindowState = FormWindowState.Maximized;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 17, FontStyle.Regular, GraphicsUnit.Pixel);
            BackColor = Color.FromArgb(236, 242, 249);
            ForeColor = Ink;
            cameraType.SelectedIndex = (int)CameraSource.File;
            cameraType.Width = 112;
            address.Width = 210;
            cameraButton.AutoSize = false;
            cameraButton.Width = 102;
            hints.SetToolTip(references[3], "白绳可计数。与其他绳色混装时，填写白绳单只参考重量才能分配净重。");
            cameraButton.BackColor = scaleButton.BackColor = Blue;
            cameraButton.ForeColor = scaleButton.ForeColor = Color.White;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 10), RowCount = 2, ColumnCount = 2 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
            Controls.Add(root);
            var banner = new Panel { Dock = DockStyle.Fill, BackColor = Ink, Margin = new Padding(6, 0, 6, 6) };
            var title = Heading("螃蟹称重与绳色识别");
            title.Font = new Font(Font.FontFamily, 24, FontStyle.Bold, GraphicsUnit.Pixel);
            title.Location = new Point(16, 8);
            title.ForeColor = Color.White;
            banner.Controls.Add(title);
            root.Controls.Add(banner, 0, 0);
            root.SetColumnSpan(banner, 2);

            var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
            left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 68));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
            left.Controls.Add(CreateVideoPanel(), 0, 0);
            left.Controls.Add(CreateRecordsPanel(), 0, 1);
            root.Controls.Add(left, 0, 1);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
            right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 41));
            right.Controls.Add(CreateScalePanel(), 0, 0);
            right.Controls.Add(CreateReferencePanel(), 0, 1);
            right.Controls.Add(CreateBasketPanel(), 0, 2);
            root.Controls.Add(right, 1, 1);

            cameraButton.Click += (s, e) => ToggleCamera();
            browseVideo.Click += (s, e) => SelectVideoFile();
            scaleButton.Click += (s, e) => ToggleScale();
            cameraType.SelectedIndexChanged += (s, e) =>
            {
                address.Clear();
                UpdateCameraInputs();
            };
            ports.DropDown += (s, e) => RefreshPorts();
            demoScale.CheckedChanged += (s, e) => UpdateScaleInputs();
            crabCount.ValueChanged += (s, e) =>
            {
                if (!changingDemoCount) cycleLoads.Checked = false;
                simulationCount = (int)crabCount.Value;
                simulatedScale.SetCount(simulationCount, appliedReferences);
            };
            cycleLoads.CheckedChanged += (s, e) =>
            {
                if (!cycleLoads.Checked) return;
                demoStep = 0;
                SetDemoCount(0);
                nextDemoStep = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
            };
            LoadReferences();
            appliedReferences = ReadReferenceInputs();
            foreach (NumericUpDown reference in references) reference.ValueChanged += (s, e) =>
            {
                referenceStatus.Text = ReadReferenceInputs().SequenceEqual(appliedReferences) ? "" : "未保存";
            };
            confirmCounts.Click += (s, e) => ConfirmBasket();
            emptyLimit.ValueChanged += (s, e) => ResetCandidates();
            tolerance.ValueChanged += (s, e) => ResetCandidates();
            automatic.CheckedChanged += (s, e) => ResetCandidates();
            preview.Paint += DrawArea;
            preview.MouseDown += BeginSelection;
            preview.MouseMove += MoveSelection;
            preview.MouseUp += EndSelection;
            timer.Tick += (s, e) => RefreshDashboard();
            RefreshPorts();
            UpdateCameraInputs();
            UpdateScaleInputs();
            try { gallery.LoadHistory(history.Load()); }
            catch (Exception ex) { captureStatus.Text = "读取历史失败。 " + ex.Message; }
            timer.Start();
        }

        Control CreateVideoPanel()
        {
            var panel = Card(4);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var reset = ActionButton("全画面");
            reset.Click += (s, e) =>
            {
                area = new Rectangle(0, 0, VideoFrame.Width, VideoFrame.Height);
                InvalidateAnalysis();
                preview.Invalidate();
            };
            panel.Controls.Add(Line(Heading("实时视频"), cameraStatus), 0, 0);
            panel.Controls.Add(Line(cameraType, address, browseVideo, cameraButton, reset), 0, 1);
            decoderControls = Line(Note("相机解码器"), ffmpeg);
            panel.Controls.Add(decoderControls, 0, 2);
            hints.SetToolTip(ffmpeg, "可填写 ffmpeg.exe 的完整路径，或将它加入 PATH。");
            preview.AutoSize = false;
            preview.Dock = DockStyle.Fill;
            preview.Margin = new Padding(0, 6, 0, 6);
            preview.BackColor = Color.FromArgb(12, 24, 41);
            preview.Cursor = Cursors.Cross;
            panel.Controls.Add(preview, 0, 3);

            hints.SetToolTip(preview, "拖动框选需要识别的筐内区域。");
            return panel;
        }

        Control CreateScalePanel()
        {
            var panel = Card(6);
            for (int i = 0; i < 5; i++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(Heading("称重"), 0, 0);
            ports.Width = 88;
            protocol.Width = 94;
            panel.Controls.Add(Line(Note("连接"), ports, protocol, demoScale, scaleButton), 0, 1);
            simulationControls = Line(Note("模拟只数"), crabCount, cycleLoads);
            panel.Controls.Add(simulationControls, 0, 2);
            weightValue.Font = new Font("Segoe UI", 43, FontStyle.Bold, GraphicsUnit.Pixel);
            weightValue.ForeColor = Blue;
            weightValue.Margin = new Padding(9, 3, 9, 3);
            StyleStatus(scaleStatus);
            var live = Line(Note("实时净重"), weightValue, scaleStatus);
            live.BackColor = Color.FromArgb(244, 247, 252);
            panel.Controls.Add(live, 0, 3);
            emptyLimit.Width = tolerance.Width = 58;
            panel.Controls.Add(Line(Note("空秤"), emptyLimit, Note("容差"), tolerance, Note("克"), automatic), 0, 4);
            hints.SetToolTip(emptyLimit, "空秤上限，单位为克。");
            hints.SetToolTip(tolerance, "稳定容差，单位为克。");
            return panel;
        }

        Control CreateReferencePanel()
        {
            var panel = Card(2);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var save = ActionButton("保存");
            save.BackColor = Blue;
            save.ForeColor = Color.White;
            save.Click += (s, e) => ApplyReferenceChanges();
            panel.Controls.Add(Line(Heading("参考重量（克）"), save, referenceStatus), 0, 0);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 2 };
            for (int i = 0; i < 2; i++)
            {
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            }
            for (int i = 0; i < references.Length; i++)
            {
                references[i].Width = 88;
                var cell = Line(ColorLabel(i), references[i]);
                cell.Margin = new Padding(0, 2, 6, 2);
                cell.BackColor = RopeFill[i];
                grid.Controls.Add(cell, i % 2, i / 2);
            }
            panel.Controls.Add(grid, 0, 1);
            return panel;
        }

        Control CreateBasketPanel()
        {
            var panel = Card(3);
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(Line(Heading("筐内螃蟹"), confirmCounts), 0, 0);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 3, Margin = new Padding(0, 4, 0, 4) };
            for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
            for (int i = 0; i < 5; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 20));
            grid.BackColor = Color.FromArgb(244, 247, 252);
            grid.Controls.Add(Note("绳色"), 0, 0);
            grid.Controls.Add(Note("数量 / 只"), 1, 0);
            grid.Controls.Add(Note("净重 / 克"), 2, 0);
            for (int i = 0; i < countCells.Length; i++)
            {
                grid.Controls.Add(ColorLabel(i), 0, i + 1);
                grid.Controls.Add(countCells[i], 1, i + 1);
                grid.Controls.Add(massCells[i], 2, i + 1);
            }
            panel.Controls.Add(grid, 0, 1);
            panel.Controls.Add(captureStatus, 0, 2);
            return panel;
        }

        static Label ColorLabel(int index)
        {
            var label = Note(Basket.Colors[index]);
            label.ForeColor = RopeInk[index];
            label.BackColor = RopeFill[index];
            label.Padding = new Padding(5, 0, 5, 0);
            return label;
        }

        Control CreateRecordsPanel()
        {
            var panel = Card(2);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(Heading("识别绳色"), 0, 0);
            gallery.Dock = DockStyle.Fill;
            gallery.ForeColor = Muted;
            gallery.Margin = Padding.Empty;
            panel.Controls.Add(gallery, 0, 1);
            return panel;
        }

        void SelectVideoFile()
        {
            using (var dialog = new OpenFileDialog
            {
                Title = "选择本地视频",
                Filter = "视频文件|*.mp4;*.mov;*.avi;*.mkv;*.wmv;*.m4v;*.mpeg;*.mpg|所有文件|*.*",
                CheckFileExists = true,
                RestoreDirectory = true
            })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) address.Text = dialog.FileName;
            }
        }

        void ToggleCamera()
        {
            if (camera != null)
            {
                bool replay = camera.Ended;
                CloseCamera("视频已断开");
                if (!replay) return;
            }
            try
            {
                var source = (CameraSource)cameraType.SelectedIndex;
                if (source == CameraSource.File && address.Text.Length == 0)
                    throw new ArgumentException("请先点击「选择视频」。");
                string executable = ffmpeg.Text.Trim();
                if (source == CameraSource.Network || source == CameraSource.Usb)
                {
                    if (executable.Length == 0) executable = "ffmpeg.exe";
                    string bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, executable);
                    if (File.Exists(bundled)) executable = bundled;
                }
                camera = new CameraSession(source, address.Text.Trim(), executable, () => simulationCount);
                if (source == CameraSource.File)
                {
                    liveGate.StartBatch();
                    demoGate.StartBatch();
                }
                cameraStarted = Stopwatch.GetTimestamp();
                cameraButton.Text = "断开视频";
                cameraStatus.Text = "等待首帧";
                UpdateCameraInputs();
                InvalidateAnalysis();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(this, "网络或 USB 相机需要 FFmpeg 解码。请将 ffmpeg.exe 放在程序目录，或在相机解码器栏填写完整路径。", "找不到相机解码器");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "连接视频失败"); }
        }

        void CloseCamera(string status)
        {
            if (camera != null) camera.Dispose();
            camera = null;
            displayed = null;
            ClearBasket();
            preview.SetFrame(null);
            InvalidateAnalysis();
            cameraStatus.Text = status;
            cameraButton.Text = "连接视频";
            UpdateCameraInputs();
        }

        void UpdateCameraInputs()
        {
            CameraSource source = (CameraSource)cameraType.SelectedIndex;
            cameraType.Enabled = camera == null;
            address.Enabled = camera == null && source != CameraSource.Demo;
            browseVideo.Visible = source == CameraSource.File;
            browseVideo.Enabled = camera == null;
            // Mask stream addresses in the UI because RTSP URLs may contain credentials.
            address.UseSystemPasswordChar = source == CameraSource.Network;
            decoderControls.Visible = source == CameraSource.Network || source == CameraSource.Usb;
            ((TableLayoutPanel)decoderControls.Parent).RowStyles[2].Height = decoderControls.Visible ? 39 : 0;
            ffmpeg.Enabled = camera == null;
            hints.SetToolTip(address, source == CameraSource.Usb ? "DirectShow 设备名称" :
                source == CameraSource.File ? "本地视频的完整路径" : "RTSP 或 HTTP 视频流地址");
        }

        void ToggleScale()
        {
            if (polling) return;
            polling = true;
            try
            {
                if (scale.IsOpen || simulationOpen)
                {
                    scale.Close();
                    simulationOpen = false;
                    cycleLoads.Checked = false;
                    SetDemoCount(0);
                }
                else if (demoScale.Checked)
                {
                    simulationOpen = true;
                    demoGate.StartBatch();
                    cycleLoads.Checked = true;
                }
                else
                {
                    string port = ports.Text.Trim();
                    if (port.Length == 0) throw new ArgumentException("请选择 COM 口或 USB 自动发现。");
                    scale.Open(port == "USB 自动发现" ? "" : port, (ScaleProtocol)protocol.SelectedIndex);
                }
                liveGate.StartBatch();
                demoGate.StartBatch();
                ClearBasket();
            }
            catch (DllNotFoundException)
            {
                MessageBox.Show(this, "请将卖家 OS2 SDK V1.017 的 SensorDll.dll 放在程序目录。", "缺少称重 DLL");
            }
            catch (BadImageFormatException)
            {
                MessageBox.Show(this, "请使用 x86 构建配置和 32 位 SensorDll.dll。", "称重 DLL 架构不匹配");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "称重连接"); }
            finally
            {
                polling = false;
                UpdateScaleInputs();
            }
        }

        void RefreshPorts()
        {
            string previous = ports.Text;
            ports.Items.Clear();
            try { ports.Items.AddRange(SerialPort.GetPortNames().OrderBy(name => name).Cast<object>().ToArray()); }
            catch (Exception) { }
            ports.Items.Add("USB 自动发现");
            if (previous.Length > 0) ports.Text = previous;
            else if (ports.Items.Count > 1) ports.SelectedIndex = 0;
        }

        void UpdateScaleInputs()
        {
            bool connected = scale.IsOpen || simulationOpen;
            demoScale.Enabled = !connected;
            ports.Enabled = protocol.Enabled = !connected && !demoScale.Checked;
            simulationControls.Visible = simulationOpen;
            scaleButton.Text = connected ? "断开秤" : "连接秤";
        }

        void RefreshDashboard()
        {
            if (refreshing || polling || closing) return;
            refreshing = true;
            try
            {
                long now = Stopwatch.GetTimestamp();
                WeightSample weight = ReadWeight(now);
                currentWeight = weight;
                now = Stopwatch.GetTimestamp();
                long frameTick = RefreshVideo(now);
                RefreshBasket(weight);
                RefreshRecording(now, weight, frameTick);
            }
            finally { refreshing = false; }
        }

        WeightSample ReadWeight(long now)
        {
            if (scale.IsOpen && CaptureGate.Elapsed(now, lastPoll) >= 0.45)
            {
                polling = true;
                try { scale.Poll(); }
                catch (Exception)
                {
                    scaleStatus.Text = "查询失败，请检查通信方式和当前协议";
                    weightValue.Text = "—";
                    return null;
                }
                finally
                {
                    lastPoll = Stopwatch.GetTimestamp();
                    polling = false;
                }
            }

            if (simulationOpen && cycleLoads.Checked && now >= nextDemoStep)
            {
                demoStep = (demoStep + 1) % DemoCounts.Length;
                SetDemoCount(DemoCounts[demoStep]);
                nextDemoStep = now + 5 * Stopwatch.Frequency;
            }
            WeightSample sample = simulationOpen ? simulatedScale.Read(now)
                : scale.IsOpen ? scale.Latest : null;
            if (sample != null && CaptureGate.Elapsed(now, sample.Tick) > 2.5) sample = null;
            double grams;
            if (sample != null && sample.Valid)
            {
                if (!Basket.TryGrams(sample.Value, sample.Unit, out grams))
                {
                    weightValue.Text = "—";
                    scaleStatus.Text = "请将秤切换到克或千克";
                    return null;
                }
                sample = new WeightSample(grams, "g", sample.Stable, true, 1, sample.Tick);
            }
            weightValue.Text = sample == null || !sample.Valid ? "—" : sample.Number + " 克";
            scaleStatus.Text = sample == null ? (scale.IsOpen ? "等待有效称重数据" : "秤未连接") :
                !sample.Valid ? "称重数据无效" : sample.Stable ? "净重稳定" : "净重变化中";
            return sample;
        }

        void SetDemoCount(int count)
        {
            changingDemoCount = true;
            try { crabCount.Value = count; }
            finally { changingDemoCount = false; }
        }

        long RefreshVideo(long now)
        {
            if (camera == null) return 0;
            if (camera.Error != null)
            {
                CloseCamera(camera.Error);
                return 0;
            }
            VideoFrame frame = camera.Latest;
            if (camera.Ended)
            {
                cameraStatus.Text = "";
                cameraButton.Text = "重播视频";
                return 0;
            }
            if (frame == null || CaptureGate.Elapsed(now, frame.Tick) > 2.5)
            {
                if (displayed != null)
                {
                    displayed = null;
                    preview.SetFrame(null);
                    InvalidateAnalysis();
                }
                cameraStatus.Text = CaptureGate.Elapsed(now, cameraStarted) > 8 ? "未收到新画面，请检查视频源" : "等待首帧";
                return 0;
            }
            if (displayed != frame)
            {
                displayed = frame;
                preview.SetFrame(frame.ToBitmap());
            }
            cameraStatus.Text = camera.Source == CameraSource.Demo ? "演示画面" :
                camera.Source == CameraSource.File ? "本地视频  " + FormatPosition(frame.Position) : "视频已连接";
            if (analyzed != frame && (analyzed == null || CaptureGate.Elapsed(now, lastAnalysis) >= 0.28))
            {
                analyzed = frame;
                lastAnalysis = now;
                var observation = CrabVision.Read(frame, area, 0.12);
                matchingFrames = latestObservation != null && latestObservation.Signature == observation.Signature
                    ? matchingFrames + 1 : 1;
                latestObservation = observation;
                if (matchingFrames >= 2) stableObservation = observation;
                if (matchingFrames >= 2)
                {
                    string[] seen = Basket.Colors.Where((color, index) => stableObservation.Counts[index] > 0).ToArray();
                    string key = string.Join("、", seen);
                    if (key != recentColors)
                    {
                        recentColors = key;
                        if (seen.Length > 0) gallery.AddColors(seen, DateTimeOffset.Now);
                    }
                }
                preview.Invalidate();
            }
            return analyzed == null || matchingFrames < 2 ? 0 : analyzed.Tick;
        }

        double[] ReadReferenceInputs()
        {
            return references.Select(input => (double)input.Value).ToArray();
        }

        string ReferenceFile { get { return Path.Combine(Path.GetDirectoryName(history.FilePath), "rope-weights.txt"); } }

        void LoadReferences()
        {
            try
            {
                if (!File.Exists(ReferenceFile)) return;
                string[] lines = File.ReadAllLines(ReferenceFile);
                for (int i = 0; i < Math.Min(references.Length, lines.Length); i++)
                {
                    decimal value;
                    if (decimal.TryParse(lines[i], NumberStyles.Number, CultureInfo.InvariantCulture, out value) &&
                        value >= references[i].Minimum && value <= references[i].Maximum)
                        references[i].Value = value;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        void ApplyReferenceChanges()
        {
            double[] values = ReadReferenceInputs();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReferenceFile));
                File.WriteAllLines(ReferenceFile, values.Select(value => value.ToString(CultureInfo.InvariantCulture)));
            }
            catch (IOException)
            {
                referenceStatus.Text = "保存失败";
                return;
            }
            catch (UnauthorizedAccessException)
            {
                referenceStatus.Text = "保存失败";
                return;
            }

            bool changed = !values.SequenceEqual(appliedReferences);
            appliedReferences = values;
            referenceStatus.Text = "已保存";
            if (!changed) return;
            if (simulationOpen) simulatedScale.SetCount(simulationCount, appliedReferences);
            liveGate.StartBatch();
            demoGate.StartBatch();
            ClearBasket();
        }

        void ClearBasket()
        {
            basketState = "";
            for (int i = 0; i < countCells.Length; i++)
            {
                countCells[i].Text = "—";
                massCells[i].Text = "—";
            }
        }

        bool CanAllocate(int[] counts)
        {
            return counts.Count(count => count > 0) <= 1 ||
                !appliedReferences.Where((reference, index) => counts[index] > 0).Any(reference => reference <= 0);
        }

        void RefreshBasket(WeightSample weight)
        {
            int[] counts = stableObservation == null ? null : stableObservation.Counts;
            bool validWeight = weight != null && weight.Valid && weight.Stable && weight.Value > (double)emptyLimit.Value;
            double[] grams = counts != null && counts.Sum() > 0 && validWeight && CanAllocate(counts)
                ? Basket.Allocate(weight.Value, counts, appliedReferences) : null;
            for (int i = 0; i < countCells.Length; i++)
            {
                countCells[i].Text = counts == null ? "—" : counts[i].ToString(CultureInfo.InvariantCulture);
                massCells[i].Text = grams == null ? "—" : grams[i].ToString("F1", CultureInfo.InvariantCulture);
            }
        }

        void RefreshRecording(long now, WeightSample weight, long frameTick)
        {
            bool stable = weight != null && weight.Valid && weight.Stable;
            double empty = (double)emptyLimit.Value;
            confirmCounts.Enabled = stable && weight.Value > empty;
            if (confirmingCounts) return;
            bool hasFrame = camera != null && frameTick != 0 && CaptureGate.Elapsed(now, frameTick) <= 0.65;
            bool local = camera != null && camera.Source == CameraSource.File;
            bool sourceMatches = hasFrame && (local || simulationOpen == (camera.Source == CameraSource.Demo));
            double stability = (double)tolerance.Value;
            CaptureGate gate = simulationOpen ? demoGate : liveGate;
            int[] counts = stableObservation == null ? null : stableObservation.Counts;
            bool detected = counts != null && counts.Sum() > 0;
            string signature = sourceMatches && detected ? stableObservation.Signature : "";
            bool allocatable = detected && CanAllocate(counts);
            if (gate.Observe(now, weight, empty, stability, allocatable ? signature : "", frameTick,
                automatic.Checked, true, true) && CaptureGate.Elapsed(now, lastWriteAttempt) >= 1)
            {
                lastWriteAttempt = now;
                SaveBasket(gate, weight, signature, counts, false);
            }

            if (!automatic.Checked) captureStatus.Text = "自动记录已暂停";
            else if (!hasFrame) captureStatus.Text = camera != null && camera.Ended ? "" : "等待稳定画面";
            else if (!detected) captureStatus.Text = "等待识别到系绳目标";
            else if (!stable || weight.Value <= empty) captureStatus.Text = "数量已更新 · 等待稳定净重";
            else if (!sourceMatches) captureStatus.Text = "演示画面请搭配模拟秤";
            else if (!allocatable) captureStatus.Text = "混装记录需填写白绳参考重量";
            else if (writeFailed) captureStatus.Text = "历史写入失败";
            else if (gate.IsHolding(weight, stability, true, signature, true)) captureStatus.Text = basketState;
            else captureStatus.Text = "准备保存识别记录";
        }

        void SaveBasket(CaptureGate gate, WeightSample weight, string detected, int[] counts, bool manual)
        {
            double[] grams = Basket.Allocate(weight.Value, counts, appliedReferences);
            bool mixed = counts.Count(count => count > 0) > 1;
            string time = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture);
            string source = (camera != null && camera.Source == CameraSource.File ? "本地视频 · " : "") +
                (simulationOpen ? "模拟秤" : "实秤") + (manual ? " · 人工核对" : " · 视频识别");
            if (mixed) source += " · 重量估算";
            var rows = new System.Collections.Generic.List<MeasurementRecord>();
            for (int i = 0; i < counts.Length; i++)
                if (counts[i] > 0)
                    rows.Add(new MeasurementRecord(time, Basket.Colors[i], grams[i], counts[i], source));
            try { history.Append(rows); }
            catch (IOException) { writeFailed = true; return; }
            catch (UnauthorizedAccessException) { writeFailed = true; return; }
            writeFailed = false;
            gate.MarkRecorded(weight, detected);
            basketState = mixed ? "已记录 · 分色重量为估算" : "已记录";
            captureStatus.Text = basketState;
            if (manual) gallery.AddColors(rows.Select(row => row.Color), DateTimeOffset.Now);
        }

        void ConfirmBasket()
        {
            var initial = currentWeight;
            if (initial == null || !initial.Valid || !initial.Stable || initial.Value <= (double)emptyLimit.Value) return;
            var inputs = Basket.Colors.Select((color, index) => Amount(stableObservation == null ? 0 :
                Math.Min(Basket.MaximumCount, stableObservation.Counts[index]), 0, 0, Basket.MaximumCount, 1)).ToArray();
            confirmingCounts = true;
            try
            {
                using (var form = new Form { Text = "核对筐内数量", StartPosition = FormStartPosition.CenterParent,
                    FormBorderStyle = FormBorderStyle.FixedDialog, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    MaximizeBox = false, MinimizeBox = false, Font = Font })
                {
                    var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false,
                        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(12) };
                    for (int i = 0; i < inputs.Length; i++) layout.Controls.Add(Line(ColorLabel(i), inputs[i], Note("只")));
                    var save = ActionButton("保存记录");
                    var cancel = ActionButton("取消");
                    cancel.DialogResult = DialogResult.Cancel;
                    layout.Controls.Add(Line(save, cancel));
                    form.AcceptButton = save;
                    form.CancelButton = cancel;
                    form.Controls.Add(layout);
                    save.Click += (s, e) =>
                    {
                        int[] counts = inputs.Select(input => (int)input.Value).ToArray();
                        if (counts.Sum() < 1 || counts.Sum() > Basket.MaximumCount)
                        {
                            MessageBox.Show(form, "请输入 1 到 60 只螃蟹的分色数量。");
                            return;
                        }
                        var sample = currentWeight;
                        if (sample == null || !sample.Stable ||
                            Math.Abs(sample.Value - initial.Value) > (double)tolerance.Value)
                        {
                            MessageBox.Show(form, "核对期间净重已变化。请等秤稳定后重新核对。");
                            form.Close();
                            return;
                        }
                        if (!CanAllocate(counts))
                        {
                            MessageBox.Show(form, "混装记录需填写白绳参考重量。");
                            return;
                        }
                        SaveBasket(simulationOpen ? demoGate : liveGate, sample,
                            stableObservation == null ? "" : stableObservation.Signature, counts, true);
                        if (writeFailed) MessageBox.Show(form, "写入历史失败，请检查记录目录权限。");
                        else form.DialogResult = DialogResult.OK;
                    };
                    form.ShowDialog(this);
                }
            }
            finally { confirmingCounts = false; }
        }

        static string FormatPosition(TimeSpan? position)
        {
            if (!position.HasValue) return "";
            TimeSpan value = position.Value;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}.{3:0}",
                (int)value.TotalHours, value.Minutes, value.Seconds, value.Milliseconds / 100);
        }

        Point ToFrame(Point point)
        {
            Rectangle bounds = preview.FrameBounds;
            return new Point(Math.Max(0, Math.Min(VideoFrame.Width, (point.X - bounds.X) * VideoFrame.Width / bounds.Width)),
                Math.Max(0, Math.Min(VideoFrame.Height, (point.Y - bounds.Y) * VideoFrame.Height / bounds.Height)));
        }

        void BeginSelection(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || displayed == null || !preview.FrameBounds.Contains(e.Location)) return;
            selectionStart = ToFrame(e.Location);
            selecting = Rectangle.Empty;
            preview.Capture = true;
        }

        void MoveSelection(object sender, MouseEventArgs e)
        {
            if (!selectionStart.HasValue || preview.FrameBounds.Width == 0 || preview.FrameBounds.Height == 0) return;
            Point start = selectionStart.Value, end = ToFrame(e.Location);
            selecting = Rectangle.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
                Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
            preview.Invalidate();
        }

        void EndSelection(object sender, MouseEventArgs e)
        {
            if (!selectionStart.HasValue) return;
            MoveSelection(sender, e);
            if (selecting.Width >= 24 && selecting.Height >= 24)
            {
                area = selecting;
                InvalidateAnalysis();
            }
            selectionStart = null;
            preview.Capture = false;
            preview.Invalidate();
        }

        void DrawArea(object sender, PaintEventArgs e)
        {
            if (displayed == null) return;
            Rectangle bounds = preview.FrameBounds;
            if (!selectionStart.HasValue && latestObservation != null)
            {
                foreach (CrabTarget target in latestObservation.Targets)
                {
                    using (var pen = new Pen(RopeInk[target.ColorIndex], 2))
                    {
                        Rectangle screen = OnPreview(target.Bounds, bounds);
                        e.Graphics.DrawRectangle(pen, screen);
                        TextRenderer.DrawText(e.Graphics, Basket.Colors[target.ColorIndex], Font,
                            new Point(screen.Left, Math.Max(0, screen.Top - Font.Height)), Color.White, RopeInk[target.ColorIndex]);
                    }
                }
            }
            Rectangle box = selectionStart.HasValue ? selecting : area;
            if (box.IsEmpty || box.Size == new Size(VideoFrame.Width, VideoFrame.Height)) return;
            using (var pen = new Pen(Color.Aquamarine, 2)) e.Graphics.DrawRectangle(pen, OnPreview(box, bounds));
        }

        static Rectangle OnPreview(Rectangle box, Rectangle bounds)
        {
            return new Rectangle(bounds.X + box.X * bounds.Width / VideoFrame.Width,
                bounds.Y + box.Y * bounds.Height / VideoFrame.Height, box.Width * bounds.Width / VideoFrame.Width,
                box.Height * bounds.Height / VideoFrame.Height);
        }

        void ResetCandidates()
        {
            demoGate.ResetCandidate();
            liveGate.ResetCandidate();
        }

        void InvalidateAnalysis()
        {
            analyzed = null;
            latestObservation = null;
            stableObservation = null;
            matchingFrames = 0;
            recentColors = "";
            preview.Invalidate();
            ResetCandidates();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (e.Cancel) return;
            if (polling)
            {
                e.Cancel = true;
                return;
            }
            closing = true;
            timer.Stop();
            CloseCamera("视频已断开");
            try { scale.Close(); }
            catch (Exception) { }
            timer.Dispose();
            hints.Dispose();
        }

        static TableLayoutPanel Card(int rows)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows,
                Padding = new Padding(14, 9, 14, 9), Margin = new Padding(6), BackColor = Color.White };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return panel;
        }

        static FlowLayoutPanel Line(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            row.Controls.AddRange(controls);
            return row;
        }

        static Label Note(string text)
        {
            return new Label { Text = text, ForeColor = Muted, AutoSize = true, Margin = new Padding(3, 5, 4, 3) };
        }

        static Label Heading(string text)
        {
            Label title = Note(text);
            title.Font = new Font("Microsoft YaHei UI", 20, FontStyle.Bold, GraphicsUnit.Pixel);
            title.ForeColor = Ink;
            return title;
        }

        static void StyleStatus(Label label)
        {
            label.BackColor = PaleBlue;
            label.ForeColor = Blue;
            label.Padding = new Padding(8, 3, 8, 3);
        }

        static Button ActionButton(string text)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 34,
                Padding = new Padding(8, 0, 8, 0), BackColor = PaleBlue, ForeColor = Blue,
                FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Cursor = Cursors.Hand, Margin = new Padding(4) };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        static ComboBox Choice(params string[] values)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            box.Items.AddRange(values);
            box.SelectedIndex = 0;
            return box;
        }

        static NumericUpDown Amount(decimal value, int places, decimal min, decimal max, decimal increment)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = value, DecimalPlaces = places,
                Increment = increment, Width = 78, ThousandsSeparator = false };
        }
    }
}
