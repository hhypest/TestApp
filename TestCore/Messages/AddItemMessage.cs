using CommunityToolkit.Mvvm.Messaging.Messages;
using TestCore.ViewModels.Ask;

namespace TestCore.Messages;

public sealed class AddItemMessage : ValueChangedMessage<IAskViewModel>
{
    public AddItemMessage(IAskViewModel value) : base(value)
    {
    }
}