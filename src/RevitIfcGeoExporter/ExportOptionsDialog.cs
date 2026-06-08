using System;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.Revit.DB;

namespace BimRoss.RevitIfcGeoExporter
{
    /// <summary>
    /// WinForms options dialog. Programmatic (no .resx) so the whole UI lives
    /// in one file alongside the command — easy to read, easy to extend.
    ///
    /// WinForms picked over WPF because Revit always ships System.Windows.Forms
    /// loaded; no extra references needed.
    /// </summary>
    internal class ExportOptionsDialog : Form
    {
        private readonly ComboBox _detailLevel;
        private readonly TrackBar _triangulation;
        private readonly Label _triangulationLabel;
        private readonly CheckBox _useInstanceReuse;
        private readonly CheckBox _emitMaterials;
        private readonly CheckBox _emitPropertySets;
        private readonly CheckBox _emitGeoreferencing;
        private readonly CheckBox _includeLinkedModels;
        private readonly NumericUpDown _warningMb;

        public ExportOptions Result { get; private set; }

        public ExportOptionsDialog(ExportOptions current)
        {
            Text = "Export Selection to IFC — Options";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(440, 360);
            Font = new Font("Segoe UI", 9F);

            int y = 12;
            const int labelW = 160;
            const int controlX = 180;
            const int controlW = 240;

            Controls.Add(new Label { Text = "Detail level:", Location = new Point(12, y + 3), Size = new Size(labelW, 20) });
            _detailLevel = new ComboBox
            {
                Location = new Point(controlX, y),
                Size = new Size(controlW, 24),
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            _detailLevel.Items.AddRange(new object[] { ViewDetailLevel.Coarse, ViewDetailLevel.Medium, ViewDetailLevel.Fine });
            _detailLevel.SelectedItem = current.DetailLevel;
            Controls.Add(_detailLevel);
            y += 32;

            Controls.Add(new Label { Text = "Triangulation tolerance:", Location = new Point(12, y + 3), Size = new Size(labelW, 20) });
            _triangulation = new TrackBar
            {
                Location = new Point(controlX, y - 4),
                Size = new Size(controlW - 50, 36),
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                Value = (int)Math.Round(Math.Max(0, Math.Min(1, current.TriangulationLevel)) * 100),
            };
            _triangulationLabel = new Label
            {
                Location = new Point(controlX + controlW - 44, y + 3),
                Size = new Size(44, 20),
                Text = _triangulation.Value.ToString() + "%",
                TextAlign = ContentAlignment.MiddleRight,
            };
            _triangulation.ValueChanged += (s, e) => _triangulationLabel.Text = _triangulation.Value + "%";
            Controls.Add(_triangulation);
            Controls.Add(_triangulationLabel);
            y += 42;

            _useInstanceReuse = AddCheck("Reuse repeated family geometry (IfcMappedItem)", current.UseInstanceReuse, ref y);
            _emitMaterials = AddCheck("Emit materials and colors", current.EmitMaterials, ref y);
            _emitPropertySets = AddCheck("Emit property sets (Pset_*)", current.EmitPropertySets, ref y);
            _emitGeoreferencing = AddCheck("Emit georeferencing (IfcMapConversion)", current.EmitGeoreferencing, ref y);
            _includeLinkedModels = AddCheck("Include selected elements in linked models", current.IncludeLinkedModels, ref y);

            y += 8;
            Controls.Add(new Label { Text = "Warn when output exceeds (MB):", Location = new Point(12, y + 3), Size = new Size(labelW + 20, 20) });
            _warningMb = new NumericUpDown
            {
                Location = new Point(controlX + 20, y),
                Size = new Size(80, 24),
                Minimum = 1,
                Maximum = 10000,
                Value = Math.Max(1, Math.Min(10000, current.LargeFileWarningMb)),
            };
            Controls.Add(_warningMb);

            var ok = new Button
            {
                Text = "Export",
                Location = new Point(ClientSize.Width - 200, ClientSize.Height - 36),
                Size = new Size(85, 26),
                DialogResult = DialogResult.OK,
            };
            ok.Click += (s, e) => { Result = Collect(); Close(); };
            Controls.Add(ok);

            var cancel = new Button
            {
                Text = "Cancel",
                Location = new Point(ClientSize.Width - 100, ClientSize.Height - 36),
                Size = new Size(85, 26),
                DialogResult = DialogResult.Cancel,
            };
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private CheckBox AddCheck(string label, bool initial, ref int y)
        {
            var cb = new CheckBox
            {
                Text = label,
                Location = new Point(14, y),
                Size = new Size(410, 22),
                Checked = initial,
            };
            Controls.Add(cb);
            y += 26;
            return cb;
        }

        private ExportOptions Collect() => new ExportOptions
        {
            DetailLevel = (ViewDetailLevel)(_detailLevel.SelectedItem ?? ViewDetailLevel.Fine),
            TriangulationLevel = _triangulation.Value / 100.0,
            UseInstanceReuse = _useInstanceReuse.Checked,
            EmitMaterials = _emitMaterials.Checked,
            EmitPropertySets = _emitPropertySets.Checked,
            EmitGeoreferencing = _emitGeoreferencing.Checked,
            IncludeLinkedModels = _includeLinkedModels.Checked,
            LargeFileWarningMb = (int)_warningMb.Value,
        };
    }
}
