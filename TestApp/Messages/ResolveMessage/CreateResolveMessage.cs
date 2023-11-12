using CommunityToolkit.Mvvm.Messaging.Messages;
using TestApp.Models;

namespace TestApp.Messages.ResolveMessage;

public sealed class CreateResolveMessage : ValueChangedMessage<TestModel>
{
    public CreateResolveMessage(TestModel value) : base(value)
    {
    }
}