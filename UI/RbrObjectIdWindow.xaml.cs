using Microsoft.Win32;
using RB_TypeName.Handlers;
using RB_TypeName.Models;
using RB_TypeName.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Autodesk.Revit.UI;

namespace RB_TypeName.UI
{
    public partial class RbrObjectIdWindow : Window
    {
        private static readonly string SettingsFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RK Tools", "RB_TypeName");

        private static readonly string SettingsFile =
            Path.Combine(SettingsFolder, "settings.json");

        // ── Handlers / events ────────────────────────────────────────────────

        private readonly AssignRbrObjectIdsHandler  _assignHandler;
        private readonly ExternalEvent              _assignEvent;

        private readonly PreviewRbrObjectIdsHandler _previewHandler;
        private readonly ExternalEvent              _previewEvent;

        private readonly ApplyRbrObjectIdsHandler   _applyHandler;
        private readonly ExternalEvent              _applyEvent;

        private readonly LoadRbrTypeGroupsHandler       _loadTypesHandler;
        private readonly ExternalEvent                  _loadTypesEvent;

        private readonly PreviewRbrTypeNumbersHandler   _previewTypeNumHandler;
        private readonly ExternalEvent                  _previewTypeNumEvent;

        private readonly ApplyRbrTypeNumbersHandler     _applyTypeNumHandler;
        private readonly ExternalEvent                  _applyTypeNumEvent;

        private readonly SaveTypeNumberMappingsHandler  _saveMappingsHandler;
        private readonly ExternalEvent                  _saveMappingsEvent;

        private readonly ImportTypeNumberMappingsHandler _importMappingsHandler;
        private readonly ExternalEvent                   _importMappingsEvent;

        private readonly ExportTypeNumberMappingsHandler _exportMappingsHandler;
        private readonly ExternalEvent                   _exportMappingsEvent;

        private readonly ClearObjectIdsHandler           _clearObjectIdsHandler;
        private readonly ExternalEvent                   _clearObjectIdsEvent;

        private readonly PreviewRbrObjectIdReconcileHandler _reconcilePreviewHandler;
        private readonly ExternalEvent                      _reconcilePreviewEvent;

        private readonly ApplyRbrObjectIdReconcileHandler   _reconcileApplyHandler;
        private readonly ExternalEvent                      _reconcileApplyEvent;

        // ── State ────────────────────────────────────────────────────────────

        private PbsExcelSourceSettings  _settings;
        private List<ObjectIdPreviewRow> _previewRows;
        private bool _isLoadingSettings;

        private List<ObjectIdReconcileRow>            _reconcileRows;

        private List<TypeNumberPreviewRow>            _typeRows;
        private Dictionary<string, PbsTypeNumberRow>  _typeNumberLookup;
        private PbsPrCodeLookupService                _prCodeLookupForTypeNumbers;
        private string                                 _currentDocPath        = string.Empty;
        private string                                 _currentTypeSourceParam = "Revit Type Name";

        // ── Constructor ──────────────────────────────────────────────────────

        public RbrObjectIdWindow(
            AssignRbrObjectIdsHandler      assignHandler,       ExternalEvent assignEvent,
            PreviewRbrObjectIdsHandler     previewHandler,      ExternalEvent previewEvent,
            ApplyRbrObjectIdsHandler       applyHandler,        ExternalEvent applyEvent,
            LoadRbrTypeGroupsHandler       loadTypesHandler,    ExternalEvent loadTypesEvent,
            PreviewRbrTypeNumbersHandler   previewTypeNumHandler, ExternalEvent previewTypeNumEvent,
            ApplyRbrTypeNumbersHandler     applyTypeNumHandler, ExternalEvent applyTypeNumEvent,
            SaveTypeNumberMappingsHandler  saveMappingsHandler,  ExternalEvent saveMappingsEvent,
            ImportTypeNumberMappingsHandler importMappingsHandler, ExternalEvent importMappingsEvent,
            ExportTypeNumberMappingsHandler exportMappingsHandler, ExternalEvent exportMappingsEvent,
            ClearObjectIdsHandler           clearObjectIdsHandler, ExternalEvent clearObjectIdsEvent,
            PreviewRbrObjectIdReconcileHandler reconcilePreviewHandler, ExternalEvent reconcilePreviewEvent,
            ApplyRbrObjectIdReconcileHandler   reconcileApplyHandler,   ExternalEvent reconcileApplyEvent)
        {
            InitializeComponent();

            _assignHandler  = assignHandler;
            _assignEvent    = assignEvent;
            _previewHandler = previewHandler;
            _previewEvent   = previewEvent;
            _applyHandler   = applyHandler;
            _applyEvent     = applyEvent;

            _loadTypesHandler     = loadTypesHandler;
            _loadTypesEvent       = loadTypesEvent;
            _previewTypeNumHandler = previewTypeNumHandler;
            _previewTypeNumEvent  = previewTypeNumEvent;
            _applyTypeNumHandler  = applyTypeNumHandler;
            _applyTypeNumEvent    = applyTypeNumEvent;
            _saveMappingsHandler   = saveMappingsHandler;
            _saveMappingsEvent     = saveMappingsEvent;
            _importMappingsHandler = importMappingsHandler;
            _importMappingsEvent   = importMappingsEvent;
            _exportMappingsHandler = exportMappingsHandler;
            _exportMappingsEvent   = exportMappingsEvent;
            _clearObjectIdsHandler = clearObjectIdsHandler;
            _clearObjectIdsEvent   = clearObjectIdsEvent;

            _reconcilePreviewHandler = reconcilePreviewHandler;
            _reconcilePreviewEvent   = reconcilePreviewEvent;
            _reconcileApplyHandler   = reconcileApplyHandler;
            _reconcileApplyEvent     = reconcileApplyEvent;

            _assignHandler.OnCompleted = (results, index) =>
                Dispatcher.Invoke(() => ShowAssignResults(results, index));

            _previewHandler.OnCompleted = (rows, index) =>
                Dispatcher.Invoke(() => ShowPreviewResults(rows, index));

            _applyHandler.OnCompleted = (assigned, skipped, errors) =>
                Dispatcher.Invoke(() => ShowApplyResults(assigned, skipped, errors));

            _loadTypesHandler.OnCompleted = (rows, docPath, paramNames) =>
                Dispatcher.Invoke(() => ShowTypeGroups(rows, docPath, paramNames));

            _previewTypeNumHandler.OnCompleted = (rows, index) =>
                Dispatcher.Invoke(() => ShowTypeNumberPreview(rows, index));

            _applyTypeNumHandler.OnCompleted = (assigned, skipped, errors) =>
                Dispatcher.Invoke(() => ShowTypeNumberApply(assigned, skipped, errors));

            _saveMappingsHandler.OnCompleted = (count, err) =>
                Dispatcher.Invoke(() =>
                {
                    SaveMappingsButton.IsEnabled = _typeRows?.Count > 0;
                    TypeNumberStatusText.Text = err != null
                        ? "Save failed: " + err
                        : $"Saved {count} mapping(s) to model.";

                    // Mark rows as saved where L1Code is valid.
                    if (_typeRows != null && err == null)
                    {
                        foreach (var r in _typeRows)
                        {
                            if (!string.IsNullOrWhiteSpace(r.L1Code) && !r.HasSavedMapping)
                                r.ApplyL1CodeFromMapping(r.L1Code, "Saved", hasSavedMapping: true);
                        }
                    }
                });

            _importMappingsHandler.OnCompleted = (upsertResult, err) =>
                Dispatcher.Invoke(() => HandleImportCompleted(upsertResult, err));

            _exportMappingsHandler.OnCompleted = (success, err) =>
                Dispatcher.Invoke(() => HandleExportMappingCompleted(success, err));

            _clearObjectIdsHandler.OnCompleted = (count, err) =>
                Dispatcher.Invoke(() =>
                {
                    ClearObjectIdsButton.IsEnabled = true;
                    if (err != null)
                        SetStatus("Clear failed: " + err, isError: true);
                    else
                        SetStatus($"Cleared {count} Object ID(s) from project.", isError: false);
                });

            _reconcilePreviewHandler.OnCompleted = result =>
                Dispatcher.Invoke(() => ShowReconcileResults(result));

            _reconcileApplyHandler.OnCompleted = (applied, skipped, errors) =>
                Dispatcher.Invoke(() => ShowApplyReconcileResults(applied, skipped, errors));

            LoadSettings();
        }

        // ── Settings ─────────────────────────────────────────────────────────

        private void LoadSettings()
        {
            _isLoadingSettings = true;

            try
            {
                _settings = PbsExcelSourceSettings.LoadFromFile(SettingsFile);

                UseRbrPrCodeLookupCheck.IsChecked       = _settings.UseRbrPrCodeLookup;
                UseManualMappingFallbackCheck.IsChecked = _settings.UseManualMappingFallback;

                if (!string.IsNullOrEmpty(_settings.ExcelPath))
                {
                    PbsFilePathBox.Text = _settings.ExcelPath;

                    if (File.Exists(_settings.ExcelPath))
                        ApplyLoadResult(PbsMappingService.Load(_settings));
                    else
                        SetStatus("PBS file not found: " + _settings.ExcelPath, isError: true);
                }
            }
            finally
            {
                _isLoadingSettings = false;
            }
        }

        private void OptionCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoadingSettings || _settings == null)
                return;

            SaveSettings();
            ResetPreviewState();
        }

        private void SaveSettings()
        {
            _settings.UseRbrPrCodeLookup      = UseRbrPrCodeLookupCheck.IsChecked == true;
            _settings.UseManualMappingFallback = UseManualMappingFallbackCheck.IsChecked == true;

            try { Directory.CreateDirectory(SettingsFolder); } catch { }
            _settings.SaveToFile(SettingsFile);
        }

        // ── PBS File ─────────────────────────────────────────────────────────

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Select PBS Excel file",
                Filter = "Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
            };

            if (!string.IsNullOrEmpty(_settings.ExcelPath) && File.Exists(_settings.ExcelPath))
                dlg.InitialDirectory = Path.GetDirectoryName(_settings.ExcelPath);

            if (dlg.ShowDialog() != true) return;

            _settings.ExcelPath = dlg.FileName;
            PbsFilePathBox.Text = dlg.FileName;
            SaveSettings();

            ApplyLoadResult(PbsMappingService.Load(_settings));
            ResetPreviewState();
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_settings?.ExcelPath))
            {
                SetStatus("No PBS file selected.", isError: true);
                return;
            }

            string previousCode = (MappingCombo.SelectedItem as PbsMapping)?.PbsCode;
            ApplyLoadResult(PbsMappingService.Load(_settings), restoreCode: previousCode);
            ResetPreviewState();
        }

        private void ApplyLoadResult(PbsLoadResult result, string restoreCode = null)
        {
            MappingCombo.ItemsSource = null;

            if (!result.Success)
            {
                SetStatus(result.ErrorMessage, isError: true);
                AssignButton.IsEnabled = false;
                return;
            }

            MappingCombo.ItemsSource = result.Mappings;

            int idx = restoreCode != null
                ? result.Mappings.FindIndex(m =>
                    string.Equals(m.PbsCode, restoreCode, StringComparison.OrdinalIgnoreCase))
                : -1;

            MappingCombo.SelectedIndex = idx >= 0 ? idx : 0;

            SetStatus(result.Mappings.Count + " mapping(s) loaded.", isError: false);
            AssignButton.IsEnabled = true;

            // Rebuild type-number lookup and discipline combo for Tab 2.
            RebuildTypeNumberLookup();
            PopulateDisciplineCombo();
        }

        private void SetStatus(string text, bool isError)
        {
            MappingStatusText.Text       = text;
            MappingStatusText.Foreground = isError
                ? Brushes.OrangeRed
                : Brushes.Gray;
        }

        // ── Preview ──────────────────────────────────────────────────────────

        private void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(_settings?.ExcelPath ?? string.Empty))
            {
                MessageBox.Show(
                    "PBS Excel file not found.\nPlease select a valid file first.",
                    "Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SaveSettings();

            _previewHandler.UsePrCodeLookup          = _settings.UseRbrPrCodeLookup;
            _previewHandler.UseManualMappingFallback  = _settings.UseManualMappingFallback;
            _previewHandler.ManualFallbackMapping     = MappingCombo.SelectedItem as PbsMapping;

            if (_settings.UseRbrPrCodeLookup)
            {
                var (success, error, prCodeRows) = PbsMappingService.LoadPrCodeRows(_settings);
                if (!success)
                {
                    MessageBox.Show(error, "PBS Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _previewHandler.LookupService = new PbsPrCodeLookupService(prCodeRows);
            }
            else
            {
                _previewHandler.LookupService = null;

                if (MappingCombo.SelectedItem is not PbsMapping)
                {
                    MessageBox.Show(
                        "Please load the PBS file and select a manual PBS mapping first.",
                        "Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            SetButtons(previewEnabled: false, applyEnabled: false, assignEnabled: false, exportEnabled: false);
            SummaryText.Text           = "Building preview\u2026";
            ResultsGrid.ItemsSource    = null;
            WarningsBorder.Visibility  = Visibility.Collapsed;
            _previewRows               = null;

            _previewEvent.Raise();
        }

        private void ShowPreviewResults(List<ObjectIdPreviewRow> rows, RbrObjectIdIndex index)
        {
            _previewRows            = rows;
            ResultsGrid.ItemsSource = rows;

            if (rows == null || rows.Count == 0)
            {
                SummaryText.Text = "No elements in current Revit selection.";
                SetButtons(previewEnabled: true, applyEnabled: false, assignEnabled: true, exportEnabled: false);
                return;
            }

            int ready    = rows.Count(r => r.Status == "Ready");
            int existing = rows.Count(r => r.Status == "Already has Object ID");
            int issues   = rows.Count(r => r.Status != "Ready" && r.Status != "Already has Object ID");

            SummaryText.Text = $"Ready: {ready}   |   Already set: {existing}   |   Issues: {issues}";

            SetButtons(
                previewEnabled: true,
                applyEnabled:   ready > 0,
                assignEnabled:  true,
                exportEnabled:  rows.Count > 0);

            ShowIndexWarnings(index);
        }

        // ── Apply ────────────────────────────────────────────────────────────

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (_previewRows == null || _previewRows.Count == 0)
            {
                MessageBox.Show("Please run Preview first.", "Apply",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var toApply = _previewRows
                .Where(r => r.IsSelected && r.IsReady)
                .ToList();

            if (toApply.Count == 0)
            {
                MessageBox.Show("No Ready rows are selected to apply.", "Apply",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _applyHandler.RowsToApply = toApply;

            SetButtons(previewEnabled: false, applyEnabled: false, assignEnabled: false, exportEnabled: false);
            SummaryText.Text          = "Applying…";
            WarningsBorder.Visibility = Visibility.Collapsed;

            _applyEvent.Raise();
        }

        private void ShowApplyResults(int assigned, int skipped, List<string> errors)
        {
            // Refresh grid to show updated statuses (status objects were mutated in-place).
            var rows = _previewRows;
            ResultsGrid.ItemsSource = null;
            ResultsGrid.ItemsSource = rows;

            SummaryText.Text = $"Assigned: {assigned}   |   Skipped: {skipped}   |   Errors: {errors.Count}";

            SetButtons(
                previewEnabled: true,
                applyEnabled:   false,   // Prevent double-apply; run Preview again if needed.
                assignEnabled:  true,
                exportEnabled:  rows != null && rows.Count > 0);

            if (errors.Count > 0)
            {
                WarningsText.Text         = string.Join("\n", errors);
                WarningsBorder.Visibility = Visibility.Visible;
            }
        }

        // ── Assign (manual fallback) ─────────────────────────────────────────

        private void Assign_Click(object sender, RoutedEventArgs e)
        {
            if (MappingCombo.SelectedItem is not PbsMapping mapping)
            {
                MessageBox.Show(
                    "Please load the PBS file and select a mapping first.",
                    "RBR Object ID", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(_settings?.ExcelPath ?? string.Empty))
            {
                MessageBox.Show(
                    "PBS Excel file was not found:\n" + (_settings?.ExcelPath ?? "(none)") +
                    "\n\nPlease select a valid file before assigning IDs.",
                    "RBR Object ID", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _assignHandler.SelectedMapping = mapping;

            SetButtons(previewEnabled: false, applyEnabled: false, assignEnabled: false, exportEnabled: false);
            SummaryText.Text          = "Running manual assignment…";
            ResultsGrid.ItemsSource   = null;
            WarningsBorder.Visibility = Visibility.Collapsed;
            _previewRows              = null;

            _assignEvent.Raise();
        }

        private void ShowAssignResults(List<RbrIdAssignmentResult> results, RbrObjectIdIndex index)
        {
            // Convert RbrIdAssignmentResult → ObjectIdPreviewRow for the shared grid.
            var rows = results?.Select(r => new ObjectIdPreviewRow
            {
                ElementId        = r.ElementId,
                ExistingObjectId = r.OldValue ?? string.Empty,
                ProposedObjectId = r.NewValue ?? string.Empty,
                Status           = MapAssignStatus(r.Status),
                Message          = r.Message ?? string.Empty,
                IsSelected       = false, // Already applied — nothing to select.
            }).ToList() ?? new List<ObjectIdPreviewRow>();

            _previewRows            = rows;
            ResultsGrid.ItemsSource = rows;

            int assigned = results?.Count(r => r.Status == "Assigned")     ?? 0;
            int skipped  = results?.Count(r => r.Status == "SkippedExisting") ?? 0;
            int issues   = results?.Count(r => r.Status != "Assigned"
                                            && r.Status != "SkippedExisting") ?? 0;

            SummaryText.Text = $"Assigned: {assigned}   |   Skipped (existing): {skipped}   |   Issues: {issues}";

            SetButtons(
                previewEnabled: true,
                applyEnabled:   false,
                assignEnabled:  true,
                exportEnabled:  rows.Count > 0);

            ShowIndexWarnings(index);
        }

        // ── Export ───────────────────────────────────────────────────────────

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            if (_previewRows == null || _previewRows.Count == 0) return;

            var dlg = new SaveFileDialog
            {
                Title      = "Export RBR Object ID Report",
                Filter     = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName   = $"RBR_ObjectID_Report_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExt = ".csv",
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Timestamp,ElementId,Category,FamilyName,TypeName," +
                              "Level,LevelCode,RBR_Pr_Code,PBS_R,PBS_S," +
                              "ExistingObjectId,ProposedObjectId,Status,Message");

                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                foreach (var row in _previewRows)
                {
                    sb.AppendLine(
                        $"{Csv(ts)}," +
                        $"{row.ElementIdValue}," +
                        $"{Csv(row.Category)}," +
                        $"{Csv(row.FamilyName)}," +
                        $"{Csv(row.TypeName)}," +
                        $"{Csv(row.LevelName)}," +
                        $"{Csv(row.LevelCode)}," +
                        $"{Csv(row.RbrPrCode)}," +
                        $"{Csv(row.PbsPartR)}," +
                        $"{Csv(row.PbsPartS)}," +
                        $"{Csv(row.ExistingObjectId)}," +
                        $"{Csv(row.ProposedObjectId)}," +
                        $"{Csv(row.Status)}," +
                        $"{Csv(row.Message)}");
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("Report exported to:\n" + dlg.FileName,
                    "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed:\n" + ex.Message,
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MappingCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isLoadingSettings)
                return;

            ResetPreviewState();
        }

        private void ClearObjectIds_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "This will remove ALL RBR-Object_ID values from every element in the project.\n\n" +
                "This cannot be undone without Revit's own Undo history.\n\n" +
                "Are you sure you want to continue?",
                "Clear All Object IDs",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            ClearObjectIdsButton.IsEnabled = false;
            SetStatus("Clearing Object IDs…", isError: false);
            _clearObjectIdsEvent.Raise();
        }

        // ── Reconcile workflow ────────────────────────────────────────────────

        private void UpdateIds_Click(object sender, RoutedEventArgs e)
        {
            // Ensure lookup service is initialised from PBS file.
            var lookupService = BuildLookupService();
            if (lookupService == null)
            {
                SetStatus("Load a PBS file before running Update IDs.", isError: true);
                return;
            }

            var options = BuildReconcileOptions();

            _reconcilePreviewHandler.Options       = options;
            _reconcilePreviewHandler.LookupService = lookupService;

            UpdateIdsButton.IsEnabled       = false;
            ApplyReconcileButton.IsEnabled  = false;
            ReconcileStatusText.Text        = "Building reconcile preview…";

            // Hide assign-workflow grids, show reconcile grid.
            ResultsGrid.Visibility          = Visibility.Collapsed;
            SummaryText.Visibility          = Visibility.Collapsed;
            ReconcileGrid.Visibility        = Visibility.Visible;
            ReconcileSummaryText.Visibility = Visibility.Visible;

            _reconcilePreviewEvent.Raise();
        }

        private void ApplyReconcile_Click(object sender, RoutedEventArgs e)
        {
            if (_reconcileRows == null || _reconcileRows.Count == 0)
                return;

            var toApply = _reconcileRows
                .Where(r => r.IsSelected && r.IsWriteAllowed
                            && !string.IsNullOrWhiteSpace(r.ProposedObjectId))
                .ToList();

            if (toApply.Count == 0)
            {
                ReconcileStatusText.Text = "No rows selected with a proposed ID.";
                return;
            }

            var lookupService = BuildLookupService();
            if (lookupService == null)
            {
                SetStatus("Load a PBS file before applying.", isError: true);
                return;
            }

            _reconcileApplyHandler.RowsToApply   = toApply;
            _reconcileApplyHandler.Options        = BuildReconcileOptions();
            _reconcileApplyHandler.LookupService  = lookupService;

            ApplyReconcileButton.IsEnabled = false;
            UpdateIdsButton.IsEnabled      = false;
            ReconcileStatusText.Text       = $"Applying {toApply.Count} fix(es)…";

            _reconcileApplyEvent.Raise();
        }

        private void ShowReconcileResults(ObjectIdReconcileResult result)
        {
            _reconcileRows = result?.Rows ?? new List<ObjectIdReconcileRow>();

            ReconcileGrid.ItemsSource    = _reconcileRows;
            UpdateIdsButton.IsEnabled    = true;
            ApplyReconcileButton.IsEnabled = _reconcileRows.Any(r => r.IsSelected && r.IsWriteAllowed
                                                                    && !string.IsNullOrWhiteSpace(r.ProposedObjectId));

            if (result == null)
            {
                ReconcileStatusText.Text      = "No result returned.";
                ReconcileSummaryText.Text     = string.Empty;
                return;
            }

            ReconcileStatusText.Text = string.Empty;
            var parts = new List<string>();
            if (result.ValidCount    > 0) parts.Add($"{result.ValidCount} valid");
            if (result.MissingCount  > 0) parts.Add($"{result.MissingCount} missing");
            if (result.DuplicateCount > 0) parts.Add($"{result.DuplicateCount} duplicate");
            if (result.ChangedCount  > 0) parts.Add($"{result.ChangedCount} changed");
            if (result.ErrorCount    > 0) parts.Add($"{result.ErrorCount} error(s)");

            ReconcileSummaryText.Text = parts.Count > 0
                ? "Reconcile preview: " + string.Join("  |  ", parts)
                : "Reconcile complete — nothing to fix.";

            if (result.Warnings?.Count > 0)
            {
                WarningsText.Text         = string.Join("\n", result.Warnings);
                WarningsBorder.Visibility = Visibility.Visible;
            }
        }

        private void ShowApplyReconcileResults(int applied, int skipped, List<string> errors)
        {
            UpdateIdsButton.IsEnabled    = true;
            ApplyReconcileButton.IsEnabled = false;

            var sb = new System.Text.StringBuilder();
            sb.Append($"Reconcile apply complete: {applied} applied");
            if (skipped > 0) sb.Append($", {skipped} skipped");
            if (errors?.Count > 0) sb.Append($", {errors.Count} error(s)");
            ReconcileSummaryText.Text  = sb.ToString();
            ReconcileStatusText.Text   = string.Empty;

            if (errors?.Count > 0)
            {
                WarningsText.Text         = string.Join("\n", errors);
                WarningsBorder.Visibility = Visibility.Visible;
            }
        }

        private ObjectIdReconcileOptions BuildReconcileOptions() =>
            new ObjectIdReconcileOptions
            {
                IncludeMissingIds          = ReconcileIncludeMissingCheck.IsChecked   == true,
                RepairDuplicates           = ReconcileRepairDuplicatesCheck.IsChecked == true,
                FlagChangedElements        = ReconcileFlagChangedCheck.IsChecked      == true,
                ReassignChangedElements    = ReconcileReassignChangedCheck.IsChecked  == true,
                IncludeValidRows           = ReconcileShowValidCheck.IsChecked        == true,
            };

        private PbsPrCodeLookupService BuildLookupService()
        {
            if (_settings == null || string.IsNullOrEmpty(_settings.ExcelPath)
                || !System.IO.File.Exists(_settings.ExcelPath))
                return null;

            var (success, _, rows) = PbsMappingService.LoadPrCodeRows(_settings);
            return success && rows != null
                ? new PbsPrCodeLookupService(rows)
                : null;
        }

        // ── DataGrid: single-click checkbox support ───────────────────────────

        private void ReconcileGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && dep is not DataGridCell)
                dep = VisualTreeHelper.GetParent(dep);

            if (dep is DataGridCell cell && !cell.IsEditing && !cell.IsReadOnly)
            {
                if (!cell.IsFocused)
                    cell.Focus();
                ReconcileGrid.BeginEdit(e);
            }
        }

        private void ResultsGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && dep is not DataGridCell)
                dep = VisualTreeHelper.GetParent(dep);

            if (dep is DataGridCell cell && !cell.IsEditing && !cell.IsReadOnly)
            {
                if (!cell.IsFocused)
                    cell.Focus();
                ResultsGrid.BeginEdit(e);
            }
        }

        private void TypeNumbersGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && dep is not DataGridCell)
                dep = VisualTreeHelper.GetParent(dep);

            if (dep is DataGridCell cell && !cell.IsEditing && !cell.IsReadOnly)
            {
                if (!cell.IsFocused)
                    cell.Focus();
                TypeNumbersGrid.BeginEdit(e);
            }
        }

        // ── Type Numbers tab ─────────────────────────────────────────────────

        private void RebuildTypeNumberLookup()
        {
            _typeNumberLookup = new Dictionary<string, PbsTypeNumberRow>(
                StringComparer.OrdinalIgnoreCase);

            if (_settings == null || string.IsNullOrEmpty(_settings.ExcelPath)
                || !File.Exists(_settings.ExcelPath))
                return;

            var (success, _, rows) = PbsMappingService.LoadTypeNumberRows(_settings);
            if (!success || rows == null) return;

            foreach (var row in rows)
                _typeNumberLookup[row.PrCodeNormalized] = row;

            // Also (re)build the PrCode lookup used by LoadRbrTypeGroupsHandler.
            var (prSuccess, _, prRows) = PbsMappingService.LoadPrCodeRows(_settings);
            if (prSuccess && prRows != null)
                _prCodeLookupForTypeNumbers = new PbsPrCodeLookupService(prRows);
        }

        private void PopulateDisciplineCombo()
        {
            var disciplines = _typeNumberLookup?.Values
                .Select(r => r.DisciplineCode)
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(d => d)
                .ToList()
                ?? new List<string>();

            var items = new List<string> { "(All)" };
            items.AddRange(disciplines);

            DisciplineCombo.ItemsSource   = items;
            DisciplineCombo.SelectedIndex = 0;
        }

        private void DisciplineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_typeRows == null) return;

            string selected = DisciplineCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(selected) || selected == "(All)")
            {
                TypeNumbersGrid.ItemsSource = _typeRows;
                return;
            }

            TypeNumbersGrid.ItemsSource = _typeRows
                .Where(r => string.Equals(r.DisciplineCode, selected,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private void LoadTypes_Click(object sender, RoutedEventArgs e)
        {
            _loadTypesHandler.LookupService    = _prCodeLookupForTypeNumbers;
            _loadTypesHandler.TypeNumberLookup = _typeNumberLookup
                ?? new Dictionary<string, PbsTypeNumberRow>(StringComparer.OrdinalIgnoreCase);

            string selected = DisciplineCombo.SelectedItem as string;
            _loadTypesHandler.SelectedDiscipline =
                (!string.IsNullOrEmpty(selected) && selected != "(All)") ? selected : null;

            // Pass the current TypeSource selection and settings folder.
            _currentTypeSourceParam = TypeSourceCombo.SelectedItem as string ?? "Revit Type Name";
            _loadTypesHandler.TypeSourceParameterName = _currentTypeSourceParam;
            _loadTypesHandler.SettingsFolder          = SettingsFolder;

            TypeNumbersGrid.ItemsSource              = null;
            TypeNumberSummaryText.Text               = "Loading all types from document…";
            TypeNumberWarningsBorder.Visibility      = Visibility.Collapsed;
            SaveMappingsButton.IsEnabled             = false;
            PreviewTypeNumbersButton.IsEnabled       = false;
            ApplyTypeNumbersButton.IsEnabled         = false;
            ExportTypeNumbersButton.IsEnabled        = false;
            ExportMappingButton.IsEnabled            = false;
            _typeRows                                = null;

            _loadTypesEvent.Raise();
        }

        private void ShowTypeGroups(List<TypeNumberPreviewRow> rows, string docPath,
                                     List<string> paramNames)
        {
            _typeRows       = rows;
            _currentDocPath = docPath ?? string.Empty;

            // Populate TypeSource combo with discovered parameter names.
            PopulateTypeSourceCombo(paramNames);

            // Restore the TypeSource setting saved in ExtStorage (via RestoredSettings).
            // If the saved source differs from the one used to build these rows,
            // warn the user that another Load Types is needed.
            string usedParam  = _currentTypeSourceParam;
            string savedParam = _loadTypesHandler.RestoredSettings?.SelectedTypeSourceParameterName;

            if (!string.IsNullOrWhiteSpace(savedParam))
            {
                int idx = TypeSourceCombo.Items.IndexOf(savedParam);
                if (idx >= 0) TypeSourceCombo.SelectedIndex = idx;

                if (!string.Equals(savedParam, usedParam, StringComparison.OrdinalIgnoreCase))
                {
                    _currentTypeSourceParam = savedParam;
                    TypeNumberStatusText.Text =
                        $"Type source restored to '{savedParam}'. " +
                        "Click Load Types again to rebuild rows with this source.";
                }
            }

            // Mappings are already applied by the handler (from ExtStorage).
            TypeNumbersGrid.ItemsSource = rows;

            int rowCount = rows?.Count ?? 0;
            if (rowCount > 0)
            {
                TypeNumberSummaryText.Text = $"{rowCount} type(s) loaded.";
            }
            else
            {
                int grpCount   = _loadTypesHandler.TypeGroupCount;
                int filtered   = _loadTypesHandler.FilteredOutCount;
                int scanned    = _loadTypesHandler.ScannedInstanceCount;
                string discFilter = _loadTypesHandler.SelectedDiscipline;

                string msg;
                if (grpCount == 0)
                {
                    msg = $"Scanned {scanned} instance(s) in the document but found no element types. " +
                          "Check that the Revit model contains placed instances (walls, doors, floors, etc.)";
                }
                else if (filtered > 0 && !string.IsNullOrWhiteSpace(discFilter))
                {
                    msg = $"Found {grpCount} type group(s) across {scanned} instance(s), " +
                          $"but all {filtered} were filtered out by Discipline = '{discFilter}'. " +
                          "Try changing Discipline to '(All)'.";  
                }
                else
                {
                    msg = $"Scanned {scanned} instance(s) in {grpCount} type group(s), " +
                          "but no rows were produced.";
                }
                TypeNumberSummaryText.Text = msg;
            }

            bool hasRows = rows != null && rows.Count > 0;
            SaveMappingsButton.IsEnabled        = hasRows;
            PreviewTypeNumbersButton.IsEnabled  = hasRows;
            ExportTypeNumbersButton.IsEnabled   = hasRows;
            ExportMappingButton.IsEnabled       = hasRows;
        }

        /// <summary>Populates the TypeSource combo while preserving the current selection.</summary>
        private void PopulateTypeSourceCombo(List<string> paramNames)
        {
            string current = TypeSourceCombo.SelectedItem as string ?? _currentTypeSourceParam;
            var items = paramNames != null && paramNames.Count > 0
                ? paramNames
                : new List<string> { "Revit Type Name", "FamilyName + TypeName" };

            TypeSourceCombo.ItemsSource = items;
            int idx = items.IndexOf(current);
            TypeSourceCombo.SelectedIndex = idx >= 0 ? idx : 0;
        }

        private void TypeSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentTypeSourceParam = TypeSourceCombo.SelectedItem as string ?? "Revit Type Name";
        }

        private void SaveMappings_Click(object sender, RoutedEventArgs e)
        {
            if (_typeRows == null || !_typeRows.Any(r => !string.IsNullOrWhiteSpace(r.L1Code)))
            {
                MessageBox.Show("No rows with L1 codes to save.",
                    "Save Mappings", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _saveMappingsHandler.Rows                          = _typeRows;
            _saveMappingsHandler.SelectedTypeSourceParameterName = _currentTypeSourceParam;
            _saveMappingsHandler.SelectedDisciplineCode          =
                DisciplineCombo.SelectedItem as string ?? string.Empty;
            TypeNumberStatusText.Text     = "Saving mappings…";
            SaveMappingsButton.IsEnabled  = false;
            _saveMappingsEvent.Raise();
        }

        private void PreviewTypeNumbers_Click(object sender, RoutedEventArgs e)        {
            if (_typeRows == null || _typeRows.Count == 0)
            {
                MessageBox.Show("Please load types from selection first.",
                    "Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _previewTypeNumHandler.Rows         = _typeRows;
            _previewTypeNumHandler.LedgerFolder = SettingsFolder;

            TypeNumberSummaryText.Text          = "Generating previews…";
            TypeNumberWarningsBorder.Visibility = Visibility.Collapsed;
            PreviewTypeNumbersButton.IsEnabled  = false;
            ApplyTypeNumbersButton.IsEnabled    = false;

            _previewTypeNumEvent.Raise();
        }

        private void ShowTypeNumberPreview(
            List<TypeNumberPreviewRow> rows, RbrTypeNumberIndex index)
        {
            _typeRows = rows;
            TypeNumbersGrid.ItemsSource = null;
            TypeNumbersGrid.ItemsSource = rows;

            int ready   = rows?.Count(r => r.Status == "Ready") ?? 0;
            int skipped = rows?.Count(r => r.Status == "Already has Type Number") ?? 0;
            int issues  = rows?.Count(r =>
                r.Status != "Ready" && r.Status != "Already has Type Number") ?? 0;

            TypeNumberSummaryText.Text = $"Ready: {ready}   |   Already set: {skipped}   |   Issues: {issues}";

            PreviewTypeNumbersButton.IsEnabled = true;
            ApplyTypeNumbersButton.IsEnabled   = ready > 0;
            ExportTypeNumbersButton.IsEnabled  = rows != null && rows.Count > 0;

            if (index?.Duplicates?.Count > 0)
            {
                TypeNumberWarningsText.Text         =
                    "Duplicate RBR-Type_number values in model: " +
                    string.Join(", ", index.Duplicates.Take(10));
                TypeNumberWarningsBorder.Visibility = Visibility.Visible;
            }
        }

        private void ApplyTypeNumbers_Click(object sender, RoutedEventArgs e)
        {
            if (_typeRows == null) return;

            var toApply = _typeRows.Where(r => r.IsSelected && r.IsReady).ToList();
            if (toApply.Count == 0)
            {
                MessageBox.Show("No Ready rows are selected.",
                    "Apply", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _applyTypeNumHandler.RowsToApply  = toApply;
            _applyTypeNumHandler.LedgerFolder = SettingsFolder;
            _applyTypeNumHandler.SelectedTypeSourceParameterName = _currentTypeSourceParam;
            _applyTypeNumHandler.SelectedDisciplineCode          =
                DisciplineCombo.SelectedItem as string ?? string.Empty;

            TypeNumberSummaryText.Text       = "Applying type numbers…";
            ApplyTypeNumbersButton.IsEnabled = false;

            _applyTypeNumEvent.Raise();
        }

        private void ShowTypeNumberApply(int assigned, int skipped, List<string> errors)
        {
            TypeNumbersGrid.ItemsSource = null;
            TypeNumbersGrid.ItemsSource = _typeRows;

            TypeNumberSummaryText.Text =
                $"Assigned: {assigned}   |   Skipped: {skipped}   |   Errors: {errors.Count}";

            ApplyTypeNumbersButton.IsEnabled = false;   // Re-run Preview to re-enable.

            if (errors.Count > 0)
            {
                TypeNumberWarningsText.Text         = string.Join("\n", errors);
                TypeNumberWarningsBorder.Visibility = Visibility.Visible;
            }
        }

        private void ExportMapping_Click(object sender, RoutedEventArgs e)
        {
            if (_typeRows == null || _typeRows.Count == 0) return;

            string modeChoice = ShowChoiceDialog(
                "Export Mapping Excel",
                "Which rows do you want to export?",
                "All rows",
                "Only unmapped rows (no saved or edited mapping)",
                "Only mapped rows (saved or edited)");
            if (modeChoice == null) return;

            var exportMode = modeChoice.Contains("unmapped") ? TypeNumberExcelExportMode.OnlyUnmapped
                           : modeChoice.Contains("mapped")   ? TypeNumberExcelExportMode.OnlyMapped
                           : TypeNumberExcelExportMode.All;

            var dlg = new SaveFileDialog
            {
                Title      = "Export Type Number Mapping",
                Filter     = "Excel files (*.xlsx)|*.xlsx",
                FileName   = $"RBR_TypeNumberMapping_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                DefaultExt = ".xlsx",
            };
            if (dlg.ShowDialog() != true) return;

            // Dispatch through the export handler so it can read stored Notes
            // from Extensible Storage (requires the Revit Document on the API thread).
            _pendingExportPath                  = dlg.FileName;
            _exportMappingsHandler.Rows         = _typeRows;
            _exportMappingsHandler.ExportMode   = exportMode;
            _exportMappingsHandler.OutputPath   = dlg.FileName;
            TypeNumberStatusText.Text           = "Exporting mapping Excel…";
            ExportMappingButton.IsEnabled       = false;
            _exportMappingsEvent.Raise();
        }

        // Stores the path for the in-flight export so the completion handler can show it.
        private string _pendingExportPath;

        private void HandleExportMappingCompleted(bool success, string error)
        {
            string path = _pendingExportPath;
            _pendingExportPath = null;
            ExportMappingButton.IsEnabled = _typeRows != null && _typeRows.Count > 0;

            if (!success)
            {
                TypeNumberStatusText.Text = "Export failed.";
                MessageBox.Show("Export failed:\n" + error,
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            TypeNumberStatusText.Text = "Mapping Excel exported.";
            MessageBox.Show("Exported to:\n" + path,
                "Export", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ImportMapping_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Import Type Number Mapping",
                Filter = "Excel files (*.xlsx)|*.xlsx",
            };
            if (dlg.ShowDialog() != true) return;

            var (records, result, error) = TypeNumberMappingExcelService.Import(dlg.FileName);
            if (error != null)
            {
                MessageBox.Show("Import failed:\n" + error,
                    "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (records == null || records.Count == 0)
            {
                MessageBox.Show("No valid mapping rows found in file.",
                    "Import", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string summary = $"Found {records.Count} valid row(s).";
            if (result.Invalid   > 0) summary += $"\nInvalid rows skipped: {result.Invalid}";
            if (result.Conflicts > 0) summary += $"\nConflicting duplicates skipped: {result.Conflicts}";

            string modeChoice = ShowChoiceDialog(
                "Import Mapping Excel",
                summary + "\n\nHow should imported mappings be applied?",
                "Override existing mappings",
                "Only add new mappings (keep existing)");
            if (modeChoice == null) return;

            bool overwrite = modeChoice.StartsWith("Override");

            // Apply to visible grid rows optimistically (display only).
            // The canonical write goes through ImportTypeNumberMappingsHandler.
            if (_typeRows != null)
            {
                var importIndex = new Dictionary<string, TypeNumberMappingRecord>(
                    StringComparer.OrdinalIgnoreCase);
                foreach (var rec in records)
                    importIndex[rec.MatchKey] = rec;

                foreach (var row in _typeRows)
                {
                    if (!importIndex.TryGetValue(row.MatchKey, out var rec)) continue;
                    if (!overwrite && row.HasSavedMapping)                   continue;

                    // Mark hasSavedMapping=true only after the handler confirms success;
                    // use "Imported" status for now so the user sees pending state.
                    row.ApplyL1CodeFromMapping(rec.L1Code, "Imported", hasSavedMapping: false);
                }

                TypeNumbersGrid.ItemsSource = null;
                TypeNumbersGrid.ItemsSource = _typeRows;
            }

            // Store pending import records for use in HandleImportCompleted.
            _pendingImportRecords = records;

            // Dispatch ALL imported records to Extensible Storage via the dedicated handler.
            TypeNumberStatusText.Text = "Importing mappings…";
            _importMappingsHandler.RecordsToImport                  = records;
            _importMappingsHandler.OverwriteExisting                = overwrite;
            _importMappingsHandler.SelectedTypeSourceParameterName  = _currentTypeSourceParam;
            _importMappingsHandler.SelectedDisciplineCode           =
                DisciplineCombo.SelectedItem as string ?? string.Empty;
            _importMappingsEvent.Raise();

            if (result.Issues.Count > 0)
            {
                TypeNumberWarningsText.Text         = string.Join("\n", result.Issues.Take(10));
                TypeNumberWarningsBorder.Visibility = Visibility.Visible;
            }
        }

        // Stores the records currently being imported so HandleImportCompleted can update rows.
        private List<TypeNumberMappingRecord> _pendingImportRecords;

        private void HandleImportCompleted(TypeNumberMappingUpsertResult upsertResult, string err)
        {
            if (err != null)
            {
                TypeNumberStatusText.Text = "Import failed: " + err;
                // Revert optimistic grid update — reset "Imported" rows back to Unmapped.
                if (_typeRows != null && _pendingImportRecords != null)
                {
                    var importedKeys = new HashSet<string>(
                        _pendingImportRecords.Select(r => r.MatchKey),
                        StringComparer.OrdinalIgnoreCase);
                    foreach (var row in _typeRows)
                    {
                        if (importedKeys.Contains(row.MatchKey)
                            && row.MappingStatus == "Imported"
                            && !row.HasSavedMapping)
                        {
                            row.ApplyL1CodeFromMapping(string.Empty, "Unmapped",
                                hasSavedMapping: false);
                        }
                    }
                }
                _pendingImportRecords = null;
                return;
            }

            // Import committed — mark visible matching rows as Saved.
            if (_typeRows != null)
            {
                foreach (var row in _typeRows)
                {
                    if (row.MappingStatus == "Imported")
                        row.ApplyL1CodeFromMapping(row.L1Code, "Saved", hasSavedMapping: true);
                }
                TypeNumbersGrid.ItemsSource = null;
                TypeNumbersGrid.ItemsSource = _typeRows;
            }

            _pendingImportRecords = null;

            TypeNumberStatusText.Text = upsertResult == null
                ? "Import complete."
                : $"Import complete. Added: {upsertResult.Added}   Updated: {upsertResult.Updated}" +
                  $"   Skipped: {upsertResult.SkippedExisting}   Invalid: {upsertResult.Invalid}";

            MessageBox.Show(
                upsertResult == null
                    ? "Import saved to model."
                    : $"Import saved to model.\n\nAdded: {upsertResult.Added}" +
                      $"\nUpdated: {upsertResult.Updated}" +
                      $"\nSkipped (existing): {upsertResult.SkippedExisting}" +
                      $"\nInvalid: {upsertResult.Invalid}",
                "Import", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportTypeNumbers_Click(object sender, RoutedEventArgs e)
        {
            if (_typeRows == null || _typeRows.Count == 0) return;

            var dlg = new SaveFileDialog
            {
                Title      = "Export RBR Type Numbers Report",
                Filter     = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName   = $"RBR_TypeNumbers_Report_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                DefaultExt = ".csv",
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Timestamp,Category,FamilyName,TypeName,Instances," +
                              "RBR_Pr_Code,PbsMatch,PbsTemplate,L1Code," +
                              "ExistingTypeNumber,ProposedTypeNumber,Status,Message");

                string ts = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                foreach (var row in _typeRows)
                {
                    sb.AppendLine(
                        $"{Csv(ts)}," +
                        $"{Csv(row.Category)}," +
                        $"{Csv(row.FamilyName)}," +
                        $"{Csv(row.TypeName)}," +
                        $"{row.InstanceCount}," +
                        $"{Csv(row.RbrPrCode)}," +
                        $"{Csv(row.PbsMatchStatus)}," +
                        $"{Csv(row.PbsTypeTemplate)}," +
                        $"{Csv(row.L1Code)}," +
                        $"{Csv(row.ExistingTypeNumber)}," +
                        $"{Csv(row.ProposedTypeNumber)}," +
                        $"{Csv(row.Status)}," +
                        $"{Csv(row.Message)}");
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show("Report exported to:\n" + dlg.FileName,
                    "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed:\n" + ex.Message,
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private void ResetPreviewState()
        {
            _previewRows               = null;
            ResultsGrid.ItemsSource    = null;
            ApplyButton.IsEnabled      = false;
            ExportButton.IsEnabled     = false;
            SummaryText.Text           = string.Empty;
            WarningsBorder.Visibility  = Visibility.Collapsed;
        }

        private void SetButtons(bool previewEnabled, bool applyEnabled,
                                 bool assignEnabled, bool exportEnabled)
        {
            PreviewButton.IsEnabled = previewEnabled;
            ApplyButton.IsEnabled   = applyEnabled;
            AssignButton.IsEnabled  = assignEnabled;
            ExportButton.IsEnabled  = exportEnabled;
        }

        private void ShowIndexWarnings(RbrObjectIdIndex index)
        {
            if (index == null) return;
            if (index.Duplicates.Count == 0 && index.Malformed.Count == 0) return;

            var sb = new StringBuilder();
            if (index.Duplicates.Count > 0)
                sb.AppendLine("Duplicate IDs found in model (" + index.Duplicates.Count + "): "
                    + string.Join(", ", index.Duplicates.Take(5))
                    + (index.Duplicates.Count > 5 ? " …" : ""));
            if (index.Malformed.Count > 0)
                sb.AppendLine("Malformed IDs found in model (" + index.Malformed.Count + "): "
                    + string.Join(", ", index.Malformed.Take(5))
                    + (index.Malformed.Count > 5 ? " …" : ""));

            WarningsText.Text         = sb.ToString().Trim();
            WarningsBorder.Visibility = Visibility.Visible;
        }

        private static string MapAssignStatus(string status) => status switch
        {
            "Assigned"        => "Assigned",
            "SkippedExisting" => "Already has Object ID",
            "MissingParameter"=> "Missing RBR-Object_ID parameter",
            "ReadOnly"        => "RBR-Object_ID parameter read-only",
            "MissingLevel"    => "Missing level",
            _                 => status,
        };

        private static string Csv(string v)
        {
            if (v == null) return string.Empty;
            if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }

        /// <summary>
        /// Displays a simple modal dialog with a prompt and a set of radio-button choices.
        /// Returns the text of the selected option, or null if the user cancelled.
        /// Built entirely in code (no XAML file required).
        /// </summary>
        private static string ShowChoiceDialog(string title, string prompt,
                                               params string[] options)
        {
            if (options == null || options.Length == 0) return null;

            string result = null;
            var win = new Window
            {
                Title                 = title,
                Width                 = 420,
                Height                = 130 + options.Length * 30,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode            = ResizeMode.NoResize,
                ShowInTaskbar         = false,
            };

            var outer = new StackPanel { Margin = new Thickness(16) };
            outer.Children.Add(new TextBlock
            {
                Text         = prompt,
                TextWrapping = TextWrapping.Wrap,
                Margin       = new Thickness(0, 0, 0, 10),
            });

            var radios = options.Select((opt, i) =>
            {
                var r = new RadioButton
                {
                    Content   = opt,
                    IsChecked = i == 0,
                    Margin    = new Thickness(0, 0, 0, 4),
                };
                outer.Children.Add(r);
                return r;
            }).ToArray();

            var btns = new StackPanel
            {
                Orientation         = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin              = new Thickness(0, 12, 0, 0),
            };
            var ok     = new Button { Content = "OK",     Width = 75, Height = 26, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "Cancel", Width = 75, Height = 26, IsCancel = true };
            ok.Click     += (s, e) =>
            {
                result = radios.FirstOrDefault(r => r.IsChecked == true)?.Content?.ToString();
                win.DialogResult = true;
            };
            cancel.Click += (s, e) => win.DialogResult = false;
            btns.Children.Add(ok);
            btns.Children.Add(cancel);
            outer.Children.Add(btns);
            win.Content = outer;

            return win.ShowDialog() == true ? result : null;
        }
    }
}
