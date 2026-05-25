using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.UpperTabs.DuplicateVideos
{
    internal sealed class UpperTabDuplicateItemViewModel : INotifyPropertyChanged
    {
        private MovieRecords _movieRecord;
        private string _thumbnailPath = "";
        private string _previewThumbnailPath = "";
        private string _movieName = "";
        private string _probText = "";
        private string _movieSizeText = "";
        private string _sizeCompareText = "";
        private string _fileDateText = "";
        private string _moviePath = "";

        public event PropertyChangedEventHandler PropertyChanged;

        public MovieRecords MovieRecord
        {
            get => _movieRecord;
            set => SetProperty(ref _movieRecord, value);
        }

        public string ThumbnailPath
        {
            get => _thumbnailPath;
            set => SetProperty(ref _thumbnailPath, value);
        }

        public string PreviewThumbnailPath
        {
            get => _previewThumbnailPath;
            set => SetProperty(ref _previewThumbnailPath, value);
        }

        public string MovieName
        {
            get => _movieName;
            set => SetProperty(ref _movieName, value);
        }

        public string ProbText
        {
            get => _probText;
            set => SetProperty(ref _probText, value);
        }

        public string MovieSizeText
        {
            get => _movieSizeText;
            set => SetProperty(ref _movieSizeText, value);
        }

        public string SizeCompareText
        {
            get => _sizeCompareText;
            set => SetProperty(ref _sizeCompareText, value);
        }

        public string FileDateText
        {
            get => _fileDateText;
            set => SetProperty(ref _fileDateText, value);
        }

        public string MoviePath
        {
            get => _moviePath;
            set => SetProperty(ref _moviePath, value);
        }

        private void SetProperty<T>(
            ref T field,
            T value,
            [CallerMemberName] string propertyName = ""
        )
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
