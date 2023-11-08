using CommunityToolkit.Mvvm.Messaging.Messages;

namespace TestApp.Messages.AnswerMessages;

public sealed class EditAnswerMessage : ValueChangedMessage<bool>
{
    public EditAnswerMessage(bool value) : base(value)
    {
    }
}