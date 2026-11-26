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
        static readonly Color Ink = Color.FromArgb(235, 241, 250);
        static readonly Color Blue = Color.FromArgb(36, 101, 232);
        static readonly Color Cyan = Color.FromArgb(34, 211, 238);
        static readonly Color PanelColor = Color.FromArgb(17, 24, 41);
        static readonly Color Surface = Color.FromArgb(11, 17, 28);
        static readonly Color Border = Color.FromArgb(47, 61, 80);
        static readonly Color Muted = Color.FromArgb(154, 170, 190);
        static readonly Color[] RopeInk = { Color.FromArgb(252, 122, 147), Color.FromArgb(67, 219, 168),
            Color.FromArgb(111, 167, 255), Color.FromArgb(232, 239, 250) };
        static readonly Color[] RopeFill = { Color.FromArgb(38, 23, 41), Color.FromArgb(14, 35, 43),
            Color.FromArgb(20, 31, 62), Color.FromArgb(28, 37, 54) };
        static readonly int[] DemoCounts = { 0, 1, 2, 3, 4, 3, 2, 1 };
        static readonly string[] DefaultNames = { "2.0两母", "2.5两母", "3.0两公", "统货标准" };
        static readonly int[] DefaultMin = { 90, 115, 140, 80 };
        static readonly int[] DefaultMax = { 110, 135, 160, 95 };

        readonly VideoSurface preview = new VideoSurface();
        readonly ComboBox cameraType = Choice("网络视频", "USB 摄像头", "本地视频", "演示画面");
        readonly TextBox address = new TextBox { Width = 290 };
        readonly TextBox ffmpeg = new TextBox { Width = 112, Text = "ffmpeg.exe" };
        readonly Button browseVideo = ActionButton("选择视频");
        readonly Button cameraButton = ActionButton("连接视频");
        readonly Label cameraStatus = Note("视频未连接");
        readonly Label referenceStatus = Note("");
        double[] appliedReferences;
        readonly TextBox[] productNames = new TextBox[4];
        readonly NumericUpDown[] minimumWeights = new NumericUpDown[4];
        readonly NumericUpDown[] maximumWeights = new NumericUpDown[4];
        string[] savedNames;
        double[] savedMinimum, savedMaximum;
        readonly Label deviceStatus = Note("");
        readonly Label taskCode = Note("未选择工单");
        readonly Label taskState = Note("");
        readonly Label progressValue = Note("—");
        readonly Label progressCount = Note("");
        readonly TaskMeter[] meters = new TaskMeter[4];
        readonly DataGridView summary = new DataGridView();
        readonly DataGridView ledger = new DataGridView();
        readonly Button tareButton = ActionButton("去皮");
        PackingOrder order;
        WeightSample currentWeight;
        VideoFrame baselineFrame;
        int[] baselineCounts;
        double baselineWeight;
        double tare;
        string candidateKey;
        long candidateSince, candidateFrameTick;
        int candidateFrames;
        double candidateWeight;
        double inferenceMs;
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
        const double EmptyLimit = 5;
        const double WeightTolerance = 1;
        readonly Timer timer = new Timer { Interval = 100 };
        readonly ToolTip hints = new ToolTip();
        readonly Os2Scale scale = new Os2Scale();
        static readonly string DataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrabWeighing");
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
        long candidateFirstWeightTick;
        bool polling;
        bool refreshing;
        bool closing;
        bool choosingOrder;

        public MainForm()
        {
            Text = "螃蟹称重计数";
            AutoSize = false;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            ClientSize = new Size(1232, 716);
            MinimumSize = new Size(1050, 680);
            WindowState = FormWindowState.Maximized;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);
            BackColor = Surface;
            ForeColor = Ink;
            cameraType.SelectedIndex = (int)CameraSource.File;
            cameraType.Width = 94;
            address.BackColor = ffmpeg.BackColor = Surface;
            address.ForeColor = ffmpeg.ForeColor = Ink;
            ports.BackColor = Surface;
            ports.ForeColor = Ink;
            address.Width = 143;
            cameraButton.AutoSize = false;
            cameraButton.Width = 90;
            cameraButton.BackColor = scaleButton.BackColor = Blue;
            cameraButton.ForeColor = scaleButton.ForeColor = Color.White;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6, 6, 6, 6), RowCount = 2, ColumnCount = 2 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53));
            Controls.Add(root);
            var banner = new Panel { Dock = DockStyle.Fill, BackColor = PanelColor, Margin = new Padding(5, 0, 5, 5) };
            var title = Heading("螃蟹称重计数");
            title.Font = new Font(Font.FontFamily, 24, FontStyle.Bold, GraphicsUnit.Pixel);
            title.Location = new Point(16, 8);
            title.ForeColor = Color.White;
            banner.Controls.Add(title);
            deviceStatus.Dock = DockStyle.Right;
            deviceStatus.AutoSize = false;
            deviceStatus.Width = 425;
            deviceStatus.TextAlign = ContentAlignment.MiddleRight;
            deviceStatus.Padding = new Padding(0, 0, 12, 0);
            banner.Controls.Add(deviceStatus);
            root.Controls.Add(banner, 0, 0);
            root.SetColumnSpan(banner, 2);

            var left = Columns(1);
            left.RowCount = 2;
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
            left.Controls.Add(CreateVideoPanel(), 0, 0);
            var leftBottom = Columns(2);
            leftBottom.ColumnStyles.Clear();
            leftBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            leftBottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
            leftBottom.Controls.Add(CreateScalePanel(), 0, 0);
            leftBottom.Controls.Add(CreateReferencePanel(), 1, 0);
            left.Controls.Add(leftBottom, 0, 1);
            root.Controls.Add(left, 0, 1);

            var right = Columns(1);
            right.RowCount = 3;
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 152));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.Controls.Add(CreateOrderPanel(), 0, 0);
            right.Controls.Add(CreateBasketPanel(), 0, 1);
            right.Controls.Add(CreateRecordsPanel(), 0, 2);
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
            demoScale.CheckedChanged += (s, e) =>
            {
                ResetOrder();
                UpdateScaleInputs();
            };
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
                nextDemoStep = Stopwatch.GetTimestamp() + 3 * Stopwatch.Frequency;
            };
            LoadReferences();
            ApplySpecInputs();
            automatic.CheckedChanged += (s, e) => ResetCandidates();
            tareButton.Click += (s, e) =>
            {
                if (currentWeight == null || !currentWeight.Valid || !currentWeight.Stable) return;
                if (order != null && order.Counts.Sum() != 0)
                {
                    MessageBox.Show(this, "当前工单已有装箱数量，请先将箱内螃蟹取出。");
                    return;
                }
                tare += currentWeight.Value;
                baselineFrame = null;
                ResetCandidates();
            };
            preview.Paint += DrawArea;
            preview.MouseDown += BeginSelection;
            preview.MouseMove += MoveSelection;
            preview.MouseUp += EndSelection;
            timer.Tick += (s, e) => RefreshDashboard();
            RefreshPorts();
            UpdateCameraInputs();
            UpdateScaleInputs();
            UpdateOrderDisplay();
            timer.Start();
        }

        Control CreateVideoPanel()
        {
            var panel = Card(3);
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            preview.Dock = DockStyle.Fill;
            preview.Margin = new Padding(0, 0, 0, 7);
            preview.BackColor = Color.FromArgb(8, 12, 19);
            preview.Cursor = Cursors.Cross;
            panel.Controls.Add(preview, 0, 0);
            decoderControls = Line(Note("解码器"), ffmpeg);
            panel.Controls.Add(decoderControls, 0, 1);
            var reset = ActionButton("全画面");
            reset.Click += (s, e) =>
            {
                area = new Rectangle(0, 0, VideoFrame.Width, VideoFrame.Height);
                InvalidateAnalysis();
            };
            panel.Controls.Add(Line(Note("输入源"), cameraType, address, browseVideo, cameraButton, reset), 0, 2);
            hints.SetToolTip(preview, "拖动框选装箱区域。识别仅在选区内运行。");
            return panel;
        }

        Control CreateScalePanel()
        {
            var panel = Card(5);
            Rows(panel, 29, 28, 28, 42, 28);
            scaleButton.Text = "连接";
            panel.Controls.Add(Line(Heading("称重"), demoScale, scaleButton), 0, 0);
            ports.Width = 84;
            protocol.Width = 93;
            panel.Controls.Add(Line(ports, protocol), 0, 1);
            crabCount.Width = 43;
            simulationControls = Line(cycleLoads, crabCount);
            panel.Controls.Add(simulationControls, 0, 2);
            weightValue.Font = new Font("Consolas", 24, FontStyle.Bold, GraphicsUnit.Pixel);
            weightValue.ForeColor = Cyan;
            panel.Controls.Add(Line(Note("实时净重"), weightValue), 0, 3);
            panel.Controls.Add(Line(tareButton, scaleStatus), 0, 4);
            return panel;
        }

        Control CreateReferencePanel()
        {
            var panel = Card(5);
            Rows(panel, 34, 28, 28, 28, 28);
            var save = ActionButton("保存");
            save.BackColor = Blue;
            save.Click += (s, e) => SaveSpecifications();
            panel.Controls.Add(Line(Heading("绳色规格配置"), save, referenceStatus), 0, 0);
            for (int i = 0; i < Basket.Colors.Length; i++)
            {
                productNames[i] = new TextBox { Text = DefaultNames[i], Width = 95, MaxLength = 18, BorderStyle = BorderStyle.FixedSingle };
                minimumWeights[i] = Amount(DefaultMin[i], 1, 0, 10000, 1);
                maximumWeights[i] = Amount(DefaultMax[i], 1, 0, 10000, 1);
                minimumWeights[i].Width = maximumWeights[i].Width = 58;
                var line = Line(ColorLabel(i), productNames[i], minimumWeights[i], Note("~"), maximumWeights[i], Note("克"));
                line.BackColor = RopeFill[i];
                line.Margin = new Padding(0, 1, 0, 1);
                productNames[i].BackColor = Surface;
                productNames[i].ForeColor = Ink;
                panel.Controls.Add(line, 0, i + 1);
            }
            return panel;
        }

        Control CreateOrderPanel()
        {
            var panel = Card(5);
            Rows(panel, 32, 24, 24, 24, 24);
            var scan = ActionButton("▦  手动扫码");
            scan.Click += (s, e) =>
            {
                choosingOrder = true;
                try { OpenOrder(); }
                finally { choosingOrder = false; }
            };
            var header = Line(Heading("●  装箱任务工单"), scan, automatic);
            panel.Controls.Add(header, 0, 0);
            var detail = Columns(2);
            detail.ColumnStyles.Clear();
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 167));
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var box = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Margin = new Padding(0, 3, 9, 0) };
            var icon = Note("▦  泡沫箱");
            icon.Font = new Font(Font.FontFamily, 18, FontStyle.Bold, GraphicsUnit.Pixel);
            icon.ForeColor = Cyan;
            icon.Location = new Point(8, 8);
            taskCode.AutoSize = false;
            taskCode.AutoEllipsis = true;
            taskCode.SetBounds(10, 34, 145, 23);
            taskState.Location = new Point(10, 61);
            box.Controls.AddRange(new Control[] { icon, taskCode, taskState });
            detail.Controls.Add(box, 0, 0);
            var bands = Columns(1);
            bands.RowCount = 4;
            for (int i = 0; i < 4; i++)
            {
                bands.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
                meters[i] = new TaskMeter(RopeInk[i], RopeFill[i]) { Dock = DockStyle.Fill, Margin = new Padding(0, 1, 0, 1) };
                bands.Controls.Add(meters[i], 0, i);
            }
            detail.Controls.Add(bands, 1, 0);
            panel.Controls.Add(detail, 0, 1);
            panel.SetRowSpan(detail, 4);
            return panel;
        }

        Control CreateBasketPanel()
        {
            var panel = Card(2);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(Heading("●  装箱数量"), 0, 0);
            var body = Columns(2);
            body.ColumnStyles.Clear();
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 79));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 21));
            StyleGrid(summary, 23);
            AddColumn(summary, "规格 / 绳色", 150);
            AddColumn(summary, "待装", 55);
            AddColumn(summary, "已装", 55);
            AddColumn(summary, "剩余", 55);
            AddColumn(summary, "总重量 g", 85);
            AddColumn(summary, "完成率", 75);
            body.Controls.Add(summary, 0, 0);
            var total = new Panel { BackColor = Color.FromArgb(11, 33, 48), Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };
            var caption = Note("总装箱进度");
            caption.ForeColor = Cyan;
            caption.Location = new Point(12, 18);
            progressValue.ForeColor = Cyan;
            progressValue.Font = new Font("Consolas", 30, FontStyle.Bold, GraphicsUnit.Pixel);
            progressValue.Location = new Point(10, 41);
            progressCount.Location = new Point(12, 80);
            total.Controls.AddRange(new Control[] { caption, progressValue, progressCount });
            body.Controls.Add(total, 1, 0);
            panel.Controls.Add(body, 0, 1);
            return panel;
        }

        static Label ColorLabel(int index)
        {
            var label = Note(Basket.Colors[index]);
            label.ForeColor = RopeInk[index];
            return label;
        }

        Control CreateRecordsPanel()
        {
            var panel = Card(2);
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(Line(Heading("●  取放螃蟹记录"), captureStatus), 0, 0);
            StyleGrid(ledger, 50);
            AddColumn(ledger, "类型", 52);
            AddImageColumn("放前");
            AddImageColumn("放后");
            AddImageColumn("螃蟹与绳子");
            AddColumn(ledger, "重量变化 g", 85);
            AddColumn(ledger, "累计 g", 70);
            AddColumn(ledger, "绳色", 53);
            AddColumn(ledger, "规格数量", 100);
            ledger.CellDoubleClick += (s, e) => ShowSnapshot(e.RowIndex, e.ColumnIndex);
            panel.Controls.Add(ledger, 0, 1);
            return panel;
        }

        void AddImageColumn(string label)
        {
            var column = new DataGridViewImageColumn { HeaderText = label, ImageLayout = DataGridViewImageCellLayout.Zoom, FillWeight = 68 };
            column.DefaultCellStyle.NullValue = null;
            ledger.Columns.Add(column);
        }

        static void AddColumn(DataGridView grid, string label, int weight)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = label, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable });
        }

        static void StyleGrid(DataGridView grid, int rowHeight)
        {
            grid.Dock = DockStyle.Fill;
            grid.Margin = Padding.Empty;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BackgroundColor = Surface;
            grid.GridColor = Border;
            grid.BorderStyle = BorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(22, 33, 52);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
            grid.ColumnHeadersHeight = 26;
            grid.RowTemplate.Height = rowHeight;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.DefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.ForeColor = Ink;
            grid.DefaultCellStyle.SelectionBackColor = Border;
            grid.DefaultCellStyle.SelectionForeColor = Ink;
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
                    baselineFrame = null;
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
            baselineFrame = null;
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
            ((TableLayoutPanel)decoderControls.Parent).RowStyles[1].Height = decoderControls.Visible ? 39 : 0;
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
                    cycleLoads.Checked = true;
                }
                else
                {
                    string port = ports.Text.Trim();
                    if (port.Length == 0) throw new ArgumentException("请选择 COM 口或 USB 自动发现。");
                    scale.Open(port == "USB 自动发现" ? "" : port, (ScaleProtocol)protocol.SelectedIndex);
                }
                tare = 0;
                baselineFrame = null;
                ResetCandidates();
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
            scaleButton.Text = connected ? "断开" : "连接";
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
                tareButton.Enabled = weight != null && weight.Valid && weight.Stable;
                deviceStatus.Text = "电子秤  " + (simulationOpen ? "模拟" : scale.IsOpen ? ports.Text : "未连接") +
                    "     视频  " + (camera == null ? "未连接" : cameraType.Text);
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
                sample = new WeightSample(grams - tare, "g", sample.Stable, true, 1, sample.Tick);
            }
            weightValue.Text = sample == null || !sample.Valid ? "—" : sample.Number + " 克";
            scaleStatus.Text = sample == null ? (scale.IsOpen ? "等待有效称重数据" : "秤未连接") :
                !sample.Valid ? "称重数据无效" : sample.Stable ? "净重稳定" : "净重变化";
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
                long started = Stopwatch.GetTimestamp();
                var observation = CrabVision.Read(frame, area, 0.12);
                inferenceMs = CaptureGate.Elapsed(Stopwatch.GetTimestamp(), started) * 1000;
                matchingFrames = latestObservation != null && latestObservation.Signature == observation.Signature
                    ? matchingFrames + 1 : 1;
                latestObservation = observation;
                if (matchingFrames >= 2) stableObservation = observation;
                preview.Invalidate();
            }
            return analyzed == null || matchingFrames < 2 ? 0 : analyzed.Tick;
        }

        string SpecificationsFile { get { return Path.Combine(DataFolder, "packing-specs.tsv"); } }

        void LoadReferences()
        {
            try
            {
                if (!File.Exists(SpecificationsFile)) return;
                string[] lines = File.ReadAllLines(SpecificationsFile);
                for (int i = 0; i < Math.Min(4, lines.Length); i++)
                {
                    string[] fields = lines[i].Split('\t');
                    decimal low, high;
                    if (fields.Length == 3 && !string.IsNullOrWhiteSpace(fields[0]) &&
                        decimal.TryParse(fields[1], NumberStyles.Number, CultureInfo.InvariantCulture, out low) &&
                        decimal.TryParse(fields[2], NumberStyles.Number, CultureInfo.InvariantCulture, out high) &&
                        low >= 0 && high >= low && high <= 10000)
                    {
                        productNames[i].Text = fields[0];
                        minimumWeights[i].Value = low;
                        maximumWeights[i].Value = high;
                    }
                }
            }
            catch (IOException) { referenceStatus.Text = "读取失败"; }
            catch (UnauthorizedAccessException) { referenceStatus.Text = "读取失败"; }
        }

        void ApplySpecInputs()
        {
            savedNames = productNames.Select(input => input.Text.Trim()).ToArray();
            savedMinimum = minimumWeights.Select(input => (double)input.Value).ToArray();
            savedMaximum = maximumWeights.Select(input => (double)input.Value).ToArray();
            appliedReferences = savedMinimum.Select((low, i) => (low + savedMaximum[i]) / 2).ToArray();
        }

        void SaveSpecifications()
        {
            var lines = new string[4];
            for (int i = 0; i < 4; i++)
            {
                string name = productNames[i].Text.Trim();
                if (name.Length == 0 || name.Any(char.IsControl) || minimumWeights[i].Value > maximumWeights[i].Value)
                {
                    referenceStatus.Text = "检查名称、范围";
                    return;
                }
                lines[i] = string.Join("\t", name, minimumWeights[i].Value.ToString(CultureInfo.InvariantCulture),
                    maximumWeights[i].Value.ToString(CultureInfo.InvariantCulture));
            }
            try
            {
                Directory.CreateDirectory(DataFolder);
                File.WriteAllLines(SpecificationsFile, lines);
            }
            catch (IOException) { referenceStatus.Text = "保存失败"; return; }
            catch (UnauthorizedAccessException) { referenceStatus.Text = "保存失败"; return; }
            ApplySpecInputs();
            if (simulationOpen) simulatedScale.SetCount(simulationCount, appliedReferences);
            referenceStatus.Text = "已保存";
            UpdateOrderDisplay();
        }

        string OrdersFolder { get { return Path.Combine(DataFolder, demoScale.Checked ? "packing-demo" : "packing-live"); } }

        void OpenOrder()
        {
            using (var form = new Form { Text = "手动扫码 / 装箱工单", StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, ClientSize = new Size(366, 302),
                MaximizeBox = false, MinimizeBox = false, Font = Font, BackColor = PanelColor, ForeColor = Ink })
            {
                var code = new TextBox { Width = 231, MaxLength = 60, BackColor = Surface, ForeColor = Ink };
                var inputs = Enumerable.Range(0, 4).Select(i => Amount(0, 0, 0, 10000, 1)).ToArray();
                var grid = Card(7);
                Rows(grid, 40, 28, 34, 34, 34, 34, 44);
                grid.Controls.Add(Line(Note("箱码"), code), 0, 0);
                grid.Controls.Add(Note("可用键盘式扫码枪。输入箱码后读取已有目标。"), 0, 1);
                for (int i = 0; i < 4; i++) grid.Controls.Add(Line(ColorLabel(i), inputs[i], Note("只")), 0, i + 2);
                var open = ActionButton("启用工单");
                var read = ActionButton("读取目标");
                grid.Controls.Add(Line(read, open), 0, 6);
                form.Controls.Add(grid);
                Action load = () =>
                {
                    if (string.IsNullOrWhiteSpace(code.Text)) return;
                    try
                    {
                        var entry = new PackingOrder(OrdersFolder, code.Text.Trim(), new int[4]);
                        string path = Path.Combine(entry.Folder, "task.txt");
                        string[] values = File.Exists(path) ? File.ReadAllLines(path) : new string[0];
                        for (int i = 0; i < inputs.Length; i++)
                        {
                            int value;
                            inputs[i].Value = i < values.Length && int.TryParse(values[i], out value) && value >= 0 && value <= 10000
                                ? value : 0;
                        }
                    }
                    catch (Exception ex) { MessageBox.Show(form, ex.Message, "读取工单失败"); }
                };
                read.Click += (s, e) => load();
                code.KeyDown += (s, e) =>
                {
                    if (e.KeyCode != Keys.Enter) return;
                    e.SuppressKeyPress = true;
                    load();
                    inputs[0].Focus();
                };
                open.Click += (s, e) =>
                {
                    try
                    {
                        var selected = new PackingOrder(OrdersFolder, code.Text.Trim(), inputs.Select(input => (int)input.Value).ToArray());
                        selected.SaveGoals();
                        order = selected;
                        baselineFrame = null;
                        ResetCandidates();
                        LoadLedger();
                        UpdateOrderDisplay();
                        form.DialogResult = DialogResult.OK;
                    }
                    catch (Exception ex) { MessageBox.Show(form, ex.Message, "启用工单失败"); }
                };
                form.ShowDialog(this);
            }
        }

        void ResetOrder()
        {
            order = null;
            baselineFrame = null;
            ResetCandidates();
            LoadLedger();
            UpdateOrderDisplay();
        }

        void UpdateOrderDisplay()
        {
            summary.Rows.Clear();
            int goal = 0, packed = 0, remaining = 0;
            double grams = 0;
            for (int i = 0; i < 4; i++)
            {
                int target = order == null ? 0 : order.Goals[i];
                int count = order == null ? 0 : order.Counts[i];
                double weight = order == null ? 0 : order.Weights[i];
                int left = Math.Max(0, target - count);
                goal += target;
                packed += count;
                remaining += left;
                grams += weight;
                string name = savedNames[i] + " / " + Basket.Colors[i];
                meters[i].SetProgress(name, count, target);
                int row = summary.Rows.Add(name, target, count, left, weight.ToString("F1"), Percent(Math.Min(count, target), target));
                summary.Rows[row].Cells[0].Style.ForeColor = RopeInk[i];
                summary.Rows[row].Cells[2].Style.ForeColor = RopeInk[1];
            }
            int applied = order == null ? 0 : order.GoalsMet;
            summary.Rows.Add("合计汇总", goal, packed, remaining, grams.ToString("F1"), Percent(applied, goal));
            progressValue.Text = Percent(applied, goal);
            progressCount.Text = applied + " / " + goal + " 只";
            taskCode.Text = order == null ? "未选择工单" : order.Code;
            taskState.Text = order == null ? "" : order.IsComplete ? "任务已完成" : "配单进行中";
            taskState.ForeColor = RopeInk[1];
        }

        static string Percent(int numerator, int denominator)
        {
            return denominator == 0 ? "—" : ((double)numerator / denominator).ToString("P0", CultureInfo.InvariantCulture);
        }

        void RefreshRecording(long now, WeightSample weight, long frameTick)
        {
            if (choosingOrder) return;
            if (order == null) { captureStatus.Text = "请先扫码选择工单"; return; }
            if (!automatic.Checked) { captureStatus.Text = "自动记录已暂停"; ResetCandidates(); return; }
            if (weight == null || !weight.Valid || !weight.Stable || stableObservation == null ||
                frameTick == 0 || CaptureGate.Elapsed(now, frameTick) > 0.65)
            {
                ResetCandidates();
                captureStatus.Text = "等待稳定画面与净重";
                return;
            }
            if (camera.Source == CameraSource.Demo && !simulationOpen)
            {
                captureStatus.Text = "演示画面需要模拟秤";
                return;
            }

            string key = stableObservation.Signature;
            if (candidateKey != key || Math.Abs(weight.Value - candidateWeight) > WeightTolerance)
            {
                candidateKey = key;
                candidateWeight = weight.Value;
                candidateSince = now;
                candidateFrameTick = 0;
                candidateFrames = 0;
                candidateFirstWeightTick = weight.Tick;
            }
            if (frameTick != candidateFrameTick)
            {
                candidateFrameTick = frameTick;
                candidateFrames++;
            }
            if (candidateFrames < 3 || weight.Tick == candidateFirstWeightTick ||
                CaptureGate.Elapsed(now, candidateSince) < 0.7)
            {
                captureStatus.Text = "确认取放动作中";
                return;
            }
            int[] counts = stableObservation.Counts;
            if (baselineFrame == null)
            {
                double expected = order.Moves.Count == 0 ? 0 : order.Moves[order.Moves.Count - 1].Weight;
                if (!counts.SequenceEqual(order.Counts) || Math.Abs(weight.Value - expected) > EmptyLimit)
                {
                    captureStatus.Text = order.Moves.Count == 0 ? "请放空箱并去皮" : "请将原箱放回并核对净重";
                    return;
                }
                baselineFrame = analyzed;
                baselineCounts = (int[])counts.Clone();
                baselineWeight = weight.Value;
                captureStatus.Text = "已就绪";
                return;
            }

            if (counts.SequenceEqual(baselineCounts))
            {
                if (Math.Abs(weight.Value - baselineWeight) <= WeightTolerance)
                {
                    baselineFrame = analyzed;
                    captureStatus.Text = "等待放入或取出";
                }
                else captureStatus.Text = "净重已变，等待画面确认";
                return;
            }
            PackingMove move = PackingMove.Detect(baselineCounts, counts, baselineWeight, weight.Value, WeightTolerance);
            if (move == null) { captureStatus.Text = "请分绳色逐次取放"; return; }
            if (CaptureGate.Elapsed(now, lastWriteAttempt) < 1) return;
            lastWriteAttempt = now;
            double perCrab = Math.Abs(move.Delta / move.Count);
            move.Spec = savedNames[move.Color];
            move.OutsideRange = perCrab < savedMinimum[move.Color] || perCrab > savedMaximum[move.Color];
            try
            {
                SaveSnapshots(move, analyzed, stableObservation);
                order.Append(move);
            }
            catch (IOException) { captureStatus.Text = "记录保存失败"; return; }
            catch (UnauthorizedAccessException) { captureStatus.Text = "记录保存失败"; return; }
            catch (System.Runtime.InteropServices.ExternalException) { captureStatus.Text = "快照保存失败"; return; }
            baselineFrame = analyzed;
            baselineCounts = (int[])counts.Clone();
            baselineWeight = weight.Value;
            AddMoveRow(move);
            UpdateOrderDisplay();
            captureStatus.Text = move.OutsideRange ? "已记录 · 单只均重超规格范围" : "已保存取放记录";
        }

        void SaveSnapshots(PackingMove move, VideoFrame frame, CrabObservation after)
        {
            string id = Guid.NewGuid().ToString("N");
            string directory = Path.Combine(order.Folder, "images");
            Directory.CreateDirectory(directory);
            move.Before = id + "-before.jpg";
            move.After = id + "-after.jpg";
            move.Target = id + "-target.jpg";
            using (Bitmap before = baselineFrame.ToBitmap())
            using (Bitmap current = frame.ToBitmap())
            {
                before.Save(Path.Combine(directory, move.Before), System.Drawing.Imaging.ImageFormat.Jpeg);
                current.Save(Path.Combine(directory, move.After), System.Drawing.Imaging.ImageFormat.Jpeg);
                bool addition = move.Count > 0;
                CrabObservation prior = CrabVision.Read(baselineFrame, area, 0.12);
                var present = (addition ? after : prior).Targets.Where(target => target.ColorIndex == move.Color);
                var other = (addition ? prior : after).Targets.Where(target => target.ColorIndex == move.Color).ToArray();
                CrabTarget focus = present.OrderByDescending(target => other.Select(candidate =>
                    Rectangle.Intersect(candidate.Bounds, target.Bounds)).Sum(box => box.Width * box.Height)).LastOrDefault();
                Bitmap image = addition ? current : before;
                if (focus == null)
                    image.Save(Path.Combine(directory, move.Target), System.Drawing.Imaging.ImageFormat.Jpeg);
                else
                {
                    Rectangle box = focus.Bounds;
                    box.Inflate(10, 10);
                    box.Intersect(new Rectangle(0, 0, image.Width, image.Height));
                    using (Bitmap crop = image.Clone(box, image.PixelFormat))
                        crop.Save(Path.Combine(directory, move.Target), System.Drawing.Imaging.ImageFormat.Jpeg);
                }
            }
        }

        void LoadLedger()
        {
            foreach (DataGridViewRow row in ledger.Rows) DisposeRowImages(row);
            ledger.Rows.Clear();
            if (order == null) return;
            foreach (PackingMove move in order.Moves.Skip(Math.Max(0, order.Moves.Count - 200))) AddMoveRow(move);
        }

        static void DisposeRowImages(DataGridViewRow row)
        {
            for (int i = 1; i <= 3; i++)
            {
                var image = row.Cells[i].Value as Image;
                if (image != null) image.Dispose();
            }
        }

        Image LoadSnapshot(string name, bool thumbnail)
        {
            if (order == null || string.IsNullOrEmpty(name) || Path.GetFileName(name) != name) return null;
            string path = Path.Combine(order.Folder, "images", name);
            try
            {
                using (Image image = Image.FromFile(path))
                    return thumbnail ? new Bitmap(image, Math.Max(1, image.Width * 39 / image.Height), 39) :
                        new Bitmap(image);
            }
            catch (IOException) { return null; }
            catch (ArgumentException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (OutOfMemoryException) { return null; }
        }

        void AddMoveRow(PackingMove move)
        {
            var row = new DataGridViewRow();
            row.CreateCells(ledger, move.Count > 0 ? "放入" : "取出", LoadSnapshot(move.Before, true),
                LoadSnapshot(move.After, true), LoadSnapshot(move.Target, true),
                move.Delta.ToString("+0.0;-0.0"), move.Weight.ToString("F1"), Basket.Colors[move.Color],
                move.Spec + " × " + move.Count + (move.OutsideRange ? " !" : ""));
            row.Tag = move;
            row.Cells[0].Style.ForeColor = move.Count > 0 ? RopeInk[1] : RopeInk[0];
            row.Cells[4].Style.ForeColor = row.Cells[0].Style.ForeColor;
            row.Cells[6].Style.ForeColor = RopeInk[move.Color];
            row.Cells[7].ToolTipText = move.Time.ToString("yyyy-MM-dd HH:mm:ss") + (move.OutsideRange ? " · 超规格范围" : "");
            ledger.Rows.Insert(0, row);
            if (ledger.Rows.Count <= 200) return;
            var last = ledger.Rows[ledger.Rows.Count - 1];
            DisposeRowImages(last);
            ledger.Rows.Remove(last);
        }

        void ShowSnapshot(int row, int column)
        {
            if (row < 0 || column < 1 || column > 3) return;
            var move = (PackingMove)ledger.Rows[row].Tag;
            using (Image image = LoadSnapshot(column == 1 ? move.Before : column == 2 ? move.After : move.Target, false))
            {
                if (image == null) return;
                using (var form = new Form { Text = ledger.Columns[column].HeaderText, StartPosition = FormStartPosition.CenterParent,
                    ClientSize = new Size(640, 360), BackColor = Surface })
                {
                    form.Controls.Add(new PictureBox { Dock = DockStyle.Fill, Image = image, SizeMode = PictureBoxSizeMode.Zoom });
                    form.ShowDialog(this);
                }
            }
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
            var info = cameraStatus.Text;
            if (analyzed != null) info += "   /   识别 " + inferenceMs.ToString("F1") + " ms";
            TextRenderer.DrawText(e.Graphics, info, Font, new Point(10, 9), Cyan, Surface);
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
            candidateKey = null;
        }

        void InvalidateAnalysis()
        {
            analyzed = null;
            latestObservation = null;
            stableObservation = null;
            matchingFrames = 0;

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
            foreach (DataGridViewRow row in ledger.Rows) DisposeRowImages(row);
        }

        static TableLayoutPanel Card(int rows)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows,
                Padding = new Padding(8, 5, 8, 5), Margin = new Padding(5), BackColor = PanelColor, CellBorderStyle = TableLayoutPanelCellBorderStyle.None };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.Paint += (sender, e) =>
            {
                if (panel.Width > 0 && panel.Height > 0)
                    using (var pen = new Pen(Border)) e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
            };
            return panel;
        }

        static TableLayoutPanel Columns(int columns)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = 1, Margin = Padding.Empty };
            for (int i = 0; i < columns; i++) panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columns));
            return panel;
        }

        static void Rows(TableLayoutPanel panel, params int[] heights)
        {
            foreach (int height in heights) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
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
            title.Font = new Font("Microsoft YaHei UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
            title.ForeColor = Ink;
            return title;
        }

        static Button ActionButton(string text)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 26,
                Padding = new Padding(5, 0, 5, 0), BackColor = Border, ForeColor = Cyan,
                FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Cursor = Cursors.Hand, Margin = new Padding(2) };
            button.FlatAppearance.BorderSize = 0;
            return button;
        }

        static ComboBox Choice(params string[] values)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120, BackColor = Surface, ForeColor = Ink };
            box.Items.AddRange(values);
            box.SelectedIndex = 0;
            return box;
        }

        static NumericUpDown Amount(decimal value, int places, decimal min, decimal max, decimal increment)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = value, DecimalPlaces = places,
                Increment = increment, Width = 78, ThousandsSeparator = false, BackColor = Surface, ForeColor = Ink };
        }
    }
}
