using CommunityToolkit.Mvvm.Messaging.Messages;

namespace TestCore.Messages;

public sealed class TitleTestMessage : ValueChangedMessage<string>
{
    public TitleTestMessage(string value) : base(value)
    {
    }
}