namespace IndigoMovieManager.Tests;

[TestFixture]
public sealed class MovieViewStableKeyPolicyTests
{
    [Test]
    public void TryResolve_MovieIdが正ならIdを優先する()
    {
        MovieRecords movie = new() { Movie_Id = 42, Movie_Path = @"C:\movies\sample.mp4" };

        bool resolved = MovieViewStableKeyPolicy.TryResolve(movie, out string stableKey);

        Assert.That(resolved, Is.True);
        Assert.That(stableKey, Is.EqualTo("id:42"));
    }

    [Test]
    public void TryResolve_MovieIdが正でなければPathへフォールバックする()
    {
        MovieRecords movie = new() { Movie_Id = 0, Movie_Path = @"C:\movies\sample.mp4" };

        bool resolved = MovieViewStableKeyPolicy.TryResolve(movie, out string stableKey);

        Assert.That(resolved, Is.True);
        Assert.That(stableKey, Is.EqualTo(@"path:C:\movies\sample.mp4"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void TryResolve_MovieIdが正でなくPathが空白なら解決できない(string? moviePath)
    {
        MovieRecords movie = new() { Movie_Id = 0, Movie_Path = moviePath! };

        bool resolved = MovieViewStableKeyPolicy.TryResolve(movie, out string stableKey);

        Assert.That(resolved, Is.False);
        Assert.That(stableKey, Is.Empty);
    }

    [Test]
    public void TryResolve_MovieがNullなら解決できない()
    {
        bool resolved = MovieViewStableKeyPolicy.TryResolve(null!, out string stableKey);

        Assert.That(resolved, Is.False);
        Assert.That(stableKey, Is.Empty);
    }

    [Test]
    public void AreSame_キー文字列は大文字小文字を区別しない()
    {
        bool areSame = MovieViewStableKeyPolicy.AreSame(
            @"path:C:\MOVIES\SAMPLE.MP4",
            @"PATH:c:\movies\sample.mp4"
        );

        Assert.That(areSame, Is.True);
    }

    [Test]
    public void AreSame_MovieRecordsもId優先で比較する()
    {
        MovieRecords left = new() { Movie_Id = 42, Movie_Path = @"C:\movies\before.mp4" };
        MovieRecords right = new() { Movie_Id = 42, Movie_Path = @"D:\movies\after.mp4" };

        Assert.That(MovieViewStableKeyPolicy.AreSame(left, right), Is.True);
    }

    [Test]
    public void AreSame_IdキーとPathキーは同一にしない()
    {
        MovieRecords registered = new() { Movie_Id = 42, Movie_Path = @"C:\movies\sample.mp4" };
        MovieRecords unregistered = new()
        {
            Movie_Id = 0,
            Movie_Path = @"C:\movies\sample.mp4",
        };

        Assert.That(MovieViewStableKeyPolicy.AreSame(registered, unregistered), Is.False);
    }
}
