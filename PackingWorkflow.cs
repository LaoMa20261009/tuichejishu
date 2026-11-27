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
        readonly CheckBox[] ruleEnabled = new CheckBox[Basket.Colors.Length];
        readonly TextBox taskNumber = new TextBox { Width = 186, MaxLength = 60, BorderStyle = BorderStyle.FixedSingle };
        readonly TextBox rawTranscript = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            WordWrap = true, BorderStyle = BorderStyle.None };
        readonly Label recognitionStatus = Hint("等待上传照片或抓拍");
        readonly Label weightState = Hint("电子秤未连接"), packingWeightState = Hint("电子秤未连接");
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
        readonly Button captureLabels = ActionButton("立即抓拍");
        readonly Button confirmLabels = ActionButton("确认规格并开始装箱");
        readonly Button photoUpload = ActionButton("上传图片文字识别");
        readonly Button pickCamera = ActionButton("实时视频");
        readonly Button replacePhoto = ActionButton("选择图片");
        readonly Label sourceHint = Hint("视频已启用自动抓拍");
        Panel settingsContent, packingSimulation;
        bool photoMode, choosingPhoto;
        LabelPhotoResult shownOcr;
        string importedPhoto, captureFile, enteredBy, lastCompletedCode;
        readonly List<string[]> draftAudit = new List<string[]>();
        bool packingActive;
        int shownGrade = -1;
        long nextCapture;

        Control CreateBanner()
        {
            var bar = new Panel { BackColor = Navy };
            var icon = RoundedPanel(Blue, Blue, 9);
            icon.Paint += (sender, e) =>
            {
                int size = ConsolePaint.Pixels(icon, 25);
                ConsoleIcons.Draw(e.Graphics, ConsoleIcon.Scale,
                    new Rectangle((icon.Width - size) / 2, (icon.Height - size) / 2, size, size), Color.White);
            };
            var title = Heading("螃蟹装箱计数系统");
            title.Font = new Font(Font.FontFamily, 24, FontStyle.Bold, GraphicsUnit.Pixel);
            title.ForeColor = Color.White;
            var subtitle = Note("工业上位机装箱控制台");
            subtitle.Font = new Font(Font.FontFamily, 12, FontStyle.Regular, GraphicsUnit.Pixel);
            subtitle.ForeColor = Color.LightGray;
            At(bar, icon, 17, 8, 40, 40);
            At(bar, title, 69, 4, 245, 26);
            At(bar, subtitle, 70, 33, 221, 17);
            ((ConsoleLabel)deviceStatus).Icon = ConsoleIcon.Scale;
            ((ConsoleLabel)cameraBadge).Icon = ConsoleIcon.Camera;
            SetBadge(deviceStatus, PaleBlue, Blue);
            SetBadge(cameraBadge, PaleBlue, Blue);
            At(bar, deviceStatus, 701, 7, 169, 42);
            At(bar, cameraBadge, 882, 7, 212, 42);
            var settings = ActionButton("⚙");
            settings.AccessibleName = "系统配置";
            settings.Click += (s, e) => OpenSettings();
            var minimize = ActionButton("—");
            var close = ActionButton("×");
            minimize.AccessibleName = "最小化";
            close.AccessibleName = "关闭";
            minimize.Click += (s, e) => WindowState = FormWindowState.Minimized;
            close.Click += (s, e) => Close();
            foreach (var button in new[] { settings, minimize, close })
                SetPrimary(button, Gauge);
            At(bar, settings, 1111, 6, 46, 44);
            At(bar, minimize, 1161, 6, 44, 44);
            At(bar, close, 1209, 6, 46, 44);
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

        static Panel RoundedPanel(Color fill, Color border, int radius)
        {
            var panel = new Panel { BackColor = fill };
            ConsolePaint.RoundEdges(panel, radius);
            panel.Paint += (sender, e) => ConsolePaint.Tile(e.Graphics, panel.ClientRectangle,
                ConsolePaint.Pixels(panel, radius), fill, border);
            return panel;
        }

        static Panel Module()
        {
            return RoundedPanel(PanelColor, Border, 12);
        }

        static void AtInput(Control parent, Control input, int x, int y, int width, int height)
        {
            var field = RoundedPanel(PanelColor, Border, 9);
            input.BackColor = PanelColor;
            var text = input as TextBox;
            if (text != null) text.BorderStyle = BorderStyle.None;
            var number = input as NumericUpDown;
            if (number != null) number.BorderStyle = BorderStyle.None;
            var choice = input as ComboBox;
            if (choice != null) choice.FlatStyle = FlatStyle.Flat;
            At(parent, field, x, y, width, height);
            field.Controls.Add(input);
            input.Size = new Size(width - 18, input.Font.Height + 5);
            input.Location = new Point(9, Math.Max(1, (height - input.Height) / 2));
        }

        static void SetBadge(Label label, Color background, Color foreground)
        {
            label.BackColor = background;
            var consoleLabel = label as ConsoleLabel;
            if (consoleLabel != null) consoleLabel.RoundedFill = true;
            label.ForeColor = foreground;
            label.TextAlign = ContentAlignment.MiddleCenter;
        }

        Control SectionTitle(string text, int size = 18)
        {
            var header = new Panel { BackColor = Color.Transparent };
            var title = Heading(text);
            if (size != 18) title.Font = new Font(Font.FontFamily, size, FontStyle.Bold, GraphicsUnit.Pixel);
            title.ForeColor = Blue;
            At(header, new Panel { BackColor = Cyan }, 0, 5, 3, 17);
            At(header, title, 11, 0, 270, 27);
            return header;
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
            photoUpload.Click += async (s, e) =>
            {
                SetLabelSource(true);
                if (labelImage.Image == null || captureFile != null) await ChooseLabelPhoto();
            };
            replacePhoto.Click += async (s, e) => await ChooseLabelPhoto();
            ((ConsoleButton)pickCamera).Icon = ConsoleIcon.Camera;
            ((ConsoleButton)photoUpload).Icon = ConsoleIcon.Photo;
            ((ConsoleButton)replacePhoto).Icon = ConsoleIcon.Photo;
            ((ConsoleButton)captureLabels).Icon = ConsoleIcon.Scan;
            ((ConsoleButton)confirmLabels).Icon = ConsoleIcon.Box;
            At(source, pickCamera, 7, 5, 143, 44);
            At(source, photoUpload, 156, 5, 211, 44);
            recognitionStatus.TextAlign = ContentAlignment.MiddleRight;
            At(source, recognitionStatus, 390, 9, 320, 36);
            At(labelPage, source, 0, 0, 730, 54);

            preview.BackColor = Color.FromArgb(16, 30, 45);
            preview.Font = sourceHint.Font;
            preview.Cursor = Cursors.Cross;
            preview.AllowDrop = labelImage.AllowDrop = true;
            preview.DragEnter += PhotoDragEnter;
            labelImage.DragEnter += PhotoDragEnter;
            preview.DragDrop += PhotoDragDrop;
            labelImage.DragDrop += PhotoDragDrop;
            labelImage.Paint += DrawLabelHighlights;
            ConsolePaint.RoundEdges(preview, 12);
            ConsolePaint.RoundEdges(labelImage, 12);
            At(labelPage, preview, 0, 60, 730, 656);
            At(labelPage, labelImage, 0, 60, 730, 656);
            labelImage.Visible = false;

            var videoActions = RoundedPanel(Navy, Gauge, 10);
            sourceHint.ForeColor = Color.LightGray;
            At(videoActions, sourceHint, 16, 12, 445, 30);
            SetPrimary(captureLabels, Blue);
            SetPrimary(replacePhoto, Blue);
            captureLabels.Click += async (s, e) => await CaptureLabelFrame();
            At(videoActions, captureLabels, 484, 4, 216, 44);
            At(videoActions, replacePhoto, 484, 4, 216, 44);
            SetLabelSource(false);
            At(labelPage, videoActions, 10, 654, 710, 52);
            videoActions.BringToFront();

            At(labelPage, BuildWeightModule(false), 742, 0, 510, 155);
            var raw = Module();
            At(raw, SectionTitle("原始标签 OCR 识别内容"), 14, 10, 285, 25);
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
            rawTranscript.BackColor = PanelColor;
            rawTranscript.ForeColor = Ink;
            rawTranscript.Font = new Font(Font.FontFamily, 14, FontStyle.Regular, GraphicsUnit.Pixel);
            At(raw, rawTranscript, 13, 44, 484, 112);
            ocrCards.BackColor = PanelColor;
            At(raw, ocrCards, 13, 44, 484, 112);
            ShowOcrTranscript(true);
            rawTranscript.ForeColor = Muted;
            rawTranscript.Text = "上传标签照片，或从视频抓拍后查看识别结果。";
            At(labelPage, raw, 742, 165, 510, 168);

            var parsed = Module();
            At(parsed, SectionTitle("标签解析规格列表"), 12, 10, 270, 25);
            At(parsed, Hint("点按字段即可修改"), 344, 12, 152, 24);
            ConfigureSpecifications();
            At(parsed, specifications, 10, 45, 489, 221);
            var add = ActionButton("＋ 手动添加规格");
            add.Click += (s, e) => StartManualGrade();
            var reset = ActionButton("重置解析数据");
            reset.Click += (s, e) => ClearDraft();
            At(parsed, add, 11, 271, 174, 44);
            At(parsed, reset, 352, 271, 145, 44);
            At(labelPage, parsed, 742, 340, 510, 321);
            SetPrimary(confirmLabels, Blue);
            confirmLabels.Click += (s, e) => BeginPacking();
            At(labelPage, confirmLabels, 742, 670, 510, 46);
            return labelPage;
        }

        Control BuildWeightModule(bool compact)
        {
            var module = Module();
            var value = compact ? packingWeight : weightValue;
            var status = compact ? packingWeightState : weightState;
            var readout = RoundedPanel(Gauge, Color.FromArgb(60, 128, 184), 11);
            readout.Paint += (sender, e) =>
            {
                int size = ConsolePaint.Pixels(readout, 20);
                ConsoleIcons.Draw(e.Graphics, ConsoleIcon.Scale,
                    new Rectangle(ConsolePaint.Pixels(readout, 13), ConsolePaint.Pixels(readout, 11), size, size), Ice);
            };
            var caption = Note("实时净重");
            caption.ForeColor = Ice;
            status.ForeColor = Color.LightGray;
            value.Font = new Font("Consolas", compact ? 47 : 42, FontStyle.Bold, GraphicsUnit.Pixel);
            value.ForeColor = Color.White;
            value.TextAlign = ContentAlignment.MiddleRight;
            ((ConsoleLabel)value).FitText = true;
            var unit = Note("g");
            SetBadge(unit, Navy, Ice);
            var tareControl = compact ? ActionButton("去皮") : tareButton;
            if (compact) tareControl.Click += (s, e) => AdjustTare();

            At(readout, caption, 43, 8, 111, 27);
            if (compact)
            {
                var task = RoundedPanel(PaleBlue, Border, 10);
                var taskCaption = Note("任务单号");
                taskCaption.ForeColor = Blue;
                packingTask.Font = new Font(Font.FontFamily, 20, FontStyle.Bold, GraphicsUnit.Pixel);
                packingTask.ForeColor = Ink;
                ((ConsoleLabel)packingTask).FitText = true;
                At(task, taskCaption, 14, 8, 195, 23);
                At(task, packingTask, 14, 33, 342, 38);
                At(module, task, 12, 9, 371, 78);
                At(readout, status, 15, 43, 164, 23);
                At(readout, value, 185, 7, 468, 64);
                At(readout, unit, 666, 23, 35, 32);
                At(module, readout, 395, 9, 714, 78);
                At(module, tareControl, 1123, 25, 113, 46);
            }
            else
            {
                At(module, SectionTitle("实时电子秤数据"), 13, 9, 314, 27);
                At(readout, status, 156, 10, 160, 23);
                At(readout, value, 14, 34, 294, 60);
                At(readout, unit, 319, 50, 34, 28);
                At(module, readout, 12, 43, 369, 101);
                At(module, tareControl, 394, 68, 102, 52);
            }
            return module;
        }

        Control CreatePackingPage()
        {
            At(packingPage, BuildWeightModule(true), 0, 0, 1252, 96);
            var deck = Module();
            var left = new Panel { BackColor = Surface };
            At(left, SectionTitle("装箱规格"), 16, 13, 280, 29);
            gradeCards.Margin = Padding.Empty;
            gradeCards.BackColor = left.BackColor;
            At(left, gradeCards, 10, 55, 446, 445);
            var guide = Hint("按规格放置螃蟹，点按卡片查看流水");
            At(left, guide, 16, 505, 419, 30);
            At(deck, left, 1, 1, 464, 546);
            At(deck, new Panel { BackColor = Border }, 465, 1, 1, 546);

            var right = new Panel { BackColor = PanelColor };
            ledgerTitle.ForeColor = Blue;
            At(right, ledgerTitle, 13, 11, 481, 29);
            SetBadge(ledgerCount, Color.FromArgb(231, 242, 255), Blue);
            At(right, ledgerCount, 516, 13, 237, 25);
            At(right, captureStatus, 14, 42, 739, 25);
            StyleGrid(ledger, 43);
            ledger.ScrollBars = ScrollBars.Vertical;
            ledger.BackgroundColor = PanelColor;
            ledger.ColumnHeadersHeight = 35;
            ledger.ColumnHeadersDefaultCellStyle.BackColor = PaleBlue;
            ledger.ColumnHeadersDefaultCellStyle.ForeColor = Blue;
            string[] names = { "计数序号", "记录时间", "操作动作", "变动只数", "重量变动", "累计重量" };
            for (int i = 0; i < names.Length; i++)
                ledger.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = names[i],
                    FillWeight = new[] { 108, 87, 106, 90, 145, 144 }[i], SortMode = DataGridViewColumnSortMode.NotSortable });
            ledger.CellPainting += PaintLedgerCell;
            ledger.CellDoubleClick += (s, e) => OpenMoveEvidence(e.RowIndex);
            ledger.Dock = DockStyle.None;
            At(right, ledger, 12, 73, 758, 462);
            At(deck, right, 467, 1, 783, 546);
            At(packingPage, deck, 0, 108, 1252, 548);

            var footer = Module();
            At(footer, bannerProgress, 16, 4, 255, 24);
            completionRate.ForeColor = Green;
            At(footer, completionRate, 274, 4, 100, 24);
            At(footer, totalProgress, 16, 33, 362, 9);
            var back = ActionButton("返回标签识别");
            ((ConsoleButton)back).Icon = ConsoleIcon.Scan;
            back.Click += (s, e) => ShowPackingPage(false);
            At(footer, back, 858, 4, 168, 42);
            packingSimulation = new Panel { BackColor = PanelColor };
            var batchSize = Choice("1 只", "2 只", "3 只", "4 只");
            batchSize.SelectedIndex = 0;
            batchSize.AccessibleName = "模拟取放数量";
            At(packingSimulation, Hint("模拟取放"), 0, 3, 87, 39);
            AtInput(packingSimulation, batchSize, 88, 2, 106, 39);
            var removeCrabs = ActionButton("取出");
            removeCrabs.Click += (s, e) => AdjustDemoCrabs(-batchSize.SelectedIndex - 1);
            var addCrabs = ActionButton("放入");
            addCrabs.Click += (s, e) => AdjustDemoCrabs(batchSize.SelectedIndex + 1);
            At(packingSimulation, removeCrabs, 204, 1, 87, 42);
            At(packingSimulation, addCrabs, 300, 1, 87, 42);
            At(footer, packingSimulation, 430, 3, 390, 43);
            packingSimulation.Visible = false;
            SetPrimary(finishPacking, Blue);
            ((ConsoleButton)finishPacking).Icon = ConsoleIcon.Box;
            finishPacking.Click += (s, e) => CompletePacking();
            At(footer, finishPacking, 1044, 4, 192, 42);
            At(packingPage, footer, 0, 667, 1252, 49);
            return packingPage;
        }

        void ConfigureSpecifications()
        {
            StyleGrid(specifications, 59);
            specifications.ColumnHeadersHeight = 38;
            specifications.DefaultCellStyle.Font = new Font(Font.FontFamily, 17, FontStyle.Regular, GraphicsUnit.Pixel);
            specifications.RowsDefaultCellStyle.BackColor = Color.FromArgb(246, 249, 254);
            specifications.AlternatingRowsDefaultCellStyle.BackColor = Color.White;
            specifications.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            specifications.BackgroundColor = PanelColor;
            specifications.ReadOnly = false;
            specifications.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            specifications.SelectionMode = DataGridViewSelectionMode.CellSelect;
            specifications.ScrollBars = ScrollBars.Vertical;
            var name = new DataGridViewTextBoxColumn { HeaderText = "规格名称", FillWeight = 149, MaxInputLength = 35 };
            var low = new DataGridViewTextBoxColumn { HeaderText = "起 (g)", FillWeight = 70, MaxInputLength = 8 };
            var high = new DataGridViewTextBoxColumn { HeaderText = "止 (g)", FillWeight = 70, MaxInputLength = 8 };
            var color = new DataGridViewComboBoxColumn { HeaderText = "绳色", FillWeight = 79, FlatStyle = FlatStyle.Flat };
            color.Items.AddRange(Basket.Colors.Cast<object>().ToArray());
            var count = new DataGridViewTextBoxColumn { HeaderText = "数量", FillWeight = 62, MaxInputLength = 5 };
            var delete = new DataGridViewButtonColumn { HeaderText = "操作", FillWeight = 76, Text = "删除",
                UseColumnTextForButtonValue = true, FlatStyle = FlatStyle.Flat };
            specifications.Columns.AddRange(name, low, high, color, count, delete);
            foreach (DataGridViewColumn column in specifications.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            specifications.CellClick += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                if (e.ColumnIndex == 5)
                {
                    specifications.Rows.RemoveAt(e.RowIndex);
                    return;
                }
                if (specifications.CurrentCellAddress != new Point(e.ColumnIndex, e.RowIndex)) return;
                specifications.BeginEdit(true);
                var choices = specifications.EditingControl as ComboBox;
                if (choices != null) choices.DroppedDown = true;
            };
            specifications.CellPainting += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 ||
                    (specifications.IsCurrentCellInEditMode && specifications.CurrentCellAddress == new Point(e.ColumnIndex, e.RowIndex))) return;
                Color background = (e.State & DataGridViewElementStates.Selected) != 0 ? Border : PanelColor;
                using (var brush = new SolidBrush(background)) e.Graphics.FillRectangle(brush, e.CellBounds);
                int rope = e.ColumnIndex == 3 ? Array.IndexOf(Basket.Colors, Convert.ToString(e.Value)) : -1;
                bool remove = e.ColumnIndex == 5;
                int insetX = ConsolePaint.Pixels(specifications, 3), insetY = ConsolePaint.Pixels(specifications, 6);
                Rectangle tile = Rectangle.Inflate(e.CellBounds, -insetX, -insetY);
                Color fill = remove ? RopeFill[0] : rope >= 0 ? RopeFill[rope] : PanelColor;
                Color textInk = remove ? RopeInk[0] : rope >= 0 ? RopeInk[rope] : Ink;
                ConsolePaint.Tile(e.Graphics, tile, ConsolePaint.Pixels(specifications, 6), fill, remove || rope >= 0 ? fill : Border);
                if (remove)
                {
                    int icon = ConsolePaint.Pixels(specifications, 24);
                    ConsoleIcons.Draw(e.Graphics, ConsoleIcon.Trash, new Rectangle(
                        tile.Left + (tile.Width - icon) / 2, tile.Top + (tile.Height - icon) / 2, icon, icon), textInk);
                }
                else
                    TextRenderer.DrawText(e.Graphics, Convert.ToString(e.Value), e.CellStyle.Font ?? Font, tile, textInk,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                e.Paint(e.ClipBounds, DataGridViewPaintParts.Border | DataGridViewPaintParts.Focus);
                e.Handled = true;
            };
            specifications.CellToolTipTextNeeded += (s, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == 5) e.ToolTipText = "删除此规格";
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
            if (importingLabel || choosingPhoto) return;
            SetLabelSource(true);
            choosingPhoto = true;
            try
            {
                using (var file = new OpenFileDialog { Title = "上传泡沫箱标签照片", Filter = "图片|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff",
                    CheckFileExists = true, RestoreDirectory = true })
                    if (file.ShowDialog(this) == DialogResult.OK) await ReadLabelPhoto(file.FileName, false);
            }
            finally { choosingPhoto = false; }
        }

        void SetLabelSource(bool usePhoto)
        {
            photoMode = usePhoto;
            SetPrimary(usePhoto ? photoUpload : pickCamera, Blue);
            Button inactive = usePhoto ? pickCamera : photoUpload;
            inactive.BackColor = Color.FromArgb(237, 244, 255);
            inactive.ForeColor = Muted;
            captureLabels.Visible = !usePhoto;
            replacePhoto.Visible = usePhoto;
            sourceHint.Text = usePhoto ? "选择标签照片，也可将照片拖入画面" : "实时视频自动抓拍标签";
            labelImage.Visible = usePhoto;
            preview.Visible = !usePhoto;
        }

        void ShowLivePreview()
        {
            SetLabelSource(false);
            if (camera != null && !camera.Ended)
            {
                if (camera.Error == null) return;
                CloseCamera("正在重新连接视频");
            }
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
            if (!captured) SetLabelSource(true);
            pickCamera.Enabled = replacePhoto.Enabled = captureLabels.Enabled = photoUpload.Enabled = confirmLabels.Enabled = false;
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
                if (!closing) pickCamera.Enabled = replacePhoto.Enabled = captureLabels.Enabled = photoUpload.Enabled = confirmLabels.Enabled = true;
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
            rawTranscript.ForeColor = Ink;
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
            recognitionStatus.Text = result.Specs.Length == 0 ? "未提取完整条目，可手动补录" :
                "识别 " + result.Specs.Length + " 项 · 请核对照片";
            if (result.Barcodes.Length > 1) recognitionStatus.Text = "检测到多个条码 · 开始装箱时选择";
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
            if (labelImage.Image == null)
            {
                int size = ConsolePaint.Pixels(this, 44);
                ConsoleIcons.Draw(e.Graphics, ConsoleIcon.Photo,
                    new Rectangle((labelImage.Width - size) / 2, labelImage.Height / 2 - 2 * size, size, size), Color.White);
                TextRenderer.DrawText(e.Graphics, "选择泡沫箱标签照片", sourceHint.Font, labelImage.ClientRectangle, Color.LightGray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            if (shownOcr == null) return;
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
            rawTranscript.ForeColor = Muted;
            rawTranscript.Text = "上传标签照片，或从视频抓拍后查看识别结果。";
            taskNumber.Clear();
            enteredBy = null;
            draftAudit.Clear();
            shownOcr = null;
            ReleaseOcrPhoto();
            SetLabelSource(photoMode);
            recognitionStatus.Text = "等待上传照片或抓拍";
        }

        void AddDraft(string name, string low, string high, int color, int quantity)
        {
            int index = specifications.Rows.Add(name, low, high, Basket.Colors[color], quantity.ToString());
            specifications.Rows[index].Height = specifications.RowTemplate.Height;
        }

        void StartManualGrade()
        {
            specifications.EndEdit();
            var available = Enumerable.Range(0, Basket.Colors.Length)
                .Where(color => ruleEnabled[color].Checked && !specifications.Rows.Cast<DataGridViewRow>()
                    .Any(row => Convert.ToString(row.Cells[3].Value) == Basket.Colors[color])).ToArray();
            if (available.Length == 0)
            {
                MessageBox.Show(this, "当前已启用的绳色都已添加，请在列表中修改规格。");
                return;
            }

            using (var dialog = CreateConsoleDialog("添加规格", new Size(761, 258)))
            {
                At(dialog, Heading("添加规格"), 23, 14, 220, 32);
                At(dialog, Hint("选择绳色后会带入参考重量，仍可修改。"), 23, 49, 714, 25);
                var name = new TextBox { MaxLength = 35, AccessibleName = "规格名称" };
                var low = new TextBox { MaxLength = 8, AccessibleName = "起始克重" };
                var high = new TextBox { MaxLength = 8, AccessibleName = "结束克重" };
                var rope = Choice(available.Select(color => Basket.Colors[color]).ToArray());
                rope.AccessibleName = "绳色";
                var quantity = Amount(1, 0, 1, 10000, 1);
                quantity.AccessibleName = "目标数量";
                var fields = new Control[] { name, low, high, rope, quantity };
                string[] captions = { "规格名称", "起 (g)", "止 (g)", "绳色", "数量" };
                int[] widths = { 249, 98, 98, 118, 97 };
                int x = 23;
                for (int i = 0; i < fields.Length; i++)
                {
                    At(dialog, Hint(captions[i]), x, 82, widths[i], 27);
                    AtInput(dialog, fields[i], x, 113, widths[i], 46);
                    x += widths[i] + 12;
                }
                EventHandler fill = (sender, e) =>
                {
                    int color = available[rope.SelectedIndex];
                    name.Text = productNames[color].Text;
                    low.Text = minimumWeights[color].Text;
                    high.Text = maximumWeights[color].Text;
                };
                rope.SelectedIndexChanged += fill;
                rope.SelectedIndex = 0;
                var feedback = Hint("");
                At(dialog, feedback, 23, 187, 433, 46);
                var cancel = ActionButton("取消");
                cancel.DialogResult = DialogResult.Cancel;
                At(dialog, cancel, 469, 187, 106, 48);
                dialog.CancelButton = cancel;
                var add = ActionButton("添加规格");
                SetPrimary(add, Blue);
                At(dialog, add, 587, 187, 150, 48);
                add.Click += (sender, e) =>
                {
                    decimal minimum, maximum;
                    if (string.IsNullOrWhiteSpace(name.Text) || name.Text.Any(char.IsControl) ||
                        !TryReadWeight(low, out minimum) || !TryReadWeight(high, out maximum) || minimum <= 0 || maximum < minimum)
                    {
                        feedback.Text = "请填写规格名称及有效的重量区间。";
                        return;
                    }
                    int color = available[rope.SelectedIndex];
                    AddDraft(name.Text.Trim(), low.Text.Trim(), high.Text.Trim(), color, (int)quantity.Value);
                    draftAudit.Add(new[] { DateTimeOffset.Now.ToString("o"), Basket.Colors[color],
                        quantity.Value.ToString(CultureInfo.InvariantCulture) });
                    dialog.DialogResult = DialogResult.OK;
                };
                dialog.ShowDialog(settingsContent.FindForm() ?? this);
            }
        }

        bool ChooseTaskBarcode()
        {
            using (var dialog = CreateConsoleDialog("选择箱码", new Size(522, 218)))
            {
                At(dialog, Heading("选择本箱条码"), 22, 15, 464, 33);
                At(dialog, Hint("照片中有多个条码，请选择本次装箱使用的条码。"), 22, 51, 464, 24);
                var code = Choice(shownOcr.Barcodes);
                code.SelectedIndex = 0;
                AtInput(dialog, code, 22, 83, 478, 47);
                var cancel = ActionButton("取消");
                cancel.DialogResult = DialogResult.Cancel;
                dialog.CancelButton = cancel;
                At(dialog, cancel, 252, 151, 106, 47);
                var confirm = ActionButton("使用条码");
                SetPrimary(confirm, Blue);
                confirm.DialogResult = DialogResult.OK;
                At(dialog, confirm, 369, 151, 130, 47);
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                taskNumber.Text = code.Text;
                return true;
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
            if (taskNumber.Text.Trim().Length == 0 && shownOcr != null && shownOcr.Barcodes.Length > 1 &&
                !ChooseTaskBarcode()) return;
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
                        File.AppendAllLines(Path.Combine(selected.Folder, "manual-audit.tsv"),
                            draftAudit.Select(row => string.Join("\t", row[0], "", code, row[1], row[2])), Encoding.UTF8);
                        draftAudit.Clear();
                    }
                }
                if (order == null || order.Code != selected.Code)
                {
                    if (simulationOpen && camera != null && camera.Source == CameraSource.Demo && selected.Moves.Count == 0)
                    {
                        demoRemovalDone = false;
                        simulatedScale.Clear();
                        UpdateDemoCount();
                        tare = 0;
                    }
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
            labelPage.Visible = !packing;
            packingPage.Visible = packing;
            if (packing) packingPage.BringToFront();
            else labelPage.BringToFront();
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
            demoReadout.Text = simulationOpen ? packingWeight.Text + " g" : "— g";
            cycleLoads.Text = camera != null && camera.Source != CameraSource.Demo ? "跟随视频" : "自动演示";
            demoHint.Text = !simulationOpen ? "启动模拟秤后可点按投放或取出。" :
                !cycleLoads.Checked ? "手动取放 · 取出按最后放入的绳色扣减" :
                camera != null && camera.Source != CameraSource.Demo ? "按视频中识别到的取放改变模拟重量" :
                packingActive ? "关闭设置后，按工单规格自动演示取放" : "可手动试重，自动演示在装箱页运行";
            packingSimulation.Visible = simulationOpen;
            if (!packingActive && !photoMode && !choosingPhoto && !importingLabel && !labelImage.Visible &&
                !choosingOrder && settingsContent.Parent == null && specifications.Rows.Count == 0 && now >= nextCapture &&
                camera != null && !camera.Ended && camera.Source != CameraSource.Demo &&
                displayed != null && CaptureGate.Elapsed(now, displayed.Tick) <= 1)
            {
                nextCapture = now + 8 * Stopwatch.Frequency;
                CaptureAutomatically();
            }
        }

        async void CaptureAutomatically()
        {
            await CaptureLabelFrame();
            if (!closing && lastCompletedCode != null && shownOcr != null && shownOcr.Barcodes.Length == 1 &&
                shownOcr.Barcodes[0] == lastCompletedCode)
            {
                ClearDraft();
                recognitionStatus.Text = "上一箱已完成 · 请更换箱子";
            }
            if (!closing && !photoMode && specifications.Rows.Count == 0)
            {
                labelImage.Visible = false;
                preview.Visible = true;
            }
        }

        void AdjustTare()
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
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "完成装箱失败");
                return;
            }
            lastCompletedCode = order.Code;
            // Give the operator time to exchange the box before the next automatic capture.
            nextCapture = Stopwatch.GetTimestamp() + 8 * Stopwatch.Frequency;
            ClearDraft();
            ResetOrder();
            recognitionStatus.Text = "装箱完成 · 请识别下一箱";
        }

        void UpdateOrderDisplay()
        {
            if (order == null) return;
            if (shownGrade < 0 && !order.Moves.Any(move => move.Color < 0))
                shownGrade = Array.FindIndex(order.Goals, target => target > 0);
            packingTask.Text = order.Code + (order.Finished ? " · 已完成" : "");
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
            string text = Convert.ToString(e.Value);
            Font font = e.CellStyle.Font ?? Font;
            const TextFormatFlags alignment = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            if (e.ColumnIndex >= 4) ConsolePaint.DrawFittedText(e.Graphics, text, font, bounds, ink, alignment);
            else TextRenderer.DrawText(e.Graphics, text, font, bounds, ink, alignment | TextFormatFlags.EndEllipsis);
            using (var pen = new Pen(Border)) e.Graphics.DrawLine(pen, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
            e.Handled = true;
        }

        void OpenMoveEvidence(int row)
        {
            if (row < 0) return;
            var move = ledger.Rows[row].Tag as PackingMove;
            if (move == null) return;
            string[] paths = { move.Before, move.After, move.Target };
            using (var form = CreateConsoleDialog("取放快照", new Size(944, 389)))
            {
                var images = new List<Image>();
                try
                {
                    for (int i = 0; i < paths.Length; i++)
                    {
                        var image = LoadSnapshot(paths[i], false);
                        if (image != null) images.Add(image);
                        At(form, Hint(new[] { "操作前", "操作后", "绳色目标" }[i]), i * 308 + 16, 52, 296, 26);
                        var photo = new PictureBox { Image = image, SizeMode = PictureBoxSizeMode.Zoom, BackColor = PanelColor };
                        ConsolePaint.RoundEdges(photo, 10);
                        At(form, photo, i * 308 + 16, 81, 296, 234);
                    }
                    At(form, Heading(move.Time.ToString("HH:mm:ss") + " · 取放快照"), 16, 13, 400, 29);
                    var close = ActionButton("关闭");
                    close.DialogResult = DialogResult.Cancel;
                    form.CancelButton = close;
                    At(form, close, 809, 329, 119, 46);
                    if (move.Color < 0 && !order.Finished)
                    {
                        var resolve = ActionButton("核对绳色和数量");
                        SetPrimary(resolve, Blue);
                        resolve.Click += (s, e) => { if (ReviewPendingMove(move, form)) form.Close(); };
                        At(form, resolve, 607, 329, 189, 46);
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
            using (var dialog = CreateConsoleDialog("核对待确认流水", new Size(526, 339)))
            {
                var grade = Choice(Basket.Colors.Select((name, index) => name + " · " + savedNames[index]).ToArray());
                var quantity = Amount(1, 0, 1, 10000, 1);
                var operatorId = new TextBox { MaxLength = 35, Text = enteredBy ?? "" };
                var save = ActionButton("保存核对结果");
                SetPrimary(save, Blue);
                At(dialog, Heading((pending.Delta > 0 ? "放入  +" : "取出  ") + pending.Delta.ToString("F1") + " g"), 23, 15, 462, 32);
                At(dialog, Hint("绳色规格"), 23, 68, 139, 47);
                AtInput(dialog, grade, 167, 68, 335, 47);
                At(dialog, Hint("实际变动只数"), 23, 127, 139, 47);
                AtInput(dialog, quantity, 167, 127, 109, 47);
                At(dialog, Hint("操作员工号"), 23, 186, 139, 47);
                AtInput(dialog, operatorId, 167, 186, 335, 47);
                var cancel = ActionButton("取消");
                cancel.DialogResult = DialogResult.Cancel;
                dialog.CancelButton = cancel;
                At(dialog, cancel, 215, 265, 106, 46);
                At(dialog, save, 333, 265, 169, 46);
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
            settingsContent = new Panel { BackColor = Surface,
                Font = new Font(Font.FontFamily, 21, FontStyle.Regular, GraphicsUnit.Pixel) };
            ConsolePaint.RoundEdges(settingsContent, 11);
            var title = Heading("系统设置");
            title.Font = new Font(Font.FontFamily, 30, FontStyle.Bold, GraphicsUnit.Pixel);
            At(settingsContent, title, 25, 17, 340, 40);
            At(settingsContent, Hint("设备连接、模拟称重与绳色规格"), 25, 58, 500, 25);

            var scaleCard = Module();
            At(scaleCard, SectionTitle("称重设备", 22), 20, 15, 270, 31);
            At(scaleCard, demoScale, 20, 55, 213, 52);
            At(scaleCard, scaleButton, 295, 55, 202, 52);

            serialControls = new Panel();
            AtInput(serialControls, ports, 0, 5, 232, 49);
            AtInput(serialControls, protocol, 243, 5, 234, 49);
            At(scaleCard, serialControls, 20, 124, 478, 62);
            At(scaleCard, serialHint, 20, 193, 476, 28);

            simulationControls = RoundedPanel(Surface, Border, 11);
            demoReadout.ForeColor = Blue;
            demoReadout.Font = new Font("Consolas", 26, FontStyle.Bold, GraphicsUnit.Pixel);
            ((ConsoleLabel)demoReadout).FitText = true;
            At(simulationControls, demoReadout, 14, 9, 153, 43);
            At(simulationControls, cycleLoads, 168, 8, 146, 47);
            At(simulationControls, Hint("在筐数量"), 311, 12, 83, 39);
            AtInput(simulationControls, crabCount, 394, 9, 70, 44);
            for (int row = 0; row < 2; row++)
            {
                bool adding = row == 0;
                int y = 63 + row * 53;
                At(simulationControls, Hint(adding ? "放入" : "取出"), 15, y, 48, 45);
                for (int count = 1; count <= 4; count++)
                {
                    int change = adding ? count : -count;
                    var button = ActionButton((adding ? "+" : "−") + count + " 只");
                    button.Click += (sender, e) => AdjustDemoCrabs(change);
                    At(simulationControls, button, 66 + (count - 1) * 100, y, 93, 45);
                }
            }
            At(simulationControls, demoHint, 14, 173, 452, 30);
            At(scaleCard, simulationControls, 20, 120, 477, 212);
            At(settingsContent, scaleCard, 23, 98, 517, 350);

            var cameraCard = Module();
            At(cameraCard, SectionTitle("视频源", 22), 20, 15, 270, 31);
            AtInput(cameraCard, cameraType, 20, 58, 257, 49);
            At(cameraCard, cameraButton, 295, 57, 202, 52);
            AtInput(cameraCard, address, 20, 121, 307, 49);
            At(cameraCard, browseVideo, 340, 119, 157, 52);
            At(settingsContent, cameraCard, 23, 464, 517, 197);

            var grades = Module();
            At(grades, SectionTitle("绳色与单只重量", 22), 20, 15, 340, 31);
            string[] columns = { "绳色", "规格名称", "最低 (g)", "最高 (g)", "启用" };
            int[] left = { 19, 109, 326, 421, 514 };
            int[] widths = { 76, 202, 82, 82, 61 };
            for (int i = 0; i < columns.Length; i++)
            {
                var header = Hint(columns[i]);
                header.TextAlign = ContentAlignment.MiddleCenter;
                At(grades, header, left[i], 58, widths[i], 34);
            }
            for (int i = 0; i < Basket.Colors.Length; i++)
            {
                string ropeName = Basket.Colors[i];
                productNames[i] = new TextBox { MaxLength = 35, Text = DefaultNames[i], AccessibleName = ropeName + "规格名称" };
                minimumWeights[i] = WeightInput(DefaultMin[i]);
                maximumWeights[i] = WeightInput(DefaultMax[i]);
                minimumWeights[i].AccessibleName = ropeName + "最低重量";
                maximumWeights[i].AccessibleName = ropeName + "最高重量";
                minimumWeights[i].Font = maximumWeights[i].Font = settingsContent.Font;
                int top = 99 + i * 67;
                var rope = Note(ropeName);
                SetBadge(rope, RopeFill[i], RopeInk[i]);
                At(grades, rope, left[0], top + 3, widths[0], 47);
                AtInput(grades, productNames[i], left[1], top, widths[1], 52);
                AtInput(grades, minimumWeights[i], left[2], top, widths[2], 52);
                AtInput(grades, maximumWeights[i], left[3], top, widths[3], 52);
                int color = i;
                ruleEnabled[color] = new CheckBox { Checked = true, AccessibleName = "启用" + ropeName,
                    CheckAlign = ContentAlignment.MiddleCenter };
                ruleEnabled[color].CheckedChanged += (sender, e) =>
                    productNames[color].Enabled = minimumWeights[color].Enabled = maximumWeights[color].Enabled = ruleEnabled[color].Checked;
                At(grades, ruleEnabled[color], 520, top + 2, 49, 48);
            }
            var useRule = ActionButton("添加规格到识别页");
            useRule.Click += (sender, e) => StartManualGrade();
            At(grades, useRule, 19, 460, 227, 52);
            At(settingsContent, grades, 555, 98, 590, 563);
        }

        Form CreateConsoleDialog(string title, Size size)
        {
            var dialog = new Form { Text = title, ClientSize = size, StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.None, AutoScaleMode = AutoScaleMode.None,
                BackColor = Surface, ForeColor = Ink, Font = settingsContent.Font };
            ConsolePaint.RoundEdges(dialog, 14);
            dialog.Paint += (sender, e) => ConsolePaint.Tile(e.Graphics, dialog.ClientRectangle, 14, Surface, Blue, 3);
            return dialog;
        }

        void OpenSettings()
        {
            if (settingsContent.Parent != null) return;
            choosingOrder = true;
            using (var form = CreateConsoleDialog("系统配置", new Size(1168, 740)))
            {
                At(form, settingsContent, 3, 3, 1162, 668);
                var save = ActionButton("保存配置并生效");
                SetPrimary(save, Blue);
                var close = ActionButton("关闭");
                close.DialogResult = DialogResult.Cancel;
                form.CancelButton = close;
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
                At(form, referenceStatus, 25, 690, 460, 30);
                At(form, close, 820, 678, 121, 50);
                At(form, save, 956, 678, 193, 50);
                try
                {
                    UpdateScaleInputs();
                    UpdateCameraInputs();
                    form.ShowDialog(this);
                }
                finally { form.Controls.Remove(settingsContent); form.Controls.Remove(referenceStatus); choosingOrder = false; }
            }
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

    }
}
