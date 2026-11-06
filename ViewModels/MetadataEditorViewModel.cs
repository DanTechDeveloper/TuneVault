using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TuneVault.Models;
using TuneVault.Services;

namespace TuneVault.ViewModels;

/// <summary>
/// Owns the track-info modal: editable tag fields, cover art, saving through
/// TagLibSharp, and the delete request that <see cref="LibraryViewModel"/> confirms.
/// </summary>
public sealed partial class MetadataEditorViewModel : ObservableObject
{
    private readonly IFilePickerService _filePicker;
    private readonly LibraryViewModel _library;
    private bool _editCoverArtChanged;

    public MetadataEditorViewModel(
        IFilePickerService filePicker,
        LibraryViewModel library)
    {
        _filePicker = filePicker;
        _library = library;

        _library.TrackRemoved += OnTrackRemoved;
    }

    [ObservableProperty]
    private bool _isEditMetadataModalOpen;

    [ObservableProperty]
    private Track? _editingTrack;

    [ObservableProperty]
    private string _editTitle = "";

    [ObservableProperty]
    private string _editArtist = "";

    [ObservableProperty]
    private string _editAlbum = "";

    [ObservableProperty]
    private string _editGenre = "";

    [ObservableProperty]
    private string _editYear = "";

    [ObservableProperty]
    private string _editYearError = "";

    [ObservableProperty]
    private string _editFilePath = "";

    [ObservableProperty]
    private string _editDuration = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditCoverArt))]
    private byte[]? _editPictureData;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEditCoverArt))]
    private Avalonia.Media.Imaging.Bitmap? _editCoverArtBitmap;

    public bool HasEditCoverArt => EditCoverArtBitmap is not null;

    [RelayCommand]
    private void EditTrack(Track? track)
    {
        if (track is null)
        {
            return;
        }

        _library.CancelRemoveTrackCommand.Execute(null);

        EditingTrack = track;
        EditTitle = track.Title;
        EditArtist = track.Artist;
        EditAlbum = track.Album ?? "";
        EditGenre = track.Genre ?? "";
        EditYear = track.Year > 0 ? track.Year.ToString() : "";
        EditYearError = "";
        EditFilePath = track.FilePath ?? "Unknown";
        EditDuration = track.DurationDisplay;
        EditPictureData = track.PictureData;
        _editCoverArtChanged = false;

        if (track.PictureData is { Length: > 0 })
        {
            try
            {
                using var ms = new MemoryStream(track.PictureData);
                EditCoverArtBitmap = new Avalonia.Media.Imaging.Bitmap(ms);
            }
            catch
            {
                EditCoverArtBitmap = null;
            }
        }
        else
        {
            EditCoverArtBitmap = null;
        }

        IsEditMetadataModalOpen = true;
    }

    [RelayCommand]
    private void CancelEditMetadata()
    {
        _library.CancelRemoveTrackCommand.Execute(null);
        IsEditMetadataModalOpen = false;
        EditingTrack = null;
        EditPictureData = null;
        EditCoverArtBitmap = null;
        EditYearError = "";
        _editCoverArtChanged = false;
    }

    [RelayCommand]
    private async Task ChooseCoverArtAsync()
    {
        try
        {
            var imagePath = await _filePicker.PickImageFileAsync();
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                return;
            }

            var bytes = await File.ReadAllBytesAsync(imagePath);
            EditPictureData = bytes;
            _editCoverArtChanged = true;

            using var ms = new MemoryStream(bytes);
            EditCoverArtBitmap = new Avalonia.Media.Imaging.Bitmap(ms);
        }
        catch
        {
        }
    }

    [RelayCommand]
    private void RemoveCoverArt()
    {
        EditPictureData = null;
        EditCoverArtBitmap = null;
        _editCoverArtChanged = true;
    }

    /// <summary>
    /// Asks the library to confirm deleting the track currently open in the editor.
    /// The confirmation overlay lives at the window level so it can also serve
    /// other delete entry points later.
    /// </summary>
    [RelayCommand]
    private void RequestRemoveEditingTrack()
    {
        _library.RequestRemoveTrackCommand.Execute(EditingTrack);
    }

    [RelayCommand]
    private async Task SaveTrackMetadataAsync()
    {
        if (EditingTrack is null)
        {
            return;
        }

        var track = EditingTrack;
        var newTitle = string.IsNullOrWhiteSpace(EditTitle) ? TitleFromPath(track.FilePath ?? "") : EditTitle.Trim();
        var newArtist = string.IsNullOrWhiteSpace(EditArtist) ? "Unknown Artist" : EditArtist.Trim();
        var newAlbum = string.IsNullOrWhiteSpace(EditAlbum) ? null : EditAlbum.Trim();
        var newGenre = string.IsNullOrWhiteSpace(EditGenre) ? null : EditGenre.Trim();
        EditYearError = "";
        uint newYear = 0;
        if (!string.IsNullOrWhiteSpace(EditYear))
        {
            if (!uint.TryParse(EditYear.Trim(), out newYear) || newYear > 9999)
            {
                EditYearError = "Enter a valid year (1–9999) or leave blank.";
                return;
            }
        }

        // The library skips duplicate title/artist pairs, so refuse the edit before
        // rewriting the audio file's tags — otherwise the file and the database diverge.
        if (await _library.IsIdentityTakenAsync(newTitle, newArtist, track.Id))
        {
            return;
        }

        var oldArtist = track.Artist;
        track.Title = newTitle;
        track.Artist = newArtist;
        // Keep the album-artist grouping key in sync when it tracked the old
        // performer, so Artists/Album Artists views follow the rename.
        var sameAlbumArtist = string.Equals(track.AlbumArtist?.Trim(), oldArtist?.Trim(), StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(track.AlbumArtist) || sameAlbumArtist)
        {
            track.AlbumArtist = newArtist;
        }
        track.Album = newAlbum;
        track.Genre = newGenre;
        track.Year = newYear;

        if (_editCoverArtChanged)
        {
            track.PictureData = EditPictureData;
        }

        // Propagate the edit to every in-memory instance of this track so all
        // views/rows update without a manual reload.
        _library.ApplyTrackMetadata(track);

        if (!string.IsNullOrEmpty(track.FilePath) && File.Exists(track.FilePath))
        {
            await Task.Run(() =>
            {
                try
                {
                    using var tagFile = TagLib.File.Create(track.FilePath);
                    tagFile.Tag.Title = newTitle;
                    tagFile.Tag.Performers = new[] { newArtist };
                    tagFile.Tag.Album = newAlbum ?? "";
                    tagFile.Tag.Genres = string.IsNullOrEmpty(newGenre) ? Array.Empty<string>() : new[] { newGenre };
                    tagFile.Tag.Year = newYear;

                    if (_editCoverArtChanged)
                    {
                        if (EditPictureData is { Length: > 0 })
                        {
                            var pic = new TagLib.Picture(new TagLib.ByteVector(EditPictureData))
                            {
                                Type = TagLib.PictureType.FrontCover,
                                Description = "Cover",
                                MimeType = "image/jpeg"
                            };
                            tagFile.Tag.Pictures = new TagLib.IPicture[] { pic };
                        }
                        else
                        {
                            tagFile.Tag.Pictures = Array.Empty<TagLib.IPicture>();
                        }
                    }

                    tagFile.Save();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to save tags to file: {ex.Message}");
                }
            });
        }

        try
        {
            await _library.PersistTrackAsync(track);
        }
        catch
        {
        }

        // Reuse the window-level toast so the edit is confirmed the same way
        // imports are, mirroring the TrackRow layout (cover + title + artist/album + genre).
        _library.ToastTitle = newTitle;
        _library.ToastSubtitle = track.ArtistAlbum;
        _library.ToastDetail = track.GenreDisplay;
        _library.ToastMessage = "DONE!";
        Avalonia.Media.Imaging.Bitmap? cover = null;
        if (track.PictureData is { Length: > 0 })
        {
            try
            {
                using var ms = new MemoryStream(track.PictureData);
                cover = new Avalonia.Media.Imaging.Bitmap(ms);
            }
            catch
            {
            }
        }
        _library.ToastCover = cover;
        _library.ShowToast = true;

        IsEditMetadataModalOpen = false;
        EditingTrack = null;
        EditPictureData = null;
        EditCoverArtBitmap = null;
        _editCoverArtChanged = false;
    }

    partial void OnEditYearChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            EditYearError = "";
            return;
        }
        if (uint.TryParse(value.Trim(), out var y) && y <= 9999)
        {
            EditYearError = "";
        }
    }

    private void OnTrackRemoved(Track track)
    {
        if (!ReferenceEquals(EditingTrack, track))
        {
            return;
        }

        IsEditMetadataModalOpen = false;
        EditingTrack = null;
        EditPictureData = null;
        EditCoverArtBitmap = null;
        _editCoverArtChanged = false;
    }

    private static string TitleFromPath(string path)
    {
        var title = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(title) ? "Untitled track" : title;
    }
}
