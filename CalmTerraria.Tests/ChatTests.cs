using CalmTerraria.Pack;
using Srui.Testing;

namespace CalmTerraria.Tests;

internal sealed class FakeChat : IChat
{
    public List<string> Sent { get; } = new();
    public void Send(string text) => Sent.Add(text);
}

public class ChatAndRingTests
{
    private static (TestApp Ui, PackScreen Screen, ChatLog Log, FakeChat Chat) Make()
    {
        var ui = new TestApp();
        var log = new ChatLog();
        var chat = new FakeChat();
        var pack = new FakePack().Add("Acorn", 5);
        return (ui, new PackScreen(ui.App, pack, () => { }, chatLog: log, chat: chat), log, chat);
    }

    [Fact]
    public void OpenedForChatItStartsInTheMessageBoxAndEnterSends()
    {
        var (ui, screen, _, chat) = Make();
        screen.Open(focus: PackFocus.Chat);
        ui.Expect("Message edit blank alt m");
        ui.Type("hi all");
        ui.Drain();
        ui.Press("enter");
        Assert.Equal(new[] { "hi all" }, chat.Sent);
        // Emptied, and still there for the next one.
        ui.Type("x");
        ui.Press("enter");
        Assert.Equal(new[] { "hi all", "x" }, chat.Sent);
        Assert.True(screen.IsOpen);
    }

    [Fact]
    public void AnEmptyMessageIsNotSent()
    {
        var (ui, screen, _, chat) = Make();
        screen.Open(focus: PackFocus.Chat);
        ui.Press("enter");
        Assert.Empty(chat.Sent);
    }

    [Fact]
    public void TheChatListLandsOnTheNewestMessage()
    {
        var (ui, screen, log, _) = Make();
        log.Add("Austin: hello");
        log.Add("Rory was slain by a zombie.");
        screen.Open(focus: PackFocus.Chat);
        ui.Drain();
        ui.Press("shift+tab");
        ui.Expect("Chat list Rory was slain by a zombie. alt h");
        ui.Press("up");
        ui.Expect("Austin: hello");
    }

    [Fact]
    public void TabbingPastTheEndIsBackInTheWorld()
    {
        var (ui, screen, _, _) = Make();
        screen.Open(focus: PackFocus.Last);
        ui.Expect("Message edit blank alt m");
        ui.Press("tab");
        ui.Expect("World");
        Assert.False(screen.IsOpen);
    }

    [Fact]
    public void ShiftTabFromTheFirstStopIsBackInTheWorldToo()
    {
        var (ui, screen, _, _) = Make();
        screen.Open();
        ui.Expect("Carried list Acorn, 5 alt i");
        ui.Press("shift+tab");
        ui.Expect("World");
        Assert.False(screen.IsOpen);
    }
}
