using System.Reflection;

namespace ScreenMind.Core;

public static class CoreAssembly
{
    public static Assembly Instance => typeof(CoreAssembly).Assembly;
}
