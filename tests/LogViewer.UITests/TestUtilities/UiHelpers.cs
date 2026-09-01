using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace LogViewer.UITests.TestUtilities;

/// <summary>
/// UI-Automation helpers that drive the app through UIA <b>patterns</b> (Invoke / Toggle / ExpandCollapse /
/// Value) rather than synthesized mouse and keyboard input — so they work in locked / headless CI sessions
/// where <c>SendInput</c> is denied.
/// </summary>
public static class UiHelpers
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    public static AutomationElement WaitFor(Func<AutomationElement?> find, string what)
    {
        var element = Retry.WhileNull(find, Timeout, throwOnTimeout: false).Result;
        return element ?? throw new TimeoutException($"Timed out waiting for: {what}.");
    }

    public static AutomationElement ByName(this AutomationElement parent, string name, ControlType type) =>
        WaitFor(() => parent.FindFirstDescendant(cf => cf.ByControlType(type).And(cf.ByName(name))), $"{type} '{name}'");

    public static AutomationElement? TryByName(this AutomationElement parent, string name, ControlType type) =>
        parent.FindFirstDescendant(cf => cf.ByControlType(type).And(cf.ByName(name)));

    public static AutomationElement? TryByAutomationId(this AutomationElement parent, string automationId) =>
        parent.FindFirstDescendant(cf => cf.ByAutomationId(automationId));

    /// <summary>The top-level menu bar of the main window.</summary>
    public static Menu MenuBar(AutomationElement window) =>
        WaitFor(() => window.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuBar)), "menu bar").AsMenu();

    /// <summary>The child items of a top-level menu (e.g. every entry under "File"), opening its popup.</summary>
    public static IReadOnlyList<string> MenuItems(AutomationElement window, string topLevelName)
    {
        var top = FindTopLevel(window, topLevelName);
        top.Expand();
        return WaitForChildren(top, $"items under '{topLevelName}'");
    }

    /// <summary>Opens a menu path like ["Tools", "Settings..."] via ExpandCollapse + Invoke (no mouse).</summary>
    public static void InvokeMenuPath(AutomationElement window, params string[] path)
    {
        var current = FindTopLevel(window, path[0]);
        for (var i = 0; i < path.Length; i++)
        {
            if (i == path.Length - 1)
            {
                current.Invoke();
                return;
            }

            current.Expand();
            var childName = path[i + 1];
            current = WaitFor(
                () => current.Items.FirstOrDefault(x => x.Name == childName),
                $"menu item '{childName}'").AsMenuItem();
        }
    }

    private static MenuItem FindTopLevel(AutomationElement window, string name) => WaitFor(
        () => window.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(name))),
        $"top-level menu '{name}'").AsMenuItem();

    private static IReadOnlyList<string> WaitForChildren(MenuItem parent, string what)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var names = parent.Items.Select(i => i.Name).Where(n => !string.IsNullOrEmpty(n)).ToList();
            if (names.Count > 0)
            {
                return names;
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException($"Timed out waiting for: {what}.");
    }

    public static bool WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? Timeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (condition())
                {
                    return true;
                }
            }
            catch
            {
                // element mid-teardown — keep polling
            }

            Thread.Sleep(100);
        }

        return false;
    }
}
