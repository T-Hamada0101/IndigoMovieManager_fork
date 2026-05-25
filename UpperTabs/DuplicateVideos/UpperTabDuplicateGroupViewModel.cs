using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace IndigoMovieManager.UpperTabs.DuplicateVideos
{
    internal sealed class UpperTabDuplicateGroupViewModel : INotifyPropertyChanged
    {
        private string _hash = "";
        private string _representativeThumbnailPath = "";
        private string _representativeMovieName = "";
        private int _duplicateCount;
        private string _maxMovieSizeText = "";
        private long _maxMovieSizeValue;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Hash
        {
            get => _hash;
            set => SetProperty(ref _hash, value);
        }

        public string RepresentativeThumbnailPath
        {
            get => _representativeThumbnailPath;
            set => SetProperty(ref _representativeThumbnailPath, value);
        }

        public string RepresentativeMovieName
        {
            get => _representativeMovieName;
            set => SetProperty(ref _representativeMovieName, value);
        }

        public int DuplicateCount
        {
            get => _duplicateCount;
            set => SetProperty(ref _duplicateCount, value);
        }

        public string MaxMovieSizeText
        {
            get => _maxMovieSizeText;
            set => SetProperty(ref _maxMovieSizeText, value);
        }

        public long MaxMovieSizeValue
        {
            get => _maxMovieSizeValue;
            set => SetProperty(ref _maxMovieSizeValue, value);
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
