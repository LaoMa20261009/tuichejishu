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
    sealed partial class MainForm : Form
    {
        static readonly Color Ink = Color.FromArgb(11, 28, 48);
        static readonly Color Blue = Color.FromArgb(0, 97, 148);
        static readonly Color Cyan = Color.FromArgb(2, 132, 199);
        static readonly Color PanelColor = Color.White;
        static readonly Color Surface = Color.FromArgb(245, 248, 255);
        static readonly Color Border = Color.FromArgb(213, 226, 245);
        static readonly Color Muted = Color.FromArgb(81, 95, 116);
        static readonly Color Green = Color.FromArgb(0, 105, 71);
        static readonly Color[] RopeInk = { Color.FromArgb(185, 28, 28), Green, Blue,
            Color.FromArgb(86, 106, 135), Color.FromArgb(137, 91, 0) };
        static readonly Color[] RopeFill = { Color.FromArgb(255, 233, 230), Color.FromArgb(223, 247, 235),
            Color.FromArgb(228, 241, 255), Color.FromArgb(235, 241, 250), Color.FromArgb(255, 246, 213) };
        static readonly int[] DemoColors = { 0, 1, 2, 3, 1, 3, 2, 0 };
        static readonly int[] DemoChanges = { 1, 2, 3, 4, -2, -4, -3, -1 };
        static readonly string[] DefaultNames = { "2.5两母蟹", "3.0两母蟹", "蓝绳蟹", "3.5两母蟹", "黄绳蟹" };
        static readonly int[] DefaultMin = { 115, 140, 140, 165, 100 };
        static readonly int[] DefaultMax = { 135, 160, 160, 185, 120 };

        readonly VideoSurface preview = new VideoSurface();
        readonly ComboBox cameraType = Choice("网络视频", "USB 摄像头", "本地视频", "演示画面");
        readonly TextBox address = new TextBox { Width = 290 };
        readonly TextBox ffmpeg = new TextBox { Width = 112, Text = "ffmpeg.exe" };
        readonly Button browseVideo = ActionButton("选择视频");
        readonly Button cameraButton = ActionButton("连接视频");
        readonly Label cameraStatus = Note("视频未连接");
        readonly Label referenceStatus = Note("");
        double[] appliedReferences;
        readonly TextBox[] productNames = new TextBox[Basket.Colors.Length];
        readonly TextBox[] minimumWeights = new TextBox[Basket.Colors.Length];
        readonly TextBox[] maximumWeights = new TextBox[Basket.Colors.Length];
        string[] savedNames;
        double[] savedMinimum, savedMaximum;
        readonly Label deviceStatus = Note("");
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
        readonly NumericUpDown crabCount = Amount(0, 0, 0, 12, 1);
        readonly CheckBox cycleLoads = new CheckBox { Text = "自动放蟹", AutoSize = true,
            Margin = new Padding(10, 7, 4, 3) };
        FlowLayoutPanel simulationControls;
        FlowLayoutPanel serialControls;
        Size? fixedWindowSize;
        Control dragControl;
        Point dragOffset;
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
        volatile int[] simulationColors = new int[0];
        Rectangle foundBox, previousBox;
        bool importingLabel;
        int demoStep;
        long weightCandidateSince;
        double weightCandidate;
        double lastUnclassifiedWeight = double.NaN;
        int unclassifiedMoves;
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
            Text = "螃蟹装箱计数系统";
            KeyPreview = true;
            AutoSize = false;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Rectangle openingBounds = ConsoleLayout.WindowBounds(Screen.FromPoint(Cursor.Position).Bounds);
            ClientSize = ConsoleLayout.DesignSize;
            Font = new Font("Microsoft YaHei UI", 17, FontStyle.Regular, GraphicsUnit.Pixel);
            BackColor = Surface;
            ForeColor = Ink;
            cameraType.SelectedIndex = (int)CameraSource.File;
            cameraType.Width = 108;
            address.BackColor = ffmpeg.BackColor = Surface;
            address.ForeColor = ffmpeg.ForeColor = Ink;
            ports.BackColor = Surface;
            ports.ForeColor = Ink;
            address.Width = 128;
            cameraButton.AutoSize = false;
            cameraButton.Width = 80;
            address.BorderStyle = BorderStyle.FixedSingle;
            browseVideo.AutoSize = false;
            browseVideo.Width = 80;
            cameraButton.BackColor = scaleButton.BackColor = Blue;
            cameraButton.ForeColor = scaleButton.ForeColor = Color.White;
            browseVideo.BackColor = Color.FromArgb(12, 108, 130);
            browseVideo.ForeColor = Color.White;

            SuspendLayout();
            Place(CreateBanner(), 0, 0, 1280, 59);
            Place(CreateLabelPage(), 14, 70, 1252, 716);
            Place(CreatePackingPage(), 14, 70, 1252, 716);
            CreateSettingsContent();
            ResumeLayout(true);
            float factor = openingBounds.Width / (float)ConsoleLayout.DesignSize.Width;
            if (Math.Abs(factor - 1) > 0.001F)
            {
                ScaleContents(this, factor);
                ledger.ColumnHeadersHeight = (int)Math.Round(28 * factor);
                ledger.RowTemplate.Height = (int)Math.Round(42 * factor);
                ScaleGridFont(ledger.ColumnHeadersDefaultCellStyle, factor);
                ScaleGridFont(ledger.DefaultCellStyle, factor);
            }
            Bounds = openingBounds;
            fixedWindowSize = openingBounds.Size;
            MinimumSize = MaximumSize = openingBounds.Size;

            cameraButton.Click += (s, e) => ToggleCamera();
            browseVideo.Click += (s, e) => SelectInputFile();
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
                if (changingDemoCount) return;
                cycleLoads.Checked = false;
                var colors = simulatedScale.Colors;
                int change = (int)crabCount.Value - colors.Length;
                int color = change < 0 && colors.Length > 0 ? colors[colors.Length - 1] : 0;
                if (change < 0) change = -Math.Min(-change, colors.Count(item => item == color));
                simulatedScale.Adjust(color, change, appliedReferences);
                UpdateDemoCount();
            };
            cycleLoads.CheckedChanged += (s, e) =>
            {
                if (!cycleLoads.Checked) return;
                demoStep = 0;
                simulatedScale.Clear();
                UpdateDemoCount();
                nextDemoStep = Stopwatch.GetTimestamp() + 4 * Stopwatch.Frequency;
            };
            LoadSettings();
            LoadReferences();
            ApplySpecInputs();
            automatic.CheckedChanged += (s, e) => ResetCandidates();
            tareButton.Click += (s, e) => AdjustScaleZero(true);
            preview.Paint += DrawArea;
            preview.MouseDown += BeginSelection;
            preview.MouseMove += MoveSelection;
            preview.MouseUp += EndSelection;
            timer.Tick += (s, e) => RefreshDashboard();
            RefreshPorts();
            UpdateCameraInputs();
            UpdateScaleInputs();
            ShowPackingPage(false);
            timer.Start();
        }

        protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        {
            if (fixedWindowSize.HasValue && WindowState == FormWindowState.Normal)
            {
                width = fixedWindowSize.Value.Width;
                height = fixedWindowSize.Value.Height;
            }
            base.SetBoundsCore(x, y, width, height, specified);
        }

        static void ScaleContents(Control root, float factor)
        {
            var fonts = new System.Collections.Generic.Dictionary<Control, Font>();
            var layoutMetrics = new System.Collections.Generic.List<Action>();
            CollectExplicitFonts(root, fonts);
            foreach (Control control in root.Controls) CollectLayoutMetrics(control, factor, layoutMetrics);
            root.SuspendLayout();
            foreach (Control control in root.Controls) control.Scale(new SizeF(factor, factor));
            foreach (var item in fonts)
            {
                Font font = item.Value;
                item.Key.Font = new Font(font.FontFamily, font.Size * factor, font.Style, font.Unit);
            }
            foreach (Action apply in layoutMetrics) apply();
            root.ResumeLayout(true);
        }

        static void CollectExplicitFonts(Control control, System.Collections.Generic.Dictionary<Control, Font> fonts)
        {
            var property = System.ComponentModel.TypeDescriptor.GetProperties(control)["Font"];
            if (property != null && property.ShouldSerializeValue(control)) fonts.Add(control, control.Font);
            foreach (Control child in control.Controls) CollectExplicitFonts(child, fonts);
        }

        static void CollectLayoutMetrics(Control control, float factor, System.Collections.Generic.List<Action> settings)
        {
            Padding padding = control.Padding, margin = control.Margin;
            settings.Add(() =>
            {
                control.Padding = ScalePadding(padding, factor);
                control.Margin = ScalePadding(margin, factor);
            });
            var table = control as TableLayoutPanel;
            if (table != null)
            {
                foreach (RowStyle row in table.RowStyles)
                {
                    if (row.SizeType != SizeType.Absolute) continue;
                    float height = row.Height;
                    settings.Add(() => row.Height = height * factor);
                }
                foreach (ColumnStyle column in table.ColumnStyles)
                {
                    if (column.SizeType != SizeType.Absolute) continue;
                    float width = column.Width;
                    settings.Add(() => column.Width = width * factor);
                }
            }
            foreach (Control child in control.Controls) CollectLayoutMetrics(child, factor, settings);
        }

        static Padding ScalePadding(Padding padding, float factor)
        {
            return new Padding((int)Math.Round(padding.Left * factor), (int)Math.Round(padding.Top * factor),
                (int)Math.Round(padding.Right * factor), (int)Math.Round(padding.Bottom * factor));
        }

        static void ScaleGridFont(DataGridViewCellStyle style, float factor)
        {
            Font font = style.Font;
            if (font != null) style.Font = new Font(font.FontFamily, font.Size * factor, font.Style, font.Unit);
        }

        void EnableWindowDrag(Control surface)
        {
            surface.MouseDown += (sender, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                dragControl = surface;
                dragOffset = surface.PointToScreen(e.Location) - (Size)Location;
                surface.Capture = true;
            };
            surface.MouseMove += (sender, e) =>
            {
                if (dragControl != surface || !surface.Capture) return;
                Point pointer = surface.PointToScreen(e.Location);
                Rectangle screen = Screen.FromPoint(pointer).Bounds;
                Location = new Point(pointer.X - dragOffset.X, Math.Max(screen.Top, pointer.Y - dragOffset.Y));
            };
            surface.MouseUp += (sender, e) =>
            {
                if (e.Button == MouseButtons.Left && dragControl == surface) surface.Capture = false;
            };
            surface.MouseCaptureChanged += (sender, e) =>
            {
                if (!surface.Capture && dragControl == surface) dragControl = null;
            };
        }

        void Place(Control control, int x, int y, int width, int height)
        {
            control.Dock = DockStyle.None;
            control.Bounds = ConsoleLayout.Place(ClientSize, new Rectangle(x, y, width, height));
            Controls.Add(control);
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
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 240, 253);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
            grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
            grid.ColumnHeadersHeight = 28;
            grid.RowHeadersDefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 15, FontStyle.Regular, GraphicsUnit.Pixel);
            grid.RowsDefaultCellStyle.ForeColor = Ink;
            grid.RowsDefaultCellStyle.BackColor = Surface;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = Ink;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 251, 255);
            grid.RowsDefaultCellStyle.SelectionForeColor = Ink;
            grid.RowsDefaultCellStyle.SelectionBackColor = Border;
            grid.RowTemplate.Height = rowHeight;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.DefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.ForeColor = Ink;
            grid.DefaultCellStyle.SelectionBackColor = Border;
            grid.DefaultCellStyle.SelectionForeColor = Ink;
        }

        void SelectInputFile()
        {
            bool file = cameraType.SelectedIndex == (int)CameraSource.File;
            if (file) SelectVideoFile();
            else
            {
                using (var dialog = new OpenFileDialog { Title = "选择相机解码器", Filter = "FFmpeg|ffmpeg.exe",
                    CheckFileExists = true, RestoreDirectory = true })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        ffmpeg.Text = dialog.FileName;
                        hints.SetToolTip(browseVideo, dialog.FileName);
                    }
                }
            }
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
                camera = new CameraSession(source, address.Text.Trim(), executable, () => simulationColors);
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
                MessageBox.Show(this, "网络或 USB 相机需要 FFmpeg 解码。请将 ffmpeg.exe 放在程序目录，或点击「选解码器」。", "找不到相机解码器");
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
            browseVideo.Text = source == CameraSource.File || source == CameraSource.Demo ? "选择视频" : "选解码器";
            browseVideo.Enabled = camera == null && source != CameraSource.Demo;
            // Mask stream addresses in the UI because RTSP URLs may contain credentials.
            address.UseSystemPasswordChar = source == CameraSource.Network;
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
                    simulatedScale.Clear();
                    UpdateDemoCount();
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
            simulationControls.Visible = demoScale.Checked;
            serialControls.Visible = !demoScale.Checked;
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
                string scaleConnection = simulationOpen ? "模拟" : !scale.IsOpen ? "未连接" :
                    ports.Text + (weight == null || !weight.Valid ? " · 待读数" : " · 在线");
                string cameraConnection = camera == null ? "未连接" : camera.Ended ? "已停止" :
                    displayed == null ? "等待画面" : cameraType.Text + " · 在线";
                deviceStatus.Text = "电子秤  " + scaleConnection + "     工业相机  " + cameraConnection;
                RefreshWorkflow(now, weight);
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

            if (simulationOpen && cycleLoads.Checked && camera != null && packingActive)
            {
                if (camera.Source == CameraSource.Demo && now >= nextDemoStep)
                {
                    simulatedScale.Adjust(DemoColors[demoStep], DemoChanges[demoStep], appliedReferences);
                    demoStep = (demoStep + 1) % DemoChanges.Length;
                    UpdateDemoCount();
                    nextDemoStep = now + 6 * Stopwatch.Frequency;
                }
                else if (camera.Source != CameraSource.Demo && stableObservation != null &&
                    matchingFrames >= 2 && analyzed != null && CaptureGate.Elapsed(now, analyzed.Tick) <= 0.65)
                {
                    int[] current = new int[Basket.Colors.Length];
                    foreach (int color in simulatedScale.Colors) current[color]++;
                    int[] changed = Enumerable.Range(0, Basket.Colors.Length).Where(i => current[i] != stableObservation.Counts[i]).ToArray();
                    if (changed.Length == 1)
                    {
                        int color = changed[0];
                        int change = stableObservation.Counts[color] - current[color];
                        if (Math.Abs(change) <= 4 && stableObservation.Counts.Sum() <= 12)
                        {
                            simulatedScale.Adjust(color, change, appliedReferences);
                            UpdateDemoCount();
                        }
                    }
                }
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

        void UpdateDemoCount()
        {
            simulationColors = simulatedScale.Colors;
            changingDemoCount = true;
            try { crabCount.Value = simulationColors.Length; }
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
                Rectangle box = FoamBox.Find(frame, area);
                var overlap = Rectangle.Intersect(previousBox, box);
                bool sameBox = !box.IsEmpty && !previousBox.IsEmpty &&
                    overlap.Width * overlap.Height >= 0.65 * Math.Max(previousBox.Width * previousBox.Height, box.Width * box.Height);
                previousBox = box;
                if (!sameBox)
                {
                    foundBox = Rectangle.Empty;
                    matchingFrames = 0;
                    latestObservation = stableObservation = null;
                }
                else
                {
                    foundBox = box;
                    var observation = CrabVision.Read(frame, foundBox, 0.12);
                    matchingFrames = latestObservation != null && latestObservation.Signature == observation.Signature
                        ? matchingFrames + 1 : 1;
                    latestObservation = observation;
                    if (matchingFrames >= 2) stableObservation = observation;
                }
                inferenceMs = CaptureGate.Elapsed(Stopwatch.GetTimestamp(), started) * 1000;
                preview.Invalidate();
            }
            return analyzed == null || matchingFrames < 2 ? 0 : analyzed.Tick;
        }

        string SpecificationsFile { get { return Path.Combine(order == null ? DataFolder : order.Folder, "packing-specs.tsv"); } }

        void ReloadSpecifications()
        {
            for (int i = 0; i < Basket.Colors.Length; i++)
            {
                productNames[i].Text = DefaultNames[i];
                minimumWeights[i].Text = DefaultMin[i].ToString(CultureInfo.CurrentCulture);
                maximumWeights[i].Text = DefaultMax[i].ToString(CultureInfo.CurrentCulture);
                ruleEnabled[i].Checked = true;
            }
            LoadReferences();
            ApplySpecInputs();
        }

        void LoadReferences()
        {
            try
            {
                string path = SpecificationsFile;
                if (!File.Exists(path)) path = Path.Combine(DataFolder, "packing-specs.tsv");
                if (!File.Exists(path)) return;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < Math.Min(Basket.Colors.Length, lines.Length); i++)
                {
                    string[] fields = lines[i].Split('\t');
                    decimal low, high;
                    if (fields.Length >= 3 && !string.IsNullOrWhiteSpace(fields[0]) &&
                        decimal.TryParse(fields[1], NumberStyles.Number, CultureInfo.InvariantCulture, out low) &&
                        decimal.TryParse(fields[2], NumberStyles.Number, CultureInfo.InvariantCulture, out high) &&
                        low >= 0 && high >= low && high <= 10000)
                    {
                        productNames[i].Text = fields[0];
                        minimumWeights[i].Text = low.ToString("0.#######", CultureInfo.CurrentCulture);
                        maximumWeights[i].Text = high.ToString("0.#######", CultureInfo.CurrentCulture);
                        if (fields.Length >= 4) ruleEnabled[i].Checked = fields[3] != "0";
                    }
                }
            }
            catch (IOException) { referenceStatus.Text = "读取失败"; }
            catch (UnauthorizedAccessException) { referenceStatus.Text = "读取失败"; }
        }

        void ApplySpecInputs()
        {
            savedNames = productNames.Select(input => input.Text.Trim()).ToArray();
            savedMinimum = minimumWeights.Select(input => double.Parse(input.Text.Trim(), CultureInfo.CurrentCulture)).ToArray();
            savedMaximum = maximumWeights.Select(input => double.Parse(input.Text.Trim(), CultureInfo.CurrentCulture)).ToArray();
            appliedReferences = savedMinimum.Select((low, i) => (low + savedMaximum[i]) / 2).ToArray();
        }

        bool SaveSpecifications()
        {
            var lines = new string[Basket.Colors.Length];
            for (int i = 0; i < Basket.Colors.Length; i++)
            {
                string name = productNames[i].Text.Trim();
                decimal low, high;
                if (name.Length == 0 || name.Any(char.IsControl) ||
                    !TryReadWeight(minimumWeights[i], out low) || !TryReadWeight(maximumWeights[i], out high) || low > high)
                {
                    referenceStatus.Text = "检查名称、范围";
                    return false;
                }
                lines[i] = string.Join("\t", name, low.ToString(CultureInfo.InvariantCulture), high.ToString(CultureInfo.InvariantCulture),
                    ruleEnabled[i].Checked ? "1" : "0");
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SpecificationsFile));
                File.WriteAllLines(SpecificationsFile, lines);
            }
            catch (IOException) { referenceStatus.Text = "保存失败"; return false; }
            catch (UnauthorizedAccessException) { referenceStatus.Text = "保存失败"; return false; }
            ApplySpecInputs();

            referenceStatus.Text = "已保存";
            UpdateOrderDisplay();
            return true;
        }

        string OrdersFolder { get { return Path.Combine(DataFolder, demoScale.Checked ? "packing-demo" : "packing-live"); } }

        void ResetOrder()
        {
            order = null;
            ReloadSpecifications();
            lastUnclassifiedWeight = double.NaN;
            unclassifiedMoves = 0;
            baselineFrame = null;
            ResetCandidates();
            ShowPackingPage(false);
        }

        static string Percent(int numerator, int denominator)
        {
            return denominator == 0 ? "—" : (100.0 * numerator / denominator).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        }

        void RefreshRecording(long now, WeightSample weight, long frameTick)
        {
            if (choosingOrder || importingLabel || !packingActive) return;
            if (order != null && order.Finished) { captureStatus.Text = "装箱已完成"; return; }
            if (order == null) { captureStatus.Text = "请先扫码选择工单"; return; }
            if (!automatic.Checked) { captureStatus.Text = "自动记录已暂停"; ResetCandidates(); return; }
            if (camera != null && camera.Source == CameraSource.Demo && !simulationOpen)
            {
                captureStatus.Text = "演示画面需要模拟秤";
                ResetCandidates();
                return;
            }
            if (weight == null || !weight.Valid || !weight.Stable)
            {
                ResetCandidates();
                captureStatus.Text = "等待净重稳定";
                return;
            }
            if (weightCandidateSince == 0 || Math.Abs(weight.Value - weightCandidate) > WeightTolerance)
            {
                weightCandidateSince = now;
                weightCandidate = weight.Value;
            }

            bool fresh = stableObservation != null && frameTick != 0 && CaptureGate.Elapsed(now, frameTick) <= 0.65;
            if (!fresh)
            {
                candidateKey = null;
                captureStatus.Text = foundBox.IsEmpty ? "寻找白色泡沫箱" : "等待箱内画面稳定";
                TryRecordUnclassified(now, weight);
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
                TryRecordUnclassified(now, weight);
                return;
            }

            int[] counts = stableObservation.Counts;
            if (baselineFrame == null)
            {
                var confirmed = order.Moves.LastOrDefault(item => item.Color >= 0);
                double expected = confirmed == null ? 0 : confirmed.Weight;
                if (!counts.SequenceEqual(order.Counts) || Math.Abs(weight.Value - expected) > EmptyLimit)
                {
                    captureStatus.Text = confirmed == null ? "请放空箱并去皮" : "请将原箱放回并核对净重";
                    return;
                }
                AcceptBaseline(counts, weight.Value);
                captureStatus.Text = "已就绪";
                return;
            }

            if (counts.SequenceEqual(baselineCounts))
            {
                if (Math.Abs(weight.Value - baselineWeight) <= WeightTolerance)
                {
                    if (unclassifiedMoves > 0) TryRecordUnclassified(now, weight);
                    if (unclassifiedMoves == 0 || Math.Abs(lastUnclassifiedWeight - weight.Value) <= WeightTolerance)
                    {
                        AcceptBaseline(counts, weight.Value);
                        captureStatus.Text = "等待放入或取出";
                    }
                }
                else
                {
                    captureStatus.Text = "净重已变，等待画面确认";
                    TryRecordUnclassified(now, weight);
                }
                return;
            }

            PackingMove move = PackingMove.Detect(baselineCounts, counts, baselineWeight, weight.Value, WeightTolerance);
            bool resolve = unclassifiedMoves == 1 && Math.Abs(lastUnclassifiedWeight - weight.Value) <= WeightTolerance;
            if (move == null || (unclassifiedMoves != 0 && !resolve))
            {
                captureStatus.Text = unclassifiedMoves == 0 ? "等待确认绳色" : "请核对待确认记录";
                TryRecordUnclassified(now, weight);
                return;
            }
            if (CaptureGate.Elapsed(now, lastWriteAttempt) < 1) return;
            lastWriteAttempt = now;
            double perCrab = Math.Abs(move.Delta / move.Count);
            move.Spec = savedNames[move.Color];
            move.OutsideRange = perCrab < savedMinimum[move.Color] || perCrab > savedMaximum[move.Color];
            try
            {
                SaveSnapshots(move, analyzed, stableObservation);
                if (resolve) order.ResolveLast(move);
                else order.Append(move);
            }
            catch (IOException) { captureStatus.Text = "记录保存失败"; return; }
            catch (UnauthorizedAccessException) { captureStatus.Text = "记录保存失败"; return; }
            catch (System.Runtime.InteropServices.ExternalException) { captureStatus.Text = "快照保存失败"; return; }
            AcceptBaseline(counts, weight.Value);
            LoadLedger();
            UpdateOrderDisplay();
            captureStatus.Text = move.OutsideRange ? "已记录 · 单只均重超规格范围" : "已保存取放记录";
        }

        void AcceptBaseline(int[] counts, double weight)
        {
            baselineFrame = analyzed;
            baselineCounts = (int[])counts.Clone();
            baselineWeight = weight;
            lastUnclassifiedWeight = double.NaN;
            unclassifiedMoves = 0;
        }

        void TryRecordUnclassified(long now, WeightSample weight)
        {
            if (baselineFrame == null || CaptureGate.Elapsed(now, weightCandidateSince) < 2.2 ||
                CaptureGate.Elapsed(now, lastWriteAttempt) < 1) return;
            double reference = unclassifiedMoves == 0 ? baselineWeight : lastUnclassifiedWeight;
            if (Math.Abs(weight.Value - reference) <= WeightTolerance) return;
            lastWriteAttempt = now;
            var move = new PackingMove { Time = DateTimeOffset.Now, Color = -1, Delta = weight.Value - reference,
                Weight = weight.Value, Spec = "待确认", Before = "", After = "", Target = "" };
            try
            {
                if (displayed != null) SaveSnapshots(move, displayed, null);
                order.Append(move);
            }
            catch (IOException) { captureStatus.Text = "记录保存失败"; return; }
            catch (UnauthorizedAccessException) { captureStatus.Text = "记录保存失败"; return; }
            catch (System.Runtime.InteropServices.ExternalException) { captureStatus.Text = "快照保存失败"; return; }
            lastUnclassifiedWeight = weight.Value;
            unclassifiedMoves++;
            LoadLedger();
            RefreshGradeCards();
            captureStatus.Text = "已记净重 · 绳色待确认";
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
                if (move.Color < 0 || after == null)
                {
                    move.Target = "";
                    return;
                }
                bool addition = move.Count > 0;
                CrabObservation prior = CrabVision.Read(baselineFrame, FoamBox.Find(baselineFrame, area), 0.12);
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

        Image LoadSnapshot(string name, bool thumbnail)
        {
            if (order == null || string.IsNullOrEmpty(name) || Path.GetFileName(name) != name) return null;
            string path = Path.Combine(order.Folder, "images", name);
            try
            {
                using (Image image = Image.FromFile(path))
                    return thumbnail ? new Bitmap(image, Math.Max(1, image.Width * ConsolePaint.Pixels(ledger, 39) / image.Height), ConsolePaint.Pixels(ledger, 39)) :
                        new Bitmap(image);
            }
            catch (IOException) { return null; }
            catch (ArgumentException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (OutOfMemoryException) { return null; }
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
            Func<int, int> px = value => ConsolePaint.Pixels(preview, value);
            string info = cameraStatus.Text;
            if (displayed != null && camera != null && !camera.Ended)
            {
                bool online = camera.Source == CameraSource.Network || camera.Source == CameraSource.Usb;
                var badge = new Rectangle(px(9), px(9), px(48), px(20));
                Color overlay = Color.FromArgb(15, 23, 42);
                ConsolePaint.Tile(e.Graphics, badge, 4, online ? Color.FromArgb(139, 28, 52) :
                    Color.FromArgb(13, 84, 108), Color.FromArgb(44, 118, 142));
                TextRenderer.DrawText(e.Graphics, online ? "LIVE" : "播放", Font, badge, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(e.Graphics, info, Font, new Point(px(65), px(10)), Color.White, overlay);
                if (analyzed != null)
                {
                    var telemetry = new Rectangle(Math.Max(0, preview.Width - px(162)), px(9), px(151), px(21));
                    TextRenderer.DrawText(e.Graphics, "识别耗时 " + inferenceMs.ToString("F1") + " ms",
                        Font, telemetry, Color.White, overlay, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                    ConsolePaint.Dot(e.Graphics, px(12), preview.Height - px(22), px(7), RopeInk[1]);
                    TextRenderer.DrawText(e.Graphics, foundBox.IsEmpty ? "寻找白色泡沫箱" : "箱内绳色识别中",
                        Font, new Point(px(25), preview.Height - px(29)), Color.White, overlay);
                }
            }
            else TextRenderer.DrawText(e.Graphics, info, Font, new Point(px(10), px(9)), Cyan, Surface);
            if (displayed == null) return;
            Rectangle bounds = preview.FrameBounds;
            if (!foundBox.IsEmpty && !selectionStart.HasValue)
            {
                using (var pen = new Pen(Cyan, px(2))) e.Graphics.DrawRectangle(pen, OnPreview(foundBox, bounds));
            }
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
            weightCandidateSince = 0;
        }

        void InvalidateAnalysis()
        {
            analyzed = null;
            foundBox = previousBox = Rectangle.Empty;
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
            ReleaseOcrPhoto();
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
            return new ConsoleLabel { Text = text, ForeColor = Muted, AutoSize = true, Margin = new Padding(3, 5, 4, 3) };
        }

        static Label Heading(string text)
        {
            Label title = Note(text);
            title.Font = new Font("Microsoft YaHei UI", 18, FontStyle.Bold, GraphicsUnit.Pixel);
            title.ForeColor = Ink;
            return title;
        }

        static Button ActionButton(string text)
        {
            var button = new ConsoleButton { Text = text, AutoSize = true, Height = 26,
                Padding = new Padding(5, 0, 5, 0), BackColor = Color.FromArgb(237, 244, 255), ForeColor = Blue,
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

        static TextBox WeightInput(int grams)
        {
            return new TextBox { Width = 57, MaxLength = 8, BackColor = Surface, ForeColor = Ink,
                BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Center,
                Font = new Font("Consolas", 14, FontStyle.Regular, GraphicsUnit.Pixel),
                Text = grams.ToString(CultureInfo.CurrentCulture) };
        }

        static bool TryReadWeight(TextBox input, out decimal value)
        {
            return decimal.TryParse(input.Text.Trim(), NumberStyles.AllowDecimalPoint,
                CultureInfo.CurrentCulture, out value) && value <= 10000;
        }

        static NumericUpDown Amount(decimal value, int places, decimal min, decimal max, decimal increment)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Value = value, DecimalPlaces = places,
                Increment = increment, Width = 78, ThousandsSeparator = false, BackColor = Surface, ForeColor = Ink };
        }
    }
}
