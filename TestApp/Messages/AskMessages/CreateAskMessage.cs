using CommunityToolkit.Mvvm.Messaging.Messages;
using TestApp.ViewModels.Ask;

namespace TestApp.Messages.AskMessages;

public sealed class CreateAskMessage : ValueChangedMessage<IAskViewModel?>
{
    public CreateAskMessage(IAskViewModel? value) : base(value)
    {
    }
}