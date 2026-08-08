using System.Reflection;

namespace BotFarm.TestKit;

public static class ReflectionTestHelper
{
    public static InstanceFieldAccessor<TValue> CreateInstanceFieldAccessor<TTarget, TValue>(string fieldName)
    {
        var field = typeof(TTarget).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Could not find field '{fieldName}' on {typeof(TTarget).FullName}.");

        return new InstanceFieldAccessor<TValue>(field);
    }

    public static TValue GetRequiredInstanceFieldValue<TValue>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException($"Could not find field '{fieldName}' on {target.GetType().FullName}.");

        return (TValue)field.GetValue(target)!;
    }
}

public sealed class InstanceFieldAccessor<TValue>(FieldInfo field)
{
    public TValue Get(object target)
    {
        return (TValue)field.GetValue(target)!;
    }

    public void Set(object target, object? value)
    {
        field.SetValue(target, value);
    }
}
