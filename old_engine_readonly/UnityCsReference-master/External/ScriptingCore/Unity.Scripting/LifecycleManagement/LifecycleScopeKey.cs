namespace Unity.Scripting.LifecycleManagement;

internal readonly record struct LifecycleScopeKey
{
    public Type Type { get; }
    public object? Context { get; }

    public LifecycleScopeKey(Type scopeType)
        : this(scopeType, null!)
    {
    }

    public LifecycleScopeKey(Type scopeType, object? context)
    {
        if (scopeType.IsAbstract || scopeType.IsInterface || scopeType.IsGenericType)
        {
            throw new InvalidOperationException($"{nameof(LifecycleScopeKey)} cannot be an abstract, interface or generic class");
        }

        Type = scopeType;
        Context = context;
    }

    public static LifecycleScopeKey CreateFromScope(LifecycleScope scope)
    {
        return new LifecycleScopeKey(scope.GetType(), null!);
    }

    public static LifecycleScopeKey CreateFromScope<T>(LifecycleScopeWithContext<T> scope)
        where T : class
    {
        return new LifecycleScopeKey(scope.GetType(), scope.Context);
    }

    public bool Equals(LifecycleScopeKey other)
    {
        return Type == other.Type && ReferenceEquals(Context, other.Context);
    }

    // Avoid System.HashCode.Combine: its static initializer P/Invokes into libmono-native
    // for random seed generation, which is unavailable during AssemblyLoaded scope entry on UAAL ARMv7 Mono.
    // TODO: remove this override when on 6.8.
    public override int GetHashCode()
    {
        unchecked
        {
            return (Type?.GetHashCode() ?? 0) * 397 ^
                   (Context != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Context) : 0);
        }
    }
}
