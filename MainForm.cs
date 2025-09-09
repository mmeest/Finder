using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinFormsFileSearcher
{
    public partial class MainForm : Form
    {
        private CancellationTokenSource cts;

        public MainForm()
        {
            Text = "WinForms File Searcher";
            Width = 1100;
            Height = 700;
            StartPosition = FormStartPosition.CenterScreen;
            InitializeComponent();
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = "Select folder to search";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    txtPath.Text = dlg.SelectedPath;
                }
            }
        }

        private async void BtnSearch_Click(object sender, EventArgs e)
        {
            var path = txtPath.Text.Trim();
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            {
                MessageBox.Show("Please select an existing folder.", "Folder missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnSearch.Enabled = false;
            btnCancel.Enabled = true;
            cts = new CancellationTokenSource();

            var options = new SearchOptions
            {
                RootPath = path,
                NameFilter = txtName.Text.Trim(),
                Extensions = ParseExtensions(txtExt.Text),
                DateFrom = dtpFrom.Value.Date,
                DateTo = dtpTo.Value.Date.AddDays(1).AddTicks(-1), // include full day
                IncludeSubdirectories = chkSubdirs.Checked,
                ContentSearch = txtContent.Text.Trim()
            };

            var list = new BindingList<SearchResult>();
            dgvResults.DataSource = list;

            var progress = new Progress<SearchProgress>(p =>
            {
                progressBar.Style = p.TotalFiles > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                if (p.TotalFiles > 0)
                {
                    progressBar.Maximum = (int)Math.Min(p.TotalFiles, int.MaxValue);
                    progressBar.Value = (int)Math.Min(p.ProcessedFiles, progressBar.Maximum);
                }
                lblStatus.Text = p.Message;
            });

            try
            {
                var results = await SearchEngine.SearchAsync(options, progress, cts.Token);
                foreach (var r in results)
                    list.Add(r);

                lblStatus.Text = $"Done. {results.Count} results.";
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Canceled.";
            }
            catch (Exception ex)
            {
                MessageBox.Show("An error occurred: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                lblStatus.Text = "Error";
            }
            finally
            {
                btnSearch.Enabled = true;
                btnCancel.Enabled = false;
                cts = null;
            }
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            btnCancel.Enabled = false;
            cts?.Cancel();
        }

        private void DgvResults_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                var item = dgvResults.Rows[e.RowIndex].DataBoundItem as SearchResult;
                if (item != null && File.Exists(item.Path))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.Path) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show("Unable to open file: " + ex.Message);
                    }
                }
            }
        }

        private List<string> ParseExtensions(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            return raw.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                      .Select(s => s.Trim().StartsWith(".") ? s.Trim().ToLowerInvariant() : "." + s.Trim().ToLowerInvariant())
                      .Distinct().ToList();
        }
    }
}