using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Microsoft.Win32;
using TheRealmProject.Core;

namespace TheRealmProject.Launcher;

public partial class MainWindow : Window
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 0x00000002;

    private readonly RealmPaths _paths = new();
    private readonly HttpClient _httpClient = new();
    private readonly ObservableCollection<NeoForgeVersion> _allNeoForgeVersions = [];
    private readonly ObservableCollection<string> _minecraftVersions = [];
    private readonly ObservableCollection<InstalledGameVersion> _installedMinecraftVersions = [];
    private readonly ObservableCollection<InstalledGameVersion> _installedNeoForgeVersions = [];
    private readonly NeoForgeService _neoForgeService;
    private readonly InstallationService _installationService;
    private readonly JavaService _javaService;
    private readonly ProfileService _profileService;
    private readonly LauncherStateService _stateService;
    private readonly ModpackService _modpackService;
    private readonly CosmeticsModDeploymentService _cosmeticsModDeploymentService;
    private readonly MinecraftLaunchService _minecraftLaunchService;
    private readonly Progress<string> _progress;
    private readonly AxisAngleRotation3D _previewYaw = new(new Vector3D(0, 1, 0), 22);
    private readonly AxisAngleRotation3D _previewPitch = new(new Vector3D(1, 0, 0), -8);
    private bool _versionsReady;
    private bool _busy;
    private bool _minecraftRunning;
    private bool _initializingRam;
    private bool _isDraggingPreview;
    private Point _lastPreviewPoint;
    private double _previewCameraDistance = 62;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += MainWindow_OnSourceInitialized;
        StateChanged += (_, _) => UpdateMaximizeButtonGlyph();
        UpdateMaximizeButtonGlyph();

        _paths.Ensure();
        _neoForgeService = new NeoForgeService(_paths, _httpClient);
        _installationService = new InstallationService(_paths);
        _javaService = new JavaService(_paths, _httpClient);
        _profileService = new ProfileService(_paths);
        _stateService = new LauncherStateService(_paths);
        _modpackService = new ModpackService(_paths, _httpClient, _stateService);
        _cosmeticsModDeploymentService = new CosmeticsModDeploymentService(_paths);
        _minecraftLaunchService = new MinecraftLaunchService(_paths, _profileService, _stateService);
        _progress = new Progress<string>(AppendLog);

        MinecraftVersionCombo.ItemsSource = _minecraftVersions;
        NeoForgeVersionCombo.ItemsSource = _allNeoForgeVersions;
        InstalledMinecraftVersionsList.ItemsSource = _installedMinecraftVersions;
        InstalledNeoForgeVersionsList.ItemsSource = _installedNeoForgeVersions;
        PathText.Text = _paths.Instance;
        VersionStatusText.Text = "Carregando versões do NeoForge...";
        UpdateActionStates();

        Loaded += async (_, _) => await InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await RunUiTaskAsync(async () =>
        {
            AppendLog("Inicializando The Realm Project...");
            var config = await _stateService.LoadConfigAsync();
            var profile = await _profileService.LoadAsync();
            PlayerIdTextBox.Text = profile.PlayerId;
            SkinPathTextBox.Text = profile.SkinPath ?? "";
            CapePathTextBox.Text = profile.CapePath ?? "";
            SetSkinPreview(profile.SkinPath, profile.CapePath);
            InitializeRamControls(config);

            try
            {
                VersionStatusText.Text = "Buscando versões no Maven oficial do NeoForge...";
                var versions = await _neoForgeService.GetVersionsAsync(config.IncludeBetaNeoForgeVersions);
                if (versions.Count == 0)
                    throw new InvalidOperationException("Nenhuma versão NeoForge válida foi encontrada no maven-metadata.xml.");

                _allNeoForgeVersions.Clear();
                _minecraftVersions.Clear();

                foreach (var version in versions)
                    _allNeoForgeVersions.Add(version);

                foreach (var minecraft in versions.Select(v => v.MinecraftVersion).Distinct())
                    _minecraftVersions.Add(minecraft);

                var state = await _stateService.LoadAsync();
                MinecraftVersionCombo.SelectedItem = _minecraftVersions.Contains(state.SelectedMinecraftVersion ?? "")
                    ? state.SelectedMinecraftVersion
                    : _minecraftVersions.FirstOrDefault();
                RefreshNeoForgeFilter();
                if (!string.IsNullOrWhiteSpace(state.SelectedNeoForgeVersion))
                {
                    NeoForgeVersionCombo.SelectedItem = _allNeoForgeVersions.FirstOrDefault(v => v.NeoForgeVersionId == state.SelectedNeoForgeVersion)
                        ?? NeoForgeVersionCombo.SelectedItem;
                }

                _versionsReady = true;
                VersionStatusText.Text = "";
            }
            catch (Exception ex)
            {
                _versionsReady = false;
                _allNeoForgeVersions.Clear();
                _minecraftVersions.Clear();
                NeoForgeVersionCombo.ItemsSource = null;
                VersionStatusText.Text = "Não foi possível carregar versões do NeoForge.";
                AppendLog($"Erro ao carregar versões NeoForge: {ex.Message}");
            }

            RefreshInstalledVersions();
            UpdateActionStates();
            AppendLog("Pronto.");
        });
    }

    private void MinecraftVersionCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshNeoForgeFilter();
        UpdateActionStates();
    }

    private void PlayTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetActiveTab(showPlay: true);
    }

    private void CustomizationTabButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetActiveTab(showPlay: false);
    }

    private void SetActiveTab(bool showPlay)
    {
        PlayContent.Visibility = showPlay ? Visibility.Visible : Visibility.Collapsed;
        CustomizationContent.Visibility = showPlay ? Visibility.Collapsed : Visibility.Visible;
        PlayTabButton.Style = (Style)FindResource(showPlay ? "ActiveNavButton" : "NavButton");
        CustomizationTabButton.Style = (Style)FindResource(showPlay ? "NavButton" : "ActiveNavButton");
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        DragMove();
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void MainWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(WndProc);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo)
        {
            ApplyMonitorWorkArea(hwnd, lParam);
            handled = true;
        }

        return nint.Zero;
    }

    private void UpdateMaximizeButtonGlyph()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = maximized ? Visibility.Visible : Visibility.Collapsed;
        MaximizeButton.ToolTip = maximized ? "Restaurar" : "Maximizar";
        WindowFrame.CornerRadius = WindowState == WindowState.Maximized ? new CornerRadius(0) : new CornerRadius(14);
    }

    private static void ApplyMonitorWorkArea(nint hwnd, nint lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == nint.Zero)
            return;

        var monitorInfo = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
            return;

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var workArea = monitorInfo.rcWork;
        var monitorArea = monitorInfo.rcMonitor;

        minMaxInfo.ptMaxPosition.X = Math.Abs(workArea.Left - monitorArea.Left);
        minMaxInfo.ptMaxPosition.Y = Math.Abs(workArea.Top - monitorArea.Top);
        minMaxInfo.ptMaxSize.X = Math.Abs(workArea.Right - workArea.Left);
        minMaxInfo.ptMaxSize.Y = Math.Abs(workArea.Bottom - workArea.Top);

        Marshal.StructureToPtr(minMaxInfo, lParam, false);
    }

    private async void InstallButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunUiTaskAsync(async () =>
        {
            var version = RequireSelectedNeoForge();
            var java = await _javaService.EnsureJavaAsync(NeoForgeService.RequiredJavaMajor(version.MinecraftVersion), _progress);
            await _neoForgeService.InstallClientAsync(version, java, _progress);
            await SaveSelectedVersionAsync(version);
            RefreshInstalledVersions();
        });
    }

    private async void PlayButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunUiTaskAsync(async () =>
        {
            var version = RequireSelectedNeoForge();
            var java = await _javaService.EnsureJavaAsync(NeoForgeService.RequiredJavaMajor(version.MinecraftVersion), _progress);
            await _neoForgeService.InstallClientAsync(version, java, _progress);
            RefreshInstalledVersions();

            var launchVersion = ResolveInstalledVersionId(version);
            if (launchVersion is null)
                throw new InvalidOperationException("NeoForge foi instalado, mas a versão final não foi encontrada em versions/.");

            await SaveSelectedVersionAsync(version);
            AppendLog("Salvando perfil offline...");
            await SaveCurrentProfileAsync();
            _cosmeticsModDeploymentService.DeployIfBuilt(_progress);
            _minecraftRunning = true;
            UpdateActionStates();
            try
            {
                await _minecraftLaunchService.LaunchAsync(launchVersion, java, _progress, OnMinecraftExited);
            }
            catch
            {
                _minecraftRunning = false;
                UpdateActionStates();
                throw;
            }
        });
    }

    private void OnMinecraftExited(int exitCode)
    {
        Dispatcher.Invoke(() =>
        {
            _minecraftRunning = false;
            UpdateActionStates();
        });
    }

    private async void RamSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_initializingRam || !IsLoaded)
            return;

        var ramMb = SnapRam((int)e.NewValue);
        RamValueText.Text = FormatRam(ramMb);
        await SaveRamAsync(ramMb);
    }

    private void OpenMinecraftFolderButton_OnClick(object sender, RoutedEventArgs e)
    {
        _paths.Ensure();
        Process.Start(new ProcessStartInfo
        {
            FileName = _paths.Minecraft,
            UseShellExecute = true
        });
    }

    private async void UninstallVersionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not InstalledGameVersion version)
            return;

        var result = MessageBox.Show(
            this,
            $"Desinstalar {version.DisplayName}?\n\nApenas a pasta da versão será removida. Mods, saves, assets e libraries compartilhadas serão preservados.",
            "Desinstalar versão",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        await RunUiTaskAsync(async () =>
        {
            _installationService.Uninstall(version);
            await ClearSelectionIfNeededAsync(version);
            RefreshInstalledVersions();
            AppendLog($"{version.DisplayName} desinstalado.");
        });
    }

    private async void DownloadModpackButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunUiTaskAsync(async () =>
        {
            await _modpackService.InstallOrUpdateLatestAsync(true, _progress);
            AppendLog("Modpack instalado.");
        });
    }

    private async void UpdateModpackButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunUiTaskAsync(async () =>
        {
            var changed = await _modpackService.InstallOrUpdateLatestAsync(false, _progress);
            AppendLog(changed ? "Modpack atualizado." : "Nenhuma atualização encontrada.");
        });
    }

    private void SelectSkinButton_OnClick(object sender, RoutedEventArgs e)
    {
        var file = PickPng();
        if (file is null)
            return;

        SkinPathTextBox.Text = file;
        SetSkinPreview(file, EmptyToNull(CapePathTextBox.Text));
    }

    private void SelectCapeButton_OnClick(object sender, RoutedEventArgs e)
    {
        var file = PickPng();
        if (file is null)
            return;

        CapePathTextBox.Text = file;
        SetSkinPreview(EmptyToNull(SkinPathTextBox.Text), file);
    }

    private async void SaveProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunUiTaskAsync(async () =>
        {
            await SaveCurrentProfileAsync();
            AppendLog("Customização salva.");
        });
    }

    private async Task SaveCurrentProfileAsync()
    {
        var profile = await _profileService.SaveAsync(PlayerIdTextBox.Text, EmptyToNull(SkinPathTextBox.Text), EmptyToNull(CapePathTextBox.Text));
        PlayerIdTextBox.Text = profile.PlayerId;
        SkinPathTextBox.Text = profile.SkinPath ?? "";
        CapePathTextBox.Text = profile.CapePath ?? "";
        SetSkinPreview(profile.SkinPath, profile.CapePath);
    }

    private void RefreshNeoForgeFilter()
    {
        var selectedMinecraft = MinecraftVersionCombo.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selectedMinecraft))
        {
            NeoForgeVersionCombo.ItemsSource = null;
            NeoForgeVersionCombo.SelectedItem = null;
            return;
        }

        var filtered = _allNeoForgeVersions.Where(v => v.MinecraftVersion == selectedMinecraft).ToList();
        var selected = filtered.FirstOrDefault();
        NeoForgeVersionCombo.ItemsSource = filtered;
        NeoForgeVersionCombo.SelectedItem = selected;
        VersionStatusText.Text = filtered.Count == 0
            ? "Sem NeoForge para esta versão."
            : "";
    }

    private void RefreshInstalledVersions()
    {
        _installedMinecraftVersions.Clear();
        _installedNeoForgeVersions.Clear();

        foreach (var version in _installationService.GetInstalledVersions())
        {
            if (version.Kind == InstalledGameVersionKind.NeoForge)
                _installedNeoForgeVersions.Add(version);
            else
                _installedMinecraftVersions.Add(version);
        }
    }

    private NeoForgeVersion RequireSelectedNeoForge()
    {
        if (NeoForgeVersionCombo.SelectedItem is NeoForgeVersion version)
            return version;

        throw new InvalidOperationException("Selecione uma versão de Minecraft/NeoForge primeiro.");
    }

    private string? ResolveInstalledVersionId(NeoForgeVersion version)
    {
        return _neoForgeService.GetInstalledVersionIds()
            .FirstOrDefault(id => id.Contains(version.NeoForgeVersionId, StringComparison.OrdinalIgnoreCase));
    }

    private async Task SaveSelectedVersionAsync(NeoForgeVersion version)
    {
        var state = await _stateService.LoadAsync();
        state.SelectedMinecraftVersion = version.MinecraftVersion;
        state.SelectedNeoForgeVersion = version.NeoForgeVersionId;
        await _stateService.SaveAsync(state);
    }

    private async Task ClearSelectionIfNeededAsync(InstalledGameVersion version)
    {
        var state = await _stateService.LoadAsync();
        var changed = false;

        if (string.Equals(state.SelectedMinecraftVersion, version.Id, StringComparison.OrdinalIgnoreCase))
        {
            state.SelectedMinecraftVersion = null;
            changed = true;
        }

        if (string.Equals($"neoforge-{state.SelectedNeoForgeVersion}", version.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state.SelectedNeoForgeVersion, version.Id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(state.SelectedNeoForgeVersion, version.Id.Replace("neoforge-", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase))
        {
            state.SelectedNeoForgeVersion = null;
            changed = true;
        }

        if (changed)
            await _stateService.SaveAsync(state);
    }

    private void InitializeRamControls(RealmAppConfig config)
    {
        _initializingRam = true;
        var maxRam = SystemMemoryService.GetRecommendedMaximumRamMb();
        RamSlider.Minimum = 1024;
        RamSlider.Maximum = Math.Max(1024, maxRam);
        RamSlider.TickFrequency = 512;
        RamSlider.Value = Math.Clamp(SnapRam(config.MaximumRamMb), (int)RamSlider.Minimum, (int)RamSlider.Maximum);
        RamValueText.Text = FormatRam((int)RamSlider.Value);
        _initializingRam = false;
    }

    private async Task SaveRamAsync(int ramMb)
    {
        var config = await _stateService.LoadConfigAsync();
        config.MaximumRamMb = ramMb;
        config.MinimumRamMb = Math.Min(1024, ramMb);
        await _stateService.SaveConfigAsync(config);
    }

    private static int SnapRam(int value) => Math.Max(1024, value / 512 * 512);

    private static string FormatRam(int mb)
    {
        return mb % 1024 == 0 ? $"{mb / 1024} GB" : $"{mb / 1024.0:0.0} GB";
    }

    private async Task RunUiTaskAsync(Func<Task> task)
    {
        SetBusy(true);
        try
        {
            await task();
        }
        catch (Exception ex)
        {
            AppendLog($"Erro: {ex.Message}");
            MessageBox.Show(this, ex.Message, "The Realm Project", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateActionStates();
    }

    private void UpdateActionStates()
    {
        var hasSelectedVersion = _versionsReady && NeoForgeVersionCombo.SelectedItem is NeoForgeVersion;
        InstallButton.IsEnabled = !_busy && hasSelectedVersion;
        PlayButton.IsEnabled = !_busy && !_minecraftRunning && hasSelectedVersion;
        DownloadModpackButton.IsEnabled = !_busy;
        UpdateModpackButton.IsEnabled = !_busy;
        SaveProfileButton.IsEnabled = !_busy;
        RamSlider.IsEnabled = !_busy;
        MinecraftVersionCombo.IsEnabled = !_busy && _versionsReady;
        NeoForgeVersionCombo.IsEnabled = !_busy && _versionsReady;
    }

    private void AppendLog(string message)
    {
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    private static string? PickPng()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private void SetSkinPreview(string? skinPath, string? capePath)
    {
        if (string.IsNullOrWhiteSpace(skinPath) || !File.Exists(skinPath))
        {
            SkinPreviewModelVisual.Content = null;
            SkinPreviewPlaceholder.Text = "Preview";
            SkinPreviewPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var skin = LoadBitmap(skinPath);
            BitmapSource? cape = null;
            if (!string.IsNullOrWhiteSpace(capePath) && File.Exists(capePath))
            {
                try
                {
                    cape = LoadBitmap(capePath);
                }
                catch (Exception ex)
                {
                    AppendLog($"Preview da capa indisponível: {ex.Message}");
                }
            }

            SkinPreviewModelVisual.Content = BuildSkinPreviewModel(skin, cape);
            SkinPreviewPlaceholder.Text = "Preview";
            SkinPreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            SkinPreviewModelVisual.Content = null;
            SkinPreviewPlaceholder.Text = "Skin inválida";
            SkinPreviewPlaceholder.Visibility = Visibility.Visible;
            AppendLog($"Preview da skin indisponível: {ex.Message}");
        }
    }

    private static BitmapSource LoadBitmap(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path);
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private Model3DGroup BuildSkinPreviewModel(BitmapSource skin, BitmapSource? cape)
    {
        var skinMap = TextureMap.CreateSkin(skin);
        var root = new Model3DGroup();
        var player = new Model3DGroup();

        AddBox(player, skinMap, new Point3D(0, 28, 0), new PreviewBoxSize(8, 8, 8), FaceSet.Head);
        AddBox(player, skinMap, new Point3D(0, 18, 0), new PreviewBoxSize(8, 12, 4), FaceSet.Body);
        AddBox(player, skinMap, new Point3D(-6.08, 18, 0), new PreviewBoxSize(3.92, 12, 4), FaceSet.RightArm);
        AddBox(player, skinMap, new Point3D(6.08, 18, 0), new PreviewBoxSize(3.92, 12, 4), skinMap.HasModernLayers ? FaceSet.LeftArm : FaceSet.RightArm);
        AddBox(player, skinMap, new Point3D(-2, 6, 0), new PreviewBoxSize(4, 12, 4), FaceSet.RightLeg);
        AddBox(player, skinMap, new Point3D(2, 6, 0), new PreviewBoxSize(4, 12, 4), skinMap.HasModernLayers ? FaceSet.LeftLeg : FaceSet.RightLeg);

        if (skinMap.HasModernLayers)
        {
            AddBox(player, skinMap, new Point3D(0, 28, 0), new PreviewBoxSize(8.8, 8.8, 8.8), FaceSet.HeadLayer);
            AddBox(player, skinMap, new Point3D(0, 18, 0), new PreviewBoxSize(8.5, 12.5, 4.5), FaceSet.BodyLayer);
            AddBox(player, skinMap, new Point3D(-6.08, 18, 0), new PreviewBoxSize(4.38, 12.5, 4.5), FaceSet.RightArmLayer);
            AddBox(player, skinMap, new Point3D(6.08, 18, 0), new PreviewBoxSize(4.38, 12.5, 4.5), FaceSet.LeftArmLayer);
            AddBox(player, skinMap, new Point3D(-2.04, 6, 0), new PreviewBoxSize(4.36, 12.45, 4.45), FaceSet.RightLegLayer);
            AddBox(player, skinMap, new Point3D(2.04, 6, 0), new PreviewBoxSize(4.36, 12.45, 4.45), FaceSet.LeftLegLayer);
        }

        if (cape is not null)
        {
            var capeMap = TextureMap.CreateCape(cape);
            AddBox(player, capeMap, new Point3D(0, 15.4, -3.2), new PreviewBoxSize(10, 16, 0.7), FaceSet.Cape);
        }

        var transform = new Transform3DGroup();
        transform.Children.Add(new RotateTransform3D(_previewPitch, new Point3D(0, 16, 0)));
        transform.Children.Add(new RotateTransform3D(_previewYaw, new Point3D(0, 16, 0)));
        player.Transform = transform;
        root.Children.Add(player);

        root.Children.Add(new GeometryModel3D
        {
            Geometry = CreateDiscMesh(new Point3D(0, -0.2, 0), 9.5, 5.2),
            Material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(85, 0, 0, 0))),
            BackMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(85, 0, 0, 0)))
        });

        return root;
    }

    private static void AddBox(Model3DGroup group, TextureMap map, Point3D center, PreviewBoxSize size, FaceSet faces)
    {
        var x0 = center.X - size.X / 2;
        var x1 = center.X + size.X / 2;
        var y0 = center.Y - size.Y / 2;
        var y1 = center.Y + size.Y / 2;
        var z0 = center.Z - size.Z / 2;
        var z1 = center.Z + size.Z / 2;

        AddFace(group, map, faces.Front, new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1));
        AddFace(group, map, faces.Back, new Point3D(x1, y0, z0), new Point3D(x0, y0, z0), new Point3D(x0, y1, z0), new Point3D(x1, y1, z0));
        AddFace(group, map, faces.Left, new Point3D(x0, y0, z0), new Point3D(x0, y0, z1), new Point3D(x0, y1, z1), new Point3D(x0, y1, z0));
        AddFace(group, map, faces.Right, new Point3D(x1, y0, z1), new Point3D(x1, y0, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1));
        AddFace(group, map, faces.Top, new Point3D(x0, y1, z1), new Point3D(x1, y1, z1), new Point3D(x1, y1, z0), new Point3D(x0, y1, z0));
        AddFace(group, map, faces.Bottom, new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), new Point3D(x0, y0, z1));
    }

    private static void AddFace(Model3DGroup group, TextureMap map, Rect source, Point3D p0, Point3D p1, Point3D p2, Point3D p3)
    {
        if (!map.Contains(source))
            return;

        var builder = new PixelMeshBuilder();
        var width = (int)source.Width;
        var height = (int)source.Height;
        var sourceX = (int)source.X;
        var sourceY = (int)source.Y;
        var faceNormal = CalculateNormal(p0, p1, p2);
        var isLayer = IsOuterLayerSource(source);
        var isCape = IsCapeSource(source);
        var inset = isCape ? 0.002 : isLayer ? 0.012 : 0.004;
        p0 += faceNormal * inset;
        p1 += faceNormal * inset;
        p2 += faceNormal * inset;
        p3 += faceNormal * inset;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var color = map.PixelColor(sourceX + x, sourceY + y);
                if (color.A < 48)
                    continue;
                color.A = 255;

                var u0 = x / (double)width;
                var u1 = (x + 1) / (double)width;
                var vTop = 1 - y / (double)height;
                var vBottom = 1 - (y + 1) / (double)height;

                builder.AddQuad(
                    color,
                    InterpolateFacePoint(p0, p1, p2, p3, u0, vBottom),
                    InterpolateFacePoint(p0, p1, p2, p3, u1, vBottom),
                    InterpolateFacePoint(p0, p1, p2, p3, u1, vTop),
                    InterpolateFacePoint(p0, p1, p2, p3, u0, vTop));
            }
        }

        builder.AddTo(group);
    }

    private static Point3D InterpolateFacePoint(Point3D p0, Point3D p1, Point3D p2, Point3D p3, double u, double v)
    {
        var bottom = new Point3D(
            p0.X + (p1.X - p0.X) * u,
            p0.Y + (p1.Y - p0.Y) * u,
            p0.Z + (p1.Z - p0.Z) * u);
        var top = new Point3D(
            p3.X + (p2.X - p3.X) * u,
            p3.Y + (p2.Y - p3.Y) * u,
            p3.Z + (p2.Z - p3.Z) * u);

        return new Point3D(
            bottom.X + (top.X - bottom.X) * v,
            bottom.Y + (top.Y - bottom.Y) * v,
            bottom.Z + (top.Z - bottom.Z) * v);
    }

    private static Vector3D CalculateNormal(Point3D p0, Point3D p1, Point3D p2)
    {
        var a = p1 - p0;
        var b = p2 - p0;
        var normal = Vector3D.CrossProduct(a, b);
        if (normal.LengthSquared <= 0.000001)
            return new Vector3D(0, 0, 1);

        normal.Normalize();
        return normal;
    }

    private static bool IsOuterLayerSource(Rect source)
        => source.Y >= 32 || source.X >= 32 && source.Y < 16;

    private static bool IsCapeSource(Rect source)
        => source.Right <= 22 && source.Bottom <= 17;

    private sealed class PixelMeshBuilder
    {
        private readonly Dictionary<Color, MeshGeometry3D> _meshes = new();

        public void AddQuad(Color color, Point3D p0, Point3D p1, Point3D p2, Point3D p3)
        {
            var mesh = GetMesh(color);
            var index = mesh.Positions.Count;
            mesh.Positions.Add(p0);
            mesh.Positions.Add(p1);
            mesh.Positions.Add(p2);
            mesh.Positions.Add(p3);
            mesh.TriangleIndices.Add(index);
            mesh.TriangleIndices.Add(index + 1);
            mesh.TriangleIndices.Add(index + 2);
            mesh.TriangleIndices.Add(index);
            mesh.TriangleIndices.Add(index + 2);
            mesh.TriangleIndices.Add(index + 3);
        }

        public void AddTo(Model3DGroup group)
        {
            foreach (var (color, mesh) in _meshes)
            {
                mesh.Freeze();
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                var material = new DiffuseMaterial(brush);
                material.Freeze();

                group.Children.Add(new GeometryModel3D
                {
                    Geometry = mesh,
                    Material = material,
                    BackMaterial = material
                });
            }
        }

        private MeshGeometry3D GetMesh(Color color)
        {
            if (_meshes.TryGetValue(color, out var mesh))
                return mesh;

            mesh = new MeshGeometry3D();
            _meshes[color] = mesh;
            return mesh;
        }
    }

    private static MeshGeometry3D CreateDiscMesh(Point3D center, double radiusX, double radiusZ)
    {
        const int segments = 48;
        var positions = new Point3DCollection { center };
        var triangles = new Int32Collection();

        for (var i = 0; i <= segments; i++)
        {
            var angle = Math.PI * 2 * i / segments;
            positions.Add(new Point3D(center.X + Math.Cos(angle) * radiusX, center.Y, center.Z + Math.Sin(angle) * radiusZ));
            if (i <= 0)
                continue;

            triangles.Add(0);
            triangles.Add(i);
            triangles.Add(i + 1);
        }

        var mesh = new MeshGeometry3D
        {
            Positions = positions,
            TriangleIndices = triangles
        };
        mesh.Freeze();
        return mesh;
    }

    private void SkinPreviewViewport_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingPreview = true;
        _lastPreviewPoint = e.GetPosition(SkinPreviewViewport);
        SkinPreviewViewport.CaptureMouse();
    }

    private void SkinPreviewViewport_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingPreview = false;
        SkinPreviewViewport.ReleaseMouseCapture();
    }

    private void SkinPreviewViewport_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingPreview)
            return;

        var point = e.GetPosition(SkinPreviewViewport);
        var delta = point - _lastPreviewPoint;
        _lastPreviewPoint = point;

        _previewYaw.Angle += delta.X * 0.55;
        _previewPitch.Angle = Math.Clamp(_previewPitch.Angle - delta.Y * 0.45, -80, 80);
    }

    private void SkinPreviewViewport_OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _previewCameraDistance = Math.Clamp(_previewCameraDistance - e.Delta / 120.0 * 3.0, 44, 82);
        SkinPreviewCamera.Position = new Point3D(0, 16, _previewCameraDistance);
        SkinPreviewCamera.LookDirection = new Vector3D(0, 0, -_previewCameraDistance);
    }

    private sealed class TextureMap
    {
        private TextureMap(BitmapSource bitmap, double scale, double baseWidth, double baseHeight)
        {
            Bitmap = ConvertToPbgra32(bitmap);
            Scale = scale;
            BaseWidth = baseWidth;
            BaseHeight = baseHeight;
            Stride = Bitmap.PixelWidth * 4;
            Pixels = new byte[Stride * Bitmap.PixelHeight];
            Bitmap.CopyPixels(Pixels, Stride, 0);
        }

        public BitmapSource Bitmap { get; }
        public double Scale { get; }
        public double BaseWidth { get; }
        public double BaseHeight { get; }
        private byte[] Pixels { get; }
        private int Stride { get; }
        public bool HasModernLayers => BaseHeight >= 64;

        public static TextureMap CreateSkin(BitmapSource bitmap)
        {
            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
                throw new InvalidOperationException("A imagem de skin não possui dimensões válidas.");

            var scale = bitmap.PixelWidth / 64.0;
            if (scale <= 0 || Math.Abs(scale - Math.Round(scale)) > 0.001)
                throw new InvalidOperationException("A skin deve usar largura equivalente a 64 pixels, como 64x64, 128x128, 64x32 ou 128x64.");

            var baseHeight = bitmap.PixelHeight / scale;
            if (Math.Abs(baseHeight - 64) > 0.001 && Math.Abs(baseHeight - 32) > 0.001)
                throw new InvalidOperationException("A skin deve estar no formato Minecraft 64x64 ou legado 64x32.");

            return new TextureMap(bitmap, scale, 64, baseHeight);
        }

        public static TextureMap CreateCape(BitmapSource bitmap)
        {
            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
                throw new InvalidOperationException("A imagem de capa não possui dimensões válidas.");

            var scale = bitmap.PixelWidth / 64.0;
            if (scale <= 0)
                throw new InvalidOperationException("A capa deve usar largura equivalente a 64 pixels.");

            return new TextureMap(bitmap, scale, 64, bitmap.PixelHeight / scale);
        }

        public bool Contains(Rect source)
            => source.X >= 0
               && source.Y >= 0
               && source.Right <= BaseWidth
               && source.Bottom <= BaseHeight;

        public Color PixelColor(int baseX, int baseY)
        {
            var startX = (int)Math.Round(baseX * Scale);
            var startY = (int)Math.Round(baseY * Scale);
            var endX = Math.Min(Bitmap.PixelWidth, Math.Max(startX + 1, (int)Math.Round((baseX + 1) * Scale)));
            var endY = Math.Min(Bitmap.PixelHeight, Math.Max(startY + 1, (int)Math.Round((baseY + 1) * Scale)));

            var a = 0;
            var r = 0;
            var g = 0;
            var b = 0;
            var count = 0;

            for (var y = startY; y < endY; y++)
            {
                for (var x = startX; x < endX; x++)
                {
                    var offset = y * Stride + x * 4;
                    b += Pixels[offset];
                    g += Pixels[offset + 1];
                    r += Pixels[offset + 2];
                    a += Pixels[offset + 3];
                    count++;
                }
            }

            if (count == 0)
                return Colors.Transparent;

            return Color.FromArgb(
                (byte)(a / count),
                (byte)(r / count),
                (byte)(g / count),
                (byte)(b / count));
        }

        private static BitmapSource ConvertToPbgra32(BitmapSource source)
        {
            if (source.Format == PixelFormats.Pbgra32)
                return source;

            var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            converted.Freeze();
            return converted;
        }
    }

    private readonly record struct PreviewBoxSize(double X, double Y, double Z);

    private readonly record struct FaceSet(Rect Right, Rect Front, Rect Left, Rect Back, Rect Top, Rect Bottom)
    {
        public static FaceSet Head => new(new Rect(0, 8, 8, 8), new Rect(8, 8, 8, 8), new Rect(16, 8, 8, 8), new Rect(24, 8, 8, 8), new Rect(8, 0, 8, 8), new Rect(16, 0, 8, 8));
        public static FaceSet HeadLayer => new(new Rect(32, 8, 8, 8), new Rect(40, 8, 8, 8), new Rect(48, 8, 8, 8), new Rect(56, 8, 8, 8), new Rect(40, 0, 8, 8), new Rect(48, 0, 8, 8));
        public static FaceSet Body => new(new Rect(16, 20, 4, 12), new Rect(20, 20, 8, 12), new Rect(28, 20, 4, 12), new Rect(32, 20, 8, 12), new Rect(20, 16, 8, 4), new Rect(28, 16, 8, 4));
        public static FaceSet BodyLayer => new(new Rect(16, 36, 4, 12), new Rect(20, 36, 8, 12), new Rect(28, 36, 4, 12), new Rect(32, 36, 8, 12), new Rect(20, 32, 8, 4), new Rect(28, 32, 8, 4));
        public static FaceSet RightArm => new(new Rect(40, 20, 4, 12), new Rect(44, 20, 4, 12), new Rect(48, 20, 4, 12), new Rect(52, 20, 4, 12), new Rect(44, 16, 4, 4), new Rect(48, 16, 4, 4));
        public static FaceSet RightArmLayer => new(new Rect(40, 36, 4, 12), new Rect(44, 36, 4, 12), new Rect(48, 36, 4, 12), new Rect(52, 36, 4, 12), new Rect(44, 32, 4, 4), new Rect(48, 32, 4, 4));
        public static FaceSet LeftArm => new(new Rect(32, 52, 4, 12), new Rect(36, 52, 4, 12), new Rect(40, 52, 4, 12), new Rect(44, 52, 4, 12), new Rect(36, 48, 4, 4), new Rect(40, 48, 4, 4));
        public static FaceSet LeftArmLayer => new(new Rect(48, 52, 4, 12), new Rect(52, 52, 4, 12), new Rect(56, 52, 4, 12), new Rect(60, 52, 4, 12), new Rect(52, 48, 4, 4), new Rect(56, 48, 4, 4));
        public static FaceSet RightLeg => new(new Rect(0, 20, 4, 12), new Rect(4, 20, 4, 12), new Rect(8, 20, 4, 12), new Rect(12, 20, 4, 12), new Rect(4, 16, 4, 4), new Rect(8, 16, 4, 4));
        public static FaceSet RightLegLayer => new(new Rect(0, 36, 4, 12), new Rect(4, 36, 4, 12), new Rect(8, 36, 4, 12), new Rect(12, 36, 4, 12), new Rect(4, 32, 4, 4), new Rect(8, 32, 4, 4));
        public static FaceSet LeftLeg => new(new Rect(16, 52, 4, 12), new Rect(20, 52, 4, 12), new Rect(24, 52, 4, 12), new Rect(28, 52, 4, 12), new Rect(20, 48, 4, 4), new Rect(24, 48, 4, 4));
        public static FaceSet LeftLegLayer => new(new Rect(0, 52, 4, 12), new Rect(4, 52, 4, 12), new Rect(8, 52, 4, 12), new Rect(12, 52, 4, 12), new Rect(4, 48, 4, 4), new Rect(8, 48, 4, 4));
        public static FaceSet Cape => new(new Rect(0, 1, 1, 16), new Rect(1, 1, 10, 16), new Rect(11, 1, 1, 16), new Rect(12, 1, 10, 16), new Rect(1, 0, 10, 1), new Rect(11, 0, 10, 1));
    }

    private void SetSkinPreview(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            SkinPreview.Source = null;
            SkinPreviewPlaceholder.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();

            SkinPreview.Source = RenderSkinPreview(bitmap);
            SkinPreviewPlaceholder.Text = "Preview";
            SkinPreviewPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            SkinPreview.Source = null;
            SkinPreviewPlaceholder.Text = "Skin inválida";
            SkinPreviewPlaceholder.Visibility = Visibility.Visible;
            AppendLog($"Preview da skin indisponível: {ex.Message}");
        }
    }

    private static ImageSource RenderSkinPreview(BitmapSource skin)
    {
        const int canvasWidth = 360;
        const int canvasHeight = 420;
        const double modelScale = 10.8;
        const double modelWidth = 16;
        const double modelHeight = 32;
        var skinMap = MinecraftSkinMap.Create(skin);
        var visual = new DrawingVisual();

        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromRgb(8, 13, 19)), null, new Rect(0, 0, canvasWidth, canvasHeight));

            var origin = new Point(
                (canvasWidth - modelWidth * modelScale) / 2,
                (canvasHeight - modelHeight * modelScale) / 2 - 4);

            var shadowBrush = new SolidColorBrush(Color.FromArgb(90, 0, 0, 0));
            context.DrawEllipse(shadowBrush, null, new Point(canvasWidth / 2.0, origin.Y + modelHeight * modelScale + 10), 86, 16);

            Rect Model(double x, double y, double width, double height)
                => new(origin.X + x * modelScale, origin.Y + y * modelScale, width * modelScale, height * modelScale);

            DrawSkinPart(context, skinMap, new Rect(8, 8, 8, 8), Model(4, 0, 8, 8));
            DrawSkinPart(context, skinMap, new Rect(20, 20, 8, 12), Model(4, 8, 8, 12));
            DrawSkinPart(context, skinMap, new Rect(44, 20, 4, 12), Model(0, 8, 4, 12));
            DrawSkinPart(context, skinMap, new Rect(36, 52, 4, 12), Model(12, 8, 4, 12), fallbackSource: new Rect(44, 20, 4, 12), flipFallback: true);
            DrawSkinPart(context, skinMap, new Rect(4, 20, 4, 12), Model(4, 20, 4, 12));
            DrawSkinPart(context, skinMap, new Rect(20, 52, 4, 12), Model(8, 20, 4, 12), fallbackSource: new Rect(4, 20, 4, 12), flipFallback: true);

            if (skinMap.HasModernLayers)
            {
                DrawSkinPart(context, skinMap, new Rect(40, 8, 8, 8), Inflate(Model(4, 0, 8, 8), 0.55 * modelScale));
                DrawSkinPart(context, skinMap, new Rect(20, 36, 8, 12), Inflate(Model(4, 8, 8, 12), 0.28 * modelScale));
                DrawSkinPart(context, skinMap, new Rect(44, 36, 4, 12), Inflate(Model(0, 8, 4, 12), 0.25 * modelScale));
                DrawSkinPart(context, skinMap, new Rect(52, 52, 4, 12), Inflate(Model(12, 8, 4, 12), 0.25 * modelScale));
                DrawSkinPart(context, skinMap, new Rect(4, 36, 4, 12), Inflate(Model(4, 20, 4, 12), 0.22 * modelScale));
                DrawSkinPart(context, skinMap, new Rect(4, 52, 4, 12), Inflate(Model(8, 20, 4, 12), 0.22 * modelScale));
            }
        }

        var target = new RenderTargetBitmap(canvasWidth, canvasHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private static void DrawSkinPart(
        DrawingContext context,
        MinecraftSkinMap skinMap,
        Rect source,
        Rect target,
        Rect? fallbackSource = null,
        bool flipFallback = false)
    {
        if (!skinMap.Contains(source))
        {
            if (fallbackSource is not { } fallback || !skinMap.Contains(fallback))
                return;

            source = fallback;
        }

        var crop = skinMap.Crop(source);
        if (flipFallback && fallbackSource is not null && source == fallbackSource.Value)
            crop = new TransformedBitmap(crop, new ScaleTransform(-1, 1, crop.Width / 2, crop.Height / 2));

        crop.Freeze();
        context.DrawImage(crop, target);
    }

    private static Rect Inflate(Rect rect, double amount)
    {
        rect.Inflate(amount, amount);
        return rect;
    }

    private sealed class MinecraftSkinMap
    {
        private MinecraftSkinMap(BitmapSource bitmap, double scale, double baseHeight)
        {
            Bitmap = bitmap;
            Scale = scale;
            BaseHeight = baseHeight;
        }

        public BitmapSource Bitmap { get; }
        public double Scale { get; }
        public double BaseHeight { get; }
        public bool HasModernLayers => BaseHeight >= 64;

        public static MinecraftSkinMap Create(BitmapSource bitmap)
        {
            if (bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
                throw new InvalidOperationException("A imagem de skin não possui dimensões válidas.");

            var scale = bitmap.PixelWidth / 64.0;
            if (scale <= 0 || Math.Abs(scale - Math.Round(scale)) > 0.001)
                throw new InvalidOperationException("A skin deve usar largura equivalente a 64 pixels, como 64x64, 128x128, 64x32 ou 128x64.");

            var baseHeight = bitmap.PixelHeight / scale;
            if (Math.Abs(baseHeight - 64) > 0.001 && Math.Abs(baseHeight - 32) > 0.001)
                throw new InvalidOperationException("A skin deve estar no formato Minecraft 64x64 ou legado 64x32.");

            return new MinecraftSkinMap(bitmap, scale, baseHeight);
        }

        public bool Contains(Rect source)
            => source.X >= 0
               && source.Y >= 0
               && source.Right <= 64
               && source.Bottom <= BaseHeight;

        public BitmapSource Crop(Rect source)
        {
            var rect = new Int32Rect(
                (int)Math.Round(source.X * Scale),
                (int)Math.Round(source.Y * Scale),
                (int)Math.Round(source.Width * Scale),
                (int)Math.Round(source.Height * Scale));
            return new CroppedBitmap(Bitmap, rect);
        }
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint hwnd, int dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint hMonitor, ref MonitorInfo lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint ptReserved;
        public NativePoint ptMaxSize;
        public NativePoint ptMaxPosition;
        public NativePoint ptMinTrackSize;
        public NativePoint ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int cbSize;
        public NativeRect rcMonitor;
        public NativeRect rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static string? EmptyToNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
