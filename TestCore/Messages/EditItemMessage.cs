using CommunityToolkit.Mvvm.Messaging.Messages;

namespace TestCore.Messages;

public sealed class EditItemMessage : ValueChangedMessage<bool>
{
    public EditItemMessage(bool value) : base(value)
    {
    }
}