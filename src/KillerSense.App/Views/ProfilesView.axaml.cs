using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using KillerSense.App.ViewModels;
using KillerSense.Ipc;

namespace KillerSense.App.Views;

public sealed partial class ProfilesView : UserControl
{
    private static readonly FilePickerFileType JsonFiles = new("Perfis do killer-sense (.json)") { Patterns = ["*.json"] };

    public ProfilesView() => InitializeComponent();

    private ProfilesViewModel? Vm => DataContext as ProfilesViewModel;

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        try
        {
            var json = vm.Export();
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Exportar perfis",
                SuggestedFileName = "killer-sense-perfis.json",
                DefaultExtension = "json",
                FileTypeChoices = [JsonFiles],
            }).ConfigureAwait(true);
            if (file is null)
            {
                return;
            }

            await using var stream = await file.OpenWriteAsync().ConfigureAwait(true);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json).ConfigureAwait(true);
            vm.Status = $"Perfis exportados para {file.Name}.";
            vm.StatusIsError = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            vm.Status = $"Não foi possível exportar: {ex.Message}";
            vm.StatusIsError = true;
        }
    }

    private async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Importar perfis",
                AllowMultiple = false,
                FileTypeFilter = [JsonFiles],
            }).ConfigureAwait(true);
            if (files.Count == 0)
            {
                return;
            }

            await using var stream = await files[0].OpenReadAsync().ConfigureAwait(true);
            if (stream.CanSeek && stream.Length > UserProfileSet.MaxFileLength)
            {
                vm.Status = "Não foi possível importar: arquivo grande demais.";
                vm.StatusIsError = true;
                return;
            }

            using var reader = new StreamReader(stream);
            vm.Import(await reader.ReadToEndAsync().ConfigureAwait(true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            vm.Status = $"Não foi possível importar: {ex.Message}";
            vm.StatusIsError = true;
        }
    }
}
