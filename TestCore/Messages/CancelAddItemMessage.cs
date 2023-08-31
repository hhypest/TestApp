using CommunityToolkit.Mvvm.Messaging.Messages;

namespace TestCore.Messages;

public sealed class CancelAddItemMessage : ValueChangedMessage<bool>
{
    public CancelAddItemMessage(bool value) : base(value)
    {
    }
}