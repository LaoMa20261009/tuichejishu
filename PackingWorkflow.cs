using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CrabWeighing
{
    sealed partial class MainForm
    {
        readonly Panel labelPage = new Panel(), packingPage = new Panel();
        readonly Button backToLabels = ActionButton("↶ 返回分选识别");
        readonly CheckBox[] ruleEnabled = new CheckBox[Basket.Colors.Length];
        readonly Button labelStep = ActionButton("1. 标签文字 OCR"), packingStep = ActionButton("2. 装箱核对");
        readonly TextBox taskNumber = new TextBox { Width = 186, MaxLength = 60, BorderStyle = BorderStyle.FixedSingle };
        readonly TextBox rawTranscript = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            WordWrap = true, BorderStyle = BorderStyle.None };
        readonly Label recognitionStatus = Note("等待上传照片或抓拍");
        readonly Label weightState = Note("电子秤未连接"), packingWeightState = Note("电子秤未连接");
        readonly Label packingWeight = Note("—"), packingTask = Note("任务单号  —");
        readonly Label ledgerTitle = Heading("装箱流水记录"), ledgerCount = Note("");
        readonly Label bannerProgress = Heading("当前整箱总进度  0 / 0 只");
        readonly Button finishPacking = ActionButton("完成装箱");
        readonly FlowLayoutPanel gradeCards = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        readonly ProgressTrack totalProgress = new ProgressTrack();
        readonly Label completionRate = Heading("0%");
        readonly Label cameraBadge = Note("工业相机  未连接");
        readonly FlowLayoutPanel ocrCards = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        readonly Button toggleTranscript = ActionButton("查看原文");
        readonly DataGridView specifications = new DataGridView();
        readonly PictureBox labelImage = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(18, 30, 44) };
        readonly CheckBox autoCapture = new CheckBox { Text = "自动抓拍", AutoSize = true, Margin = new Padding(3, 9, 5, 3) };
        readonly Button captureLabels = ActionButton("抓拍并识别  空格");
        readonly Button confirmLabels = ActionButton("确认标签与规格 · 开始装箱  Enter");
        readonly Button photoUpload = ActionButton("上传图片文字识别");
        readonly Button pickCamera = ActionButton("实时视频");
        Panel settingsContent;
        LabelPhotoResult shownOcr;
        string importedPhoto, captureFile, enteredBy;
        readonly List<string> draftAudit = new List<string>();
        bool packingActive;
        int shownGrade = -1;
        long nextCapture;

        Control CreateBanner()
        {
            var bar = new Panel { BackColor = PanelColor };
            var icon = new Label { Text = "秤", TextAlign = ContentAlignment.MiddleCenter, BackColor = Blue, ForeColor = Color.White,
                Font = new Font(Font.FontFamily, 20, FontStyle.Bold, GraphicsUnit.Pixel) };
            var title = Heading("螃蟹装箱计数系统");
            title.Font = new Font(Font.FontFamily, 24, FontStyle.Bold, GraphicsUnit.Pixel);
            var subtitle = Note("工业上位机装箱控制台");
            subtitle.Font = new Font(Font.FontFamily, 12, FontStyle.Regular, GraphicsUnit.Pixel);
            At(bar, icon, 20, 12, 39, 36);
            At(bar, title, 73, 7, 245, 26);
            At(bar, subtitle, 74, 36, 221, 17);
            deviceStatus.AutoSize = false;
            deviceStatus.BackColor = Color.FromArgb(237, 244, 255);
            deviceStatus.TextAlign = ContentAlignment.MiddleCenter;
            SetBadge(deviceStatus, Color.FromArgb(237, 244, 255), Blue);
            SetBadge(cameraBadge, Color.FromArgb(237, 244, 255), Blue);
            At(bar, deviceStatus, 468, 13, 145, 33);
            At(bar, cameraBadge, 623, 13, 166, 33);
            labelStep.Click += (s, e) => ShowPackingPage(false);
            packingStep.Click += (s, e) => { if (order != null) ShowPackingPage(true); else BeginPacking(); };
            At(bar, labelStep, 798, 10, 149, 39);
            At(bar, packingStep, 953, 10, 149, 39);
            backToLabels.Click += (s, e) => ShowPackingPage(false);
            At(bar, backToLabels, 944, 10, 158, 39);
            var settings = ActionButton("⚙");
            settings.AccessibleName = "系统配置";
            settings.Click += (s, e) => OpenSettings();
            var minimize = ActionButton("—");
            var close = ActionButton("×");
            minimize.AccessibleName = "最小化";
            close.AccessibleName = "关闭";
            minimize.Click += (s, e) => WindowState = FormWindowState.Minimized;
            close.Click += (s, e) => Close();
            At(bar, settings, 1115, 10, 45, 39);
            At(bar, minimize, 1166, 10, 42, 39);
            At(bar, close, 1213, 10, 45, 39);
            foreach (Control surface in new Control[] { bar, icon, title, subtitle, deviceStatus, cameraBadge }) EnableWindowDrag(surface);
            return bar;
        }

        static void At(Control parent, Control control, int x, int y, int width, int height)
        {
            var button = control as Button;
            if (button != null) button.AutoSize = false;
            var label = control as Label;
            if (label != null) label.AutoSize = false;
            control.Bounds = new Rectangle(x, y, width, height);
            parent.Controls.Add(control);
        }

        static Panel Module()
        {
            var panel = new Panel { BackColor = PanelColor };
            panel.Paint += (sender, e) => ConsolePaint.Tile(e.Graphics, panel.ClientRectangle, ConsolePaint.Pixels(panel, 8), PanelColor, Border);
            return panel;
        }

        static void SetBadge(Label label, Color background, Color foreground)
        {
            label.BackColor = background;
            label.ForeColor = foreground;
            label.TextAlign = ContentAlignment.MiddleCenter;
        }

        static void SetPrimary(Button button, Color background)
        {
            button.BackColor = background;
            button.ForeColor = Color.White;
        }

        Control CreateLabelPage()
        {
            var source = Module();
            pickCamera.Click += (s, e) => ShowLivePreview();
            photoUpload.Click += async (s, e) => await ChooseLabelPhoto();
            At(source, pickCamera, 12, 6, 125, 37);
            At(source, photoUpload, 144, 6, 165, 37);
            At(source, autoCapture, 320, 8, 110, 30);
            SetBadge(recognitionStatus, Color.FromArgb(234, 245, 251), Blue);
            At(source, recognitionStatus, 465, 9, 253, 31);
            At(labelPage, source, 0, 0, 730, 49);

            preview.BackColor = Color.FromArgb(16, 30, 45);
            preview.Cursor = Cursors.Cross;
            preview.AllowDrop = labelImage.AllowDrop = true;
            preview.DragEnter += PhotoDragEnter;
            labelImage.DragEnter += PhotoDragEnter;
            preview.DragDrop += PhotoDragDrop;
            labelImage.DragDrop += PhotoDragDrop;
            labelImage.Paint += DrawLabelHighlights;
            At(labelPage, preview, 0, 60, 730, 656);
            At(labelPage, labelImage, 0, 60, 730, 656);
            labelImage.Visible = false;

            var videoActions = new Panel { BackColor = Color.FromArgb(17, 30, 43) };
            var note = Note("框选秤上的箱子区域，或抓拍标签放大核对");
            note.ForeColor = Color.FromArgb(222, 234, 244);
            At(videoActions, note, 12, 14, 432, 26);
            SetPrimary(captureLabels, Green);
            captureLabels.Click += async (s, e) => await CaptureLabelFrame();
            At(videoActions, captureLabels, 454, 6, 245, 40);
            At(labelPage, videoActions, 10, 654, 710, 52);
            videoActions.BringToFront();

            At(labelPage, BuildWeightModule(false), 742, 0, 510, 155);
            var raw = Module();
            At(raw, Heading("原始标签 OCR 识别内容"), 14, 10, 285, 25);
            toggleTranscript.Click += (s, e) => ShowOcrTranscript(!rawTranscript.Visible);
            At(raw, toggleTranscript, 340, 7, 80, 30);
            var copy = ActionButton("复制");
            copy.Click += (s, e) =>
            {
                if (rawTranscript.TextLength > 0)
                    try { Clipboard.SetText(rawTranscript.Text); }
                    catch (System.Runtime.InteropServices.ExternalException) { recognitionStatus.Text = "剪贴板占用中，请重试"; }
            };
            At(raw, copy, 429, 7, 65, 30);
            rawTranscript.BackColor = Surface;
            rawTranscript.ForeColor = Ink;
            rawTranscript.Font = new Font(Font.FontFamily, 14, FontStyle.Regular, GraphicsUnit.Pixel);
            At(raw, rawTranscript, 13, 44, 484, 112);
            ocrCards.BackColor = PanelColor;
            At(raw, ocrCards, 13, 44, 484, 112);
            ShowOcrTranscript(true);
            rawTranscript.Text = "上传标签照片，或从视频抓拍后查看识别结果。";
            At(labelPage, raw, 742, 165, 510, 168);

            var parsed = Module();
            At(parsed, Heading("标签解析规格列表"), 12, 10, 270, 25);
            At(parsed, Note("启用前可编辑"), 344, 12, 146, 24);
            ConfigureSpecifications();
            At(parsed, specifications, 10, 45, 489, 233);
            var add = ActionButton("＋ 手动添加规格");
            add.Click += (s, e) => StartManualGrade();
            var reset = ActionButton("重置解析数据");
            reset.Click += (s, e) => ClearDraft();
            At(parsed, add, 11, 284, 158, 38);
            At(parsed, reset, 358, 284, 139, 38);
            At(labelPage, parsed, 742, 345, 510, 328);
            SetPrimary(confirmLabels, Green);
            confirmLabels.Click += (s, e) => BeginPacking();
            At(labelPage, confirmLabels, 742, 679, 510, 37);
            return labelPage;
        }

        Control BuildWeightModule(bool compact)
        {
            var module = Module();
            var value = compact ? packingWeight : weightValue;
            var status = compact ? packingWeightState : weightState;
            value.Font = new Font("Consolas", compact ? 37 : 44, FontStyle.Bold, GraphicsUnit.Pixel);
            value.ForeColor = compact ? Blue : Ink;
            var zero = ActionButton("置零");
            var tareControl = compact ? ActionButton("去皮") : tareButton;
            zero.Click += (s, e) => AdjustScaleZero(false);
            if (compact) tareControl.Click += (s, e) => AdjustScaleZero(true);
            if (compact)
            {
                SetBadge(packingTask, Color.FromArgb(237, 244, 255), Blue);
                packingTask.TextAlign = ContentAlignment.MiddleLeft;
                packingTask.Padding = new Padding(12, 0, 8, 0);
                packingTask.Font = new Font(Font.FontFamily, 17, FontStyle.Bold, GraphicsUnit.Pixel);
                At(module, packingTask, 19, 14, 474, 43);
                At(module, Heading("实时称重"), 612, 15, 112, 33);
                At(module, status, 750, 17, 122, 28);
                At(module, value, 849, 11, 156, 51);
                At(module, Note("g"), 1008, 19, 31, 36);
                At(module, zero, 1046, 12, 87, 46);
                At(module, tareControl, 1142, 12, 87, 46);
            }
            else
            {
                At(module, Heading("实时电子秤数据"), 12, 10, 314, 27);
                var setup = ActionButton("系统配置");
                setup.Click += (s, e) => OpenSettings();
                At(module, setup, 396, 6, 102, 34);
                var readout = new Panel { BackColor = Color.FromArgb(240, 246, 255) };
                readout.Paint += (s, e) => ConsolePaint.Tile(e.Graphics, readout.ClientRectangle,
                    ConsolePaint.Pixels(readout, 7), readout.BackColor, Color.FromArgb(197, 218, 241));
                var netWeight = Note("实时净重");
                netWeight.ForeColor = Blue;
                At(readout, netWeight, 13, 6, 121, 22);
                value.TextAlign = ContentAlignment.MiddleRight;
                At(readout, value, 14, 27, 200, 48);
                var unit = Note("g");
                SetBadge(unit, Color.White, Blue);
                At(readout, unit, 225, 41, 32, 26);
                At(readout, status, 14, 75, 243, 22);
                At(module, readout, 12, 46, 280, 100);
                At(module, zero, 307, 87, 88, 45);
                At(module, tareControl, 406, 87, 88, 45);
            }
            return module;
        }

        Control CreatePackingPage()
        {
            At(packingPage, BuildWeightModule(true), 0, 0, 1252, 71);
            var deck = Module();
            var left = new Panel { BackColor = Color.FromArgb(245, 248, 253) };
            At(left, Heading("装箱规格步骤选择"), 16, 13, 390, 29);
            gradeCards.Margin = Padding.Empty;
            gradeCards.BackColor = left.BackColor;
            At(left, gradeCards, 10, 55, 446, 474);
            var guide = Note("按规格放置螃蟹，点击卡片查看对应流水");
            SetBadge(guide, Color.FromArgb(233, 241, 253), Blue);
            At(left, guide, 16, 536, 419, 30);
            At(deck, left, 1, 1, 464, 572);
            At(deck, new Panel { BackColor = Border }, 465, 1, 1, 572);

            var right = new Panel { BackColor = PanelColor };
            At(right, ledgerTitle, 13, 11, 481, 29);
            SetBadge(ledgerCount, Color.FromArgb(231, 242, 255), Blue);
            At(right, ledgerCount, 516, 13, 237, 25);
            At(right, captureStatus, 14, 42, 739, 25);
            StyleGrid(ledger, 43);
            ledger.ScrollBars = ScrollBars.Vertical;
            ledger.BackgroundColor = PanelColor;
            string[] names = { "计数序号", "记录时间", "操作动作", "变动只数", "重量变动", "累计重量" };
            for (int i = 0; i < names.Length; i++)
                ledger.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = names[i],
                    FillWeight = new[] { 116, 90, 128, 95, 120, 122 }[i], SortMode = DataGridViewColumnSortMode.NotSortable });
            ledger.CellPainting += PaintLedgerCell;
            ledger.CellDoubleClick += (s, e) => OpenMoveEvidence(e.RowIndex);
            ledger.Dock = DockStyle.None;
            At(right, ledger, 12, 73, 758, 490);
            At(deck, right, 467, 1, 783, 572);
            At(packingPage, deck, 0, 85, 1252, 574);

            var footer = Module();
            At(footer, bannerProgress, 16, 4, 255, 24);
            completionRate.ForeColor = Green;
            At(footer, completionRate, 274, 4, 100, 24);
            At(footer, totalProgress, 16, 33, 362, 9);
            SetPrimary(finishPacking, Green);
            finishPacking.Click += (s, e) => CompletePacking();
            At(footer, finishPacking, 1044, 6, 192, 42);
            At(packingPage, footer, 0, 667, 1252, 49);
            return packingPage;
        }

        void ConfigureSpecifications()
        {
            StyleGrid(specifications, 40);
            specifications.BackgroundColor = PanelColor;
            specifications.ReadOnly = false;
            specifications.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            specifications.SelectionMode = DataGridViewSelectionMode.CellSelect;
            specifications.ScrollBars = ScrollBars.Vertical;
            var name = new DataGridViewTextBoxColumn { HeaderText = "规格名称", FillWeight = 144, MaxInputLength = 35 };
            var low = new DataGridViewTextBoxColumn { HeaderText = "起 (g)", FillWeight = 57, MaxInputLength = 8 };
            var high = new DataGridViewTextBoxColumn { HeaderText = "止 (g)", FillWeight = 57, MaxInputLength = 8 };
            var color = new DataGridViewComboBoxColumn { HeaderText = "绳色", FillWeight = 72, FlatStyle = FlatStyle.Flat };
            color.Items.AddRange(Basket.Colors.Cast<object>().ToArray());
            var count = new DataGridViewTextBoxColumn { HeaderText = "数量", FillWeight = 62, MaxInputLength = 5 };
            var delete = new DataGridViewButtonColumn { HeaderText = "操作", FillWeight = 62, Text = "删除",
                UseColumnTextForButtonValue = true, FlatStyle = FlatStyle.Flat };
            specifications.Columns.AddRange(name, low, high, color, count, delete);
            foreach (DataGridViewColumn column in specifications.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            specifications.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && e.ColumnIndex < 5) specifications.BeginEdit(true);
            };
            specifications.CellContentClick += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == 5) specifications.Rows.RemoveAt(e.RowIndex);
            };
            specifications.CellPainting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 ||
                    (specifications.IsCurrentCellInEditMode && specifications.CurrentCellAddress == new Point(e.ColumnIndex, e.RowIndex))) return;
                e.PaintBackground(e.ClipBounds, true);
                int rope = e.ColumnIndex == 3 ? Array.IndexOf(Basket.Colors, Convert.ToString(e.Value)) : -1;
                bool remove = e.ColumnIndex == 5;
                if (rope >= 0 || remove)
                {
                    int inset = ConsolePaint.Pixels(specifications, 6);
                    Rectangle chip = Rectangle.Inflate(e.CellBounds, -inset, -inset);
                    Color fill = remove ? PanelColor : RopeFill[rope];
                    ConsolePaint.Tile(e.Graphics, chip, ConsolePaint.Pixels(specifications, 4), fill, remove ? Border : fill);
                }
                TextRenderer.DrawText(e.Graphics, remove ? "删除" : Convert.ToString(e.Value), e.CellStyle.Font ?? Font, e.CellBounds,
                    remove ? Muted : rope >= 0 ? RopeInk[rope] : Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                e.Paint(e.ClipBounds, DataGridViewPaintParts.Border | DataGridViewPaintParts.Focus);
                e.Handled = true;
            };
            specifications.Dock = DockStyle.None;
        }

        void PhotoDragEnter(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (!importingLabel && files != null && files.Length > 0 && IsPhotoFile(files[0])) e.Effect = DragDropEffects.Copy;
        }

        async void PhotoDragDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0 && IsPhotoFile(files[0])) await ReadLabelPhoto(files[0], false);
        }

        static bool IsPhotoFile(string path)
        {
            return new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" }.Contains(Path.GetExtension(path).ToLowerInvariant());
        }

        async Task ChooseLabelPhoto()
        {
            if (importingLabel) return;
            using (var file = new OpenFileDialog { Title = "上传泡沫箱标签照片", Filter = "图片|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff",
                CheckFileExists = true, RestoreDirectory = true })
                if (file.ShowDialog(this) == DialogResult.OK) await ReadLabelPhoto(file.FileName, false);
        }

        void ShowLivePreview()
        {
            labelImage.Visible = false;
            preview.Visible = true;
            if (camera != null) return;
            if (cameraType.SelectedIndex == (int)CameraSource.File && address.Text.Length == 0) SelectVideoFile();
            if (cameraType.SelectedIndex != (int)CameraSource.File || address.Text.Length > 0) ToggleCamera();
        }

        async Task CaptureLabelFrame()
        {
            if (importingLabel) return;
            VideoFrame frame = displayed;
            if (frame == null)
            {
                recognitionStatus.Text = "请先连接视频或上传图片";
                return;
            }
            string filename = Path.Combine(Path.GetTempPath(), "crab-capture-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                using (Bitmap image = frame.ToBitmap()) image.Save(filename, System.Drawing.Imaging.ImageFormat.Png);
                await ReadLabelPhoto(filename, true);
            }
            catch (Exception ex) { recognitionStatus.Text = "抓拍失败：" + ex.Message; }
            finally { if (captureFile != filename && File.Exists(filename)) File.Delete(filename); }
        }

        async Task ReadLabelPhoto(string file, bool captured)
        {
            if (importingLabel) return;
            importingLabel = true;
            captureLabels.Enabled = photoUpload.Enabled = confirmLabels.Enabled = false;
            recognitionStatus.Text = "正在识别整图和标签分区…";
            try
            {
                using (Image photo = Image.FromFile(file))
                {
                    ReleaseOcrPhoto();
                    var image = new Bitmap(photo);
                    if (photo.PropertyIdList.Contains(0x0112))
                    {
                        byte[] orientation = photo.GetPropertyItem(0x0112).Value;
                        if (orientation.Length > 0 && orientation[0] == 3) image.RotateFlip(RotateFlipType.Rotate180FlipNone);
                        else if (orientation.Length > 0 && orientation[0] == 6) image.RotateFlip(RotateFlipType.Rotate90FlipNone);
                        else if (orientation.Length > 0 && orientation[0] == 8) image.RotateFlip(RotateFlipType.Rotate270FlipNone);
                    }
                    labelImage.Image = image;
                }
                captureFile = captured ? file : null;
                importedPhoto = file;
                preview.Visible = false;
                labelImage.Visible = true;
                shownOcr = null;
                specifications.Rows.Clear();
                draftAudit.Clear();
                ClearOcrCards();
                ShowOcrTranscript(true);
                rawTranscript.Clear();
                LabelPhotoResult result = await LabelPhoto.ReadAsync(file);
                if (closing || IsDisposed) return;
                ApplyLabelResult(result);
            }
            catch (Exception ex) { if (!closing) rawTranscript.Text = "识别失败：" + ex.Message; }
            finally
            {
                importingLabel = false;
                if (!closing) captureLabels.Enabled = photoUpload.Enabled = confirmLabels.Enabled = true;
            }
        }

        void ApplyLabelResult(LabelPhotoResult result)
        {
            shownOcr = result;
            ClearOcrCards();
            foreach (LabelSpec spec in result.Specs)
            {
                Label card = Note(Basket.Colors[spec.Color] + "      " + spec.Minimum + "–" + spec.Maximum + " g/只      " + spec.Quantity + " 只");
                card.AutoSize = false;
                card.Size = new Size(ocrCards.ClientSize.Width - ConsolePaint.Pixels(this, 23), ConsolePaint.Pixels(this, 32));
                card.Margin = new Padding(0, 0, 0, ConsolePaint.Pixels(this, 4));
                card.Padding = new Padding(ConsolePaint.Pixels(this, 12), 0, 0, 0);
                card.BackColor = RopeFill[spec.Color];
                card.ForeColor = RopeInk[spec.Color];
                ocrCards.Controls.Add(card);
            }
            ShowOcrTranscript(result.Specs.Length == 0);
            rawTranscript.Text = (result.Barcodes.Length == 0 ? "" : "条码：" + string.Join("、", result.Barcodes) + Environment.NewLine + Environment.NewLine) +
                (result.Note.Length == 0 ? "" : result.Note + Environment.NewLine + Environment.NewLine) + result.Transcript;
            if (result.Barcodes.Length == 1) taskNumber.Text = result.Barcodes[0];
            specifications.Rows.Clear();
            string cleaned = string.Concat(result.Transcript.Where(character => !char.IsWhiteSpace(character)));
            string product = cleaned.Contains("母蟹") && !cleaned.Contains("公蟹") ? "母蟹" : "蟹";
            foreach (LabelSpec spec in result.Specs)
            {
                string name = ((spec.Minimum + spec.Maximum) / 100M).ToString("0.#", CultureInfo.InvariantCulture) + "两" + product;
                AddDraft(name, spec.Minimum.ToString(CultureInfo.CurrentCulture),
                    spec.Maximum.ToString(CultureInfo.CurrentCulture), spec.Color, spec.Quantity);
            }
            specifications.ClearSelection();
            SetBadge(recognitionStatus, result.Specs.Length > 0 ? RopeFill[1] : RopeFill[2],
                result.Specs.Length > 0 ? Green : Blue);
            recognitionStatus.Text = result.Specs.Length == 0 ? "未提取完整条目，可手动补录" :
                "识别 " + result.Specs.Length + " 项 · 请核对照片";
            if (result.Barcodes.Length > 1) recognitionStatus.Text = "检测到多个条码 · 请选任务单号";
            labelImage.Invalidate();
        }

        void ShowOcrTranscript(bool show)
        {
            rawTranscript.Visible = show;
            ocrCards.Visible = !show;
            toggleTranscript.Text = show ? "查看摘要" : "查看原文";
            toggleTranscript.Enabled = ocrCards.Controls.Count > 0;
        }

        void ClearOcrCards()
        {
            foreach (Control card in ocrCards.Controls.Cast<Control>().ToArray()) card.Dispose();
        }

        void DrawLabelHighlights(object sender, PaintEventArgs e)
        {
            if (shownOcr == null || labelImage.Image == null) return;
            Image image = labelImage.Image;
            float scale = Math.Min(labelImage.Width / (float)image.Width, labelImage.Height / (float)image.Height);
            float width = image.Width * scale, height = image.Height * scale;
            float x = (labelImage.Width - width) / 2, y = (labelImage.Height - height) / 2;
            foreach (LabelSpec spec in shownOcr.Specs.Where(item => !item.TextArea.IsEmpty))
            {
                RectangleF box = spec.TextArea;
                box = new RectangleF(x + box.X * width, y + box.Y * height, box.Width * width, box.Height * height);
                Color color = RopeInk[spec.Color];
                using (var pen = new Pen(color, ConsolePaint.Pixels(this, 2)))
                    e.Graphics.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                string caption = Basket.Colors[spec.Color] + "  " + spec.Minimum + "–" + spec.Maximum + " g/只  ·  " + spec.Quantity + " 只";
                Size size = TextRenderer.MeasureText(caption, labelImage.Font);
                int padding = ConsolePaint.Pixels(this, 8);
                var tag = new Rectangle(Math.Max(0, Math.Min((int)box.Left, labelImage.Width - size.Width - 2 * padding)),
                    Math.Max(0, (int)box.Top - size.Height - padding), size.Width + 2 * padding, size.Height + padding);
                ConsolePaint.Tile(e.Graphics, tag, ConsolePaint.Pixels(this, 4), Color.FromArgb(18, 30, 44), color);
                TextRenderer.DrawText(e.Graphics, caption, labelImage.Font, tag, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        void ReleaseOcrPhoto()
        {
            Image prior = labelImage.Image;
            labelImage.Image = null;
            if (prior != null) prior.Dispose();
            if (captureFile != null)
            {
                try { File.Delete(captureFile); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            captureFile = importedPhoto = null;
        }

        void ClearDraft()
        {
            if (importingLabel) return;
            specifications.Rows.Clear();
            ClearOcrCards();
            ShowOcrTranscript(true);
            rawTranscript.Text = "上传标签照片，或从视频抓拍后查看识别结果。";
            taskNumber.Clear();
            enteredBy = null;
            draftAudit.Clear();
            shownOcr = null;
            ReleaseOcrPhoto();
            labelImage.Visible = false;
            preview.Visible = true;
            SetBadge(recognitionStatus, RopeFill[2], Blue);
            recognitionStatus.Text = "等待上传照片或抓拍";
        }

        void AddDraft(string name, string low, string high, int color, int quantity)
        {
            int index = specifications.Rows.Add(name, low, high, Basket.Colors[color], quantity.ToString());
            specifications.Rows[index].Height = specifications.RowTemplate.Height;
        }

        void StartManualGrade()
        {
            using (var dialog = new Form { Text = "手动补录条码 / 规格", ClientSize = new Size(500, 302),
                FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                BackColor = PanelColor, ForeColor = Ink, Font = new Font(Font.FontFamily, 17, FontStyle.Regular, GraphicsUnit.Pixel) })
            {
                var code = new ComboBox { Width = 265, MaxLength = 60, DropDownStyle = ComboBoxStyle.DropDown, Text = taskNumber.Text };
                if (shownOcr != null) code.Items.AddRange(shownOcr.Barcodes.Cast<object>().ToArray());
                var operatorId = new TextBox { Width = 265, MaxLength = 35, Text = enteredBy ?? "" };
                int[] choices = Enumerable.Range(0, Basket.Colors.Length).Where(index => ruleEnabled[index].Checked).ToArray();
                if (choices.Length == 0)
                {
                    MessageBox.Show(this, "请在系统配置中启用至少一种规格。");
                    return;
                }
                var grade = Choice(choices.Select(index => Basket.Colors[index] + " · " + savedNames[index]).ToArray());
                grade.Width = 265;
                var qty = Amount(1, 0, 1, 10000, 1);
                var add = ActionButton("补录");
                SetPrimary(add, Blue);
                At(dialog, Note("条码 / 箱码"), 21, 22, 160, 29);
                At(dialog, code, 187, 22, 270, 27);
                At(dialog, Note("操作员工号"), 21, 72, 160, 29);
                At(dialog, operatorId, 187, 72, 270, 27);
                At(dialog, Note("绳色与规格"), 21, 120, 160, 29);
                At(dialog, grade, 187, 120, 270, 27);
                At(dialog, Note("装箱计划数量"), 21, 167, 160, 29);
                At(dialog, qty, 187, 167, 100, 27);
                At(dialog, add, 342, 236, 110, 42);
                dialog.AcceptButton = add;
                add.Click += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(code.Text) || string.IsNullOrWhiteSpace(operatorId.Text) ||
                        code.Text.Any(char.IsControl) || operatorId.Text.Any(char.IsControl))
                    {
                        MessageBox.Show(dialog, "请填写条码和操作员工号。");
                        return;
                    }
                    int color = choices[grade.SelectedIndex];
                    if (specifications.Rows.Cast<DataGridViewRow>().Any(row => Convert.ToString(row.Cells[3].Value) == Basket.Colors[color]))
                    {
                        MessageBox.Show(dialog, "这个绳色已有规格，可直接在列表修改。");
                        return;
                    }
                    taskNumber.Text = code.Text.Trim();
                    enteredBy = operatorId.Text.Trim();
                    draftAudit.Add(DateTimeOffset.Now.ToString("o") + "\t" + enteredBy + "\t" + code.Text.Trim() +
                        "\t" + Basket.Colors[color] + "\t" + qty.Value.ToString(CultureInfo.InvariantCulture));
                    AddDraft(savedNames[color], savedMinimum[color].ToString(CultureInfo.CurrentCulture),
                        savedMaximum[color].ToString(CultureInfo.CurrentCulture), color, (int)qty.Value);
                    dialog.DialogResult = DialogResult.OK;
                };
                dialog.ShowDialog(this);
            }
        }

        void BeginPacking()
        {
            if (importingLabel) return;
            specifications.EndEdit();
            if (order != null && specifications.Rows.Count == 0)
            {
                ShowPackingPage(true);
                return;
            }
            int[] goals = new int[Basket.Colors.Length];
            if (taskNumber.Text.Trim().Length == 0 && shownOcr != null && shownOcr.Barcodes.Length > 1)
            {
                MessageBox.Show(this, "照片中有多个条码，请在系统配置或「手动添加规格」中选定箱码。");
                return;
            }
            string code = string.IsNullOrWhiteSpace(taskNumber.Text) ? "BX-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") : taskNumber.Text.Trim();
            try
            {
                var selected = new PackingOrder(OrdersFolder, code, goals);
                var specificationsPath = Path.Combine(selected.Folder, "packing-specs.tsv");
                var specificationsLines = savedNames.Select((name, index) => name + "\t" +
                    savedMinimum[index].ToString(CultureInfo.InvariantCulture) + "\t" +
                    savedMaximum[index].ToString(CultureInfo.InvariantCulture) + "\t" + (ruleEnabled[index].Checked ? "1" : "0")).ToArray();
                var enteredColors = new HashSet<int>();
                foreach (DataGridViewRow row in specifications.Rows)
                {
                    string name = Convert.ToString(row.Cells[0].Value).Trim();
                    decimal low, high;
                    int count;
                    int color = Array.IndexOf(Basket.Colors, Convert.ToString(row.Cells[3].Value));
                    if (color < 0 || !enteredColors.Add(color) || name.Length == 0 || name.Any(char.IsControl) ||
                        !decimal.TryParse(Convert.ToString(row.Cells[1].Value), NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out low) ||
                        !decimal.TryParse(Convert.ToString(row.Cells[2].Value), NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out high) ||
                        low <= 0 || high < low || high > 10000 ||
                        !int.TryParse(Convert.ToString(row.Cells[4].Value), out count) || count < 1 || count > 10000)
                    {
                        MessageBox.Show(this, "请核对规格名称、唯一绳色、重量区间和目标只数。");
                        return;
                    }
                    goals[color] = count;
                    specificationsLines[color] = name + "\t" + low.ToString(CultureInfo.InvariantCulture) +
                        "\t" + high.ToString(CultureInfo.InvariantCulture) + "\t1";
                }

                string goalPath = Path.Combine(selected.Folder, "task.txt");
                if (selected.Finished || enteredColors.Count == 0)
                {
                    if (!File.Exists(goalPath))
                    {
                        MessageBox.Show(this, "请先识别、补录规格，或输入一个已存在的箱码。");
                        return;
                    }
                    string[] previous = File.ReadAllLines(goalPath);
                    for (int i = 0; i < Math.Min(goals.Length, previous.Length); i++)
                    {
                        int number;
                        if (int.TryParse(previous[i], out number) && number >= 0 && number <= 10000) goals[i] = number;
                    }
                }
                else
                {
                    selected.SaveGoals();
                    File.WriteAllLines(specificationsPath, specificationsLines);
                    if (labelImage.Image != null && importedPhoto != null)
                        using (var thumbnail = new Bitmap(labelImage.Image, 200, Math.Max(1, labelImage.Image.Height * 200 / labelImage.Image.Width)))
                            thumbnail.Save(Path.Combine(selected.Folder, "box-label.jpg"), System.Drawing.Imaging.ImageFormat.Jpeg);
                    if (draftAudit.Count > 0)
                    {
                        File.AppendAllLines(Path.Combine(selected.Folder, "manual-audit.tsv"), draftAudit, Encoding.UTF8);
                        draftAudit.Clear();
                    }
                }
                if (order == null || order.Code != selected.Code)
                {
                    baselineFrame = null;
                    lastUnclassifiedWeight = double.NaN;
                    unclassifiedMoves = 0;
                }
                order = selected;
                taskNumber.Text = selected.Code;
                ReloadSpecifications();
                shownGrade = -1;
                ShowPackingPage(true);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "启用工单失败"); }
        }

        void ShowPackingPage(bool packing)
        {
            if (packingActive && !packing) baselineFrame = null;
            packingActive = packing;
            labelStep.Visible = packingStep.Visible = !packing;
            backToLabels.Visible = packing;
            labelPage.Visible = !packing;
            packingPage.Visible = packing;
            if (packing) packingPage.BringToFront();
            else labelPage.BringToFront();
            labelStep.BackColor = packing ? Color.FromArgb(237, 244, 255) : Blue;
            labelStep.ForeColor = packing ? Blue : Color.White;
            packingStep.BackColor = packing ? Blue : Color.FromArgb(237, 244, 255);
            packingStep.ForeColor = packing ? Color.White : Blue;
            if (packing && order != null) UpdateOrderDisplay();
            ResetCandidates();
        }

        void RefreshWorkflow(long now, WeightSample weight)
        {
            packingWeight.Text = weight == null || !weight.Valid ? "—" : weight.Number;
            weightValue.Text = packingWeight.Text;
            string liveState = weight == null || !weight.Valid ? "等待有效称重" : weight.Stable ? "● 称重稳定" : "净重变化中";
            if (!simulationOpen && !scale.IsOpen) liveState = "电子秤未连接";
            weightState.Text = packingWeightState.Text = liveState;
            weightState.ForeColor = packingWeightState.ForeColor = weight != null && weight.Valid && weight.Stable ? Green : Muted;
            if (!packingActive && autoCapture.Checked && displayed != null && !importingLabel && !labelImage.Visible &&
                specifications.Rows.Count == 0 && now >= nextCapture && camera.Source != CameraSource.Demo)
            {
                nextCapture = now + 8 * Stopwatch.Frequency;
                CaptureAutomatically(true);
            }
        }

        async void CaptureAutomatically(bool keepScanning = false)
        {
            await CaptureLabelFrame();
            if (keepScanning && !closing && specifications.Rows.Count == 0 && autoCapture.Checked)
            {
                labelImage.Visible = false;
                preview.Visible = true;
            }
        }

        void AdjustScaleZero(bool useTare)
        {
            if (currentWeight == null || !currentWeight.Stable || !currentWeight.Valid)
            {
                MessageBox.Show(this, "请等待秤读数稳定。");
                return;
            }
            if (packingActive && order != null && (order.Finished || order.Counts.Sum() != 0 || unclassifiedMoves > 0))
            {
                MessageBox.Show(this, "当前工单还有装箱数据，无法重新设置空秤基线。");
                return;
            }
            if (!useTare && Math.Abs(currentWeight.Value + tare) > EmptyLimit)
            {
                MessageBox.Show(this, "置零前请清空秤台。箱子放在秤台上时，请使用去皮。");
                return;
            }
            tare += currentWeight.Value;
            baselineFrame = null;
            ResetCandidates();
        }

        void CompletePacking()
        {
            if (order == null || !order.CanFinish)
            {
                MessageBox.Show(this, "请核对每种规格的装箱数量和待确认记录。");
                return;
            }
            try
            {
                order.Finish();
                UpdateOrderDisplay();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "完成装箱失败"); }
        }

        void UpdateOrderDisplay()
        {
            if (order == null) return;
            if (shownGrade < 0 && !order.Moves.Any(move => move.Color < 0))
                shownGrade = Array.FindIndex(order.Goals, target => target > 0);
            packingTask.Text = "任务单号  " + order.Code + (order.Finished ? " · 已完成" : "");
            RefreshGradeCards();
            LoadLedger();
            int goal = order.Goals.Sum(), packed = order.Counts.Sum();
            bannerProgress.Text = "当前整箱总进度  " + packed + " / " + goal + " 只";
            completionRate.Text = Percent(order.GoalsMet, goal);
            totalProgress.Value = goal == 0 ? 0 : order.GoalsMet * 1000 / goal;
            finishPacking.Enabled = order.CanFinish;
            finishPacking.Text = order.Finished ? "装箱已完成" : "完成装箱";
        }

        void RefreshGradeCards()
        {
            gradeCards.SuspendLayout();
            foreach (Control card in gradeCards.Controls.Cast<Control>().ToArray()) card.Dispose();
            if (order != null)
            {
                float scale = ClientSize.Width / (float)ConsoleLayout.DesignSize.Width;
                for (int i = 0; i < Basket.Colors.Length; i++)
                {
                    if (order.Goals[i] == 0 && order.Counts[i] == 0) continue;
                    int color = i;
                    var card = new GradeCard(savedNames[i], Basket.Colors[i], order.Counts[i], order.Goals[i], shownGrade == i, RopeInk[i], RopeFill[i])
                    {
                        Size = new Size(gradeCards.ClientSize.Width - (int)(24 * scale), (int)(87 * scale)),
                        Margin = new Padding((int)(7 * scale), 0, 0, (int)(9 * scale))
                    };
                    card.Click += (s, e) => { shownGrade = color; RefreshGradeCards(); LoadLedger(); };
                    gradeCards.Controls.Add(card);
                }
                if (order.Moves.Any(move => move.Color < 0))
                {
                    var pending = ActionButton("待确认记录");
                    pending.Width = (int)(170 * scale);
                    pending.Click += (s, e) => { shownGrade = -1; RefreshGradeCards(); LoadLedger(); };
                    gradeCards.Controls.Add(pending);
                }
            }
            gradeCards.ResumeLayout(true);
        }

        void LoadLedger()
        {
            ledger.SuspendLayout();
            ledger.Rows.Clear();
            if (order == null)
            {
                ledger.ResumeLayout();
                return;
            }
            var moves = order.Moves.Where(move => move.Color == shownGrade).ToArray();
            var visible = new List<object[]>();
            int count = 0;
            double grams = 0;
            foreach (PackingMove move in moves)
            {
                int before = count;
                count += move.Count;
                grams = shownGrade < 0 ? move.Weight : grams + move.Delta;
                string serial = shownGrade < 0 ? "待确认" : move.Count < 0 ? "调整纠错" :
                    move.Count == 1 ? "第 " + count + " 只" : "第 " + (before + 1) + "–" + count + " 只";
                visible.Add(new object[] { serial, move.Time.ToString("HH:mm:ss"), move.Delta > 0 ? "放入装箱" : "取出螃蟹",
                    shownGrade < 0 ? "—" : move.Count.ToString("+0;-0") + " 只",
                    move.Delta.ToString("+0.0;-0.0") + " g" + (move.OutsideRange ? " !" : ""), grams.ToString("F1") + " g", move });
            }
            foreach (var row in visible.AsEnumerable().Reverse().Take(200))
            {
                int index = ledger.Rows.Add(row.Take(6).ToArray());
                var move = (PackingMove)row[6];
                ledger.Rows[index].Tag = move;
                if (move.OutsideRange) ledger.Rows[index].Cells[4].ToolTipText = "单只均重超出规格范围";
                ledger.Rows[index].Height = ledger.RowTemplate.Height;
            }
            ledgerTitle.Text = shownGrade < 0 ? "待确认称重记录" :
                "【" + Basket.Colors[shownGrade] + " · " + savedNames[shownGrade] + "】装箱流水记录";
            ledgerCount.Text = moves.Length + " 条流水 · 净计 " + count + " 只";
            ledger.ResumeLayout();
        }

        void PaintLedgerCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            var move = ledger.Rows[e.RowIndex].Tag as PackingMove;
            if (move == null) return;
            bool plus = move.Delta > 0;
            Color background = plus ? (e.RowIndex % 2 == 0 ? Color.White : Surface) : Color.FromArgb(255, 242, 242);
            using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.CellBounds);
            Rectangle bounds = Rectangle.Inflate(e.CellBounds, -6, 0);
            Color ink = !plus && e.ColumnIndex != 1 ? RopeInk[0] :
                e.ColumnIndex == 2 || e.ColumnIndex == 3 ? Green : Ink;
            if (e.ColumnIndex == 0 || e.ColumnIndex == 2 || e.ColumnIndex == 3)
            {
                Rectangle chip = Rectangle.Inflate(bounds, -5, -9);
                ConsolePaint.Tile(e.Graphics, chip, 5, plus ? RopeFill[e.ColumnIndex == 0 ? 2 : 1] : RopeFill[0], background);
            }
            TextRenderer.DrawText(e.Graphics, Convert.ToString(e.Value), e.CellStyle.Font ?? Font, bounds, ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (var pen = new Pen(Border)) e.Graphics.DrawLine(pen, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
            e.Handled = true;
        }

        void OpenMoveEvidence(int row)
        {
            if (row < 0) return;
            var move = ledger.Rows[row].Tag as PackingMove;
            if (move == null) return;
            string[] paths = { move.Before, move.After, move.Target };
            using (var form = new Form { Text = move.Time.ToString("HH:mm:ss") + " · 取放快照", ClientSize = new Size(935, move.Color < 0 && !order.Finished ? 365 : 312),
                StartPosition = FormStartPosition.CenterParent, BackColor = Surface, Font = new Font(Font.FontFamily, 16, FontStyle.Regular, GraphicsUnit.Pixel) })
            {
                var images = new List<Image>();
                try
                {
                    for (int i = 0; i < paths.Length; i++)
                    {
                        var image = LoadSnapshot(paths[i], false);
                        if (image != null) images.Add(image);
                        At(form, Note(new[] { "操作前", "操作后", "绳色目标" }[i]), i * 308 + 9, 11, 296, 26);
                        At(form, new PictureBox { Image = image, SizeMode = PictureBoxSizeMode.Zoom, BackColor = PanelColor }, i * 308 + 9, 43, 296, 244);
                    }
                    if (move.Color < 0 && !order.Finished)
                    {
                        var resolve = ActionButton("核对绳色和数量");
                        SetPrimary(resolve, Blue);
                        resolve.Click += (s, e) => { if (ReviewPendingMove(move, form)) form.Close(); };
                        At(form, resolve, 730, 313, 183, 38);
                    }
                    bool wasChoosing = choosingOrder;
                    choosingOrder = true;
                    try { form.ShowDialog(this); }
                    finally { choosingOrder = wasChoosing; }
                }
                finally { foreach (Image image in images) image.Dispose(); }
            }
        }

        bool ReviewPendingMove(PackingMove pending, Form owner)
        {
            using (var dialog = new Form { Text = "核对待确认流水", ClientSize = new Size(424, 247),
                StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                BackColor = PanelColor, ForeColor = Ink, Font = new Font(Font.FontFamily, 16, FontStyle.Regular, GraphicsUnit.Pixel) })
            {
                var grade = Choice(Basket.Colors.Select((name, index) => name + " · " + savedNames[index]).ToArray());
                var quantity = Amount(1, 0, 1, 10000, 1);
                var operatorId = new TextBox { MaxLength = 35, Text = enteredBy ?? "" };
                var save = ActionButton("保存核对结果");
                SetPrimary(save, Blue);
                At(dialog, Heading((pending.Delta > 0 ? "放入  +" : "取出  ") + pending.Delta.ToString("F1") + " g"), 15, 10, 362, 32);
                At(dialog, Note("绳色规格"), 19, 62, 113, 27);
                At(dialog, grade, 136, 60, 252, 30);
                At(dialog, Note("实际变动只数"), 19, 103, 119, 28);
                At(dialog, quantity, 136, 101, 92, 28);
                At(dialog, Note("操作员工号"), 19, 145, 119, 28);
                At(dialog, operatorId, 136, 145, 251, 29);
                At(dialog, save, 231, 188, 158, 41);
                dialog.AcceptButton = save;
                save.Click += (s, e) =>
                {
                    string reviewer = operatorId.Text.Trim();
                    if (reviewer.Length == 0 || reviewer.Any(char.IsControl))
                    {
                        MessageBox.Show(dialog, "请填写操作员工号。");
                        return;
                    }
                    int color = grade.SelectedIndex;
                    int count = (int)quantity.Value;
                    double average = Math.Abs(pending.Delta) / count;
                    var confirmed = new PackingMove
                    {
                        Color = color, Count = Math.Sign(pending.Delta) * count, Delta = pending.Delta,
                        Weight = pending.Weight, Spec = savedNames[color], Before = pending.Before, After = pending.After, Target = pending.Target,
                        OutsideRange = average < savedMinimum[color] || average > savedMaximum[color]
                    };
                    try
                    {
                        order.Resolve(pending, confirmed);
                        // The ledger remains authoritative; the identity is written to a separate local audit trail.
                        try
                        {
                            File.AppendAllText(Path.Combine(order.Folder, "manual-audit.tsv"), DateTimeOffset.Now.ToString("o") +
                                "\t" + reviewer + "\t" + pending.Time.ToString("o") + "\t" + Basket.Colors[color] +
                                "\t" + confirmed.Count + Environment.NewLine, Encoding.UTF8);
                        }
                        catch (IOException ex) { MessageBox.Show(dialog, "流水已补全，审计记录写入失败：" + ex.Message); }
                        catch (UnauthorizedAccessException ex) { MessageBox.Show(dialog, "流水已补全，审计记录写入失败：" + ex.Message); }
                        enteredBy = reviewer;
                        baselineFrame = null;
                        lastUnclassifiedWeight = double.NaN;
                        unclassifiedMoves = 0;
                        ResetCandidates();
                        shownGrade = color;
                        UpdateOrderDisplay();
                        dialog.DialogResult = DialogResult.OK;
                    }
                    catch (Exception ex) { MessageBox.Show(dialog, ex.Message, "核对记录失败"); }
                };
                return dialog.ShowDialog(owner) == DialogResult.OK;
            }
        }

        string SettingsFile { get { return Path.Combine(DataFolder, "console-settings.txt"); } }

        void CreateSettingsContent()
        {
            settingsContent = new Panel { BackColor = Surface };
            serialControls = Line(ports, protocol);
            serialControls.Dock = DockStyle.None;
            simulationControls = Line(cycleLoads, crabCount);
            simulationControls.Dock = DockStyle.None;
            At(settingsContent, Heading("工控通信接口"), 17, 11, 420, 31);
            At(settingsContent, Note("电子秤"), 17, 54, 100, 29);
            At(settingsContent, serialControls, 125, 51, 236, 35);
            At(settingsContent, simulationControls, 125, 51, 236, 35);
            At(settingsContent, demoScale, 374, 57, 100, 27);
            At(settingsContent, scaleButton, 497, 48, 88, 40);
            At(settingsContent, Note("工业相机"), 17, 109, 100, 29);
            At(settingsContent, cameraType, 125, 105, 121, 29);
            At(settingsContent, address, 255, 105, 276, 29);
            At(settingsContent, browseVideo, 540, 98, 94, 40);
            At(settingsContent, cameraButton, 642, 98, 93, 40);
            At(settingsContent, Note("PLC"), 17, 156, 98, 27);
            At(settingsContent, Note("未配置 · 工单使用本机视频和秤数据"), 125, 156, 515, 29);
            At(settingsContent, automatic, 579, 155, 146, 32);
            At(settingsContent, Note("任务单号"), 17, 208, 100, 29);
            At(settingsContent, taskNumber, 125, 204, 280, 29);
            var existing = ActionButton("打开已有工单");
            existing.Click += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(taskNumber.Text)) return;
                if (order != null && order.Code == taskNumber.Text.Trim()) ShowPackingPage(true);
                else OpenExistingOrder();
                if (packingActive && settingsContent.FindForm() != null) settingsContent.FindForm().Close();
            };
            At(settingsContent, existing, 429, 199, 154, 40);
            var grades = Module();
            At(grades, Heading("绳色重量范围规则"), 13, 11, 400, 30);
            var useRule = ActionButton("添加到解析列表");
            useRule.Click += (s, e) => StartManualGrade();
            At(grades, useRule, 530, 8, 180, 37);
            for (int i = 0; i < Basket.Colors.Length; i++)
            {
                productNames[i] = new TextBox { MaxLength = 35, Text = DefaultNames[i], BorderStyle = BorderStyle.FixedSingle };
                minimumWeights[i] = WeightInput(DefaultMin[i]);
                maximumWeights[i] = WeightInput(DefaultMax[i]);
                int top = 58 + i * 41;
                At(grades, Note(Basket.Colors[i]), 19, top, 93, 25);
                At(grades, productNames[i], 121, top, 269, 27);
                At(grades, minimumWeights[i], 411, top, 97, 27);
                At(grades, Note("～"), 523, top, 32, 26);
                At(grades, maximumWeights[i], 554, top, 70, 27);
                At(grades, Note("g"), 632, top, 21, 26);
                int color = i;
                ruleEnabled[color] = new CheckBox { Text = "启用", Checked = true };
                ruleEnabled[color].CheckedChanged += (s, e) =>
                    productNames[color].Enabled = minimumWeights[color].Enabled = maximumWeights[color].Enabled = ruleEnabled[color].Checked;
                At(grades, ruleEnabled[color], 654, top, 63, 27);
            }
            At(settingsContent, grades, 13, 253, 723, 274);
        }

        void OpenSettings()
        {
            if (settingsContent.Parent != null) return;
            choosingOrder = true;
            using (var form = new Form { Text = "系统配置", ClientSize = new Size(750, 600), StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, BackColor = Surface,
                ForeColor = Ink, Font = new Font(Font.FontFamily, 17, FontStyle.Regular, GraphicsUnit.Pixel) })
            {
                At(form, settingsContent, 0, 0, 750, 535);
                var save = ActionButton("保存配置并生效");
                SetPrimary(save, Blue);
                var close = ActionButton("关闭");
                close.Click += (s, e) => form.Close();
                save.Click += (s, e) =>
                {
                    if (order != null && order.Finished) referenceStatus.Text = "已完成工单的规格已锁定";
                    else if (!SaveSpecifications()) return;
                    try
                    {
                        Directory.CreateDirectory(DataFolder);
                        File.WriteAllLines(SettingsFile, new[] { ports.Text, protocol.SelectedIndex.ToString(),
                            cameraType.SelectedIndex.ToString() });
                        form.Close();
                    }
                    catch (Exception ex) { MessageBox.Show(form, ex.Message, "保存配置失败"); }
                };
                At(form, referenceStatus, 18, 553, 350, 30);
                At(form, close, 465, 545, 91, 45);
                At(form, save, 563, 545, 165, 45);
                try
                {
                    UpdateScaleInputs();
                    UpdateCameraInputs();
                    form.ShowDialog(this);
                }
                finally { form.Controls.Remove(settingsContent); form.Controls.Remove(referenceStatus); choosingOrder = false; }
            }
        }

        void OpenExistingOrder()
        {
            try
            {
                int[] goals = new int[Basket.Colors.Length];
                var selected = new PackingOrder(OrdersFolder, taskNumber.Text.Trim(), goals);
                string path = Path.Combine(selected.Folder, "task.txt");
                if (!File.Exists(path))
                {
                    MessageBox.Show(this, "未找到这个箱码的装箱工单。");
                    return;
                }
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < Math.Min(lines.Length, goals.Length); i++)
                {
                    int value;
                    if (int.TryParse(lines[i], out value) && value >= 0) goals[i] = value;
                }
                order = selected;
                baselineFrame = null;
                lastUnclassifiedWeight = double.NaN;
                unclassifiedMoves = 0;
                shownGrade = -1;
                ReloadSpecifications();
                ShowPackingPage(true);
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "无法打开工单"); }
        }

        void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile)) return;
                string[] values = File.ReadAllLines(SettingsFile);
                if (values.Length > 0) ports.Text = values[0];
                int value;
                if (values.Length > 1 && int.TryParse(values[1], out value) && value >= 0 && value < protocol.Items.Count)
                    protocol.SelectedIndex = value;
                if (values.Length > 2 && int.TryParse(values[2], out value) && value >= 0 && value < cameraType.Items.Count)
                    cameraType.SelectedIndex = value;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (packingActive || importingLabel || settingsContent.Parent != null || specifications.IsCurrentCellInEditMode ||
                e.Modifiers != Keys.None || ActiveControl is TextBoxBase || ActiveControl is ComboBox || ActiveControl is Button) return;
            if (e.KeyCode == Keys.Space) { CaptureAutomatically(); e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Enter) { BeginPacking(); e.SuppressKeyPress = true; }
        }
    }
}
