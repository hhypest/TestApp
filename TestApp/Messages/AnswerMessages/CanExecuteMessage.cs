using CommunityToolkit.Mvvm.Messaging.Messages;

namespace TestApp.Messages.AnswerMessages;

public sealed class CanExecuteMessage : ValueChangedMessage<bool>
{
    public CanExecuteMessage(bool value) : base(value)
    {
    }
}