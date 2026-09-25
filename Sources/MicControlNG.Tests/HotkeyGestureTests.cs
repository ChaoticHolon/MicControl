using MicControlNG.Hotkeys;

namespace MicControlNG.Tests;

public sealed class HotkeyGestureTests
{
    [Theory]
    [InlineData("F1", HotkeyModifiers.None, "F1")]
    [InlineData("CONTROL+SHIFT+F1", HotkeyModifiers.Control | HotkeyModifiers.Shift, "F1")]
    [InlineData("Ctrl+Alt+Delete", HotkeyModifiers.Control | HotkeyModifiers.Alt, "Delete")]
    [InlineData("CTRL++", HotkeyModifiers.Control, "OemPlus")]
    [InlineData("+", HotkeyModifiers.None, "OemPlus")]
    [InlineData("WIN+5", HotkeyModifiers.Windows, "D5")]
    [InlineData("Num *", HotkeyModifiers.None, "Multiply")]
    [InlineData("ENTER", HotkeyModifiers.None, "Return")]
    public void Parse_Keyboard(string text, HotkeyModifiers modifiers, string key)
    {
        var gesture = HotkeyGesture.Parse(text);

        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(key, gesture.Key, ignoreCase: true);
        Assert.False(gesture.IsMouse);
    }

    [Theory]
    [InlineData("MouseXButton1", HotkeyModifiers.None, MouseHotkey.XButton1)]
    [InlineData("ALT+MouseMiddle", HotkeyModifiers.Alt, MouseHotkey.Middle)]
    [InlineData("Ctrl+WheelUp", HotkeyModifiers.Control, MouseHotkey.WheelUp)]
    public void Parse_Mouse(string text, HotkeyModifiers modifiers, MouseHotkey mouse)
    {
        var gesture = HotkeyGesture.Parse(text);

        Assert.Equal(new HotkeyGesture(modifiers, Mouse: mouse), gesture);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("None")]
    public void Parse_Empty(string? text)
    {
        Assert.True(HotkeyGesture.Parse(text).IsEmpty);
    }

    [Theory]
    [InlineData("Ctrl+Shift+F1")]
    [InlineData("Alt+MouseXButton2")]
    [InlineData("Win+OemPlus")]
    [InlineData("None")]
    public void ToString_RoundTrips(string text)
    {
        Assert.Equal(text, HotkeyGesture.Parse(text).ToString());
    }

    [Fact]
    public void TryParse_RejectsUnknownModifier()
    {
        Assert.False(HotkeyGesture.TryParse("Hyper+F1", out _));
    }
}
