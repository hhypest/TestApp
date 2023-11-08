using CommunityToolkit.Mvvm.Messaging.Messages;

namespace TestApp.Messages.TestMessages;

public sealed class CreateTestMessage : ValueChangedMessage<string>
{
    public CreateTestMessage(string value) : base(value)
    {
    }
}