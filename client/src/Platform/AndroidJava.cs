using System;
using Godot;

namespace Tribes.Client.Platform;

/// <summary>
/// Calls the game's Java helpers (client/android/platform/java, built into the APK by the gradle
/// build template) through Godot's JavaClassWrapper. Works from any thread: Godot attaches the
/// calling thread to the Java VM and finds the classes through the app's class loader.
/// </summary>
internal static class AndroidJava
{
    private const string RuntimeSingleton = "AndroidRuntime";

    /// <summary>Whether the game runs on Android.</summary>
    public static bool IsAndroid => OS.GetName() == "Android";

    /// <summary>The Godot activity, which the Java helpers take as their context.</summary>
    public static GodotObject Activity()
    {
        if (!IsAndroid || !Engine.HasSingleton(RuntimeSingleton))
        {
            throw new InvalidOperationException("The AndroidRuntime singleton is only available on Android.");
        }

        GodotObject? activity = Engine.GetSingleton(RuntimeSingleton).Call("getActivity").AsGodotObject();
        return activity ?? throw new InvalidOperationException("The Android activity is not available.");
    }

    /// <summary>Calls the public static Java method <paramref name="method"/> of <paramref name="className"/>.</summary>
    /// <exception cref="InvalidOperationException">The class or method is missing, or the method threw.</exception>
    public static Variant CallStatic(string className, string method, params Variant[] args)
    {
        if (!IsAndroid)
        {
            throw new InvalidOperationException($"{className}.{method} is only available on Android.");
        }

        JavaClass? javaClass = JavaClassWrapper.Wrap(className);
        if (javaClass is null || !javaClass.HasJavaMethod(method))
        {
            throw new InvalidOperationException($"The Java method {className}.{method} was not found in this build.");
        }

        Variant result = javaClass.Call(method, args);
        JavaObject? exception = JavaClassWrapper.GetException();
        if (exception is not null)
        {
            throw new InvalidOperationException($"{className}.{method} threw {exception}.");
        }

        return result;
    }

    /// <summary>Calls a static Java method whose first parameter is the activity.</summary>
    /// <exception cref="InvalidOperationException">Not on Android, the method is missing or threw.</exception>
    public static Variant CallWithActivity(string className, string method, params Variant[] args)
    {
        Variant[] withActivity = [Activity(), .. args];
        return CallStatic(className, method, withActivity);
    }

    /// <summary>
    /// A Java <c>boolean</c> result. JavaClassWrapper hands it over as an integer; nil means that
    /// no Java overload matched the arguments.
    /// </summary>
    /// <exception cref="InvalidOperationException">The result is not a boolean.</exception>
    public static bool ToBool(Variant result, string method) => result.VariantType switch
    {
        Variant.Type.Bool => result.AsBool(),
        Variant.Type.Int => result.AsInt64() != 0,
        _ => throw new InvalidOperationException($"{method} returned {result.VariantType} instead of a boolean."),
    };

    /// <summary>A Java <c>String</c> result; nil means that no Java overload matched the arguments.</summary>
    /// <exception cref="InvalidOperationException">The result is not a string.</exception>
    public static string ToText(Variant result, string method) => result.VariantType switch
    {
        Variant.Type.String or Variant.Type.StringName => result.AsString(),
        _ => throw new InvalidOperationException($"{method} returned {result.VariantType} instead of a string."),
    };
}
