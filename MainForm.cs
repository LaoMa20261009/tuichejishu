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
        static readonly int[] DemoCounts = { 0, 1, 2, 4, 3 };

        readonly PictureBox preview = new PictureBox();
        readonly ComboBox cameraType = Choice("网络视频", "USB 摄像头", "本地视频", "演示画面");
        readonly TextBox address = new TextBox { Width = 290 };
        readonly TextBox ffmpeg = new TextBox { Width = 112, Text = "ffmpeg.exe" };
        readonly Button browseVideo = ActionButton("选择视频");
        readonly Button cameraButton = ActionButton("连接视频");
        readonly Label cameraStatus = Note("视频未连接");
        readonly Label ropeStatus = Note("绳色  —");
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
        readonly NumericUpDown emptyLimit = Amount(0.02M, 3, 0, 100000, 0.01M);
        readonly NumericUpDown tolerance = Amount(0.01M, 3, 0, 100000, 0.01M);
        readonly NumericUpDown coverage = Amount(0.12M, 2, 0.01M, 10, 0.01M);
        readonly DataGridView records = new DataGridView();
        readonly Timer timer = new Timer { Interval = 100 };
        readonly ToolTip hints = new ToolTip();
        readonly Os2Scale scale = new Os2Scale();
        readonly History history = new History(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CrabWeighing", "measurements.tsv"));
        readonly CaptureGate liveGate = new CaptureGate();
        readonly CaptureGate demoGate = new CaptureGate();
        CameraSession camera;
        VideoFrame displayed;
        VideoFrame analyzed;
        Rectangle area = new Rectangle(0, 0, VideoFrame.Width, VideoFrame.Height);
        Point? selectionStart;
        Rectangle selecting;
        string colors = "";
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
            ClientSize = new Size(1190, 795);
            MinimumSize = new Size(990, 690);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = Color.FromArgb(236, 242, 249);
            ForeColor = Ink;
            cameraType.SelectedIndex = (int)CameraSource.File;
            cameraType.Width = 112;
            address.Width = 245;
            cameraButton.BackColor = scaleButton.BackColor = Blue;
            cameraButton.ForeColor = scaleButton.ForeColor = Color.White;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18, 14, 18, 14), RowCount = 3, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 67));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(root);
            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var banner = new Panel { Dock = DockStyle.Fill, BackColor = Ink, Margin = new Padding(5, 0, 5, 8) };
            var title = Heading("螃蟹称重与绳色识别");
            title.Font = new Font(Font.FontFamily, 17, FontStyle.Bold);
            title.Location = new Point(17, 12);
            title.ForeColor = Color.White;
            banner.Controls.Add(title);
            root.Controls.Add(banner, 0, 0);
            root.Controls.Add(top, 0, 1);
            top.Controls.Add(CreateVideoPanel(), 0, 0);
            top.Controls.Add(CreateScalePanel(), 1, 0);
            root.Controls.Add(CreateRecordsPanel(), 0, 2);

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
                simulatedScale.SetCount(simulationCount);
            };
            cycleLoads.CheckedChanged += (s, e) =>
            {
                if (!cycleLoads.Checked) return;
                demoStep = 0;
                SetDemoCount(0);
                nextDemoStep = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
            };
            coverage.ValueChanged += (s, e) => InvalidateAnalysis();
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
            try { foreach (MeasurementRecord record in history.Load()) AddRow(record); }
            catch (Exception ex) { captureStatus.Text = "读取历史失败。 " + ex.Message; }
            timer.Start();
        }

        Control CreateVideoPanel()
        {
            var panel = Card(5);
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.Controls.Add(Heading("实时视频"), 0, 0);
            panel.Controls.Add(Line(cameraType, address, browseVideo, cameraButton), 0, 1);
            var reset = ActionButton("全画面");
            reset.Click += (s, e) =>
            {
                area = new Rectangle(0, 0, VideoFrame.Width, VideoFrame.Height);
                InvalidateAnalysis();
                preview.Invalidate();
            };
            decoderControls = Line(Note("相机解码器"), ffmpeg);
            panel.Controls.Add(decoderControls, 0, 2);
            hints.SetToolTip(ffmpeg, "可填写 ffmpeg.exe 的完整路径，或将它加入 PATH。");
            preview.Dock = DockStyle.Fill;
            preview.BackColor = Color.FromArgb(19, 32, 40);
            preview.SizeMode = PictureBoxSizeMode.Zoom;
            preview.Cursor = Cursors.Cross;
            panel.Controls.Add(preview, 0, 3);
            StyleStatus(ropeStatus);
            panel.Controls.Add(Line(cameraStatus, ropeStatus, reset, Note("拖动框选识别区")), 0, 4);
            preview.Paint += (sender, e) =>
            {
                if (preview.Image == null)
                    TextRenderer.DrawText(e.Graphics, "选择视频源并连接", Font, preview.ClientRectangle,
                        Color.FromArgb(173, 194, 221), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            return panel;
        }

        Control CreateScalePanel()
        {
            var panel = Card(11);
            for (int i = 0; i < 10; i++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(Heading("称重"), 0, 0);
            panel.Controls.Add(Line(ports, demoScale), 0, 1);
            panel.Controls.Add(Line(protocol, scaleButton), 0, 2);
            weightValue.Font = new Font("Segoe UI", 39, FontStyle.Bold);
            weightValue.ForeColor = Blue;
            weightValue.Margin = new Padding(3, 11, 3, 5);
            panel.Controls.Add(weightValue, 0, 3);
            StyleStatus(scaleStatus);
            simulationControls = Line(Note("筐内只数"), crabCount, cycleLoads);
            panel.Controls.Add(scaleStatus, 0, 4);
            panel.Controls.Add(simulationControls, 0, 5);
            panel.Controls.Add(Line(Note("空秤上限"), emptyLimit, Note("稳定容差"), tolerance), 0, 6);
            panel.Controls.Add(Line(Note("绳色覆盖率 %"), coverage, automatic), 0, 7);
            panel.Controls.Add(Note("空秤上限和稳定容差使用秤当前单位"), 0, 8);
            StyleStatus(captureStatus);
            panel.Controls.Add(captureStatus, 0, 9);
            return panel;
        }

        Control CreateRecordsPanel()
        {
            var panel = Card(2);
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var folder = ActionButton("打开记录目录");
            folder.Click += (s, e) =>
            {
                try
                {
                    string directory = Path.GetDirectoryName(history.FilePath);
                    Directory.CreateDirectory(directory);
                    Process.Start(directory);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "打开记录目录失败"); }
            };
            panel.Controls.Add(Line(Heading("测量历史"), folder), 0, 0);
            records.Dock = DockStyle.Fill;
            records.ReadOnly = true;
            records.AllowUserToAddRows = false;
            records.AllowUserToDeleteRows = false;
            records.RowHeadersVisible = false;
            records.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            records.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            records.BackgroundColor = Color.White;
            records.BorderStyle = BorderStyle.None;
            records.EnableHeadersVisualStyles = false;
            records.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            records.ColumnHeadersDefaultCellStyle.BackColor = Ink;
            records.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            records.ColumnHeadersDefaultCellStyle.SelectionBackColor = Ink;
            records.ColumnHeadersHeight = 34;
            records.RowTemplate.Height = 30;
            records.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            records.GridColor = Color.FromArgb(230, 236, 245);
            records.DefaultCellStyle.ForeColor = Ink;
            records.DefaultCellStyle.SelectionBackColor = PaleBlue;
            records.DefaultCellStyle.SelectionForeColor = Ink;
            records.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(245, 248, 253);
            records.Columns.Add("time", "记录时间");
            records.Columns.Add("colors", "绳子颜色");
            records.Columns.Add("weight", "净重");
            records.Columns.Add("unit", "单位");
            records.Columns.Add("source", "来源");
            records.Columns.Add("position", "视频位置");
            records.Columns[0].FillWeight = 156;
            records.Columns[1].FillWeight = 130;
            records.Columns[3].FillWeight = 55;
            foreach (DataGridViewColumn column in records.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            panel.Controls.Add(records, 0, 1);
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
            ReplaceImage(null);
            InvalidateAnalysis();
            cameraStatus.Text = status;
            ropeStatus.Text = "绳色  —";
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
                now = Stopwatch.GetTimestamp();
                long frameTick = RefreshVideo(now);
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
                nextDemoStep = now + 4 * Stopwatch.Frequency;
            }
            WeightSample sample = simulationOpen ? simulatedScale.Read(now)
                : scale.IsOpen ? scale.Latest : null;
            if (sample != null && CaptureGate.Elapsed(now, sample.Tick) > 2.5) sample = null;
            weightValue.Text = sample == null || !sample.Valid ? "—" : sample.Number + " " + sample.Unit;
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
                cameraStatus.Text = "视频已播完";
                cameraButton.Text = "重播视频";
                return 0;
            }
            if (frame == null || CaptureGate.Elapsed(now, frame.Tick) > 2.5)
            {
                if (displayed != null)
                {
                    displayed = null;
                    ReplaceImage(null);
                    InvalidateAnalysis();
                }
                cameraStatus.Text = CaptureGate.Elapsed(now, cameraStarted) > 8 ? "未收到新画面，请检查视频源" : "等待首帧";
                ropeStatus.Text = "绳色  —";
                return 0;
            }
            if (displayed != frame)
            {
                displayed = frame;
                ReplaceImage(frame.ToBitmap());
            }
            cameraStatus.Text = camera.Source == CameraSource.Demo ? "演示画面" :
                camera.Source == CameraSource.File ? "本地视频  " + FormatPosition(frame.Position) : "视频已连接";
            if (analyzed != frame && (analyzed == null || CaptureGate.Elapsed(now, lastAnalysis) >= 0.28))
            {
                analyzed = frame;
                lastAnalysis = now;
                colors = RopeColor.Detect(frame, area, (double)coverage.Value);
                ropeStatus.Text = "绳色  " + (colors.Length == 0 ? "未识别" : colors);
            }
            return analyzed == null ? 0 : analyzed.Tick;
        }

        void RefreshRecording(long now, WeightSample weight, long frameTick)
        {
            bool hasFrame = camera != null && frameTick != 0 && CaptureGate.Elapsed(now, frameTick) <= 0.65;
            bool local = camera != null && camera.Source == CameraSource.File;
            bool sourceMatches = hasFrame && (local || simulationOpen == (camera.Source == CameraSource.Demo));
            bool allowLoadChanges = simulationOpen || local;
            double changeTolerance = (double)tolerance.Value;
            CaptureGate gate = simulationOpen ? demoGate : liveGate;
            double empty = (double)emptyLimit.Value;
            if (gate.Observe(now, weight, empty, changeTolerance, sourceMatches ? colors : "", frameTick,
                automatic.Checked, allowLoadChanges, local) && CaptureGate.Elapsed(now, lastWriteAttempt) >= 2)
            {
                lastWriteAttempt = now;
                var record = new MeasurementRecord(DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                    colors, weight.Number, weight.Unit, local ? (simulationOpen ? "本地视频 · 模拟秤" : "本地视频 · 实秤")
                        : simulationOpen ? "演示" : "实测", local ? FormatPosition(analyzed.Position) : "");
                try { history.Append(record); }
                catch (Exception)
                {
                    writeFailed = true;
                    captureStatus.Text = "写入历史失败，系统将重试";
                    return;
                }
                writeFailed = false;
                gate.MarkRecorded(weight, colors);
                AddRow(record);
            }

            if (!automatic.Checked) captureStatus.Text = "自动记录已暂停";
            else if (weight == null || !weight.Valid || !weight.Stable) captureStatus.Text = "等待稳定净重";
            else if (Math.Abs(weight.Value) <= empty) captureStatus.Text = "空秤，等待上筐";
            else if (!hasFrame) captureStatus.Text = camera != null && camera.Ended ? "视频已播完" : "等待视频画面";
            else if (!sourceMatches) captureStatus.Text = "演示画面请搭配模拟秤";
            else if (gate.IsHolding(weight, changeTolerance, allowLoadChanges, colors, local)) captureStatus.Text = allowLoadChanges
                ? (local ? "已记录，等待绳色或重量变化" : "已记录，等待下一次重量变化")
                : "已记录，移筐后可继续";
            else if (colors.Length == 0) captureStatus.Text = "等待识别绳色";
            else if (writeFailed) captureStatus.Text = "写入历史失败，等待重试";
            else captureStatus.Text = "正在确认绳色与净重";
        }

        static string FormatPosition(TimeSpan? position)
        {
            if (!position.HasValue) return "";
            TimeSpan value = position.Value;
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}.{3:0}",
                (int)value.TotalHours, value.Minutes, value.Seconds, value.Milliseconds / 100);
        }

        void AddRow(MeasurementRecord record)
        {
            var time = DateTimeOffset.ParseExact(record.Time, "o", CultureInfo.InvariantCulture);
            records.Rows.Insert(0, time.ToString("yyyy-MM-dd HH:mm:ss zzz"), record.Colors, record.Weight, record.Unit, record.Source, record.Position);
        }

        void ReplaceImage(Image next)
        {
            Image previous = preview.Image;
            preview.Image = next;
            if (previous != null) previous.Dispose();
        }

        Rectangle VideoBounds()
        {
            double scale = Math.Min(preview.Width / (double)VideoFrame.Width, preview.Height / (double)VideoFrame.Height);
            int width = (int)(VideoFrame.Width * scale), height = (int)(VideoFrame.Height * scale);
            return new Rectangle((preview.Width - width) / 2, (preview.Height - height) / 2, width, height);
        }

        Point ToFrame(Point point)
        {
            Rectangle bounds = VideoBounds();
            return new Point(Math.Max(0, Math.Min(VideoFrame.Width, (point.X - bounds.X) * VideoFrame.Width / bounds.Width)),
                Math.Max(0, Math.Min(VideoFrame.Height, (point.Y - bounds.Y) * VideoFrame.Height / bounds.Height)));
        }

        void BeginSelection(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || displayed == null || !VideoBounds().Contains(e.Location)) return;
            selectionStart = ToFrame(e.Location);
            selecting = Rectangle.Empty;
            preview.Capture = true;
        }

        void MoveSelection(object sender, MouseEventArgs e)
        {
            if (!selectionStart.HasValue || VideoBounds().IsEmpty) return;
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
            Rectangle box = selectionStart.HasValue ? selecting : area;
            if (box.IsEmpty || box.Size == new Size(VideoFrame.Width, VideoFrame.Height)) return;
            Rectangle bounds = VideoBounds();
            using (var pen = new Pen(Color.Aquamarine, 2))
            {
                e.Graphics.DrawRectangle(pen, bounds.X + box.X * bounds.Width / VideoFrame.Width,
                    bounds.Y + box.Y * bounds.Height / VideoFrame.Height, box.Width * bounds.Width / VideoFrame.Width,
                    box.Height * bounds.Height / VideoFrame.Height);
            }
        }

        void ResetCandidates()
        {
            demoGate.ResetCandidate();
            liveGate.ResetCandidate();
        }

        void InvalidateAnalysis()
        {
            analyzed = null;
            colors = "";
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
                Padding = new Padding(15, 10, 15, 10), Margin = new Padding(5), BackColor = Color.White };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return panel;
        }

        static FlowLayoutPanel Line(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty };
            row.Controls.AddRange(controls);
            return row;
        }

        static Label Note(string text)
        {
            return new Label { Text = text, ForeColor = Muted, AutoSize = true, Margin = new Padding(3, 7, 4, 5) };
        }

        static Label Heading(string text)
        {
            Label title = Note(text);
            title.Font = new Font("Microsoft YaHei UI", 13, FontStyle.Bold);
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
            var button = new Button { Text = text, AutoSize = true, Height = 32,
                Padding = new Padding(8, 0, 8, 0), BackColor = PaleBlue, ForeColor = Blue,
                FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, Cursor = Cursors.Hand };
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
