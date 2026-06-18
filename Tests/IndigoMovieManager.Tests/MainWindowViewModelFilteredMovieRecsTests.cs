using System.Collections.Specialized;
using System.Threading;
using IndigoMovieManager;
using IndigoMovieManager.ViewModels;

namespace IndigoMovieManager.Tests;

[TestFixture]
[Apartment(ApartmentState.STA)]
public sealed class MainWindowViewModelFilteredMovieRecsTests
{
    [Test]
    public void 同一順序なら無変更で終わる()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords movieA = CreateMovie("A");
        MovieRecords movieB = CreateMovie("B");
        MovieRecords movieC = CreateMovie("C");

        _ = viewModel.ReplaceFilteredMovieRecs([movieA, movieB, movieC]);

        FilteredMovieRecsUpdateResult result = viewModel.ReplaceFilteredMovieRecs(
            [movieA, movieB, movieC]
        );

        Assert.That(result.HasChanges, Is.False);
        Assert.That(result.RetainedPrefixCount, Is.EqualTo(3));
        Assert.That(result.RetainedSuffixCount, Is.EqualTo(0));
        Assert.That(result.MovedCount, Is.EqualTo(0));
        Assert.That(viewModel.FilteredMovieRecs, Is.EqualTo([movieA, movieB, movieC]));
    }

    [Test]
    public void 共通prefixとsuffixを残して中間だけ差し替える()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords movieA = CreateMovie("A");
        MovieRecords movieB = CreateMovie("B");
        MovieRecords movieC = CreateMovie("C");
        MovieRecords movieD = CreateMovie("D");
        MovieRecords movieE = CreateMovie("E");
        MovieRecords movieX = CreateMovie("X");
        MovieRecords movieY = CreateMovie("Y");

        _ = viewModel.ReplaceFilteredMovieRecs([movieA, movieB, movieC, movieD, movieE]);

        FilteredMovieRecsUpdateResult result = viewModel.ReplaceFilteredMovieRecs(
            [movieA, movieX, movieY, movieE]
        );

        Assert.That(result.HasChanges, Is.True);
        Assert.That(result.RetainedPrefixCount, Is.EqualTo(1));
        Assert.That(result.RetainedSuffixCount, Is.EqualTo(1));
        Assert.That(result.RemovedCount, Is.EqualTo(3));
        Assert.That(result.InsertedCount, Is.EqualTo(2));
        Assert.That(result.MovedCount, Is.EqualTo(0));
        Assert.That(result.UpdatedCount, Is.EqualTo(0));
        Assert.That(viewModel.FilteredMovieRecs, Is.EqualTo([movieA, movieX, movieY, movieE]));
    }

    [Test]
    public void 同じMoviePathの別インスタンス差し替えはInPlaceのUpdateとして扱う()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords movieA = CreateMovie("A");
        MovieRecords movieB = CreateMovie("B");
        MovieRecords movieBUpdated = CreateMovie("B");
        movieBUpdated.Score = 9;
        MovieRecords movieC = CreateMovie("C");

        _ = viewModel.ReplaceFilteredMovieRecs([movieA, movieB, movieC]);

        List<NotifyCollectionChangedAction> collectionActions = [];
        viewModel.FilteredMovieRecs.CollectionChanged += (_, args) =>
        {
            collectionActions.Add(args.Action);
        };

        FilteredMovieRecsUpdateResult result = viewModel.ReplaceFilteredMovieRecs(
            [movieA, movieBUpdated, movieC]
        );

        Assert.That(result.HasChanges, Is.True);
        Assert.That(result.RetainedPrefixCount, Is.EqualTo(1));
        Assert.That(result.RetainedSuffixCount, Is.EqualTo(1));
        Assert.That(result.RemovedCount, Is.EqualTo(0));
        Assert.That(result.InsertedCount, Is.EqualTo(0));
        Assert.That(result.UpdatedCount, Is.EqualTo(1));
        Assert.That(collectionActions, Is.EqualTo([NotifyCollectionChangedAction.Replace]));
        Assert.That(collectionActions, Does.Not.Contain(NotifyCollectionChangedAction.Remove));
        Assert.That(collectionActions, Does.Not.Contain(NotifyCollectionChangedAction.Add));
        Assert.That(viewModel.FilteredMovieRecs, Is.EqualTo([movieA, movieBUpdated, movieC]));
    }

    [Test]
    public void 同じMoviePath更新に続く小さな追加はReplaceとAddで適用する()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords movieA = CreateMovie("A");
        MovieRecords movieB = CreateMovie("B");
        MovieRecords movieBUpdated = CreateMovie("B");
        MovieRecords movieC = CreateMovie("C");
        MovieRecords movieX = CreateMovie("X");

        _ = viewModel.ReplaceFilteredMovieRecs([movieA, movieB, movieC]);

        List<NotifyCollectionChangedAction> collectionActions = [];
        viewModel.FilteredMovieRecs.CollectionChanged += (_, args) =>
        {
            collectionActions.Add(args.Action);
        };

        FilteredMovieRecsUpdateResult result = viewModel.ReplaceFilteredMovieRecs(
            [movieA, movieBUpdated, movieX, movieC]
        );

        Assert.That(result.HasChanges, Is.True);
        Assert.That(result.RemovedCount, Is.EqualTo(0));
        Assert.That(result.InsertedCount, Is.EqualTo(1));
        Assert.That(result.UpdatedCount, Is.EqualTo(1));
        Assert.That(
            collectionActions,
            Is.EqualTo([NotifyCollectionChangedAction.Replace, NotifyCollectionChangedAction.Add])
        );
        Assert.That(
            viewModel.FilteredMovieRecs,
            Is.EqualTo([movieA, movieBUpdated, movieX, movieC])
        );
    }

    [Test]
    public void 同じMoviePath更新に続く小さな削除はReplaceとRemoveで適用する()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords movieA = CreateMovie("A");
        MovieRecords movieB = CreateMovie("B");
        MovieRecords movieBUpdated = CreateMovie("B");
        MovieRecords movieC = CreateMovie("C");
        MovieRecords movieX = CreateMovie("X");

        _ = viewModel.ReplaceFilteredMovieRecs([movieA, movieB, movieX, movieC]);

        List<NotifyCollectionChangedAction> collectionActions = [];
        viewModel.FilteredMovieRecs.CollectionChanged += (_, args) =>
        {
            collectionActions.Add(args.Action);
        };

        FilteredMovieRecsUpdateResult result = viewModel.ReplaceFilteredMovieRecs(
            [movieA, movieBUpdated, movieC]
        );

        Assert.That(result.HasChanges, Is.True);
        Assert.That(result.RemovedCount, Is.EqualTo(1));
        Assert.That(result.InsertedCount, Is.EqualTo(0));
        Assert.That(result.UpdatedCount, Is.EqualTo(1));
        Assert.That(
            collectionActions,
            Is.EqualTo(
                [NotifyCollectionChangedAction.Replace, NotifyCollectionChangedAction.Remove]
            )
        );
        Assert.That(viewModel.FilteredMovieRecs, Is.EqualTo([movieA, movieBUpdated, movieC]));
    }

    [Test]
    public void 重複MoviePathはStableKey更新扱いにしない()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords movieA = CreateMovie("A");
        MovieRecords movieB1 = CreateMovie("B1", @"C:\movies\dup.mp4");
        MovieRecords movieB1Updated = CreateMovie("B1-updated", @"C:\movies\dup.mp4");
        MovieRecords movieB2 = CreateMovie("B2", @"C:\movies\dup.mp4");
        MovieRecords movieC = CreateMovie("C");

        _ = viewModel.ReplaceFilteredMovieRecs([movieA, movieB1, movieB2, movieC]);

        List<NotifyCollectionChangedAction> collectionActions = [];
        viewModel.FilteredMovieRecs.CollectionChanged += (_, args) =>
        {
            collectionActions.Add(args.Action);
        };

        FilteredMovieRecsUpdateResult result = viewModel.ReplaceFilteredMovieRecs(
            [movieA, movieB1Updated, movieB2, movieC]
        );

        Assert.That(result.HasChanges, Is.True);
        Assert.That(result.RemovedCount, Is.EqualTo(1));
        Assert.That(result.InsertedCount, Is.EqualTo(1));
        Assert.That(result.UpdatedCount, Is.EqualTo(0));
        Assert.That(
            collectionActions,
            Is.EqualTo([NotifyCollectionChangedAction.Remove, NotifyCollectionChangedAction.Add])
        );
        Assert.That(
            viewModel.FilteredMovieRecs,
            Is.EqualTo([movieA, movieB1Updated, movieB2, movieC])
        );
    }

    [Test]
    public void sort_onlyはMove中心で並び替える()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords movieA = CreateMovie("A");
        MovieRecords movieB = CreateMovie("B");
        MovieRecords movieC = CreateMovie("C");
        MovieRecords movieD = CreateMovie("D");

        _ = viewModel.ReplaceFilteredMovieRecs([movieA, movieB, movieC, movieD]);

        FilteredMovieRecsUpdateResult result = viewModel.ReplaceFilteredMovieRecs(
            [movieD, movieB, movieA, movieC],
            updateMode: FilteredMovieRecsUpdateMode.Move
        );

        Assert.That(result.HasChanges, Is.True);
        Assert.That(result.RemovedCount, Is.EqualTo(0));
        Assert.That(result.InsertedCount, Is.EqualTo(0));
        Assert.That(result.MovedCount, Is.GreaterThan(0));
        Assert.That(result.UpdatedCount, Is.EqualTo(0));
        Assert.That(viewModel.FilteredMovieRecs, Is.EqualTo([movieD, movieB, movieA, movieC]));
    }

    [Test]
    public void Resetモードは全件入れ直し経路へ落ちる()
    {
        MainWindowViewModel viewModel = new();
        MovieRecords movieA = CreateMovie("A");
        MovieRecords movieB = CreateMovie("B");
        MovieRecords movieC = CreateMovie("C");

        _ = viewModel.ReplaceFilteredMovieRecs([movieA, movieB, movieC]);

        FilteredMovieRecsUpdateResult result = viewModel.ReplaceFilteredMovieRecs(
            [movieC, movieB, movieA],
            updateMode: FilteredMovieRecsUpdateMode.Reset
        );

        Assert.That(result.HasChanges, Is.True);
        Assert.That(result.MovedCount, Is.EqualTo(0));
        Assert.That(result.RemovedCount, Is.EqualTo(3));
        Assert.That(result.InsertedCount, Is.EqualTo(3));
        Assert.That(viewModel.FilteredMovieRecs, Is.EqualTo([movieC, movieB, movieA]));
    }

    private static MovieRecords CreateMovie(string name, string? moviePath = null)
    {
        return new MovieRecords
        {
            Movie_Name = name,
            Movie_Path = moviePath ?? $@"C:\movies\{name}.mp4",
        };
    }
}
