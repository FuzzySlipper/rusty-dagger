namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A value built once and shared by every fact that reads it, like <see cref="Lazy{T}"/>, except that a
/// failed build is not kept: the next reader builds again. A shared payload parse that hit a transient
/// failure (an allocation refused while other facts held memory) would otherwise fail every later fact
/// that reads it with that one cached exception.
/// </summary>
internal sealed class SharedFixture<T>(Func<T> build)
{
    private readonly object _gate = new();
    private bool _built;
    private T _value = default!;

    internal T Value
    {
        get
        {
            if (Volatile.Read(ref _built)) return _value;
            lock (_gate)
            {
                if (_built) return _value;
                _value = build();
                Volatile.Write(ref _built, true);
                return _value;
            }
        }
    }
}
