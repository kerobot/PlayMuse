using PlayMuse.Core.Models;
using PlayMuse.Core.Services;

namespace PlayMuse.Tests.Fakes;

/// <summary>
/// <see cref="IMetadataService"/> のテスト用フェイク。実際のタグ読み取りは行わず、常に何もせず完了する。
/// </summary>
public sealed class FakeMetadataService : IMetadataService
{
    public Task ApplyMetadataAsync(Track track, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
