using CommunityToolkit.Mvvm.Messaging.Messages;

namespace TestApp.Messages.AskMessages;

public sealed class EditAskMessage : ValueChangedMessage<bool>
{
    public EditAskMessage(bool value) : base(value)
    {
    }
}