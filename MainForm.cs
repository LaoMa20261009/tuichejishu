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
        readonly Button loadButton = ActionButton("放上演示筐");
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
        volatile bool simulationLoaded;
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
            BackColor = Color.FromArgb(237, 242, 244);
            ForeColor = Color.FromArgb(24, 52, 62);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), RowCount = 2, ColumnCount = 1 };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 66));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(root);
            var top = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.Controls.Add(top, 0, 0);
            top.Controls.Add(CreateVideoPanel(), 0, 0);
            top.Controls.Add(CreateScalePanel(), 1, 0);
            root.Controls.Add(CreateRecordsPanel(), 0, 1);

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
            loadButton.Click += (s, e) =>
            {
                simulationLoaded = !simulationLoaded;
                loadButton.Text = simulationLoaded ? "移开演示筐" : "放上演示筐";
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
            panel.Controls.Add(Line(Note("FFmpeg"), ffmpeg, reset, Note("在视频上拖动划定识别区")), 0, 2);
            hints.SetToolTip(ffmpeg, "可填写 ffmpeg.exe 的完整路径，或将它加入 PATH。");
            preview.Dock = DockStyle.Fill;
            preview.BackColor = Color.FromArgb(19, 32, 40);
            preview.SizeMode = PictureBoxSizeMode.Zoom;
            preview.Cursor = Cursors.Cross;
            panel.Controls.Add(preview, 0, 3);
            panel.Controls.Add(Line(cameraStatus, ropeStatus), 0, 4);
            return panel;
        }

        Control CreateScalePanel()
        {
            var panel = Card(10);
            for (int i = 0; i < 9; i++) panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(Heading("称重"), 0, 0);
            panel.Controls.Add(Line(ports, demoScale), 0, 1);
            panel.Controls.Add(Line(protocol, scaleButton), 0, 2);
            weightValue.Font = new Font(Font.FontFamily, 31, FontStyle.Bold);
            weightValue.ForeColor = Color.FromArgb(0, 105, 105);
            weightValue.Margin = new Padding(3, 11, 3, 5);
            panel.Controls.Add(weightValue, 0, 3);
            panel.Controls.Add(Line(scaleStatus, loadButton), 0, 4);
            panel.Controls.Add(Line(Note("空秤上限"), emptyLimit, Note("稳定容差"), tolerance), 0, 5);
            panel.Controls.Add(Line(Note("绳色覆盖率 %"), coverage, automatic), 0, 6);
            panel.Controls.Add(Note("空秤上限和稳定容差使用秤当前单位"), 0, 7);
            panel.Controls.Add(captureStatus, 0, 8);
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
            records.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(241, 248, 247);
            records.Columns.Add("time", "记录时间");
            records.Columns.Add("colors", "绳子颜色");
            records.Columns.Add("weight", "净重");
            records.Columns.Add("unit", "单位");
            records.Columns.Add("source", "来源");
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
                Filter = "常见视频文件|*.mp4;*.mov;*.avi;*.mkv;*.wmv;*.webm;*.m4v;*.mpg;*.mpeg;*.mts;*.m2ts;*.ts|所有文件|*.*",
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
                CloseCamera("视频已断开");
                return;
            }
            try
            {
                string executable = ffmpeg.Text.Trim();
                if (cameraType.SelectedIndex != (int)CameraSource.Demo && executable.Length == 0)
                    throw new ArgumentException("请填写 FFmpeg 路径。");
                camera = new CameraSession((CameraSource)cameraType.SelectedIndex, address.Text.Trim(), executable,
                    () => simulationLoaded);
                cameraStarted = Stopwatch.GetTimestamp();
                cameraButton.Text = "断开视频";
                cameraStatus.Text = "等待首帧";
                UpdateCameraInputs();
                InvalidateAnalysis();
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
            ffmpeg.Enabled = camera == null && source != CameraSource.Demo;
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
                    simulationLoaded = false;
                }
                else if (demoScale.Checked)
                {
                    simulationOpen = true;
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
            loadButton.Visible = simulationOpen;
            loadButton.Text = simulationLoaded ? "移开演示筐" : "放上演示筐";
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

            WeightSample sample = simulationOpen ? new WeightSample(simulationLoaded ? 2.35 : 0, "kg", true, true, 3)
                : scale.IsOpen ? scale.Latest : null;
            if (sample != null && CaptureGate.Elapsed(now, sample.Tick) > 2.5) sample = null;
            weightValue.Text = sample == null || !sample.Valid ? "—" : sample.Number + " " + sample.Unit;
            scaleStatus.Text = sample == null ? (scale.IsOpen ? "等待有效称重数据" : "秤未连接") :
                !sample.Valid ? "称重数据无效" : sample.Stable ? "净重稳定" : "净重变化中";
            return sample;
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
                camera.Source == CameraSource.File ? "本地视频" : "视频已连接";
            if (analyzed != frame && (analyzed == null || CaptureGate.Elapsed(now, lastAnalysis) >= 0.18))
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
            bool sourceMatches = hasFrame && (simulationOpen ==
                (camera.Source == CameraSource.Demo || camera.Source == CameraSource.File));
            CaptureGate gate = simulationOpen ? demoGate : liveGate;
            double empty = (double)emptyLimit.Value;
            if (gate.Observe(now, weight, empty, (double)tolerance.Value, sourceMatches ? colors : "", frameTick,
                automatic.Checked) && CaptureGate.Elapsed(now, lastWriteAttempt) >= 2)
            {
                lastWriteAttempt = now;
                var record = new MeasurementRecord(DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture),
                    colors, weight.Number, weight.Unit, simulationOpen ? "演示" : "实测");
                try { history.Append(record); }
                catch (Exception)
                {
                    writeFailed = true;
                    captureStatus.Text = "写入历史失败，系统将重试";
                    return;
                }
                writeFailed = false;
                gate.MarkRecorded();
                AddRow(record);
            }

            if (!automatic.Checked) captureStatus.Text = "自动记录已暂停";
            else if (weight == null || !weight.Valid || !weight.Stable) captureStatus.Text = "等待稳定净重";
            else if (Math.Abs(weight.Value) <= empty) captureStatus.Text = "空秤，等待上筐";
            else if (!hasFrame) captureStatus.Text = "等待实时画面";
            else if (!sourceMatches) captureStatus.Text = "请搭配实测源，或搭配模拟秤与离线画面";
            else if (gate.HasRecorded) captureStatus.Text = "本筐已记录，移开后可测下一筐";
            else if (colors.Length == 0) captureStatus.Text = "等待识别绳色";
            else if (writeFailed) captureStatus.Text = "写入历史失败，等待重试";
            else captureStatus.Text = "正在确认绳色与净重";
        }

        void AddRow(MeasurementRecord record)
        {
            var time = DateTimeOffset.ParseExact(record.Time, "o", CultureInfo.InvariantCulture);
            records.Rows.Insert(0, time.ToString("yyyy-MM-dd HH:mm:ss zzz"), record.Colors, record.Weight, record.Unit, record.Source);
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
                Padding = new Padding(12), Margin = new Padding(4), BackColor = Color.White };
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
            return new Label { Text = text, AutoSize = true, Margin = new Padding(3, 7, 4, 5) };
        }

        static Label Heading(string text)
        {
            Label title = Note(text);
            title.Font = new Font("Microsoft YaHei UI", 12, FontStyle.Bold);
            return title;
        }

        static Button ActionButton(string text)
        {
            return new Button { Text = text, AutoSize = true, Height = 29, BackColor = Color.FromArgb(221, 239, 237),
                FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false };
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
