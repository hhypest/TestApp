using TestApp.Extensions;
using TestApp.Views;
using TestApp.Views.Shell;

namespace TestApp.Services.Factory;

public interface IFactoryService
{
    public IShellView CreateShellView();

    public IView CreateView(NavigationType typeView);

    public T CreateViewModel<T>(NavigationType type) where T : class;
}