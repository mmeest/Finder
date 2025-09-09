using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace WinFormsFileSearcher
{
    public partial class MainForm : Form
    {
        // Controls
        private TextBox txtPath;
        private Button btnBrowse;
        private TextBox txtName;
        private TextBox txtExt;
        private DateTimePicker dtpFrom;
        private DateTimePicker dtpTo;
        private CheckBox chkSubdirs;
        private Button btnSearch;
        private Button btnCancel;
        private DataGridView dgvResults;
        private ProgressBar progressBar;
        private Label lblStatus;
        private TextBox txtContent;
        private SplitContainer splitContainer;
        private TableLayoutPanel filtersPanel;

        private void InitializeComponent()
        {
            // Top controls
            txtPath = new TextBox { Width = 500, Anchor = AnchorStyles.Left | AnchorStyles.Right };
            btnBrowse = new Button { Text = "Browse...", AutoSize = true };
            btnBrowse.Click += BtnBrowse_Click;

            chkSubdirs = new CheckBox { Text = "Include subfolders", Checked = true, AutoSize = true };

            txtName = new TextBox { Width = 200 };
            txtExt = new TextBox { Width = 200 };

            dtpFrom = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 120 };
            dtpTo = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Width = 120 };
            dtpFrom.Value = DateTime.Now.AddYears(-1);
            dtpTo.Value = DateTime.Now;

            btnSearch = new Button { Text = "Search", AutoSize = true };
            btnSearch.Click += BtnSearch_Click;
            btnCancel = new Button { Text = "Cancel", AutoSize = true, Enabled = false };
            btnCancel.Click += BtnCancel_Click;

            txtContent = new TextBox { Width = 400 };

            progressBar = new ProgressBar { Width = 300, Style = ProgressBarStyle.Continuous };
            lblStatus = new Label { Text = "Ready", AutoSize = true };

            // Use TableLayoutPanel for better control
            var tableLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
            tableLayout.ColumnCount = 8;
            tableLayout.RowCount = 3;
            
            // Set column styles
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));  // Path label
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F)); // Path textbox
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));  // Browse button
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120)); // Checkbox
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50));  // Name label
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200)); // Name textbox
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));  // Extension label
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200)); // Extension textbox
            
            // Row 1: Path and Browse
            tableLayout.Controls.Add(new Label { Text = "Path:", TextAlign = ContentAlignment.MiddleRight }, 0, 0);
            tableLayout.Controls.Add(txtPath, 1, 0);
            tableLayout.Controls.Add(btnBrowse, 2, 0);
            tableLayout.Controls.Add(chkSubdirs, 3, 0);
            
            // Row 2: Name, Extension, Date
            tableLayout.Controls.Add(new Label { Text = "Name:", TextAlign = ContentAlignment.MiddleRight }, 0, 1);
            tableLayout.Controls.Add(txtName, 1, 1);
            tableLayout.Controls.Add(new Label { Text = "Extension:", TextAlign = ContentAlignment.MiddleRight }, 2, 1);
            tableLayout.Controls.Add(txtExt, 3, 1);
            tableLayout.Controls.Add(new Label { Text = "Date from:", TextAlign = ContentAlignment.MiddleRight }, 4, 1);
            tableLayout.Controls.Add(dtpFrom, 5, 1);
            tableLayout.Controls.Add(new Label { Text = "to", TextAlign = ContentAlignment.MiddleCenter }, 6, 1);
            tableLayout.Controls.Add(dtpTo, 7, 1);
            
            // Row 3: Content and buttons
            tableLayout.Controls.Add(new Label { Text = "Content:", TextAlign = ContentAlignment.MiddleRight }, 0, 2);
            tableLayout.Controls.Add(txtContent, 1, 2);
            tableLayout.Controls.Add(btnSearch, 2, 2);
            tableLayout.Controls.Add(btnCancel, 3, 2);
            tableLayout.Controls.Add(progressBar, 4, 2);
            tableLayout.Controls.Add(lblStatus, 5, 2);
            
            filtersPanel = tableLayout;

            // Split container for results and preview
            splitContainer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 380 };

            dgvResults = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoGenerateColumns = false };
            dgvResults.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            dgvResults.MultiSelect = false;
            dgvResults.CellDoubleClick += DgvResults_CellDoubleClick;

            // Setup columns
            dgvResults.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Name", HeaderText = "Name", Width = 300 });
            dgvResults.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Path", HeaderText = "Path", Width = 300 });
            dgvResults.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Extension", HeaderText = "Ext", Width = 60 });
            dgvResults.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Size", HeaderText = "Size (bytes)", Width = 90 });
            dgvResults.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Modified", HeaderText = "Modified", Width = 130 });
            dgvResults.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Attributes", HeaderText = "Attributes", Width = 120 });

            splitContainer.Panel1.Controls.Add(dgvResults);

            // Preview text box
            var previewPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            var lblPreview = new Label { Text = "Preview / Snippet:", Dock = DockStyle.Top };
            var txtPreview = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both, ReadOnly = true }; // will be updated

            previewPanel.Controls.Add(txtPreview);
            previewPanel.Controls.Add(lblPreview);

            splitContainer.Panel2.Controls.Add(previewPanel);

            // Keep a reference to update preview
            dgvResults.SelectionChanged += (s, e) =>
            {
                if (dgvResults.SelectedRows.Count > 0)
                {
                    var item = dgvResults.SelectedRows[0].DataBoundItem;
                    if (item != null)
                    {
                        var previewProperty = item.GetType().GetProperty("PreviewSnippet");
                        txtPreview.Text = previewProperty?.GetValue(item)?.ToString() ?? "";
                    }
                }
            };

            // Layout
            Controls.Add(splitContainer);
            Controls.Add(filtersPanel);

            // Setup data-binding - will be set in MainForm.cs
            dgvResults.DataSource = new System.ComponentModel.BindingList<object>();
        }
    }
}