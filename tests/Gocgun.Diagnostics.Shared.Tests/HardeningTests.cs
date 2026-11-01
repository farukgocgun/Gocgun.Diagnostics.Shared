using Gocgun.Diagnostics.Fixture;
using Gocgun.Diagnostics.Shared.Auth;
using Gocgun.Diagnostics.Shared.Decoding;
using Gocgun.Diagnostics.Shared.Hosting;
using Gocgun.Diagnostics.Shared.Profiles;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gocgun.Diagnostics.Tooling.Tests;

public sealed class HardeningTests
{
    [Fact]
    public void Throttle_LocksAfterMaxFailures_AndUnlocksAfterLockout()
    {
        var time = new ManualTime();
        var options = new DiagnosticsOptions { LoginMaxFailures = 3, LoginLockout = TimeSpan.FromMinutes(5) };
        var throttle = new LoginThrottle(options, time);
        Assert.False(throttle.RecordFailure());
        Assert.False(throttle.RecordFailure());
        Assert.False(throttle.IsLocked());
        Assert.True(throttle.RecordFailure());
        Assert.True(throttle.IsLocked());
        time.Advance(TimeSpan.FromMinutes(4));
        Assert.True(throttle.IsLocked());
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(throttle.IsLocked());
    }

    [Fact]
    public void Throttle_SuccessResetsTheCounter()
    {
        var throttle = new LoginThrottle(new DiagnosticsOptions { LoginMaxFailures = 2 }, new ManualTime());
        Assert.False(throttle.RecordFailure());
        throttle.Reset();
        Assert.False(throttle.RecordFailure());
        Assert.False(throttle.IsLocked());
    }

    [Fact]
    public void Options_HaveSafeDefaults_AndReadConfiguration()
    {
        var defaults = DiagnosticsOptions.From(new ConfigurationBuilder().Build());
        Assert.Equal(TimeSpan.FromHours(8), defaults.SessionLifetime);
        Assert.Equal(5, defaults.LoginMaxFailures);
        Assert.Equal(1_048_576, defaults.MaxValueBytes);
        Assert.Null(defaults.ContractRoot);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Diagnostics:SessionHours"] = "2",
                ["Diagnostics:MaxValueBytes"] = "10",
                ["Diagnostics:LoginMaxFailures"] = "bad",
                ["Diagnostics:ContractRoot"] = "/contracts",
            })
            .Build();
        var custom = DiagnosticsOptions.From(configuration);
        Assert.Equal(TimeSpan.FromHours(2), custom.SessionLifetime);
        Assert.Equal(10, custom.MaxValueBytes);
        Assert.Equal(5, custom.LoginMaxFailures);
        Assert.Equal("/contracts", custom.ContractRoot);
    }

    [Fact]
    public void SetupToken_IsRandomBase64Url_AndComparedExactly()
    {
        var first = new SetupToken();
        var second = new SetupToken();
        Assert.False(string.Equals(first.Value, second.Value, StringComparison.Ordinal));
        Assert.Equal(43, first.Value.Length);
        Assert.True(first.Value.IndexOfAny(['+', '/', '=']) < 0);
        Assert.True(first.Matches(first.Value));
        Assert.False(first.Matches(second.Value));
        Assert.False(first.Matches(null));
        Assert.False(first.Matches(string.Empty));
        Assert.False(first.Matches(first.Value[..^1]));
    }

    [Fact]
    public async Task Sessions_ExpireWithLifetime_AndRemoveDisposes()
    {
        var time = new ManualTime();
        var store = new SessionStore<Holder>(new DiagnosticsOptions { SessionLifetime = TimeSpan.FromHours(1) }, time);
        var first = await store.GetOrAddAsync("a", _ => new Holder());
        Assert.Same(first, await store.GetOrAddAsync("a", _ => new Holder()));
        Assert.True(store.TryGet("a", out _));

        var removed = await store.RemoveAsync("a");
        Assert.Same(first, removed);
        Assert.True(first.Disposed);
        Assert.False(store.TryGet("a", out _));
        Assert.Null(await store.RemoveAsync("a"));

        var second = await store.GetOrAddAsync("b", _ => new Holder());
        time.Advance(TimeSpan.FromHours(2));
        Assert.False(store.TryGet("b", out _));
        await store.SweepAsync();
        Assert.True(second.Disposed);
    }

    [Fact]
    public void SignInRegistry_LogoutRevokesAndEntriesExpire()
    {
        var time = new ManualTime();
        var registry = new SignInRegistry(new DiagnosticsOptions { SessionLifetime = TimeSpan.FromHours(1) }, time);
        registry.Add("sid");
        Assert.True(registry.IsActive("sid"));
        registry.Remove("sid");
        Assert.False(registry.IsActive("sid"));
        registry.Add("late");
        time.Advance(TimeSpan.FromHours(2));
        Assert.False(registry.IsActive("late"));
    }

    [Fact]
    public void SetPassword_UsesCreateNew_AndOwnerOnlyMode()
    {
        var path = Path.Combine(NewDir(), "admin.hash");
        var store = new LocalAdminStore(path);
        store.SetPassword("correct-horse-battery");
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
        }

        var original = File.ReadAllText(path);
        var racing = new LocalAdminStore(path);
        Assert.Throws<InvalidOperationException>(() => racing.SetPassword("another-long-password"));
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void ProfileAndMapFiles_AreOwnerOnly()
    {
        var directory = NewDir();
        var store = new ProtectedProfileStore(directory, new EphemeralDataProtectionProvider().CreateProtector("profiles"));
        store.Save("dev", "profile-password-1", new Dictionary<string, string>(StringComparer.Ordinal));
        var maps = new PatternMapStore(Path.Combine(directory, "maps.json"));
        maps.Save([new PatternMap("t:*", "demo.Type")]);
        if (!OperatingSystem.IsWindows())
        {
            var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            Assert.Equal(expected, File.GetUnixFileMode(Path.Combine(directory, "dev.json")));
            Assert.Equal(expected, File.GetUnixFileMode(Path.Combine(directory, "maps.json")));
        }
    }

    [Theory]
    [InlineData("dev", true)]
    [InlineData("a.b_c-9", true)]
    [InlineData("..", false)]
    [InlineData(".", false)]
    [InlineData("", false)]
    [InlineData("a/b", false)]
    [InlineData("a\\b", false)]
    [InlineData("a b", false)]
    [InlineData("name\n", false)]
    [InlineData("CON", false)]
    [InlineData("nul", false)]
    [InlineData("Com1", false)]
    [InlineData("LPT9.dev", false)]
    [InlineData("aux.", false)]
    [InlineData("console", true)]
    [InlineData("com10", true)]
    [InlineData("dev.con", true)]
    public void ProfileName_AllowsOnlySafeCharacters(string name, bool valid)
    {
        Assert.Equal(valid, ProtectedProfileStore.IsValidName(name));
    }

    [Fact]
    public void ProfileName_LongerThan64_IsRejected()
    {
        Assert.True(ProtectedProfileStore.IsValidName(new string('a', 64)));
        Assert.False(ProtectedProfileStore.IsValidName(new string('a', 65)));
    }

    [Fact]
    public void Contracts_RefusedWithoutRoot_AndOutsideIt()
    {
        var root = NewDir();
        Assert.Throws<InvalidOperationException>(() => ContractCatalog.ResolveFolder(root, null));
        Assert.Throws<InvalidOperationException>(() => ContractCatalog.ResolveFolder(root, "  "));
        Assert.Throws<InvalidOperationException>(() => ContractCatalog.ResolveFolder(Path.GetTempPath(), root));
        Assert.Throws<InvalidOperationException>(() => ContractCatalog.ResolveFolder(Path.Combine(root, "..", "elsewhere"), root));
        Assert.Throws<InvalidOperationException>(() => ContractCatalog.ResolveFolder(root + "-evil", root));
    }

    [Fact]
    public void Contracts_AllowedInsideRoot_RelativeOrAbsolute()
    {
        var root = NewDir();
        var inner = Path.Combine(root, "v1");
        Directory.CreateDirectory(inner);
        Assert.Equal(inner, ContractCatalog.ResolveFolder(inner, root));
        Assert.Equal(inner, ContractCatalog.ResolveFolder("v1", root + Path.DirectorySeparatorChar));
        Assert.Equal(root, ContractCatalog.ResolveFolder(root, root));
    }

    [Fact]
    public void Binding_BoundAddresses_AreCheckedForLoopback()
    {
        Assert.Empty(BindingRules.FindNonLoopback(["http://127.0.0.1:5000", "http://[::1]:5001", "http://localhost:5002"]));
        var offending = BindingRules.FindNonLoopback(["http://127.0.0.1:5000", "http://0.0.0.0:5001", "http://[::]:5002", "junk"]);
        Assert.Equal(3, offending.Count);
    }

    [Fact]
    public void Throttle_LockoutAnnouncesUnlockCode_ThatWorksOnce()
    {
        var time = new ManualTime();
        var announced = new List<string>();
        var throttle = new LoginThrottle(new DiagnosticsOptions { LoginMaxFailures = 1, LoginLockout = TimeSpan.FromMinutes(5) }, time, announced.Add);
        Assert.False(throttle.TryUnlock("anything"));
        Assert.True(throttle.RecordFailure());
        Assert.True(throttle.IsLocked());
        var line = Assert.Single(announced);
        var code = line[(line.LastIndexOf(' ') + 1)..];
        Assert.Equal(22, code.Length);

        Assert.False(throttle.TryUnlock(null));
        Assert.False(throttle.TryUnlock(code[..^1]));
        Assert.True(throttle.IsLocked());
        Assert.True(throttle.TryUnlock(" " + code + " "));
        Assert.False(throttle.IsLocked());
        Assert.False(throttle.TryUnlock(code));

        Assert.True(throttle.RecordFailure());
        Assert.Equal(2, announced.Count);
        Assert.False(throttle.TryUnlock(code));
        time.Advance(TimeSpan.FromMinutes(6));
        var second = announced[1][(announced[1].LastIndexOf(' ') + 1)..];
        Assert.False(throttle.TryUnlock(second));
    }

    [Fact]
    public async Task Sessions_ExpiredEntry_IsDisposedWhenReplaced()
    {
        var time = new ManualTime();
        var store = new SessionStore<Holder>(new DiagnosticsOptions { SessionLifetime = TimeSpan.FromHours(1) }, time);
        var old = await store.GetOrAddAsync("a", _ => new Holder());
        time.Advance(TimeSpan.FromHours(2));
        var fresh = await store.GetOrAddAsync("a", _ => new Holder());
        Assert.NotSame(old, fresh);
        Assert.True(old.Disposed);
        Assert.False(fresh.Disposed);
    }

    [Fact]
    public async Task SweepService_SweepsEverySweeper_OnItsOwnTimer()
    {
        var first = new CountingSweeper();
        var second = new CountingSweeper();
        var options = new DiagnosticsOptions { SessionSweepInterval = TimeSpan.FromMilliseconds(20) };
        using var service = new SessionSweepService([first, second], options, TimeProvider.System, NullLogger<SessionSweepService>.Instance);
        await service.SweepOnceAsync();
        Assert.Equal(1, first.Count);
        Assert.Equal(1, second.Count);

        await service.StartAsync(CancellationToken.None);
        await second.WaitForAsync(3, TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None);
        Assert.True(first.Count >= 3);
    }

    [Fact]
    public async Task SweepService_FailingSweeper_DoesNotStopTheOthers()
    {
        var after = new CountingSweeper();
        using var service = new SessionSweepService([new ThrowingSweeper(), after], new DiagnosticsOptions(), TimeProvider.System, NullLogger<SessionSweepService>.Instance);
        await service.SweepOnceAsync();
        await service.SweepOnceAsync();
        Assert.Equal(2, after.Count);
    }

    [Fact]
    public void Options_SessionSweepInterval_DefaultsAndReads()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), DiagnosticsOptions.From(new ConfigurationBuilder().Build()).SessionSweepInterval);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Diagnostics:SessionSweepSeconds"] = "15" })
            .Build();
        Assert.Equal(TimeSpan.FromSeconds(15), DiagnosticsOptions.From(configuration).SessionSweepInterval);
    }

    [Fact]
    public void AllowedHosts_EmptyAllowsAny_AndReadsStringOrArray()
    {
        var none = DiagnosticsOptions.From(new ConfigurationBuilder().Build());
        Assert.Empty(none.AllowedHosts);
        Assert.True(none.IsHostAllowed("anything.example", 1));

        var text = DiagnosticsOptions.From(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Diagnostics:AllowedHosts"] = " redis ; 10.0.0.5:6380, " })
            .Build());
        Assert.Equal("redis|10.0.0.5:6380", string.Join('|', text.AllowedHosts));

        var array = DiagnosticsOptions.From(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Diagnostics:AllowedHosts:0"] = "redis",
                ["Diagnostics:AllowedHosts:1"] = "[::1]:6379",
            })
            .Build());
        Assert.Equal("redis|[::1]:6379", string.Join('|', array.AllowedHosts));
    }

    [Theory]
    [InlineData("redis", 6379, true)]
    [InlineData("REDIS", 1, true)]
    [InlineData(" redis ", 6379, true)]
    [InlineData("redis.evil", 6379, false)]
    [InlineData("10.0.0.5", 6380, true)]
    [InlineData("10.0.0.5", 6379, false)]
    [InlineData("::1", 6379, true)]
    [InlineData("[::1]", 6379, true)]
    [InlineData("::1", 6380, false)]
    [InlineData("fe80::1", 5, true)]
    [InlineData("bad", 6379, false)]
    [InlineData("169.254.169.254", 80, false)]
    public void AllowedHosts_MatchHostAndOptionalPort(string host, int port, bool allowed)
    {
        var options = new DiagnosticsOptions { AllowedHosts = ["redis", "10.0.0.5:6380", "[::1]:6379", "fe80::1", "bad:port"] };
        Assert.Equal(allowed, options.IsHostAllowed(host, port));
    }

    [Fact]
    public void Contracts_LinkedFolderInsideRoot_IsRefused()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links needs elevated rights on Windows.");

        var root = NewDir();
        var outside = NewDir();
        Directory.CreateSymbolicLink(Path.Combine(root, "linked"), outside);
        Directory.CreateDirectory(Path.Combine(outside, "deeper"));
        var ex = Assert.Throws<InvalidOperationException>(() => ContractCatalog.ResolveFolder("linked", root));
        Assert.Contains("symbolic link", ex.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => ContractCatalog.ResolveFolder(Path.Combine("linked", "deeper"), root));
    }

    [Fact]
    public void Contracts_LinkedDll_IsSkipped_RegularCopyLoads()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating symbolic links needs elevated rights on Windows.");

        var source = typeof(PersonCache).Assembly.Location;
        var root = NewDir();
        var linked = Path.Combine(root, "linked");
        var copied = Path.Combine(root, "copied");
        Directory.CreateDirectory(linked);
        Directory.CreateDirectory(copied);
        File.CreateSymbolicLink(Path.Combine(linked, "contracts.dll"), source);
        File.Copy(source, Path.Combine(copied, "contracts.dll"));
        Assert.Empty(ContractCatalog.LoadFolder("linked", root).TypeNames);
        Assert.NotEmpty(ContractCatalog.LoadFolder("copied", root).TypeNames);
    }

    private static string NewDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "gocgun-hardening-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ThrowingSweeper : ISessionSweeper
    {
        public ValueTask SweepAsync()
        {
            return ValueTask.FromException(new IOException("dispose failed"));
        }
    }

    private sealed class CountingSweeper : ISessionSweeper
    {
        private int count;

        internal int Count => Volatile.Read(ref count);

        public ValueTask SweepAsync()
        {
            Interlocked.Increment(ref count);
            return ValueTask.CompletedTask;
        }

        internal async Task WaitForAsync(int target, TimeSpan timeout)
        {
            var deadline = DateTimeOffset.UtcNow + timeout;
            while (Count < target)
            {
                Assert.True(DateTimeOffset.UtcNow < deadline, "Sweep timer did not fire.");
                await Task.Delay(10);
            }
        }
    }

    private sealed class Holder : IAsyncDisposable
    {
        internal bool Disposed { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return now;
        }

        internal void Advance(TimeSpan by)
        {
            now += by;
        }
    }
}
